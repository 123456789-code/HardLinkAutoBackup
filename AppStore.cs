using System.Text;
using System.Text.Json;

namespace HardLinkAutoBackup;

internal sealed class AppStore {
	private readonly Lock LogLock = new();
	private readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
	public string DataDirectory { get; }
	public string ConfigurationPath => Path.Combine(DataDirectory, "settings.json");
	public string LogPath => Path.Combine(DataDirectory, "activity.log");

	public AppStore(string? data_directory = null) {
		DataDirectory = data_directory ?? Path.Combine(AppContext.BaseDirectory, "data");
	}

	public AppSettings Load() {
		Directory.CreateDirectory(DataDirectory);
		if (!File.Exists(ConfigurationPath))
			return new AppSettings();
		AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigurationPath))
			?? throw new InvalidDataException("配置为空。请检查 settings.json，原文件已保留。");
		if (settings.Version != 1 || settings.Tasks is null || settings.Tasks.Count > 100)
			throw new InvalidDataException("不支持的配置版本或任务数量。原配置已保留。");
		if (settings.Tasks.Select(task => task.Id).Distinct().Count() != settings.Tasks.Count
			|| settings.Tasks.Any(task => task.IntervalSeconds < 10 || task.IntervalSeconds > 31_536_000 || task.Exclusions is null
				|| string.IsNullOrWhiteSpace(task.Source) || string.IsNullOrWhiteSpace(task.Destination)))
			throw new InvalidDataException("配置含重复任务 ID 或无效周期。原配置已保留。");
		return settings;
	}

	public void Save(AppSettings settings) {
		Directory.CreateDirectory(DataDirectory);
		string temporary_path = ConfigurationPath + ".tmp";
		File.WriteAllText(temporary_path, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(true));
		if (File.Exists(ConfigurationPath))
			File.Replace(temporary_path, ConfigurationPath, ConfigurationPath + ".bak");
		else
			File.Move(temporary_path, ConfigurationPath);
	}

	public void Log(string message) {
		lock (LogLock) {
			try {
				Directory.CreateDirectory(DataDirectory);
				if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2 * 1024 * 1024)
					File.Move(LogPath, LogPath + ".previous", true);
				File.AppendAllText(LogPath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  {message}{Environment.NewLine}", Encoding.UTF8);
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
		}
	}
}
