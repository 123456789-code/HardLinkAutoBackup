using System.ComponentModel;
using System.Diagnostics;

namespace HardLinkAutoBackup;

internal sealed class BackupEngine(string data_directory) {
	private readonly string DataDirectory = data_directory;

	public ScanResult Scan(BackupTask task, IReadOnlyList<BackupTask> tasks, CancellationToken cancellation, IProgress<int>? progress = null, IReadOnlyList<string>? changes = null) {
		var result = new ScanResult { FullScan = changes is null };
		var stopwatch = Stopwatch.StartNew();
		long last_progress = 0;
		try {
			cancellation.ThrowIfCancellationRequested();
			PathRules.Validate(task, tasks);
			result.VolumeIdentity = task.VolumeIdentity;
			if (PathRules.Contains(task.Destination, DataDirectory) || PathRules.Contains(DataDirectory, task.Destination))
				throw new IOException("备份目录不能与程序 data 目录相同或互相嵌套。");
			cancellation.ThrowIfCancellationRequested();
			Directory.CreateDirectory(task.Destination);
			var pending = new Stack<string>();
			IEnumerable<string> roots = changes is null ? [task.Source] : changes;
			// 合并父子路径，目录重命名只需遍历一次。
			var selected = new List<string>();
			foreach (string root in roots.OrderBy(path => path.Length)) {
				string path = Path.GetFullPath(root);
				if (!PathRules.Contains(task.Source, path) || selected.Any(parent => PathRules.Contains(parent, path)))
					continue;
				selected.Add(path);
				pending.Push(path);
			}
			while (pending.TryPop(out string? entry)) {
				cancellation.ThrowIfCancellationRequested();
				try {
					if (PathRules.IsExcluded(task, entry, DataDirectory)) { result.Skipped++; continue; }
					FileAttributes attributes = File.GetAttributes(entry);
					if ((attributes & FileAttributes.ReparsePoint) != 0) { result.Skipped++; continue; }
					PathRules.RejectReparseAncestors(entry);
					if ((attributes & FileAttributes.Directory) != 0) {
						// 枚举器逐项读取，避免单个大目录额外分配完整数组。
						foreach (string child in Directory.EnumerateFileSystemEntries(entry)) {
							cancellation.ThrowIfCancellationRequested();
							try {
								if ((File.GetAttributes(child) & FileAttributes.Directory) != 0)
									pending.Push(child);
								else
									ProcessEntry(child);
							}
							catch (FileNotFoundException) { result.Skipped++; }
							catch (DirectoryNotFoundException) { result.Skipped++; }
							catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception) {
								result.AddError($"{child}：{exception.Message}");
							}
						}
					}
					else
						ProcessEntry(entry);
				}
				catch (FileNotFoundException) { result.Skipped++; }
				catch (DirectoryNotFoundException) { result.Skipped++; }
				catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception) {
					result.AddError($"{entry}：{exception.Message}");
				}
			}
		}
		catch (OperationCanceledException) { result.Cancelled = true; }
		catch (Exception exception) { result.AddError(exception.Message); }
		result.DurationSeconds = stopwatch.Elapsed.TotalSeconds;
		return result;

		void ProcessEntry(string source) {
			cancellation.ThrowIfCancellationRequested();
			try {
				if (PathRules.IsExcluded(task, source, DataDirectory) || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) {
					result.Skipped++;
					return;
				}
				ProcessFile(task, source, Path.GetRelativePath(task.Source, source), result);
			}
			catch (FileNotFoundException) { result.Skipped++; }
			catch (DirectoryNotFoundException) { result.Skipped++; }
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception) {
				result.AddError($"{source}：{exception.Message}");
			}
			result.Visited++;
			if (stopwatch.ElapsedMilliseconds - last_progress >= 150) {
				last_progress = stopwatch.ElapsedMilliseconds;
				progress?.Report(result.Visited);
			}
		}
	}

	private static void ProcessFile(BackupTask task, string source, string relative, ScanResult result) {
		PathRules.RejectReparseAncestors(source);
		string identity = NativeFiles.Identity(source);
		string destination = Path.Combine(task.Destination, relative);
		PathRules.RejectReparseAncestors(destination);
		bool conflict = false;
		bool parent_is_file = false;
		for (string? parent = Path.GetDirectoryName(destination); parent is not null && PathRules.Contains(task.Destination, parent); parent = Path.GetDirectoryName(parent)) {
			if (File.Exists(parent)) { parent_is_file = true; break; }
		}
		if (parent_is_file || Directory.Exists(destination) || (File.Exists(destination) && NativeFiles.Identity(destination) != identity)) {
			conflict = true;
			string conflict_root = Path.Combine(task.Destination, PathRules.CONFLICT_DIRECTORY, task.Id.ToString("N"), identity);
			destination = Path.Combine(conflict_root, relative);
			PathRules.RejectReparseAncestors(destination);
		}
		if (File.Exists(destination)) {
			if (NativeFiles.Identity(destination) != identity)
				throw new IOException("冲突保存位置存在不同文件，已保留原文件。请检查目标目录。");
			result.Existing++;
		}
		else {
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			NativeFiles.Link(destination, source);
			result.Created++;
		}
		if (conflict)
			result.Conflicts++;
		if (NativeFiles.Identity(destination) != identity || NativeFiles.Identity(source) != identity)
			throw new IOException("源文件在建链过程中被替换，将在后续扫描重试。");
	}
}
