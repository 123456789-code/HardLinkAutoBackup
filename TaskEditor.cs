namespace HardLinkAutoBackup;

internal sealed class TaskEditor : Form {
	private readonly TextBox NameBox = new() { Dock = DockStyle.Fill };
	private readonly TextBox SourceBox = new() { Dock = DockStyle.Fill };
	private readonly TextBox DestinationBox = new() { Dock = DockStyle.Fill };
	private readonly CheckBox AutomaticBox = new() { Text = "自动监控文件变化，并按周期完整扫描", AutoSize = true, Checked = true };
	private readonly NumericUpDown IntervalBox = new() { Minimum = 1, Maximum = 31536000, Value = 1, Width = 110 };
	private readonly ComboBox UnitBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
	private readonly TextBox ExclusionBox = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
	private readonly IReadOnlyList<BackupTask> Tasks;
	private readonly Guid TaskId;
	public BackupTask? Result { get; private set; }

	public TaskEditor(IReadOnlyList<BackupTask> tasks, BackupTask? original = null) {
		Tasks = tasks;
		TaskId = original?.Id ?? Guid.NewGuid();
		Text = original is null ? "新建备份任务" : "编辑备份任务";
		Font = new Font("Microsoft YaHei UI", 10);
		BackColor = UiTheme.Canvas;
		ClientSize = new Size(680, 550);
		MinimumSize = new Size(620, 570);
		StartPosition = FormStartPosition.CenterParent;
		ShowInTaskbar = false;
		MaximizeBox = false;
		MinimizeBox = false;
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 3, RowCount = 10 };
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
		for (int i = 0; i < 6; i++)
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 0 ? 52 : 46));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
		var heading = UiTheme.Label("选择目录，建立持续的防误删保护");
		heading.Font = new Font(Font, FontStyle.Bold);
		layout.Controls.Add(heading, 0, 0);
		layout.SetColumnSpan(heading, 3);
		AddPathRow(layout, 1, "任务名称", NameBox, false);
		AddPathRow(layout, 2, "源目录", SourceBox, true);
		AddPathRow(layout, 3, "备份目录", DestinationBox, true);
		layout.Controls.Add(AutomaticBox, 1, 4);
		layout.SetColumnSpan(AutomaticBox, 2);
		layout.Controls.Add(UiTheme.Label("扫描间隔"), 0, 5);
		var interval_panel = new FlowLayoutPanel { Dock = DockStyle.Fill };
		UnitBox.Items.AddRange(["秒", "分钟", "小时", "天"]);
		UnitBox.SelectedIndex = 2;
		interval_panel.Controls.AddRange([IntervalBox, UnitBox]);
		layout.Controls.Add(interval_panel, 1, 5);
		layout.SetColumnSpan(interval_panel, 2);
		var exclusion_label = UiTheme.Label("排除路径（选填，每行一个相对路径，不支持通配符）", UiTheme.Muted);
		exclusion_label.Margin = Padding.Empty;
		layout.Controls.Add(exclusion_label, 0, 6);
		layout.SetColumnSpan(exclusion_label, 3);
		layout.Controls.Add(ExclusionBox, 0, 7);
		layout.SetColumnSpan(ExclusionBox, 3);
		layout.Controls.Add(new Label { Text = "源与备份必须位于同一 NTFS 卷，且不能互相嵌套。\r\n硬链接共享内容，不能恢复原地修改前的内容。", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Padding = new Padding(0, 8, 0, 0) }, 0, 8);
		layout.SetColumnSpan(layout.GetControlFromPosition(0, 8)!, 3);
		var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
		Button save = UiTheme.Button("保存任务", SaveTask, true);
		Button cancel = UiTheme.Button("取消", (_, _) => Close());
		cancel.DialogResult = DialogResult.Cancel;
		buttons.Controls.AddRange([save, cancel]);
		layout.Controls.Add(buttons, 0, 9);
		layout.SetColumnSpan(buttons, 3);
		Controls.Add(layout);
		AcceptButton = save;
		CancelButton = cancel;
		if (original is not null) {
			NameBox.Text = original.Name;
			SourceBox.Text = original.Source;
			DestinationBox.Text = original.Destination;
			AutomaticBox.Checked = original.Enabled;
			int[] factors = [1, 60, 3600, 86400];
			int unit = 3;
			while (unit > 0 && original.IntervalSeconds % factors[unit] != 0)
				unit--;
			UnitBox.SelectedIndex = unit;
			IntervalBox.Value = Math.Clamp(original.IntervalSeconds / factors[unit], 1, 31536000);
			ExclusionBox.Lines = original.Exclusions.ToArray();
		}
	}

	private void AddPathRow(TableLayoutPanel layout, int row, string label, TextBox box, bool browse) {
		layout.Controls.Add(UiTheme.Label(label), 0, row);
		layout.Controls.Add(box, 1, row);
		if (!browse) { layout.SetColumnSpan(box, 2); return; }
		Button button = UiTheme.Button("选择…", (_, _) => {
			using var dialog = new FolderBrowserDialog { Description = label, UseDescriptionForTitle = true, SelectedPath = box.Text };
			if (dialog.ShowDialog(this) == DialogResult.OK)
				box.Text = dialog.SelectedPath;
		});
		button.Width = 82;
		button.Height = 30;
		layout.Controls.Add(button, 2, row);
	}

	private void SaveTask(object? sender, EventArgs args) {
		try {
			if (string.IsNullOrWhiteSpace(SourceBox.Text) || string.IsNullOrWhiteSpace(DestinationBox.Text))
				throw new IOException("请填写源目录和备份目录。");
			int[] factors = [1, 60, 3600, 86400];
			var task = new BackupTask {
				Id = TaskId,
				Name = NameBox.Text.Trim(),
				Source = SourceBox.Text.Trim(),
				Destination = DestinationBox.Text.Trim(),
				Enabled = AutomaticBox.Checked,
				IntervalSeconds = checked((int)IntervalBox.Value * factors[UnitBox.SelectedIndex]),
				Exclusions = ExclusionBox.Lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToList()
			};
			if (task.Name.Length == 0)
				task.Name = new DirectoryInfo(task.Source).Name;
			PathRules.Validate(task, Tasks);
			Result = task;
			DialogResult = DialogResult.OK;
			Close();
		}
		catch (Exception exception) { MessageBox.Show(this, exception.Message, "任务检查未通过", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
	}
}
