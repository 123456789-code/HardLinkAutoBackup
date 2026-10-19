namespace HardLinkAutoBackup;

// FileSystemWatcher 回调来自线程池；调度和状态更新只在 UI 线程执行。
internal sealed class ChangeBuffer {
	private readonly Lock Gate = new();
	private readonly HashSet<string> Paths = new(StringComparer.OrdinalIgnoreCase);
	private DateTimeOffset First;
	private DateTimeOffset Last;
	private bool Full;

	public void Add(string? path) {
		lock (Gate) {
			if (Paths.Count == 0 && !Full)
				First = DateTimeOffset.Now;
			Last = DateTimeOffset.Now;
			if (path is null || Paths.Count >= 1024) { Full = true; Paths.Clear(); }
			else if (!Full)
				Paths.Add(path);
		}
	}

	public bool IsDue(DateTimeOffset now) {
		lock (Gate) {
			return (Full || Paths.Count > 0) && ((now - Last).TotalSeconds >= 2 || (now - First).TotalSeconds >= 15);
		}
	}

	public string[]? Drain(bool full = false) {
		lock (Gate) {
			string[]? result = full || Full ? null : Paths.ToArray();
			Full = false;
			Paths.Clear();
			return result;
		}
	}
}
