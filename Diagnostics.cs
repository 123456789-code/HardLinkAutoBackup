using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace HardLinkAutoBackup;

internal static class Diagnostics {
	[DllImport("user32.dll", ExactSpelling = true)]
	private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr parameter, IntPtr data);

	public static int Run(string output_directory) {
		string root = Path.GetFullPath(output_directory);
		Directory.CreateDirectory(root);
		string run_root = Path.Combine(root, $"run-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(run_root);
		var results = new List<string>();
		try {
			string source = Path.Combine(run_root, "source");
			string destination = Path.Combine(run_root, "backup");
			Directory.CreateDirectory(source);
			var task = new BackupTask { Name = "核心测试", Source = source, Destination = destination, IntervalSeconds = 10 };
			var engine = new BackupEngine(Path.Combine(run_root, "data"));
			string file = Path.Combine(source, "中文-é-한글.txt");
			File.WriteAllText(file, "original");
			ScanResult first = engine.Scan(task, [task], CancellationToken.None);
			string backup_file = Path.Combine(destination, Path.GetFileName(file));
			Check(first.Created == 1 && first.Failed == 0 && NativeFiles.Identity(file) == NativeFiles.Identity(backup_file), "首次创建真实硬链接", results);
			ScanResult second = engine.Scan(task, [task], CancellationToken.None);
			Check(second.Created == 0 && second.Existing == 1, "重复扫描不重复建链", results);
			File.WriteAllText(file, "edited in place");
			Check(File.ReadAllText(backup_file) == "edited in place", "原地修改共享内容", results);
			File.Delete(file);
			Check(File.ReadAllText(backup_file) == "edited in place", "删除源路径后备份保留", results);
			File.WriteAllText(file, "replacement");
			ScanResult replaced = engine.Scan(task, [task], CancellationToken.None);
			Check(replaced.Created == 1 && replaced.Conflicts == 1 && replaced.Failed == 0 && File.ReadAllText(backup_file) == "edited in place", "同名替换保留旧对象并另存新对象", results);
			ScanResult repeated = engine.Scan(task, [task], CancellationToken.None);
			Check(repeated.Created == 0 && repeated.Failed == 0, "冲突区建链幂等", results);
			string renamed = Path.Combine(source, "renamed.txt");
			File.Move(file, renamed);
			Check(engine.Scan(task, [task], CancellationToken.None).Created == 1 && File.Exists(backup_file), "重命名保留旧备份", results);
			Directory.CreateDirectory(Path.Combine(source, "cache"));
			File.WriteAllText(Path.Combine(source, "cache", "ignored.txt"), "excluded");
			task.Exclusions = ["cache"];
			Check(engine.Scan(task, [task], CancellationToken.None).Skipped > 0 && !Directory.Exists(Path.Combine(destination, "cache")), "相对路径排除", results);
			var nested = new BackupTask { Source = source, Destination = Path.Combine(source, "nested") };
			Check(engine.Scan(nested, [nested], CancellationToken.None).Failed == 1 && !Directory.Exists(nested.Destination), "嵌套目录拒绝且不创建目标", results);
			using var cancelled = new CancellationTokenSource();
			cancelled.Cancel();
			Check(engine.Scan(task, [task], cancelled.Token).Cancelled, "取消扫描", results);
			var store = new AppStore(Path.Combine(run_root, "data"));
			store.Save(new AppSettings { Tasks = [task], AutomaticEnabled = false });
			var legacy = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(store.ConfigurationPath))!.AsObject();
			legacy.Remove(nameof(AppSettings.HideUsageReminder));
			File.WriteAllText(store.ConfigurationPath, legacy.ToJsonString());
			AppSettings loaded = store.Load();
			Check(loaded.Tasks.Single().Id == task.Id && !loaded.AutomaticEnabled, "配置保存与加载", results);
			Check(!loaded.HideUsageReminder, "既有配置默认显示使用提醒", results);
			loaded.HideUsageReminder = true;
			store.Save(loaded);
			Check(store.Load().HideUsageReminder, "不再提示偏好持久保存", results);
			string shape = Path.Combine(source, "shape");
			File.WriteAllText(shape, "old file");
			engine.Scan(task, [task], CancellationToken.None);
			File.Delete(shape);
			Directory.CreateDirectory(shape);
			File.WriteAllText(Path.Combine(shape, "child.txt"), "new child");
			ScanResult shape_result = engine.Scan(task, [task], CancellationToken.None);
			Check(shape_result.Failed == 0 && File.ReadAllText(Path.Combine(destination, "shape")) == "old file", "文件改为目录时另存新子文件", results);
			string long_directory = Path.Combine(source, new string('a', 90), new string('b', 90), new string('c', 90));
			Directory.CreateDirectory(long_directory);
			string long_file = Path.Combine(long_directory, "长路径.txt");
			File.WriteAllText(long_file, "long path");
			Check(engine.Scan(task, [task], CancellationToken.None).Failed == 0 && NativeFiles.Identity(long_file) == NativeFiles.Identity(Path.Combine(destination, Path.GetRelativePath(source, long_file))), "长路径 Unicode 文件建链", results);
			RunReleaseChecks(run_root, task, engine, results);
			RunStartupChecks(run_root, results);
			RunCoordinatorChecks(run_root, results);
			File.WriteAllText(Path.Combine(root, "latest-results.txt"), $"测试目录：{run_root}\r\n{string.Join("\r\n", results)}\r\nALL PASSED\r\n", new UTF8Encoding(true));
			return 0;
		}
		catch (Exception exception) {
			File.WriteAllText(Path.Combine(root, "latest-results.txt"), $"测试目录：{run_root}\r\n{string.Join("\r\n", results)}\r\nFAILED: {exception}\r\n", new UTF8Encoding(true));
			return 1;
		}
	}


	private static void RunReleaseChecks(string root, BackupTask task, BackupEngine engine, List<string> results) {
		string changed = Path.Combine(task.Source, "partial.txt");
		File.WriteAllText(changed, "partial");
		ScanResult partial = engine.Scan(task, [task], CancellationToken.None, changes: [changed]);
		Check(!partial.FullScan && partial.Visited == 1 && partial.Created == 1 && partial.Failed == 0, "变化扫描只检查指定文件", results);
		string sub = Path.Combine(task.Source, "sub");
		Directory.CreateDirectory(sub);
		File.WriteAllText(Path.Combine(sub, "a.txt"), "a");
		File.WriteAllText(Path.Combine(sub, "b.txt"), "b");
		ScanResult merged = engine.Scan(task, [task], CancellationToken.None, changes: [sub, Path.Combine(sub, "a.txt")]);
		Check(merged.Visited == 2 && merged.Created == 2, "父子变化路径合并去重", results);
		var buffer = new ChangeBuffer();
		for (int index = 0; index < 1100; index++)
			buffer.Add(Path.Combine(task.Source, index.ToString()));
		Check(buffer.Drain() is null && buffer.Drain()!.Length == 0, "事件队列有界且溢出退回完整扫描", results);
		var wrong_volume = task.Snapshot();
		wrong_volume.VolumeIdentity = "wrong-volume";
		wrong_volume.Destination = Path.Combine(root, "wrong-volume-target");
		Check(engine.Scan(wrong_volume, [wrong_volume], CancellationToken.None).Failed == 1 && !Directory.Exists(wrong_volume.Destination), "卷身份变化拒绝写入", results);
		var data_target = task.Snapshot();
		data_target.Destination = Path.Combine(root, "data");
		Check(engine.Scan(data_target, [data_target], CancellationToken.None).Failed == 1, "拒绝备份覆盖程序数据目录", results);
		var relative = task.Snapshot();
		relative.Source = "relative";
		Check(engine.Scan(relative, [relative], CancellationToken.None).Failed == 1, "拒绝相对源路径", results);
		var invalid_rule = task.Snapshot();
		invalid_rule.Exclusions = ["../escape"];
		Check(engine.Scan(invalid_rule, [invalid_rule], CancellationToken.None).Failed == 1, "拒绝越界排除规则", results);
		var store = new AppStore(Path.Combine(root, "recovery"));
		store.Save(new AppSettings { Tasks = [task], AutomaticEnabled = false });
		store.Save(new AppSettings { Tasks = [task], AutomaticEnabled = true });
		File.WriteAllText(store.ConfigurationPath, "{broken");
		bool rejected = false;
		try { store.Load(); }
		catch (System.Text.Json.JsonException) { rejected = true; }
		Check(rejected, "损坏配置不静默覆盖", results);
		Check(!store.RestoreBackup().AutomaticEnabled && Directory.GetFiles(store.DataDirectory, "settings.damaged-*.json").Length == 1, "配置备份恢复且保留损坏文件", results);
		File.WriteAllText(store.ConfigurationPath, "{\"Version\":1,\"Tasks\":[null]}");
		bool null_rejected = false;
		try { store.Load(); }
		catch (InvalidDataException) { null_rejected = true; }
		Check(null_rejected, "空任务配置被拒绝", results);
		string reserved_source = Path.Combine(root, "reserved-source");
		Directory.CreateDirectory(Path.Combine(reserved_source, PathRules.CONFLICT_DIRECTORY));
		var reserved = new BackupTask { Source = reserved_source, Destination = Path.Combine(root, "reserved-target") };
		Check(engine.Scan(reserved, [reserved], CancellationToken.None).Failed == 1 && !Directory.Exists(reserved.Destination), "拒绝源顶层冲突区保留名", results);
		string blocked_source = Path.Combine(root, "blocked-source");
		Directory.CreateDirectory(blocked_source);
		File.WriteAllText(Path.Combine(blocked_source, "blocked.txt"), "blocked");
		File.WriteAllText(Path.Combine(blocked_source, "good.txt"), "good");
		var blocked = new BackupTask { Source = blocked_source, Destination = Path.Combine(root, "blocked-target") };
		Directory.CreateDirectory(blocked.Destination);
		File.WriteAllText(Path.Combine(blocked.Destination, "blocked.txt"), "old-object");
		string blocked_conflict = Path.Combine(blocked.Destination, PathRules.CONFLICT_DIRECTORY);
		File.WriteAllText(blocked_conflict, "not-a-directory");
		ScanResult blocked_result = engine.Scan(blocked, [blocked], CancellationToken.None);
		Check(blocked_result.Failed == 1 && File.Exists(Path.Combine(blocked.Destination, "good.txt")), "单项路径创建失败不阻断其他文件", results);
		File.Delete(blocked_conflict);
		Check(engine.Scan(blocked, [blocked], CancellationToken.None).Failed == 0
			&& File.ReadAllText(Path.Combine(blocked.Destination, "blocked.txt")) == "old-object", "解除路径阻塞后补建且旧对象保留", results);
		File.WriteAllText(Path.Combine(blocked.Destination, "foreign.txt"), "keep");
		File.WriteAllText(Path.Combine(blocked_source, "foreign.txt"), "new");
		Check(engine.Scan(blocked, [blocked], CancellationToken.None).Conflicts == 2 && File.ReadAllText(Path.Combine(blocked.Destination, "foreign.txt")) == "keep", "用户目标同名文件原样保留", results);
		string executable = Path.Combine(root, "A & B", "backup.exe");
		string xml = StartupRegistration.BuildDefinition("S-1-5-21-test", executable, true);
		var document = System.Xml.Linq.XDocument.Parse(xml);
		System.Xml.Linq.XNamespace schema = "http://schemas.microsoft.com/windows/2004/02/mit/task";
		Check(document.Descendants(schema + "Command").Single().Value == executable
			&& document.Descendants(schema + "RunLevel").Single().Value == "HighestAvailable"
			&& document.Descendants(schema + "ExecutionTimeLimit").Single().Value == "PT0S"
			&& document.Descendants(schema + "LogonType").Single().Value == "InteractiveToken", "自启 XML 转义、权限与无限运行时长", results);
		Check(StartupRegistration.ReferencesExecutable(xml, executable.ToUpperInvariant()), "卸载识别当前路径自启且忽略大小写", results);
		Check(!StartupRegistration.ReferencesExecutable(xml, Path.Combine(root, "another.exe")), "卸载不阻断其他位置的自启任务", results);
		Check(!StartupRegistration.ReferencesExecutable("<Task />", executable), "无执行路径不误判为当前程序自启", results);
		Check(StartupRegistration.BuildDefinition("user", executable, false).Contains("LeastPrivilege"), "普通权限自启定义", results);
		string many = Path.Combine(task.Source, "many");
		Directory.CreateDirectory(many);
		for (int index = 0; index < 2000; index++)
			File.WriteAllText(Path.Combine(many, index + ".txt"), "load");
		ScanResult load = engine.Scan(task, [task], CancellationToken.None);
		Check(load.Failed == 0 && load.Created == 2000 && load.Visited >= 2000, $"2000 文件完整扫描（{load.DurationSeconds:F2} 秒）", results);
		ScanResult one = engine.Scan(task, [task], CancellationToken.None, changes: [changed]);
		Check(one.Visited == 1 && one.Failed == 0, "大目录中的单文件变化仍只检查一项", results);
	}


	private static void RunStartupChecks(string root, List<string> results) {
		Exception mapped = System.Runtime.InteropServices.Marshal.GetExceptionForHR(unchecked((int)0x80070002))!;
		Check(mapped is FileNotFoundException && StartupRegistration.ReadExistingTask(() => throw mapped) is null, "CLR 映射的任务不存在异常可继续创建", results);
		Check(StartupRegistration.ReadExistingTask(() => throw new System.Runtime.InteropServices.COMException("missing", unchecked((int)0x80070002))) is null, "COM 形式的任务不存在异常同样处理", results);
		object existing = new();
		Check(ReferenceEquals(existing, StartupRegistration.ReadExistingTask(() => existing)), "已有计划任务正常返回", results);
		bool denied = false;
		try { StartupRegistration.ReadExistingTask(() => throw new UnauthorizedAccessException("denied")); }
		catch (UnauthorizedAccessException) { denied = true; }
		Check(denied, "查询权限不足不能误判为任务不存在", results);
		var store = new AppStore(Path.Combine(root, "write-probe"));
		store.EnsureWritable();
		Check(Directory.Exists(store.DataDirectory) && Directory.GetFiles(store.DataDirectory).Length == 0, "启动可写检查不残留探针文件", results);
		int confirmations = 0;
		int restarts = 0;
		var messages = new List<string>();
		bool Confirm() { confirmations++; return true; }
		void Restart() { restarts++; }
		void Deny() { throw new UnauthorizedAccessException("test"); }
		bool normal = StartupAccess.EnsureDataAccess(store.EnsureWritable, false, Confirm, Restart, messages.Add);
		Check(normal && confirmations == 0 && restarts == 0, "普通可写目录不请求管理员", results);
		bool protected_path = StartupAccess.EnsureDataAccess(Deny, false, Confirm, Restart, messages.Add);
		Check(!protected_path && confirmations == 1 && restarts == 1, "数据目录无权限时先确认再请求重启", results);
		bool declined = StartupAccess.EnsureDataAccess(Deny, false, () => false, Restart, messages.Add);
		Check(!declined && restarts == 1, "拒绝提权不继续读写数据也不重启", results);
		bool cancelled = StartupAccess.EnsureDataAccess(Deny, false, Confirm, () => throw new System.ComponentModel.Win32Exception(1223), messages.Add);
		Check(!cancelled && messages.Last().Contains("已取消管理员授权"), "取消 UAC 给出可执行的退出提示", results);
		bool administrator = StartupAccess.EnsureDataAccess(Deny, true, Confirm, Restart, messages.Add);
		Check(!administrator && restarts == 1 && messages.Last().Contains("已经以管理员身份运行"), "管理员仍不可写时避免循环提权", results);
		bool other_error = false;
		try { StartupAccess.EnsureDataAccess(() => throw new IOException("disk error"), false, Confirm, Restart, messages.Add); }
		catch (IOException) { other_error = true; }
		Check(other_error, "磁盘等非权限错误不误触发提权", results);
		Check(Thread.CurrentThread.CurrentUICulture.Name == "zh-CN", "应用 UI 资源固定为简体中文", results);
	}

	private static void Check(bool condition, string name, List<string> results) {
		if (!condition)
			throw new InvalidOperationException(name);
		results.Add($"PASS {name}");
	}

	private static void RunCoordinatorChecks(string root, List<string> results) {
		Exception? failure = null;
		using var context = new ApplicationContext();
		using var timer = new System.Windows.Forms.Timer { Interval = 20 };
		timer.Tick += async (_, _) => {
			timer.Stop();
			try { await CheckCoordinatorAsync(root, results); }
			catch (Exception exception) { failure = exception; }
			finally { context.ExitThread(); }
		};
		timer.Start();
		Application.Run(context);
		if (failure is not null)
			throw failure;
	}

	private static async Task CheckCoordinatorAsync(string root, List<string> results) {
		SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
		string source = Path.Combine(root, "scheduled-source");
		Directory.CreateDirectory(source);
		var task = new BackupTask { Source = source, Destination = Path.Combine(root, "scheduled-backup"), IntervalSeconds = 3600 };
		var settings = new AppSettings { Tasks = [task] };
		var store = new AppStore(Path.Combine(root, "scheduler-data"));
		var coordinator = new BackupCoordinator(settings, store);
		try {
			File.WriteAllText(Path.Combine(source, "first.txt"), "initial");
			coordinator.Reconfigure();
			coordinator.Tick();
			await WaitUntilAsync(() => !coordinator.IsRunning && File.Exists(Path.Combine(task.Destination, "first.txt")), coordinator);
			Check(true, "后台首次扫描", results);
			DateTimeOffset due = coordinator.State(task).NextRun;
			File.WriteAllText(Path.Combine(source, "watched.txt"), "event");
			await WaitUntilAsync(() => !coordinator.IsRunning && File.Exists(Path.Combine(task.Destination, "watched.txt")), coordinator);
			Check(coordinator.State(task).Result is { FullScan: false, Visited: 1 }, "文件监控触发单文件变化扫描", results);
			Check(coordinator.State(task).NextRun == due, "文件事件不推迟周期完整扫描", results);
			File.Delete(Path.Combine(task.Destination, "first.txt"));
			coordinator.State(task).NextRun = DateTimeOffset.Now.AddSeconds(-1);
			await WaitUntilAsync(() => !coordinator.IsRunning && File.Exists(Path.Combine(task.Destination, "first.txt")), coordinator);
			Check(true, "周期扫描补建遗漏链接", results);
			coordinator.Reconfigure();
			DateTimeOffset before = coordinator.State(task).NextRun;
			coordinator.Reconfigure();
			Check(coordinator.State(task).NextRun == before, "不相关配置不重置扫描时间", results);
			settings.AutomaticEnabled = false;
			coordinator.Reconfigure();
			File.WriteAllText(Path.Combine(source, "manual.txt"), "manual");
			coordinator.Tick();
			await Task.Delay(200);
			Check(!File.Exists(Path.Combine(task.Destination, "manual.txt")), "总开关暂停自动备份", results);
			coordinator.Request(task);
			await WaitUntilAsync(() => !coordinator.IsRunning && File.Exists(Path.Combine(task.Destination, "manual.txt")), coordinator);
			Check(true, "总开关关闭时手动扫描可用", results);
			coordinator.Request(task);
			coordinator.Request(task);
			coordinator.Cancel(task);
			await WaitUntilAsync(() => !coordinator.IsRunning, coordinator);
			await Task.Delay(200);
			Check(!coordinator.IsRunning && !coordinator.State(task).PendingManual, "取消清除追加手动扫描", results);
			settings.AutomaticEnabled = true;
			coordinator.Reconfigure();
			coordinator.State(task).NextRun = DateTimeOffset.Now.AddHours(1);
			coordinator.Resume();
			Check(coordinator.State(task).NextRun <= DateTimeOffset.Now, "恢复通知安排完整补扫", results);
			coordinator.Tick();
			await WaitUntilAsync(() => !coordinator.IsRunning, coordinator);
			Check(coordinator.State(task).Result is { FullScan: true, Failed: 0 }, "恢复补扫完成", results);
			var restored = new BackupCoordinator(settings, store);
			restored.RestoreState();
			Check(restored.State(task).LastFinished is not null && restored.State(task).LastSuccess is not null, "重新启动恢复最近扫描记录", results);
			await restored.StopAsync();
		}
		finally { await coordinator.StopAsync(); }
		Check(true, "后台任务可取消并退出", results);
	}

	private static async Task WaitUntilAsync(Func<bool> condition, BackupCoordinator coordinator) {
		DateTimeOffset deadline = DateTimeOffset.Now.AddSeconds(15);
		while (!condition()) {
			if (DateTimeOffset.Now > deadline)
				throw new TimeoutException("后台调度测试超时。");
			coordinator.Tick();
			await Task.Delay(50);
		}
	}

	public static int Render(string output_directory) {
		Directory.CreateDirectory(output_directory);
		var report = new List<string>();
		try {
			CheckTogglePainting(output_directory, report);
			CheckSettingsDesign(output_directory, report);
			foreach (float scale in new[] { 1f, 1.5f, 2f }) {
				foreach (bool compact in new[] { false, true }) {
					var settings = new AppSettings { Tasks = [new BackupTask { Name = "工作文档", Source = @"E:\Documents", Destination = @"E:\Backups\Documents" }, new BackupTask { Name = "照片资料", Source = @"E:\Photos", Destination = @"E:\Backups\Photos", Enabled = false }] };
					string suffix = scale == 1 && !compact ? "" : $"-{scale * 100:0}{(compact ? "-compact" : "")}";
					using var main = new MainForm(settings, new AppStore(Path.Combine(output_directory, "preview-data")), false, true);
					RenderForm(main, Path.Combine(output_directory, $"main{suffix}.png"), scale, compact, report);
					using var editor = new TaskEditor(settings.Tasks, settings.Tasks[0]);
					RenderForm(editor, Path.Combine(output_directory, $"task-editor{suffix}.png"), scale, compact, report);
					using var preferences = new SettingsDialog(settings, Path.Combine(AppContext.BaseDirectory, "data"));
					RenderForm(preferences, Path.Combine(output_directory, $"settings{suffix}.png"), scale, compact, report);
				}
			}
			var interaction_store = new AppStore(Path.Combine(output_directory, $"interaction-{Guid.NewGuid():N}"));
			var interaction_settings = new AppSettings();
			using (var main = new MainForm(interaction_settings, interaction_store, false, true)) {
				RenderForm(main, Path.Combine(output_directory, "main-before-dismiss.png"), 1, false, report);
				main.Show();
				Button dismiss = Descendants(main).OfType<Button>().Single(button => button.Text == "不再提示");
				int previous_height = dismiss.Parent!.Height;
				dismiss.PerformClick();
				Application.DoEvents();
				Check(interaction_store.Load().HideUsageReminder && !dismiss.Parent.Visible, "点击不再提示后隐藏并保存", report);
				var layout = (TableLayoutPanel)dismiss.Parent.Parent!;
				Check(previous_height > 0 && layout.GetRowHeights().Last() == 0, "隐藏提醒后不保留底部空白", report);
				ToggleSwitch automatic = Descendants(main).OfType<ToggleSwitch>().Single();
				automatic.Checked = false;
				Check(!interaction_store.Load().AutomaticEnabled && (automatic.AccessibilityObject.State & AccessibleStates.Checked) == 0, "滑块关闭保存且无障碍状态同步", report);
				RenderForm(main, Path.Combine(output_directory, "main-dismissed-paused.png"), 1, false, report);
			}
			using (var reopened = new MainForm(interaction_store.Load(), interaction_store, false, true)) {
				RenderForm(reopened, Path.Combine(output_directory, "main-reopened.png"), 1, false, report);
				reopened.Show();
				Check(!Descendants(reopened).OfType<Button>().Single(button => button.Text == "不再提示").Visible, "重新打开窗口仍隐藏提醒", report);
				ToggleSwitch automatic = Descendants(reopened).OfType<ToggleSwitch>().Single();
				automatic.Checked = true;
				Check(interaction_store.Load().AutomaticEnabled && (automatic.AccessibilityObject.State & AccessibleStates.Checked) != 0, "滑块恢复保存且无障碍状态同步", report);
			}
			File.WriteAllLines(Path.Combine(output_directory, "layout-results.txt"), report, new UTF8Encoding(true));
			return 0;
		}
		catch (Exception exception) {
			File.WriteAllText(Path.Combine(output_directory, "render-error.txt"), exception.ToString());
			File.WriteAllLines(Path.Combine(output_directory, "layout-results.txt"), report, new UTF8Encoding(true));
			return 1;
		}
	}

	private static void CheckSettingsDesign(string output_directory, List<string> report) {
		var settings = new AppSettings { AutomaticEnabled = false, StayInTray = true, HideUsageReminder = false };
		using (var dialog = new SettingsDialog(settings, Path.Combine(output_directory, "settings-data"))) {
			dialog.StartPosition = FormStartPosition.Manual;
			dialog.Location = new Point(-32000, -32000);
			dialog.Show();
			ToggleSwitch[] toggles = Descendants(dialog).OfType<ToggleSwitch>().ToArray();
			Check(toggles.Length == 4 && toggles.All(toggle => !toggle.Text.Contains("自动备份")), "设置页仅含四个程序选项，无重复备份总开关", report);
			Check(!dialog.AutoStart.Checked && !dialog.Elevated.Checked && dialog.Tray.Checked && dialog.Reminder.Checked, "四个设置滑块正确加载已有配置", report);
			dialog.AutoStart.Checked = true;
			dialog.Tray.Checked = false;
			Descendants(dialog).OfType<Button>().Single(button => button.Text == "同步自启任务").PerformClick();
			Check(dialog.RepairStartupRequested && !settings.StartAtLogon && settings.StayInTray, "维护按钮只安排保存时同步，未立即更改配置或系统", report);
			Descendants(dialog).OfType<Button>().Single(button => button.Text == "取消").PerformClick();
			Check(dialog.DialogResult == DialogResult.Cancel && !settings.StartAtLogon && settings.StayInTray, "取消设置保留原配置", report);
		}
		var store = new AppStore(Path.Combine(output_directory, $"interaction-{Guid.NewGuid():N}"));
		using var main = new MainForm(settings, store, false, true);
		main.StartPosition = FormStartPosition.Manual;
		main.Location = new Point(-32000, -32000);
		main.ShowInTaskbar = false;
		main.Show();
		// 在真实设置对话框中保存普通选项，不触碰系统自启设置。
		using var timer = new System.Windows.Forms.Timer { Interval = 50 };
		timer.Tick += (_, _) => {
			timer.Stop();
			var dialog = Application.OpenForms.OfType<SettingsDialog>().Single();
			dialog.Tray.Checked = false;
			dialog.Reminder.Checked = false;
			Descendants(dialog).OfType<Button>().Single(button => button.Text == "保存设置").PerformClick();
		};
		timer.Start();
		Descendants(main).OfType<Button>().Single(button => button.Text == "程序设置").PerformClick();
		AppSettings saved = store.Load();
		Check(!saved.AutomaticEnabled && !saved.StayInTray && saved.HideUsageReminder, "保存程序选项不改变已暂停的备份总开关", report);
		Check(!Descendants(main).OfType<Button>().Single(button => button.Text == "不再提示").Visible, "设置保存后主界面提醒即时更新", report);
	}

	private static void CheckTogglePainting(string output_directory, List<string> report) {
		foreach (float scale in new[] { 1f, 1.5f, 2f }) {
			foreach (Color background in new[] { UiTheme.Canvas, Color.White }) {
				using var form = new Form { ClientSize = new Size(800, 200), BackColor = background, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
				using var font = new Font("Microsoft YaHei UI", 10 * scale);
				var toggle = new ToggleSwitch { Text = "自动备份", Font = font, Location = new Point(24, 24), AutoSize = false };
				form.Controls.Add(toggle);
				form.Show();
				foreach (bool enabled in new[] { true, false }) {
					foreach (bool active in new[] { true, false, true }) {
						toggle.Enabled = enabled;
						toggle.Checked = active;
						toggle.Size = toggle.GetPreferredSize(Size.Empty) + new Size(active ? 20 : 0, 0);
						using var bitmap = new Bitmap(toggle.Width, toggle.Height);
						using (Graphics graphics = Graphics.FromImage(bitmap)) {
							// 污染复用缓冲区，再走实际 WM_PAINT 路径；DrawToBitmap 不覆盖此故障。
							using (BufferedGraphics buffer = BufferedGraphicsManager.Current.Allocate(graphics, toggle.ClientRectangle))
								buffer.Graphics.Clear(Color.Magenta);
							IntPtr device = graphics.GetHdc();
							try { SendMessageW(toggle.Handle, 0x000f, device, IntPtr.Zero); }
							finally { graphics.ReleaseHdc(device); }
						}
						string name = $"toggle-paint-{scale * 100:0}-{background.ToArgb():X8}-{enabled}-{active}.png";
						bitmap.Save(Path.Combine(output_directory, name), ImageFormat.Png);
						// 外沿不包含滑块、文字或焦点框，重绘后必须恢复父容器背景。
						for (int x = 0; x < bitmap.Width; x++) {
							if (bitmap.GetPixel(x, 0).ToArgb() != background.ToArgb() || bitmap.GetPixel(x, bitmap.Height - 1).ToArgb() != background.ToArgb())
								throw new InvalidOperationException($"{name} 滑块重绘留下旧像素：x={x}");
						}
					}
				}
				Check(true, $"滑块 WM_PAINT 背景恢复：{scale * 100:0}% / {background.Name}，切换、禁用及尺寸变化", report);
			}
		}
	}

	private static void RenderForm(Form form, string path, float scale, bool compact, List<string> report) {
		form.StartPosition = FormStartPosition.Manual;
		form.Location = new Point(-32000, -32000);
		form.ShowInTaskbar = false;
		form.Show();
		Application.DoEvents();
		if (compact)
			form.Size = form.MinimumSize;
		if (scale != 1) {
			// 合成布局检查：放大尺寸和每个控件字体，不改 Windows 显示设置。
			var fonts = Descendants(form).Prepend(form).Select(control => (Control: control, Font: control.Font)).ToArray();
			form.AutoScaleMode = AutoScaleMode.None;
			form.Scale(new SizeF(scale, scale));
			foreach (var item in fonts)
				item.Control.Font = new Font(item.Font.FontFamily, item.Font.Size * scale, item.Font.Style);
		}
		form.PerformLayout();
		Application.DoEvents();
		using var bitmap = new Bitmap(form.Width, form.Height);
		form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
		bitmap.Save(path, ImageFormat.Png);
		foreach (ButtonBase button in Descendants(form).OfType<ButtonBase>().Where(button => button.Visible)) {
			Size measured = button is ToggleSwitch toggle
				? toggle.GetPreferredSize(Size.Empty) - new Size(button.Padding.Horizontal, button.Padding.Vertical)
				: TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.SingleLine);
			if (button.ClientSize.Width < measured.Width + button.Padding.Horizontal
				|| button.ClientSize.Height < measured.Height + button.Padding.Vertical)
				throw new InvalidOperationException($"{Path.GetFileName(path)} 按钮文字空间不足：{button.Text}，按钮 {button.Size}，文字 {measured}");
			if (button.Parent is Control parent && (button.Left < 0 || button.Right > parent.ClientSize.Width + 1 || button.Bottom > parent.ClientSize.Height + 1))
				throw new InvalidOperationException($"{Path.GetFileName(path)} 按钮超出父容器：{button.Text}");
		}
		Check(Descendants(form).OfType<CheckBox>().All(control => control is ToggleSwitch), $"{Path.GetFileName(path)}：开关样式统一、按钮与滑块文字及容器边界", report);
		form.Hide();
	}

	private static IEnumerable<Control> Descendants(Control parent) {
		foreach (Control child in parent.Controls) {
			yield return child;
			foreach (Control descendant in Descendants(child))
				yield return descendant;
		}
	}
}
