using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace HardLinkAutoBackup;

internal static class StartupRegistration {
	private const string OWNER = "HardLinkAutoBackup owned task v1";
	public static string TaskName => $"HardLinkAutoBackup-{WindowsIdentity.GetCurrent().User!.Value}";

	public static bool IsAdministrator() {
		using WindowsIdentity identity = WindowsIdentity.GetCurrent();
		return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
	}

	public static async Task ApplyAsync(bool enabled, bool elevated, AppStore store) {
		if (IsAdministrator()) { Apply(enabled, elevated); return; }
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

	public static void Apply(bool enabled, bool elevated) {
		Type type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new IOException("Windows 任务计划服务不可用。");
		dynamic service = Activator.CreateInstance(type)!;
		try {
			service.Connect();
			dynamic folder = service.GetFolder("\\");
			try {
				dynamic? existing = null;
				try { existing = folder.GetTask(TaskName); }
				catch (COMException exception) when (exception.HResult == unchecked((int)0x80070002)) { }
				if (existing is not null) {
					string owner = existing.Definition.RegistrationInfo.Description;
					Marshal.FinalReleaseComObject(existing);
					if (owner != OWNER)
						throw new IOException("同名计划任务不属于本程序，已停止操作。");
				}
				if (!enabled) {
					if (existing is not null)
						folder.DeleteTask(TaskName, 0);
					return;
				}
				dynamic definition = service.NewTask(0);
				try {
					string user = WindowsIdentity.GetCurrent().User!.Value;
					definition.RegistrationInfo.Description = OWNER;
					definition.Principal.UserId = user;
					definition.Principal.LogonType = 3;
					definition.Principal.RunLevel = elevated ? 1 : 0;
					definition.Settings.Enabled = true;
					definition.Settings.DisallowStartIfOnBatteries = false;
					definition.Settings.StopIfGoingOnBatteries = false;
					definition.Settings.ExecutionTimeLimit = "PT0S";
					definition.Settings.MultipleInstances = 2;
					dynamic trigger = definition.Triggers.Create(9);
					trigger.UserId = user;
					dynamic action = definition.Actions.Create(0);
					action.Path = Environment.ProcessPath;
					action.Arguments = "--tray";
					action.WorkingDirectory = AppContext.BaseDirectory;
					dynamic registered = folder.RegisterTaskDefinition(TaskName, definition, 6, user, null, 3, null);
					Marshal.FinalReleaseComObject(registered);
				}
				finally { Marshal.FinalReleaseComObject(definition); }
			}
			finally { Marshal.FinalReleaseComObject(folder); }
		}
		finally { Marshal.FinalReleaseComObject(service); }
	}
}
