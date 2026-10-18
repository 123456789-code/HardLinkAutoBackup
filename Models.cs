namespace HardLinkAutoBackup;

public sealed class AppSettings {
	public int Version { get; set; } = 1;
	public bool StartAtLogon { get; set; }
	public bool RunAsAdministrator { get; set; }
	public bool AutomaticEnabled { get; set; } = true;
	public bool StayInTray { get; set; } = true;
	public List<BackupTask> Tasks { get; set; } = [];
}

public sealed class BackupTask {
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Name { get; set; } = "新任务";
	public string Source { get; set; } = "";
	public string Destination { get; set; } = "";
	public bool Enabled { get; set; } = true;
	public int IntervalSeconds { get; set; } = 3600;
	public List<string> Exclusions { get; set; } = [];
}

internal sealed class TaskState {
	public bool Running { get; set; }
	public bool PendingManual { get; set; }
	public string Status { get; set; } = "等待首次扫描";
	public DateTimeOffset? LastFinished { get; set; }
	public DateTimeOffset NextRun { get; set; } = DateTimeOffset.Now;
	public ScanResult? Result { get; set; }
	public CancellationTokenSource? Cancellation { get; set; }
}

internal sealed class ScanResult {
	public int Created { get; set; }
	public int Existing { get; set; }
	public int Conflicts { get; set; }
	public int Skipped { get; set; }
	public int Failed { get; set; }
	public bool Cancelled { get; set; }
	public List<string> Errors { get; } = [];
	public string Summary => $"新增 {Created} · 已存在 {Existing} · 异名保存 {Conflicts} · 跳过 {Skipped} · 失败 {Failed}";

	public void AddError(string message) {
		Failed++;
		if (Errors.Count < 100)
			Errors.Add(message);
	}
}
