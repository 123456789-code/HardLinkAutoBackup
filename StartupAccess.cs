namespace HardLinkAutoBackup;

internal static class StartupAccess {
	public static bool EnsureDataAccess(Action check, bool administrator, Func<bool> confirm_elevation, Action restart, Action<string> report) {
		try { check(); return true; }
		catch (Exception exception) when (exception is UnauthorizedAccessException || exception.HResult == unchecked((int)0x80070005)) {
			if (administrator) {
				report("已经以管理员身份运行，数据目录仍不可写。请检查目录访问权限，或将整个程序文件夹移到自己的可写目录。\r\n\r\n" + exception.Message);
				return false;
			}
			if (!confirm_elevation())
				return false;
			try { restart(); }
			catch (System.ComponentModel.Win32Exception cancelled) when (cancelled.NativeErrorCode == 1223) {
				report("已取消管理员授权，程序未启动。可以重新打开并授权，或将整个程序文件夹移到自己的可写目录后运行。");
			}
			return false;
		}
	}
}
