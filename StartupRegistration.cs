using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

namespace HardLinkAutoBackup;

internal static class StartupRegistration {
	private const string OWNER = "HardLinkAutoBackup owned task v1";
	public static string TaskName => $"HardLinkAutoBackup-{WindowsIdentity.GetCurrent().User!.Value}";
	private static readonly XNamespace Schema = "http://schemas.microsoft.com/windows/2004/02/mit/task";

	public static bool IsAdministrator() {
		using WindowsIdentity identity = WindowsIdentity.GetCurrent();
		return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
	}

	public static string BuildDefinition(string user, string executable, bool elevated) {
		XElement Element(string name, object content) {
			return new XElement(Schema + name, content);
		}
		return new XDocument(new XElement(Schema + "Task", new XAttribute("version", "1.2"),
			Element("RegistrationInfo", Element("Description", OWNER)),
			Element("Triggers", Element("LogonTrigger", new object[] { Element("Enabled", true), Element("UserId", user) })),
			Element("Principals", new XElement(Schema + "Principal", new XAttribute("id", "User"),
				Element("UserId", user), Element("LogonType", "InteractiveToken"), Element("RunLevel", elevated ? "HighestAvailable" : "LeastPrivilege"))),
			Element("Settings", new object[] {
				Element("MultipleInstancesPolicy", "IgnoreNew"), Element("DisallowStartIfOnBatteries", false),
				Element("StopIfGoingOnBatteries", false), Element("StartWhenAvailable", true),
				Element("Enabled", true), Element("ExecutionTimeLimit", "PT0S")
			}),
			new XElement(Schema + "Actions", new XAttribute("Context", "User"),
				Element("Exec", new object[] { Element("Command", executable), Element("Arguments", "--tray"), Element("WorkingDirectory", Path.GetDirectoryName(executable)!) }))
		)).ToString();
	}

	public static string Describe() {
		using var scope = new ComScope();
		dynamic service = scope.Add(Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service") ?? throw new IOException("任务计划服务不可用。"))!);
		service.Connect();
		dynamic folder = scope.Add(service.GetFolder("\\"));
		dynamic? existing = FindTask(scope, folder);
		if (existing is null)
			return "未注册当前用户的自启任务。";
		XDocument xml = XDocument.Parse((string)existing.Xml);
		if (xml.Descendants(Schema + "Description").FirstOrDefault()?.Value != OWNER)
			return "存在不属于本程序的同名任务，不能修改。";
		string command = xml.Descendants(Schema + "Command").FirstOrDefault()?.Value ?? "";
		string level = xml.Descendants(Schema + "RunLevel").FirstOrDefault()?.Value ?? "";
		return $"任务：{TaskName}\r\n启用：{existing.Enabled}\r\n管理员：{level == "HighestAvailable"}\r\n路径：{command}\r\n路径匹配当前程序：{command.Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)}";
	}

	public static async Task ApplyAsync(bool enabled, bool elevated, AppStore store) {
		Directory.CreateDirectory(store.DataDirectory);
		if (IsAdministrator()) { Apply(enabled, elevated); return; }
		if (!elevated) {
			try { Apply(enabled, false); return; }
			catch (COMException exception) when (exception.HResult == unchecked((int)0x80070005)) { }
			catch (UnauthorizedAccessException) { }
		}
		string result_path = Path.Combine(store.DataDirectory, $"startup-result-{Guid.NewGuid():N}.txt");
		var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
		start.ArgumentList.Add("--configure-startup");
		start.ArgumentList.Add(enabled.ToString());
		start.ArgumentList.Add(elevated.ToString());
		start.ArgumentList.Add(result_path);
		start.ArgumentList.Add(WindowsIdentity.GetCurrent().User!.Value);
		using Process process = Process.Start(start) ?? throw new IOException("无法启动权限设置程序。");
		await process.WaitForExitAsync();
		string result = File.Exists(result_path) ? await File.ReadAllTextAsync(result_path) : "操作被取消或设置程序未返回结果。";
		if (File.Exists(result_path))
			File.Delete(result_path);
		if (result != "OK")
			throw new IOException(result);
	}

	private static object? FindTask(ComScope scope, dynamic folder) {
		try { return scope.Add(folder.GetTask(TaskName)); }
		catch (COMException exception) when (exception.HResult == unchecked((int)0x80070002)) { return null; }
	}

	public static void Apply(bool enabled, bool elevated) {
		using var scope = new ComScope();
		dynamic service = scope.Add(Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service") ?? throw new IOException("Windows 任务计划服务不可用。"))!);
		service.Connect();
		dynamic folder = scope.Add(service.GetFolder("\\"));
		dynamic? existing = FindTask(scope, folder);
		if (existing is not null) {
			XDocument xml = XDocument.Parse((string)existing.Xml);
			if (xml.Descendants(Schema + "Description").FirstOrDefault()?.Value != OWNER)
				throw new IOException("同名计划任务不属于本程序，已停止操作。");
		}
		if (!enabled) {
			if (existing is not null)
				folder.DeleteTask(TaskName, 0);
			return;
		}
		string user = WindowsIdentity.GetCurrent().User!.Value;
		string definition = BuildDefinition(user, Environment.ProcessPath!, elevated);
		scope.Add(folder.RegisterTask(TaskName, definition, 6, user, null, 3, null));
	}

	private sealed class ComScope : IDisposable {
		private readonly List<object> Objects = [];
		public object Add(object value) {
			Objects.Add(value);
			return value;
		}
		public void Dispose() {
			for (int index = Objects.Count - 1; index >= 0; index--)
				Marshal.FinalReleaseComObject(Objects[index]);
		}
	}
}
