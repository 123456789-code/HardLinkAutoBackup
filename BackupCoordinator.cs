using System.Collections.Concurrent;

namespace HardLinkAutoBackup;

internal sealed class BackupCoordinator {
	private readonly AppSettings Settings;
	private readonly AppStore Store;
	private readonly BackupEngine Engine;
	private readonly Dictionary<Guid, TaskState> States = [];
	private readonly Dictionary<Guid, FileSystemWatcher> Watchers = [];
	private readonly Dictionary<Guid, Task> Runs = [];
	private readonly Dictionary<Guid, ChangeBuffer> Buffers = [];
	private readonly Dictionary<Guid, string> Definitions = [];
	private readonly Dictionary<Guid, DateTimeOffset> WatcherRetries = [];
	private readonly ConcurrentDictionary<Guid, string> BrokenWatchers = [];
	private readonly SemaphoreSlim Slots = new(2);
	private bool Stopping;
	public event Action? Changed;
	public event Action<string>? Attention;
	public bool IsRunning => States.Values.Any(state => state.Running);

	public BackupCoordinator(AppSettings settings, AppStore store) {
		Settings = settings;
		Store = store;
		Engine = new BackupEngine(store.DataDirectory);
	}

	public void RestoreState() {
		foreach ((Guid id, SavedTaskState saved) in Store.LoadState()) {
			if (Settings.Tasks.Any(task => task.Id == id) && saved is not null) {
				States[id] = new TaskState { LastFinished = saved.LastFinished, LastSuccess = saved.LastSuccess, Result = saved.Result };
				if (saved.Result is not null)
					saved.Result.Errors ??= [];
			}
		}
	}

	public TaskState State(BackupTask task) {
		if (!States.TryGetValue(task.Id, out TaskState? state))
			States[task.Id] = state = new TaskState();
		return state;
	}

	public void Reconfigure() {
		foreach (Guid id in Definitions.Keys.Where(id => Settings.Tasks.All(task => task.Id != id)).ToArray()) {
			RemoveWatcher(id);
			Definitions.Remove(id);
			States.Remove(id);
			Buffers.Remove(id);
		}
		foreach (BackupTask task in Settings.Tasks) {
			string key = $"{task.DefinitionKey()}\n{task.Enabled}\n{task.IntervalSeconds}\n{Settings.AutomaticEnabled}";
			if (Definitions.TryGetValue(task.Id, out string? old) && old == key)
				continue;
			Definitions[task.Id] = key;
			RemoveWatcher(task.Id);
			Buffers[task.Id] = new ChangeBuffer();
			TaskState state = State(task);
			state.NextRun = DateTimeOffset.Now;
			state.RetryAfter = DateTimeOffset.MinValue;
			state.WatcherWarning = "";
			if (!state.Running)
				state.Status = IsEnabled(task) ? "等待扫描" : "自动备份已暂停";
		}
		Changed?.Invoke();
	}

	private void RemoveWatcher(Guid id) {
		if (Watchers.Remove(id, out FileSystemWatcher? watcher))
			watcher.Dispose();
		WatcherRetries.Remove(id);
		BrokenWatchers.TryRemove(id, out _);
	}

	private bool IsEnabled(BackupTask task) {
		return Settings.AutomaticEnabled && task.Enabled;
	}

	public void Tick() {
		if (Stopping)
			return;
		foreach (Guid id in Runs.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray())
			Runs.Remove(id);
		foreach (BackupTask task in Settings.Tasks.ToArray()) {
			if (!IsEnabled(task))
				continue;
			TaskState state = State(task);
			if (BrokenWatchers.TryRemove(task.Id, out string? error)) {
				RemoveWatcher(task.Id);
				state.WatcherWarning = error;
				WatcherRetries[task.Id] = DateTimeOffset.Now.AddSeconds(30);
				Buffer(task).Add(null);
				state.NextRun = DateTimeOffset.Now;
				Store.Log($"[{task.Name}] 文件监控中断，将完整扫描补漏：{error}");
			}
			EnsureWatcher(task);
			DateTimeOffset now = DateTimeOffset.Now;
			if (!state.Running && now >= state.RetryAfter) {
				if (state.NextRun <= now)
					Start(task, null);
				else if (Buffer(task).IsDue(now))
					Start(task, Buffer(task).Drain());
			}
		}
	}

	private ChangeBuffer Buffer(BackupTask task) {
		if (!Buffers.TryGetValue(task.Id, out ChangeBuffer? buffer))
			Buffers[task.Id] = buffer = new ChangeBuffer();
		return buffer;
	}

	private void EnsureWatcher(BackupTask task) {
		if (Watchers.ContainsKey(task.Id) || (WatcherRetries.TryGetValue(task.Id, out DateTimeOffset retry) && retry > DateTimeOffset.Now))
			return;
		FileSystemWatcher? watcher = null;
		try {
			PathRules.RejectReparseAncestors(task.Source);
			BackupTask snapshot = task.Snapshot();
			ChangeBuffer buffer = Buffer(task);
			watcher = new FileSystemWatcher(task.Source) {
				IncludeSubdirectories = true,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime,
				InternalBufferSize = 16 * 1024
			};
			void MarkDirty(object sender, FileSystemEventArgs args) {
				if (!PathRules.IsExcluded(snapshot, args.FullPath, Store.DataDirectory))
					buffer.Add(args.FullPath);
			}
			watcher.Created += MarkDirty;
			watcher.Renamed += (sender, args) => MarkDirty(sender, args);
			watcher.Changed += MarkDirty;
			watcher.Error += (_, args) => BrokenWatchers[task.Id] = args.GetException().Message;
			watcher.EnableRaisingEvents = true;
			Watchers[task.Id] = watcher;
			WatcherRetries.Remove(task.Id);
			State(task).WatcherWarning = "";
		}
		catch (Exception exception) {
			watcher?.Dispose();
			State(task).WatcherWarning = "变化监控暂不可用，周期扫描继续：" + exception.Message;
			WatcherRetries[task.Id] = DateTimeOffset.Now.AddSeconds(30);
			Store.Log($"[{task.Name}] {State(task).WatcherWarning}");
		}
	}

	public void Request(BackupTask task, bool manual = true) {
		if (Stopping)
			return;
		if (State(task).Running) {
			if (manual)
				State(task).PendingManual = true;
			return;
		}
		Start(task, null);
	}

	private void Start(BackupTask task, string[]? paths) {
		TaskState state = State(task);
		if (paths is null)
			Buffer(task).Drain(true);
		state.Running = true;
		state.Status = "排队中";
		state.Cancellation = new CancellationTokenSource();
		BackupTask snapshot = task.Snapshot();
		BackupTask[] tasks = Settings.Tasks.Select(item => item.Snapshot()).ToArray();
		Runs[task.Id] = RunAsync(snapshot, tasks, state, paths, state.Cancellation.Token);
	}

	private async Task RunAsync(BackupTask task, BackupTask[] tasks, TaskState state, string[]? paths, CancellationToken cancellation) {
		bool entered = false;
		try {
			Changed?.Invoke();
			await Slots.WaitAsync(cancellation);
			entered = true;
			state.Status = paths is null ? "完整扫描中" : "变化扫描中";
			Changed?.Invoke();
			var progress = new Progress<int>(count => {
				if (!state.Running || cancellation.IsCancellationRequested || state.Cancellation?.Token != cancellation)
					return;
				state.Status = $"扫描中 · 已检查 {count:N0} 项";
				Changed?.Invoke();
			});
			ScanResult result = await Task.Run(() => Engine.Scan(task, tasks, cancellation, progress, paths));
			state.Result = result;
			BackupTask? live = Settings.Tasks.FirstOrDefault(item => item.Id == task.Id);
			if (live is not null && live.VolumeIdentity is null && result.VolumeIdentity is not null) {
				live.VolumeIdentity = result.VolumeIdentity;
				Definitions[live.Id] = $"{live.DefinitionKey()}\n{live.Enabled}\n{live.IntervalSeconds}\n{Settings.AutomaticEnabled}";
				try { Store.Save(Settings); }
				catch (Exception exception) {
					live.VolumeIdentity = null;
					result.AddError("保存磁盘卷绑定失败：" + exception.Message);
				}
			}
			state.Status = result.Cancelled ? "已取消" : result.Failed > 0 ? $"有 {result.Failed} 项失败" : result.Conflicts > 0 ? "完成 · 有异名保存" : "完成";
			if (!result.Cancelled && result.Failed == 0)
				state.LastSuccess = DateTimeOffset.Now;
			Store.Log($"[{task.Name}] {state.Status}：{result.Summary}");
			foreach (string error in result.Errors)
				Store.Log($"[{task.Name}] {error}");
			if (result.Failed > 0 && !Stopping)
				Attention?.Invoke($"{task.Name}：{result.Failed} 项失败，打开任务详情查看。");
		}
		catch (OperationCanceledException) { state.Status = "已取消"; state.Result = new ScanResult { Cancelled = true }; }
		catch (Exception exception) {
			state.Status = "扫描失败";
			Store.Log($"[{task.Name}] {exception}");
			state.Result = new ScanResult();
			state.Result.AddError(exception.Message);
		}
		finally {
			if (entered)
				Slots.Release();
			state.Running = false;
			state.LastFinished = DateTimeOffset.Now;
			if (paths is null || state.Result?.Cancelled == true)
				state.NextRun = DateTimeOffset.Now.AddSeconds(task.IntervalSeconds);
			if (state.Result?.Failed > 0) {
				state.RetryAfter = DateTimeOffset.Now.AddSeconds(60);
				state.NextRun = state.RetryAfter;
			}
			else
				state.RetryAfter = DateTimeOffset.MinValue;
			if (state.Result?.Cancelled == true) {
				Buffer(task).Drain(true);
				state.RetryAfter = state.NextRun;
			}
			state.Cancellation?.Dispose();
			state.Cancellation = null;
			Store.SaveState(States.ToDictionary(pair => pair.Key, pair => new SavedTaskState {
				LastFinished = pair.Value.LastFinished,
				LastSuccess = pair.Value.LastSuccess,
				Result = pair.Value.Result
			}));
			Changed?.Invoke();
			if (state.PendingManual && !Stopping) {
				SynchronizationContext.Current?.Post(_ => {
					if (!state.PendingManual || Stopping)
						return;
					state.PendingManual = false;
					BackupTask? live = Settings.Tasks.FirstOrDefault(item => item.Id == task.Id);
					if (live is not null)
						Request(live);
				}, null);
			}
		}
	}

	public void Resume() {
		foreach (BackupTask task in Settings.Tasks) {
			if (!IsEnabled(task))
				continue;
			RemoveWatcher(task.Id);
			Buffer(task).Add(null);
			State(task).NextRun = DateTimeOffset.Now;
			State(task).RetryAfter = DateTimeOffset.MinValue;
		}
	}

	public void Cancel(BackupTask task) {
		TaskState state = State(task);
		state.PendingManual = false;
		Buffer(task).Drain(true);
		state.NextRun = DateTimeOffset.Now.AddSeconds(task.IntervalSeconds);
		state.RetryAfter = state.NextRun;
		state.Cancellation?.Cancel();
	}

	public async Task StopAsync() {
		if (Stopping)
			return;
		Stopping = true;
		foreach (FileSystemWatcher watcher in Watchers.Values)
			watcher.Dispose();
		Watchers.Clear();
		foreach (TaskState state in States.Values)
			state.Cancellation?.Cancel();
		await Task.WhenAll(Runs.Values.ToArray());
		Slots.Dispose();
	}
}
