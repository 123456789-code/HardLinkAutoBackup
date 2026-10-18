using System.ComponentModel;

namespace HardLinkAutoBackup;

internal sealed class BackupEngine(string data_directory) {
	private readonly string DataDirectory = data_directory;

	public ScanResult Scan(BackupTask task, IReadOnlyList<BackupTask> tasks, CancellationToken cancellation, IProgress<int>? progress = null) {
		var result = new ScanResult();
		try {
			PathRules.Validate(task, tasks);
			Directory.CreateDirectory(task.Destination);
			var directories = new Stack<string>();
			directories.Push(task.Source);
			int visited = 0;
			while (directories.TryPop(out string? directory)) {
				cancellation.ThrowIfCancellationRequested();
				try {
					PathRules.RejectReparseAncestors(directory);
					foreach (string entry in Directory.EnumerateFileSystemEntries(directory)) {
						cancellation.ThrowIfCancellationRequested();
						string relative = Path.GetRelativePath(task.Source, entry);
						try {
							if (IsExcluded(task, entry, relative)) { result.Skipped++; continue; }
							FileAttributes attributes = File.GetAttributes(entry);
							if ((attributes & FileAttributes.ReparsePoint) != 0) { result.Skipped++; continue; }
							if ((attributes & FileAttributes.Directory) != 0) { directories.Push(entry); continue; }
							ProcessFile(task, entry, relative, result);
						}
						catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception) {
							result.AddError($"{relative}：{exception.Message}");
						}
						visited++;
						if (visited % 100 == 0)
							progress?.Report(visited);
					}
				}
				catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
					result.AddError($"{directory}：{exception.Message}");
				}
			}
		}
		catch (OperationCanceledException) { result.Cancelled = true; }
		catch (Exception exception) { result.AddError(exception.Message); }
		return result;
	}

	private bool IsExcluded(BackupTask task, string path, string relative) {
		if (PathRules.Contains(DataDirectory, path))
			return true;
		foreach (string exclusion in task.Exclusions) {
			string normalized = exclusion.Replace('/', '\\').TrimEnd('\\');
			if (relative.Equals(normalized, StringComparison.OrdinalIgnoreCase)
				|| relative.StartsWith(normalized + "\\", StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	private static void ProcessFile(BackupTask task, string source, string relative, ScanResult result) {
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
		Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
		if (File.Exists(destination)) {
			if (NativeFiles.Identity(destination) != identity)
				throw new IOException("冲突保存位置存在不同文件，已保留原文件。请检查目标目录。");
			result.Existing++;
		}
		else {
			NativeFiles.Link(destination, source);
			result.Created++;
		}
		if (conflict)
			result.Conflicts++;
		if (NativeFiles.Identity(destination) != identity || NativeFiles.Identity(source) != identity)
			throw new IOException("源文件在建链过程中被替换，将在后续扫描重试。");
	}
}
