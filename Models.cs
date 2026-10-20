namespace HardLinkAutoBackup;

public sealed class AppSettings {
	public int Version { get; set; } = 1;
	public bool StartAtLogon { get; set; }
	public bool RunAsAdministrator { get; set; }
	public bool AutomaticEnabled { get; set; } = true;
	public bool StayInTray { get; set; } = true;
	public bool HideUsageReminder { get; set; }
	public List<BackupTask> Tasks { get; set; } = [];
}

public sealed class BackupTask {
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Name { get; set; } = "新任务";
	public string Source { get; set; } = "";
	public string Destination { get; set; } = "";
	public string? VolumeIdentity { get; set; }
	public bool Enabled { get; set; } = true;
	public int IntervalSeconds { get; set; } = 3600;
	public List<string> Exclusions { get; set; } = [];

	public BackupTask Snapshot() {
		return new BackupTask {
			Id = Id,
			Name = Name,
			Source = Source,
			Destination = Destination,
			VolumeIdentity = VolumeIdentity,
			Enabled = Enabled,
			IntervalSeconds = IntervalSeconds,
			Exclusions = [.. Exclusions]
		};
	}

	public string DefinitionKey(bool automatic_enabled) {
		return $"{Source}\n{Destination}\n{VolumeIdentity}\n{string.Join("\n", Exclusions)}\n{Enabled}\n{IntervalSeconds}\n{automatic_enabled}";
	}
}

internal sealed class TaskState {
	public bool Running { get; set; }
	public bool PendingManual { get; set; }
	public string Status { get; set; } = "等待首次扫描";
	public string WatcherWarning { get; set; } = "";
	public DateTimeOffset? LastFinished { get; set; }
	public DateTimeOffset? LastSuccess { get; set; }
	public DateTimeOffset NextRun { get; set; } = DateTimeOffset.Now;
	public DateTimeOffset RetryAfter { get; set; } = DateTimeOffset.MinValue;
	public ScanResult? Result { get; set; }
	public CancellationTokenSource? Cancellation { get; set; }
}

internal sealed class ScanResult {
	public bool FullScan { get; set; } = true;
	public int Visited { get; set; }
	public int Created { get; set; }
	public int Existing { get; set; }
	public int Conflicts { get; set; }
	public int Skipped { get; set; }
	public int Failed { get; set; }
	public bool Cancelled { get; set; }
	public double DurationSeconds { get; set; }
	public string? VolumeIdentity { get; set; }
	public List<string> Errors { get; set; } = [];
	public string Summary => $"{(FullScan ? "完整扫描" : "变化扫描")} · 检查 {Visited} · 新增 {Created} · 已存在 {Existing} · 异名 {Conflicts} · 跳过 {Skipped} · 失败 {Failed} · {DurationSeconds:F1} 秒";

	public void AddError(string message) {
		Failed++;
		if (Errors.Count < 100)
			Errors.Add(message);
	}
}

internal sealed class SavedTaskState {
	public DateTimeOffset? LastFinished { get; set; }
	public DateTimeOffset? LastSuccess { get; set; }
	public ScanResult? Result { get; set; }
}
