namespace HardLinkAutoBackup;

internal sealed class SettingsDialog : Form {
	public CheckBox AutoStart { get; } = new() { Text = "登录 Windows 后自动启动", AutoSize = true };
	public CheckBox Elevated { get; } = new() { Text = "以管理员权限启动", AutoSize = true };
	public CheckBox Automatic { get; } = new() { Text = "启用自动备份", AutoSize = true };
	public CheckBox Tray { get; } = new() { Text = "关闭窗口后继续运行", AutoSize = true };
	public CheckBox RepairStartup { get; } = new() { Text = "保存时同步 / 修复自启任务", AutoSize = true };

	public SettingsDialog(AppSettings settings, string data_directory) {
		Text = "程序设置";
		UiTheme.Apply(this);
		ClientSize = new Size(760, 800);
		MinimumSize = new Size(720, 600);
		StartPosition = FormStartPosition.CenterParent;
		ShowInTaskbar = false;
		MaximizeBox = false;
		MinimizeBox = false;
		var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(28) };
		root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		var heading = UiTheme.Label("程序设置");
		heading.Font = new Font(Font.FontFamily, 19, FontStyle.Bold);
		heading.Margin = new Padding(0, 0, 0, 20);
		root.Controls.Add(heading, 0, 0);
		var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
		var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(0, 0, 16, 0) };
		content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		viewport.Controls.Add(content);
		root.Controls.Add(viewport, 0, 1);
		AutoStart.Checked = settings.StartAtLogon;
		Elevated.Checked = settings.RunAsAdministrator;
		Automatic.Checked = settings.AutomaticEnabled;
		Tray.Checked = settings.StayInTray;
		AddSetting(content, AutoStart, "登录后开始保护；保存前会确认 Windows 计划任务的变更。");
		AddSetting(content, Elevated, "下次启动生效。普通可写目录通常无需管理员权限。");
		AddSetting(content, Automatic, "监控文件变化并按周期完整扫描；关闭后仍可手动扫描。");
		AddSetting(content, Tray, "隐藏到右下角通知区域；需要停止时从托盘菜单退出。");
		var maintenance = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16), BackColor = Color.White, Margin = new Padding(0, 12, 0, 12) };
		maintenance.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		maintenance.Controls.Add(RepairStartup);
		maintenance.Controls.Add(UiTheme.Note("程序搬动位置或自启任务丢失时使用；同步前仍会询问确认。"));
		maintenance.Controls.Add(UiTheme.Button("核对自启状态", (_, _) => {
			try { MessageBox.Show(this, StartupRegistration.Describe(), "Windows 中的实际自启状态"); }
			catch (Exception exception) { MessageBox.Show(this, exception.Message, "无法读取自启状态"); }
		}));
		content.Controls.Add(maintenance);
		content.Controls.Add(UiTheme.Note("配置与日志位置"));
		var path = new TextBox { Text = data_directory, ReadOnly = true, Dock = DockStyle.Fill, BackColor = UiTheme.Canvas, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 12) };
		content.Controls.Add(path);
		FlowLayoutPanel buttons = UiTheme.Actions();
		Button save = UiTheme.Button("保存设置", (_, _) => { DialogResult = DialogResult.OK; Close(); }, true);
		Button cancel = UiTheme.Button("取消", (_, _) => Close());
		cancel.DialogResult = DialogResult.Cancel;
		buttons.Controls.AddRange([save, cancel]);
		root.Controls.Add(buttons, 0, 2);
		Controls.Add(root);
		AcceptButton = save;
		CancelButton = cancel;
	}

	private static void AddSetting(TableLayoutPanel content, CheckBox checkbox, string description) {
		var card = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, BackColor = Color.White, Padding = new Padding(16, 12, 16, 8), Margin = new Padding(0, 0, 0, 8) };
		card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		checkbox.Margin = new Padding(0, 0, 0, 2);
		card.Controls.Add(checkbox);
		Label note = UiTheme.Note(description);
		note.Margin = new Padding(20, 4, 0, 4);
		card.Controls.Add(note);
		content.Controls.Add(card);
	}
}
