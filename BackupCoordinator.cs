using System.Collections.Concurrent;

namespace HardLinkAutoBackup;

internal sealed class BackupCoordinator(AppSettings settings, AppStore store) {
	private readonly AppSettings Settings = settings;
	private readonly AppStore Store = store;
	private readonly BackupEngine Engine = new(store.DataDirectory);
	private readonly Dictionary<Guid, TaskState> States = [];
	private readonly Dictionary<Guid, FileSystemWatcher> Watchers = [];
	private readonly Dictionary<Guid, Task> Runs = [];
	private readonly ConcurrentDictionary<Guid, (DateTimeOffset First, DateTimeOffset Last)> Dirty = [];
	private readonly ConcurrentDictionary<Guid, byte> BrokenWatchers = [];
	private readonly SemaphoreSlim Slots = new(2);
	private bool Stopping;
	public event Action? Changed;
	public event Action<string>? Attention;
	public bool IsRunning => States.Values.Any(state => state.Running);

	public TaskState State(BackupTask task) {
		if (!States.TryGetValue(task.Id, out TaskState? state))
			States[task.Id] = state = new TaskState();
		return state;
	}

	public void Reconfigure() {
		foreach (FileSystemWatcher watcher in Watchers.Values)
			watcher.Dispose();
		Watchers.Clear();
		Dirty.Clear();
		BrokenWatchers.Clear();
		foreach (BackupTask task in Settings.Tasks) {
			TaskState state = State(task);
			state.NextRun = DateTimeOffset.Now;
			if (!state.Running)
				state.Status = IsEnabled(task) ? "等待扫描" : "自动备份已暂停";
		}
		Changed?.Invoke();
	}

	private bool IsEnabled(BackupTask task) {
		return Settings.AutomaticEnabled && task.Enabled;
	}

	public void Tick() {
		if (Stopping)
			return;
		foreach (BackupTask task in Settings.Tasks.ToArray()) {
			if (!IsEnabled(task))
				continue;
			if (BrokenWatchers.TryRemove(task.Id, out _) && Watchers.Remove(task.Id, out FileSystemWatcher? broken))
				broken.Dispose();
			EnsureWatcher(task);
			TaskState state = State(task);
			DateTimeOffset now = DateTimeOffset.Now;
			bool dirty = Dirty.TryGetValue(task.Id, out var times)
				&& ((now - times.Last).TotalSeconds >= 2 || (now - times.First).TotalSeconds >= 15);
			if (!state.Running && (state.NextRun <= now || dirty))
				Request(task, false);
		}
	}

	private void EnsureWatcher(BackupTask task) {
		if (Watchers.ContainsKey(task.Id) || !Directory.Exists(task.Source))
			return;
		try {
			var watcher = new FileSystemWatcher(task.Source) {
				IncludeSubdirectories = true,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime,
				InternalBufferSize = 16 * 1024
			};
			watcher.Created += (_, args) => MarkDirty(task.Id, args.FullPath);
			watcher.Deleted += (_, args) => MarkDirty(task.Id, args.FullPath);
			watcher.Renamed += (_, args) => MarkDirty(task.Id, args.FullPath);
			watcher.Changed += (_, args) => MarkDirty(task.Id, args.FullPath);
			watcher.Error += (_, args) => {
				Store.Log($"[{task.Name}] 文件监控错误，将由完整扫描补漏：{args.GetException().Message}");
				Dirty[task.Id] = (DateTimeOffset.MinValue, DateTimeOffset.MinValue);
				BrokenWatchers[task.Id] = 1;
			};
			watcher.EnableRaisingEvents = true;
			Watchers[task.Id] = watcher;
		}
		catch (Exception exception) {
			Store.Log($"[{task.Name}] 无法建立监控：{exception.Message}");
		}
	}

	private void MarkDirty(Guid id, string path) {
		if (PathRules.Contains(Store.DataDirectory, path))
			return;
		DateTimeOffset now = DateTimeOffset.Now;
		Dirty.AddOrUpdate(id, (now, now), (_, old) => (old.First, now));
	}

	public void Request(BackupTask task, bool manual = true) {
		if (Stopping)
			return;
		TaskState state = State(task);
		if (state.Running) {
			if (manual)
				state.PendingManual = true;
			return;
		}
		Dirty.TryRemove(task.Id, out _);
		state.Running = true;
		state.Status = "排队中";
		state.Cancellation = new CancellationTokenSource();
		Runs[task.Id] = RunAsync(task, state, state.Cancellation.Token);
	}

	private async Task RunAsync(BackupTask task, TaskState state, CancellationToken cancellation) {
		bool entered = false;
		try {
			Changed?.Invoke();
			await Slots.WaitAsync(cancellation);
			entered = true;
			state.Status = "扫描中";
			Changed?.Invoke();
			var progress = new Progress<int>(count => {
				if (!state.Running)
					return;
				state.Status = $"扫描中 · 已检查 {count:N0} 项";
				Changed?.Invoke();
			});
			BackupTask[] tasks = Settings.Tasks.ToArray();
			ScanResult result = await Task.Run(() => Engine.Scan(task, tasks, cancellation, progress));
			state.Result = result;
			state.Status = result.Cancelled ? "已取消" : result.Failed > 0 ? $"有 {result.Failed} 项失败" : result.Conflicts > 0 ? "完成 · 有异名保存" : "完成";
			Store.Log($"[{task.Name}] {state.Status}：{result.Summary}");
			foreach (string error in result.Errors)
				Store.Log($"[{task.Name}] {error}");
			if (result.Failed > 0 && !Stopping)
				Attention?.Invoke($"{task.Name}：{result.Failed} 项失败，打开任务详情查看。");
		}
		catch (OperationCanceledException) { state.Status = "已取消"; }
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
			state.NextRun = DateTimeOffset.Now.AddSeconds(task.IntervalSeconds);
			state.Cancellation?.Dispose();
			state.Cancellation = null;
			Changed?.Invoke();
			if (state.PendingManual && !Stopping) {
				state.PendingManual = false;
				Dirty[task.Id] = (DateTimeOffset.MinValue, DateTimeOffset.MinValue);
				// 将手动追加请求留到 UI 消息循环，避免覆盖当前 Run 的登记。
				SynchronizationContext.Current?.Post(_ => Request(task), null);
			}
		}
	}

	public void Cancel(BackupTask task) {
		TaskState state = State(task);
		state.PendingManual = false;
		state.Cancellation?.Cancel();
	}

	public async Task StopAsync() {
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
