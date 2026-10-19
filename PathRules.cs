namespace HardLinkAutoBackup;

internal static class PathRules {
	public const string CONFLICT_DIRECTORY = ".hlab-conflicts";

	public static string Normalize(string path) {
		if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
			throw new IOException("请选择本地磁盘上的绝对路径。");
		string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
		string suffix = normalized[Path.GetPathRoot(normalized)!.Length..];
		foreach (string segment in suffix.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) {
			if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.EndsWith('.') || segment.EndsWith(' '))
				throw new IOException("目录路径含不支持的字符或结尾空格/句点。");
		}
		return normalized;
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

	public static string NormalizeExclusion(string value) {
		if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
			throw new IOException("排除项必须是非空的相对路径。");
		string normalized = value.Trim().Replace('/', '\\').TrimEnd('\\');
		string[] segments = normalized.Split('\\');
		if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."
			|| segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.EndsWith('.') || segment.EndsWith(' ')))
			throw new IOException("排除项不支持通配符、空路径段、. 或 ..。");
		return normalized;
	}

	public static bool IsExcluded(BackupTask task, string path, string data_directory) {
		if (Contains(data_directory, path))
			return true;
		string relative = Path.GetRelativePath(task.Source, path);
		return task.Exclusions.Any(exclusion => relative.Equals(exclusion, StringComparison.OrdinalIgnoreCase)
			|| relative.StartsWith(exclusion + "\\", StringComparison.OrdinalIgnoreCase));
	}

	public static void ValidateDefinition(BackupTask task) {
		if (task.Id == Guid.Empty || string.IsNullOrWhiteSpace(task.Name) || task.Name.Length > 100)
			throw new IOException("任务 ID 或名称无效，名称最多 100 字。");
		task.Source = Normalize(task.Source);
		task.Destination = Normalize(task.Destination);
		if (Contains(task.Source, task.Destination) || Contains(task.Destination, task.Source))
			throw new IOException("源目录与备份目录不能相同或互相嵌套。");
		if (task.IntervalSeconds < 10 || task.IntervalSeconds > 31_536_000)
			throw new IOException("扫描周期应在 10 秒到 365 天之间。");
		if (task.Exclusions is null || task.Exclusions.Count > 1000)
			throw new IOException("排除规则无效或超过 1000 条。");
		task.Exclusions = task.Exclusions.Select(NormalizeExclusion).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static string CanonicalIfAvailable(string path) {
		string normalized = Normalize(path);
		try { return NativeFiles.CanonicalPath(normalized); }
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) {
			return normalized;
		}
	}

	public static void Validate(BackupTask task, IEnumerable<BackupTask> tasks) {
		ValidateDefinition(task);
		RejectReparseAncestors(task.Source);
		RejectReparseAncestors(task.Destination);
		task.Source = NativeFiles.CanonicalPath(task.Source);
		task.Destination = NativeFiles.CanonicalPath(task.Destination);
		if (!Directory.Exists(task.Source))
			throw new DirectoryNotFoundException("源目录不存在或不可访问。");
		if (Contains(task.Source, task.Destination) || Contains(task.Destination, task.Source))
			throw new IOException("源目录与备份目录不能相同或互相嵌套。");
		if (File.Exists(task.Destination))
			throw new IOException("备份路径指向文件，请选择目录。");
		string existing_parent = task.Destination;
		while (!Directory.Exists(existing_parent))
			existing_parent = Path.GetDirectoryName(existing_parent) ?? throw new IOException("备份目录没有可访问的父目录。");
		string volume = NativeFiles.NtfsVolume(task.Source);
		if (!volume.Equals(NativeFiles.NtfsVolume(existing_parent), StringComparison.OrdinalIgnoreCase))
			throw new IOException("硬链接不支持跨卷，请选择同一 NTFS 卷。");
		if (task.VolumeIdentity is not null && !task.VolumeIdentity.Equals(volume, StringComparison.OrdinalIgnoreCase))
			throw new IOException("目录对应的磁盘卷已改变。请核对磁盘后编辑任务，重新绑定该卷。");
		task.VolumeIdentity = volume;
		foreach (BackupTask other in tasks.Where(other => other.Id != task.Id)) {
			string other_source = CanonicalIfAvailable(other.Source);
			string other_destination = CanonicalIfAvailable(other.Destination);
			if (Contains(task.Destination, other_destination) || Contains(other_destination, task.Destination)
				|| Contains(task.Source, other_destination) || Contains(other_destination, task.Source)
				|| Contains(other_source, task.Destination) || Contains(task.Destination, other_source))
				throw new IOException($"路径与任务“{other.Name}”的源或备份目录形成冲突或嵌套。");
		}
		if (Directory.Exists(Path.Combine(task.Source, CONFLICT_DIRECTORY)) || File.Exists(Path.Combine(task.Source, CONFLICT_DIRECTORY)))
			throw new IOException($"源目录顶层不能含程序保留名称 {CONFLICT_DIRECTORY}。");
	}
}
