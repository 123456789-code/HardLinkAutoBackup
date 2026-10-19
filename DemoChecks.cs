using System.Drawing.Imaging;
using System.Text;

namespace HardLinkAutoBackup;

internal static class DemoChecks {
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
			AppSettings loaded = store.Load();
			Check(loaded.Tasks.Single().Id == task.Id && !loaded.AutomaticEnabled, "配置保存与加载", results);
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
		try {
			var settings = new AppSettings { Tasks = [new BackupTask { Name = "工作文档", Source = @"E:\Documents", Destination = @"E:\Backups\Documents" }, new BackupTask { Name = "照片资料", Source = @"E:\Photos", Destination = @"E:\Backups\Photos", Enabled = false }] };
			using var main = new Form1(settings, new AppStore(Path.Combine(output_directory, "preview-data")), false, true);
			RenderForm(main, Path.Combine(output_directory, "main.png"));
			using var editor = new TaskEditor(settings.Tasks, settings.Tasks[0]);
			RenderForm(editor, Path.Combine(output_directory, "task-editor.png"));
			using var preferences = new SettingsDialog(settings, Path.Combine(AppContext.BaseDirectory, "data"));
			RenderForm(preferences, Path.Combine(output_directory, "settings.png"));
			return 0;
		}
		catch (Exception exception) { File.WriteAllText(Path.Combine(output_directory, "render-error.txt"), exception.ToString()); return 1; }
	}

	private static void RenderForm(Form form, string path) {
		form.StartPosition = FormStartPosition.Manual;
		form.Location = new Point(-32000, -32000);
		form.ShowInTaskbar = false;
		form.Show();
		Application.DoEvents();
		form.PerformLayout();
		using var bitmap = new Bitmap(form.Width, form.Height);
		form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
		bitmap.Save(path, ImageFormat.Png);
		form.Hide();
	}
}
