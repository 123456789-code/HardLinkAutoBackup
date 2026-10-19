using System.Text;
using System.Text.Json;

namespace HardLinkAutoBackup;

internal sealed class AppStore {
	private readonly Lock LogLock = new();
	private readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
	public string DataDirectory { get; }
	public string ConfigurationPath => Path.Combine(DataDirectory, "settings.json");
	public string LogPath => Path.Combine(DataDirectory, "activity.log");
	public string? LastLogError { get; private set; }

	public AppStore(string? data_directory = null) {
		DataDirectory = Path.GetFullPath(data_directory ?? Path.Combine(AppContext.BaseDirectory, "data"));
	}

	public AppSettings Load() {
		Directory.CreateDirectory(DataDirectory);
		return File.Exists(ConfigurationPath) ? ReadSettings(ConfigurationPath) : new AppSettings();
	}

	public AppSettings ReadSettings(string path) {
		if (new FileInfo(path).Length > 4 * 1024 * 1024)
			throw new InvalidDataException("配置文件超过 4 MB。");
		AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))
			?? throw new InvalidDataException("配置为空，原文件已保留。");
		Validate(settings);
		return settings;
	}

	public static void Validate(AppSettings settings) {
		if (settings.Version != 1 || settings.Tasks is null || settings.Tasks.Count > 100)
			throw new InvalidDataException("不支持的配置版本或任务数量，原配置已保留。");
		if (settings.Tasks.Any(task => task is null) || settings.Tasks.Select(task => task.Id).Distinct().Count() != settings.Tasks.Count)
			throw new InvalidDataException("配置含空任务或重复任务 ID。");
		foreach (BackupTask task in settings.Tasks)
			PathRules.ValidateDefinition(task);
	}

	public void Save(AppSettings settings) {
		Validate(settings);
		WriteJson(ConfigurationPath, settings);
	}

	private void WriteJson<T>(string path, T value) {
		Directory.CreateDirectory(DataDirectory);
		string temporary_path = path + ".tmp";
		byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions));
		using (var stream = new FileStream(temporary_path, FileMode.Create, FileAccess.Write, FileShare.None)) {
			stream.Write(bytes);
			stream.Flush(true);
		}
		if (File.Exists(path))
			File.Replace(temporary_path, path, path + ".bak");
		else
			File.Move(temporary_path, path);
	}

	public AppSettings RestoreBackup() {
		AppSettings settings = ReadSettings(ConfigurationPath + ".bak");
		string damaged = Path.Combine(DataDirectory, $"settings.damaged-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
		if (File.Exists(ConfigurationPath))
			File.Copy(ConfigurationPath, damaged);
		// 保留已验证的备份，不能让损坏的主文件覆盖它。
		string replacement = ConfigurationPath + ".recovery";
		File.Copy(ConfigurationPath + ".bak", replacement, true);
		File.Move(replacement, ConfigurationPath, true);
		return settings;
	}

	public Dictionary<Guid, SavedTaskState> LoadState() {
		try {
			string path = Path.Combine(DataDirectory, "state.json");
			if (!File.Exists(path))
				return [];
			if (new FileInfo(path).Length > 8 * 1024 * 1024)
				throw new InvalidDataException("状态记录过大。");
			return JsonSerializer.Deserialize<Dictionary<Guid, SavedTaskState>>(File.ReadAllText(path)) ?? [];
		}
		catch (Exception exception) {
			Log($"无法读取上次扫描状态，将重新扫描：{exception.Message}");
			return [];
		}
	}

	public void SaveState(Dictionary<Guid, SavedTaskState> states) {
		try { WriteJson(Path.Combine(DataDirectory, "state.json"), states); }
		catch (Exception exception) { Log($"保存扫描状态失败：{exception.Message}"); }
	}

	public void Log(string message) {
		lock (LogLock) {
			try {
				Directory.CreateDirectory(DataDirectory);
				if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2 * 1024 * 1024)
					File.Move(LogPath, LogPath + ".previous", true);
				File.AppendAllText(LogPath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  {message}{Environment.NewLine}", Encoding.UTF8);
				LastLogError = null;
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { LastLogError = exception.Message; }
		}
	}
}
