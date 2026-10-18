using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HardLinkAutoBackup;

internal static class NativeFiles {
	[StructLayout(LayoutKind.Sequential)]
	private struct FileInformation {
		public uint Attributes;
		public System.Runtime.InteropServices.ComTypes.FILETIME Created;
		public System.Runtime.InteropServices.ComTypes.FILETIME Accessed;
		public System.Runtime.InteropServices.ComTypes.FILETIME Written;
		public uint Volume;
		public uint SizeHigh;
		public uint SizeLow;
		public uint Links;
		public uint IndexHigh;
		public uint IndexLow;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CreateHardLinkW(string new_path, string existing_path, IntPtr security);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetVolumePathNameW(string path, StringBuilder volume_path, uint length);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetVolumeInformationW(string root, StringBuilder? name, uint name_length, out uint serial, out uint maximum_component, out uint flags, StringBuilder file_system, uint file_system_length);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetVolumeNameForVolumeMountPointW(string root, StringBuilder volume_name, uint length);

	public static string Extended(string path) {
		if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
			return path;
		return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
	}

	public static string Identity(string path) {
		using SafeFileHandle handle = CreateFileW(Extended(path), 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
		if (handle.IsInvalid)
			throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法打开文件：{path}");
		if (!GetFileInformationByHandle(handle, out FileInformation information))
			throw new Win32Exception(Marshal.GetLastWin32Error());
		return $"{information.Volume:X8}-{information.IndexHigh:X8}{information.IndexLow:X8}";
	}

	public static string CanonicalPath(string path) {
		var missing = new Stack<string>();
		string existing = path;
		while (!Directory.Exists(existing) && !File.Exists(existing)) {
			missing.Push(Path.GetFileName(existing));
			existing = Path.GetDirectoryName(existing) ?? throw new IOException("路径没有可访问的父目录。");
		}
		using SafeFileHandle handle = CreateFileW(Extended(existing), 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
		if (handle.IsInvalid)
			throw new Win32Exception(Marshal.GetLastWin32Error());
		var buffer = new StringBuilder(32768);
		uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
		if (length == 0 || length >= buffer.Capacity)
			throw new IOException("无法解析真实路径。");
		string resolved = buffer.ToString();
		if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
			resolved = @"\\" + resolved[8..];
		else if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal))
			resolved = resolved[4..];
		while (missing.TryPop(out string? segment))
			resolved = Path.Combine(resolved, segment);
		return Path.TrimEndingDirectorySeparator(resolved);
	}

	public static string NtfsVolume(string path) {
		var root = new StringBuilder(32768);
		if (!GetVolumePathNameW(path, root, (uint)root.Capacity))
			throw new Win32Exception(Marshal.GetLastWin32Error());
		var file_system = new StringBuilder(64);
		if (!GetVolumeInformationW(root.ToString(), null, 0, out _, out _, out _, file_system, (uint)file_system.Capacity))
			throw new Win32Exception(Marshal.GetLastWin32Error());
		if (!file_system.ToString().Equals("NTFS", StringComparison.OrdinalIgnoreCase))
			throw new IOException("源目录和备份目录必须使用 NTFS。");
		var volume = new StringBuilder(64);
		if (!GetVolumeNameForVolumeMountPointW(root.ToString(), volume, (uint)volume.Capacity))
			throw new Win32Exception(Marshal.GetLastWin32Error());
		return volume.ToString();
	}

	public static void Link(string destination, string source) {
		if (!CreateHardLinkW(Extended(destination), Extended(source), IntPtr.Zero)) {
			int error = Marshal.GetLastWin32Error();
			throw new Win32Exception(error, $"创建硬链接失败（{error}）：{new Win32Exception(error).Message}");
		}
	}
}
