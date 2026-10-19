using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace HardLinkAutoBackup;

internal static class Program {
	internal static readonly int ActivateMessage = (int)RegisterWindowMessageW("HardLinkAutoBackup.ShowWindow.v1");

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern uint RegisterWindowMessageW(string name);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PostMessageW(IntPtr window, uint message, IntPtr parameter, IntPtr data);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ChangeWindowMessageFilterEx(IntPtr window, uint message, uint action, IntPtr filter);

	internal static void AllowActivation(IntPtr handle) {
		ChangeWindowMessageFilterEx(handle, (uint)ActivateMessage, 1, IntPtr.Zero);
	}

	[STAThread]
	private static int Main(string[] args) {
		ApplicationConfiguration.Initialize();
		if (args.Length == 1 && args[0] == "--version")
			return 0;
#if DIAGNOSTICS
		if (args.Length == 2 && args[0] == "--self-test")
			return DemoChecks.Run(args[1]);
		if (args.Length == 2 && args[0] == "--render-preview")
			return DemoChecks.Render(args[1]);
#endif
		var store = new AppStore();
		AppDomain.CurrentDomain.UnhandledException += (_, event_args) => store.Log($"未处理异常：{event_args.ExceptionObject}");
		if (args.Length == 5 && args[0] == "--configure-startup") {
			string result_path = Path.GetFullPath(args[3]);
			if (!Path.GetDirectoryName(result_path)!.Equals(Path.GetFullPath(store.DataDirectory), StringComparison.OrdinalIgnoreCase))
				return 2;
			try {
				if (WindowsIdentity.GetCurrent().User!.Value != args[4])
					throw new IOException("请使用当前用户进行提权，暂不支持切换为另一个管理员账户。");
				StartupRegistration.Apply(bool.Parse(args[1]), bool.Parse(args[2]));
				File.WriteAllText(result_path, "OK");
				return 0;
			}
			catch (Exception exception) { File.WriteAllText(result_path, exception.Message); return 1; }
		}
		try {
			string user_id = WindowsIdentity.GetCurrent().User!.Value;
			string mutex_name = $"Local\\HardLinkAutoBackup-{user_id}";
			if (Mutex.TryOpenExisting(mutex_name, out Mutex? running)) {
				running.Dispose();
				PostMessageW(new IntPtr(0xffff), (uint)ActivateMessage, IntPtr.Zero, IntPtr.Zero);
				return 0;
			}
			AppSettings settings;
			try { settings = store.Load(); }
			catch (Exception exception) {
				if (!File.Exists(store.ConfigurationPath + ".bak")
					|| MessageBox.Show($"配置无法读取：{exception.Message}\r\n\r\n是否从 settings.json.bak 恢复？损坏的原文件会另存保留。", "恢复配置",
						MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
					throw;
				settings = store.RestoreBackup();
			}
			if (settings.RunAsAdministrator && !StartupRegistration.IsAdministrator()) {
				var elevated = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
				foreach (string argument in args)
					elevated.ArgumentList.Add(argument);
				try { Process.Start(elevated)?.Dispose(); return 0; }
				catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223) {
					if (MessageBox.Show("管理员授权已取消。是否关闭管理员启动设置，并以普通权限继续？", "启动权限", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
						return 0;
					settings.RunAsAdministrator = false;
					store.Save(settings);
				}
			}
			using var instance = new Mutex(true, mutex_name, out bool created);
			if (!created) {
				PostMessageW(new IntPtr(0xffff), (uint)ActivateMessage, IntPtr.Zero, IntPtr.Zero);
				return 0;
			}
			Application.ThreadException += (_, event_args) => {
				store.Log(event_args.Exception.ToString());
				MessageBox.Show(event_args.Exception.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
			};
			using var form = new Form1(settings, store, args.Contains("--tray"));
			using var context = new ApplicationContext(form);
			Application.Run(context);
			return 0;
		}
		catch (Exception exception) {
			MessageBox.Show($"{exception.Message}\r\n\r\n配置路径：{store.ConfigurationPath}", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
			return 1;
		}
	}
}
