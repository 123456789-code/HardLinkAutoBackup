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
			RunCoordinatorChecks(run_root, results);
			File.WriteAllText(Path.Combine(root, "latest-results.txt"), $"测试目录：{run_root}\r\n{string.Join("\r\n", results)}\r\nALL PASSED\r\n", new UTF8Encoding(true));
			return 0;
		}
		catch (Exception exception) {
			File.WriteAllText(Path.Combine(root, "latest-results.txt"), $"测试目录：{run_root}\r\n{string.Join("\r\n", results)}\r\nFAILED: {exception}\r\n", new UTF8Encoding(true));
			return 1;
		}
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
			File.WriteAllText(Path.Combine(source, "watched.txt"), "event");
			await WaitUntilAsync(() => !coordinator.IsRunning && File.Exists(Path.Combine(task.Destination, "watched.txt")), coordinator);
			Check(true, "文件监控触发增量扫描", results);
			File.Delete(Path.Combine(task.Destination, "first.txt"));
			coordinator.State(task).NextRun = DateTimeOffset.Now.AddSeconds(-1);
			await WaitUntilAsync(() => !coordinator.IsRunning && File.Exists(Path.Combine(task.Destination, "first.txt")), coordinator);
			Check(true, "周期扫描补建遗漏链接", results);
			settings.AutomaticEnabled = false;
			coordinator.Reconfigure();
			File.WriteAllText(Path.Combine(source, "manual.txt"), "manual");
			coordinator.Tick();
			await Task.Delay(200);
			Check(!File.Exists(Path.Combine(task.Destination, "manual.txt")), "总开关暂停自动备份", results);
			coordinator.Request(task);
			await WaitUntilAsync(() => !coordinator.IsRunning && File.Exists(Path.Combine(task.Destination, "manual.txt")), coordinator);
			Check(true, "总开关关闭时手动扫描可用", results);
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
