namespace HardLinkAutoBackup;

internal sealed class SettingsDialog : Form {
	public CheckBox AutoStart { get; } = new() { Text = "登录 Windows 后自动启动", AutoSize = true };
	public CheckBox Elevated { get; } = new() { Text = "以管理员权限启动（下次启动生效）", AutoSize = true };
	public CheckBox Automatic { get; } = new() { Text = "启用周期备份与文件变化监控", AutoSize = true };
	public CheckBox Tray { get; } = new() { Text = "关闭窗口后在通知区域继续运行", AutoSize = true };

	public CheckBox RepairStartup { get; } = new() { Text = "保存时同步 / 修复自启任务（路径移动或任务被删除时使用）", AutoSize = true };

	public SettingsDialog(AppSettings settings, string data_directory) {
		Text = "程序设置";
		Font = new Font("Microsoft YaHei UI", 10);
		BackColor = UiTheme.Canvas;
		ClientSize = new Size(680, 510);
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterParent;
		ShowInTaskbar = false;
		MaximizeBox = false;
		MinimizeBox = false;
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 9 };
		for (int i = 0; i < 4; i++)
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
		AutoStart.Checked = settings.StartAtLogon;
		Elevated.Checked = settings.RunAsAdministrator;
		Automatic.Checked = settings.AutomaticEnabled;
		Tray.Checked = settings.StayInTray;
		layout.Controls.Add(AutoStart, 0, 0);
		layout.Controls.Add(Elevated, 0, 1);
		layout.Controls.Add(Automatic, 0, 2);
		layout.Controls.Add(Tray, 0, 3);
		layout.Controls.Add(new Label { Text = "自启使用当前用户的登录计划任务，保存前会展示修改内容。\r\n普通文件夹一般无需管理员权限；手动退出后自动备份停止。", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }, 0, 4);
		layout.Controls.Add(new Label { Text = $"配置和日志保存在：\r\n{data_directory}", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }, 0, 5);
		layout.Controls.Add(RepairStartup, 0, 6);
		layout.Controls.Add(UiTheme.Button("核对自启状态", (_, _) => {
			try { MessageBox.Show(this, StartupRegistration.Describe(), "Windows 中的实际自启状态"); }
			catch (Exception exception) { MessageBox.Show(this, exception.Message, "无法读取自启状态"); }
		}), 0, 7);
		var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
		Button save = UiTheme.Button("保存设置", (_, _) => { DialogResult = DialogResult.OK; Close(); }, true);
		Button cancel = UiTheme.Button("取消", (_, _) => Close());
		cancel.DialogResult = DialogResult.Cancel;
		buttons.Controls.AddRange([save, cancel]);
		layout.Controls.Add(buttons, 0, 8);
		Controls.Add(layout);
		AcceptButton = save;
		CancelButton = cancel;
	}
}
