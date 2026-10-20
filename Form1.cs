using System.Diagnostics;
using Microsoft.Win32;

namespace HardLinkAutoBackup;

public partial class Form1 : Form {
	private readonly AppSettings Settings;
	private readonly AppStore Store;
	private readonly BackupCoordinator Coordinator;
	private readonly DataGridView Grid = new TaskGrid();
	private readonly Label Summary = new();
	private readonly Label Details = new();
	private readonly CheckBox AutomaticSwitch = new();
	private readonly System.Windows.Forms.Timer Pulse = new() { Interval = 1000 };
	private readonly NotifyIcon TrayIcon = new();
	private readonly bool StartHidden;
	private readonly bool Preview;
	private bool Refreshing;
	private bool Exiting;
	private bool ClosingDown;
	private DateTimeOffset LastNotification = DateTimeOffset.MinValue;

	public Form1() : this(new AppSettings(), new AppStore(), false, true) { }

	internal Form1(AppSettings settings, AppStore store, bool start_hidden, bool preview = false) {
		Settings = settings;
		Store = store;
		StartHidden = start_hidden;
		Preview = preview;
		Coordinator = new BackupCoordinator(settings, store);
		InitializeComponent();
		BuildInterface();
		Coordinator.Changed += RefreshTasks;
		Coordinator.Attention += Notify;
		Pulse.Tick += (_, _) => { Coordinator.Tick(); RefreshTasks(); };
		Shown += (_, _) => {
			if (Preview)
				return;
			Coordinator.RestoreState();
			Coordinator.Reconfigure();
			SystemEvents.PowerModeChanged += HandlePowerMode;
			Pulse.Start();
			Coordinator.Tick();
			if (StartHidden && Settings.StayInTray)
				Hide();
		};
		FormClosing += HandleClosing;
		FormClosed += (_, _) => { SystemEvents.PowerModeChanged -= HandlePowerMode; Pulse.Dispose(); TrayIcon.Dispose(); };
		RefreshTasks();
	}

	private void HandlePowerMode(object sender, PowerModeChangedEventArgs args) {
		if (args.Mode == PowerModes.Resume && !IsDisposed && IsHandleCreated)
			BeginInvoke(() => { if (!ClosingDown) Coordinator.Resume(); });
	}

	private void BuildInterface() {
		Text = $"HardLinkAutoBackup {typeof(Form1).Assembly.GetName().Version?.ToString(3)}";
		UiTheme.Apply(this);
		ClientSize = new Size(1200, 780);
		MinimumSize = new Size(960, 620);
		StartPosition = FormStartPosition.CenterScreen;
		var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
		root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		var header = new TableLayoutPanel {
			Dock = DockStyle.Fill,
			AutoSize = true,
			ColumnCount = 3,
			RowCount = 1,
			Padding = new Padding(24, 22, 24, 18),
			BackColor = Color.White,
			Margin = Padding.Empty
		};
		header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
		header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		header.Controls.Add(new PictureBox { Image = UiTheme.AppImage, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(56, 56), Margin = new Padding(0, 0, 16, 0), Anchor = AnchorStyles.Left }, 0, 0);
		var branding = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
		branding.Controls.Add(new Label { Text = "硬链接自动备份", AutoSize = true, Font = new Font(Font.FontFamily, 22, FontStyle.Bold), Margin = Padding.Empty });
		branding.Controls.Add(new Label { Text = "同卷硬链接  /  文件变化监控  /  周期补漏", AutoSize = true, ForeColor = UiTheme.Muted, Margin = new Padding(2, 6, 0, 0) });
		header.Controls.Add(branding, 1, 0);
		Button settings_button = UiTheme.Button("程序设置", async (_, _) => await EditSettingsAsync());
		settings_button.Anchor = AnchorStyles.Right;
		settings_button.Margin = Padding.Empty;
		header.Controls.Add(settings_button, 2, 0);
		root.Controls.Add(header, 0, 0);
		var toolbar = new FlowLayoutPanel {
			Dock = DockStyle.Fill,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Padding = new Padding(24, 20, 16, 4),
			WrapContents = true,
			Margin = Padding.Empty
		};
		toolbar.Controls.Add(UiTheme.Button("新建任务", (_, _) => EditTask(null), true));
		toolbar.Controls.Add(UiTheme.Button("立即扫描", (_, _) => { if (SelectedTask() is BackupTask task) Coordinator.Request(task); }));
		toolbar.Controls.Add(UiTheme.Button("编辑任务", (_, _) => { if (SelectedTask() is BackupTask task) EditTask(task); }));
		toolbar.Controls.Add(UiTheme.Button("启用 / 暂停", (_, _) => ToggleSelected()));
		toolbar.Controls.Add(UiTheme.Button("取消扫描", (_, _) => { if (SelectedTask() is BackupTask task) Coordinator.Cancel(task); }));
		toolbar.Controls.Add(UiTheme.Button("查看日志", (_, _) => ShowLog()));
		Button remove = UiTheme.Button("移除任务", (_, _) => RemoveSelected());
		remove.ForeColor = Color.FromArgb(166, 42, 48);
		toolbar.Controls.Add(remove);
		root.Controls.Add(toolbar, 0, 1);
		var status = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(24, 4, 24, 14), Margin = Padding.Empty };
		status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		status.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		Summary.AutoSize = true;
		Summary.Dock = DockStyle.Fill;
		Summary.TextAlign = ContentAlignment.MiddleLeft;
		Summary.ForeColor = UiTheme.Muted;
		Summary.Margin = new Padding(0, 5, 12, 5);
		AutomaticSwitch.Text = "自动备份";
		AutomaticSwitch.AutoSize = true;
		AutomaticSwitch.Anchor = AnchorStyles.Right;
		AutomaticSwitch.Checked = Settings.AutomaticEnabled;
		AutomaticSwitch.CheckedChanged += (_, _) => {
			if (Refreshing)
				return;
			bool old = Settings.AutomaticEnabled;
			Settings.AutomaticEnabled = AutomaticSwitch.Checked;
			if (!SaveSettings())
				Settings.AutomaticEnabled = old;
			Coordinator.Reconfigure();
			RefreshTasks();
		};

		status.Controls.Add(Summary, 0, 0);
		status.Controls.Add(AutomaticSwitch, 1, 0);
		root.Controls.Add(status, 0, 2);
		Grid.Dock = DockStyle.Fill;
		Grid.Margin = new Padding(24, 0, 24, 16);
		Grid.BackgroundColor = Color.White;
		Grid.BorderStyle = BorderStyle.None;
		Grid.ReadOnly = true;
		Grid.AllowUserToAddRows = false;
		Grid.AllowUserToDeleteRows = false;
		Grid.AllowUserToResizeRows = false;
		Grid.MultiSelect = false;
		Grid.RowHeadersVisible = false;
		Grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
		Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
		Grid.EnableHeadersVisualStyles = false;
		Grid.ColumnHeadersHeight = 44;
		Grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(233, 238, 244);
		Grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.Navy;
		Grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
		Grid.RowTemplate.Height = 52;
		Grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(226, 239, 252);
		Grid.DefaultCellStyle.SelectionForeColor = UiTheme.Navy;
		Grid.DefaultCellStyle.Padding = new Padding(8, 10, 8, 10);
		Grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 254);
		Grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
		Grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(233, 238, 244);
		Grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiTheme.Navy;
		Grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		Grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
		Grid.GridColor = Color.FromArgb(232, 237, 245);
		AddColumn("任务", 100);
		AddColumn("源目录", 160);
		AddColumn("备份目录", 160);
		AddColumn("状态", 120);
		AddColumn("本次链接", 85);
		AddColumn("上次结束", 105);
		AddColumn("下次扫描", 105);
		Grid.SelectionChanged += (_, _) => RefreshDetails();
		Grid.CellDoubleClick += (_, args) => { if (args.RowIndex >= 0 && SelectedTask() is BackupTask task) EditTask(task); };
		root.Controls.Add(Grid, 0, 3);

		var detail_panel = new TableLayoutPanel {
			Dock = DockStyle.Fill,
			AutoSize = true,
			ColumnCount = 2,
			BackColor = Color.White,
			Padding = new Padding(20, 16, 12, 12),
			Margin = new Padding(24, 0, 24, 16)
		};
		detail_panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		detail_panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		Details.AutoSize = true;
		Details.Dock = DockStyle.Fill;
		Details.ForeColor = UiTheme.Muted;
		Details.Margin = new Padding(0, 4, 16, 8);
		var detail_buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
		detail_buttons.Controls.Add(UiTheme.Button("扫描详情", (_, _) => ShowDetails()));
		detail_buttons.Controls.Add(UiTheme.Button("打开备份", (_, _) => OpenBackup()));
		detail_panel.Controls.Add(Details, 0, 0);
		detail_panel.Controls.Add(detail_buttons, 1, 0);
		root.Controls.Add(detail_panel, 0, 4);
		root.Controls.Add(new Label {
			Text = "使用提醒   硬链接共享内容：删除源路径后备份仍在；原地修改或加密会同时影响备份。",
			AutoSize = true,
			Dock = DockStyle.Fill,
			BackColor = Color.FromArgb(255, 248, 231),
			ForeColor = Color.FromArgb(119, 85, 32),
			Padding = new Padding(24, 12, 24, 12),
			Margin = Padding.Empty
		}, 0, 5);
		Controls.Add(root);
		var menu = new ContextMenuStrip();
		menu.Items.Add("打开界面", null, (_, _) => ShowWindow());
		menu.Items.Add("暂停 / 恢复自动备份", null, (_, _) => AutomaticSwitch.Checked = !AutomaticSwitch.Checked);
		menu.Items.Add("立即扫描全部任务", null, (_, _) => { foreach (BackupTask task in Settings.Tasks) Coordinator.Request(task); });
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add("退出程序", null, async (_, _) => await ExitAsync());
		TrayIcon.Icon = UiTheme.AppIcon;
		TrayIcon.Text = "硬链接自动备份";
		TrayIcon.ContextMenuStrip = menu;
		TrayIcon.Visible = !Preview;
		TrayIcon.DoubleClick += (_, _) => ShowWindow();
	}

	private void AddColumn(string name, float weight) {
		Grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, FillWeight = weight, MinimumWidth = 112, SortMode = DataGridViewColumnSortMode.NotSortable });
	}

	private BackupTask? SelectedTask() {
		return Grid.CurrentRow?.Tag as BackupTask;
	}

	private void RefreshTasks() {
		if (IsDisposed || ClosingDown)
			return;
		Refreshing = true;
		AutomaticSwitch.Checked = Settings.AutomaticEnabled;
		if (Grid.Rows.Count != Settings.Tasks.Count || Grid.Rows.Cast<DataGridViewRow>().Where((row, index) => !ReferenceEquals(row.Tag, Settings.Tasks[index])).Any()) {
			Guid? selected = SelectedTask()?.Id;
			Grid.Rows.Clear();
			foreach (BackupTask task in Settings.Tasks) {
				int index = Grid.Rows.Add();
				Grid.Rows[index].Tag = task;
				if (task.Id == selected)
					Grid.CurrentCell = Grid.Rows[index].Cells[0];
			}
		}
		foreach (DataGridViewRow row in Grid.Rows) {
			var task = (BackupTask)row.Tag!;
			TaskState state = Coordinator.State(task);
			bool enabled = Settings.AutomaticEnabled && task.Enabled;
			row.SetValues(task.Name, task.Source, task.Destination, state.Running ? state.Status : !enabled ? "自动已暂停" : state.Status,
				state.Result is null ? "—" : (state.Result.Created + state.Result.Existing).ToString("N0"),
				state.LastFinished?.ToLocalTime().ToString("MM-dd HH:mm") ?? "—", enabled ? state.Running ? "完成后计算" : state.NextRun.ToLocalTime().ToString("MM-dd HH:mm") : "—");
			row.Cells[1].ToolTipText = task.Source;
			row.Cells[2].ToolTipText = task.Destination;
			row.Cells[4].ToolTipText = "本次扫描确认的硬链接数量；此前保留的已删除源文件不计入本次统计。";
			row.Cells[5].ToolTipText = state.LastFinished?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未扫描";
			row.Cells[6].ToolTipText = enabled ? state.NextRun.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "自动备份已暂停";
			row.Cells[3].ToolTipText = state.WatcherWarning;
			row.Cells[3].Style.ForeColor = state.Result?.Failed > 0 ? Color.Firebrick : enabled ? UiTheme.Blue : UiTheme.Muted;
		}
		Summary.Text = $"{Settings.Tasks.Count} 个任务    ·    {Settings.Tasks.Count(task => Coordinator.State(task).Running)} 个运行中    ·    {(Settings.AutomaticEnabled ? "自动备份开启" : "自动备份暂停，仍可手动扫描")}";
		Refreshing = false;
		RefreshDetails();
	}

	private void RefreshDetails() {
		BackupTask? task = SelectedTask();
		if (task is null) { Details.Text = "尚未选择任务\r\n点击“新建任务”选择源目录和备份目录。源文件会直接建立硬链接。"; return; }
		TaskState state = Coordinator.State(task);
		Details.Text = $"{task.Name}   ·   每 {task.IntervalSeconds:N0} 秒完整扫描\r\n{state.Result?.Summary ?? "尚未扫描；运行结果会显示在这里。"}\r\n{Store.LastLogError ?? (state.WatcherWarning.Length > 0 ? state.WatcherWarning : state.Result?.Errors.FirstOrDefault() ?? "源文件删除后，备份链接会继续保留。")}";
	}

	private void EditTask(BackupTask? original) {
		if (original is not null && Coordinator.State(original).Running) { MessageBox.Show(this, "请等待扫描结束，或先取消该任务扫描。", "任务正在运行"); return; }
		if (original is null && Settings.Tasks.Count >= 100) { MessageBox.Show(this, "最多支持 100 个任务。", "任务数量"); return; }
		using var dialog = new TaskEditor(Settings.Tasks, original);
		if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null)
			return;
		int index = original is null ? Settings.Tasks.Count : Settings.Tasks.IndexOf(original);
		if (original is null)
			Settings.Tasks.Add(dialog.Result);
		else
			Settings.Tasks[index] = dialog.Result;
		if (!SaveSettings()) {
			if (original is null)
				Settings.Tasks.Remove(dialog.Result);
			else
				Settings.Tasks[index] = original;
		}
		Coordinator.Reconfigure();
		RefreshTasks();
	}

	private void ToggleSelected() {
		if (SelectedTask() is not BackupTask task)
			return;
		task.Enabled = !task.Enabled;
		if (!SaveSettings())
			task.Enabled = !task.Enabled;
		Coordinator.Reconfigure();
		RefreshTasks();
	}

	private void RemoveSelected() {
		if (SelectedTask() is not BackupTask task)
			return;
		if (Coordinator.State(task).Running) { MessageBox.Show(this, "请先取消扫描，待结束后移除任务。", "任务正在运行"); return; }
		if (MessageBox.Show(this, $"移除任务“{task.Name}”？\r\n源文件和已有备份文件都会保留。", "移除任务", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
			return;
		int index = Settings.Tasks.IndexOf(task);
		Settings.Tasks.Remove(task);
		if (!SaveSettings())
			Settings.Tasks.Insert(index, task);
		Coordinator.Reconfigure();
		RefreshTasks();
	}

	private bool SaveSettings() {
		try { Store.Save(Settings); return true; }
		catch (Exception exception) { MessageBox.Show(this, exception.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
	}

	private async Task EditSettingsAsync() {
		using var dialog = new SettingsDialog(Settings, Store.DataDirectory);
		if (dialog.ShowDialog(this) != DialogResult.OK)
			return;
		bool old_start = Settings.StartAtLogon;
		bool old_admin = Settings.RunAsAdministrator;
		bool old_auto = Settings.AutomaticEnabled;
		bool old_tray = Settings.StayInTray;
		bool update_startup = dialog.RepairStartup.Checked || old_start != dialog.AutoStart.Checked || ((old_start || dialog.AutoStart.Checked) && old_admin != dialog.Elevated.Checked);
		try {
			if (update_startup) {
				string action = dialog.AutoStart.Checked ? "创建或更新" : "移除";
				string message = $"将{action} Windows 登录计划任务：\r\n{StartupRegistration.TaskName}\r\n\r\n程序：{Environment.ProcessPath}\r\n管理员运行：{(dialog.Elevated.Checked ? "是" : "否")}\r\n\r\n该设置会持续生效，可在本界面关闭自启撤销；也可在 Windows 任务计划程序中删除上述任务。\r\n继续时 Windows 可能要求管理员确认。";
				if (MessageBox.Show(this, message, "确认修改系统自启设置", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
					return;
				Enabled = false;
				await StartupRegistration.ApplyAsync(dialog.AutoStart.Checked, dialog.Elevated.Checked, Store);
			}
			Settings.StartAtLogon = dialog.AutoStart.Checked;
			Settings.RunAsAdministrator = dialog.Elevated.Checked;
			Settings.AutomaticEnabled = dialog.Automatic.Checked;
			Settings.StayInTray = dialog.Tray.Checked;
			try { Store.Save(Settings); }
			catch {
				Settings.StartAtLogon = old_start;
				Settings.RunAsAdministrator = old_admin;
				Settings.AutomaticEnabled = old_auto;
				Settings.StayInTray = old_tray;
				// 系统变更可能已经完成；不按旧配置猜测并覆盖原先的系统状态。
				if (update_startup)
					Store.Log("自启系统设置已更新，但配置保存失败。请核对自启状态并使用同步/修复。");
				throw;
			}
			Coordinator.Reconfigure();
			RefreshTasks();
		}
		catch (Exception exception) { MessageBox.Show(this, $"{exception.Message}\r\n若操作被中断，请在 Windows 任务计划程序核对 {StartupRegistration.TaskName}。", "设置未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
		finally { Enabled = true; }
	}

	private void ShowDetails() {
		if (SelectedTask() is not BackupTask task)
			return;
		ScanResult? result = Coordinator.State(task).Result;
		ShowText($"{task.Name} · 扫描详情", result is null ? "尚无扫描结果。" : $"{result.Summary}\r\n\r\n最近成功：{Coordinator.State(task).LastSuccess?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚无成功记录"}\r\n监控：{(Coordinator.State(task).WatcherWarning.Length == 0 ? "正常或未启用" : Coordinator.State(task).WatcherWarning)}\r\n\r\n异名文件保存在备份目录的 {PathRules.CONFLICT_DIRECTORY} 中。\r\n\r\n{string.Join(Environment.NewLine, result.Errors)}");
	}

	private void ShowLog() {
		try { ShowText("运行日志（最近记录）", File.Exists(Store.LogPath) ? string.Join(Environment.NewLine, File.ReadLines(Store.LogPath).TakeLast(500)) : "尚无日志。"); }
		catch (Exception exception) { MessageBox.Show(this, exception.Message, "无法读取日志"); }
	}

	private void ShowText(string title, string text) {
		using var window = new Form { Text = title, Size = new Size(900, 560), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false };
		UiTheme.Apply(window);
		window.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false, Text = text, BackColor = Color.White });
		window.ShowDialog(this);
	}

	private void OpenBackup() {
		if (SelectedTask() is not BackupTask task)
			return;
		try {
			if (!Directory.Exists(task.Destination))
				throw new IOException("备份目录尚未创建，先执行一次扫描。");
			Process.Start(new ProcessStartInfo(task.Destination) { UseShellExecute = true });
		}
		catch (Exception exception) { MessageBox.Show(this, exception.Message, "无法打开目录"); }
	}

	private void Notify(string message) {
		if (DateTimeOffset.Now - LastNotification < TimeSpan.FromMinutes(1))
			return;
		LastNotification = DateTimeOffset.Now;
		TrayIcon.ShowBalloonTip(5000, "硬链接备份需要注意", message, ToolTipIcon.Warning);
	}

	internal void ShowWindow() {
		Show();
		WindowState = FormWindowState.Normal;
		Activate();
	}

	private async void HandleClosing(object? sender, FormClosingEventArgs args) {
		if (Exiting || Preview)
			return;
		if (args.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing) {
			Pulse.Stop();
			TrayIcon.Visible = false;
			foreach (BackupTask task in Settings.Tasks)
				Coordinator.Cancel(task);
			return;
		}
		args.Cancel = true;
		if (Settings.StayInTray && !ClosingDown) { Hide(); return; }
		await ExitAsync();
	}

	private async Task ExitAsync() {
		if (ClosingDown)
			return;
		if (Coordinator.IsRunning && MessageBox.Show(this, "仍有扫描正在运行。退出将取消本次扫描，已有硬链接保留。", "退出程序", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
			return;
		ClosingDown = true;
		Enabled = false;
		Pulse.Stop();
		await Coordinator.StopAsync();
		TrayIcon.Visible = false;
		Exiting = true;
		Close();
	}

	protected override void WndProc(ref Message message) {
		if (message.Msg == Program.ActivateMessage)
			ShowWindow();
		base.WndProc(ref message);
	}

	protected override void OnHandleCreated(EventArgs args) {
		base.OnHandleCreated(args);
		Program.AllowActivation(Handle);
	}
}
