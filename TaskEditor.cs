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
		UiTheme.Apply(this);
		ClientSize = new Size(760, 720);
		MinimumSize = new Size(720, 600);
		StartPosition = FormStartPosition.CenterParent;
		ShowInTaskbar = false;
		MaximizeBox = false;
		MinimizeBox = false;
		NameBox.MaxLength = 100;
		var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 2 };
		root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
		var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 16, 0), ColumnCount = 3, RowCount = 10 };
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		for (int row = 0; row < 10; row++)
			layout.RowStyles.Add(new RowStyle(row == 8 ? SizeType.Absolute : SizeType.AutoSize, row == 8 ? 104 : 0));
		var heading = UiTheme.Label(original is null ? "添加备份任务" : "编辑备份任务");
		heading.Font = new Font(Font.FontFamily, 19, FontStyle.Bold);
		heading.Margin = new Padding(0, 0, 0, 6);
		layout.Controls.Add(heading, 0, 0);
		layout.SetColumnSpan(heading, 3);
		Label subtitle = UiTheme.Note("选择同一 NTFS 卷上的两个目录，为文件增加保留路径。");
		subtitle.Margin = new Padding(0, 0, 0, 22);
		layout.Controls.Add(subtitle, 0, 1);
		layout.SetColumnSpan(subtitle, 3);
		AddPathRow(layout, 2, "任务名称", NameBox, false);
		AddPathRow(layout, 3, "源目录", SourceBox, true);
		AddPathRow(layout, 4, "备份目录", DestinationBox, true);
		AutomaticBox.Margin = new Padding(0, 8, 0, 14);
		layout.Controls.Add(AutomaticBox, 1, 5);
		layout.SetColumnSpan(AutomaticBox, 2);
		layout.Controls.Add(UiTheme.Label("扫描间隔"), 0, 6);
		var interval_panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 12) };
		UnitBox.Items.AddRange(["秒", "分钟", "小时", "天"]);
		UnitBox.SelectedIndex = 2;
		IntervalBox.Width = 128;
		UnitBox.Width = 104;
		interval_panel.Controls.AddRange([IntervalBox, UnitBox]);
		layout.Controls.Add(interval_panel, 1, 6);
		layout.SetColumnSpan(interval_panel, 2);
		Label exclusion_label = UiTheme.Note("排除路径（选填）  ·  每行一个相对路径，不支持通配符");
		layout.Controls.Add(exclusion_label, 0, 7);
		layout.SetColumnSpan(exclusion_label, 3);
		ExclusionBox.BackColor = Color.White;
		ExclusionBox.BorderStyle = BorderStyle.FixedSingle;
		ExclusionBox.Margin = new Padding(0, 0, 0, 12);
		ExclusionBox.MinimumSize = new Size(0, 80);
		layout.Controls.Add(ExclusionBox, 0, 8);
		layout.SetColumnSpan(ExclusionBox, 3);
		Label note = UiTheme.Note("源与备份不能互相嵌套。硬链接共享内容，不能恢复原地修改前的内容。");
		layout.Controls.Add(note, 0, 9);
		layout.SetColumnSpan(note, 3);
		FlowLayoutPanel buttons = UiTheme.Actions();
		Button save = UiTheme.Button("保存任务", SaveTask, true);
		Button cancel = UiTheme.Button("取消", (_, _) => Close());
		cancel.DialogResult = DialogResult.Cancel;
		buttons.Controls.AddRange([save, cancel]);
		viewport.Controls.Add(layout);
		root.Controls.Add(viewport, 0, 0);
		root.Controls.Add(buttons, 0, 1);
		Controls.Add(root);
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
		Label caption = UiTheme.Label(label);
		caption.Margin = new Padding(0, 9, 20, 12);
		layout.Controls.Add(caption, 0, row);
		box.Margin = new Padding(0, 7, 10, 16);
		layout.Controls.Add(box, 1, row);
		if (!browse) { layout.SetColumnSpan(box, 2); return; }
		Button button = UiTheme.Button("选择…", (_, _) => {
			using var dialog = new FolderBrowserDialog { Description = label, UseDescriptionForTitle = true, SelectedPath = box.Text };
			if (dialog.ShowDialog(this) == DialogResult.OK)
				box.Text = dialog.SelectedPath;
		});
		button.MinimumSize = new Size(88, 36);
		button.Margin = new Padding(0, 0, 0, 10);
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
