namespace HardLinkAutoBackup;

internal static class PathRules {
	public const string CONFLICT_DIRECTORY = ".hlab-conflicts";

	public static string Normalize(string path) {
		return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
	}

	public static bool Contains(string parent, string child) {
		parent = Path.TrimEndingDirectorySeparator(parent);
		child = Path.TrimEndingDirectorySeparator(child);
		return child.Equals(parent, StringComparison.OrdinalIgnoreCase)
			|| child.StartsWith(parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	public static void RejectReparseAncestors(string path) {
		for (string? current = path; current is not null; current = Path.GetDirectoryName(current)) {
			try {
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
					throw new IOException($"路径经过符号链接或联接点，暂不支持：{current}");
			}
			catch (FileNotFoundException) { }
			catch (DirectoryNotFoundException) { }
		}
	}

	public static void Validate(BackupTask task, IEnumerable<BackupTask> tasks) {
		task.Source = Normalize(task.Source);
		task.Destination = Normalize(task.Destination);
		RejectReparseAncestors(task.Source);
		RejectReparseAncestors(task.Destination);
		task.Source = NativeFiles.CanonicalPath(task.Source);
		task.Destination = NativeFiles.CanonicalPath(task.Destination);
		if (!Path.IsPathFullyQualified(task.Source) || task.Source.StartsWith(@"\\", StringComparison.Ordinal)
			|| task.Destination.StartsWith(@"\\", StringComparison.Ordinal))
			throw new IOException("请选择本地磁盘上的绝对路径。");
		if (!Directory.Exists(task.Source))
			throw new DirectoryNotFoundException("源目录不存在或不可访问。");
		if (Contains(task.Source, task.Destination) || Contains(task.Destination, task.Source))
			throw new IOException("源目录与备份目录不能相同或互相嵌套。");
		if (task.IntervalSeconds < 10 || task.IntervalSeconds > 31_536_000)
			throw new IOException("扫描周期应在 10 秒到 365 天之间。");
		RejectReparseAncestors(task.Source);
		RejectReparseAncestors(task.Destination);
		if (File.Exists(task.Destination))
			throw new IOException("备份路径指向文件，请选择目录。");
		string existing_parent = task.Destination;
		while (!Directory.Exists(existing_parent))
			existing_parent = Path.GetDirectoryName(existing_parent) ?? throw new IOException("备份目录没有可访问的父目录。");
		if (!NativeFiles.NtfsVolume(task.Source).Equals(NativeFiles.NtfsVolume(existing_parent), StringComparison.OrdinalIgnoreCase))
			throw new IOException("硬链接不支持跨卷，请在源目录所在 NTFS 卷内选择备份目录。");
		foreach (BackupTask other in tasks.Where(other => other.Id != task.Id)) {
			if (Contains(task.Destination, other.Destination) || Contains(other.Destination, task.Destination)
				|| Contains(task.Source, other.Destination) || Contains(other.Destination, task.Source)
				|| Contains(other.Source, task.Destination) || Contains(task.Destination, other.Source))
				throw new IOException($"路径与任务“{other.Name}”的源或备份目录形成冲突或嵌套。");
		}
		if (Directory.Exists(Path.Combine(task.Source, CONFLICT_DIRECTORY)) || File.Exists(Path.Combine(task.Source, CONFLICT_DIRECTORY)))
			throw new IOException($"源目录顶层不能含程序保留名称 {CONFLICT_DIRECTORY}。");
		foreach (string exclusion in task.Exclusions) {
			if (Path.IsPathRooted(exclusion) || exclusion.Split('\\', '/').Contains(".."))
				throw new IOException("排除项必须为源目录内的相对路径，不能包含 ..。");
		}
	}
}
