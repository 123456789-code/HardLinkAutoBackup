namespace HardLinkAutoBackup;

internal static class UiTheme {
	public static readonly Color Navy = Color.FromArgb(32, 37, 43);
	public static readonly Color Blue = Color.FromArgb(15, 108, 189);
	public static readonly Color Canvas = Color.FromArgb(243, 245, 248);
	public static readonly Color Muted = Color.FromArgb(99, 111, 125);
	public static readonly Color Border = Color.FromArgb(221, 227, 234);
	public static readonly Icon AppIcon = LoadIcon();
	public static readonly Image AppImage = LoadImage();

	private static Icon LoadIcon() {
		using Stream stream = typeof(UiTheme).Assembly.GetManifestResourceStream("HardLinkAutoBackup.Assets.App.ico")!;
		using var icon = new Icon(stream, new Size(32, 32));
		return (Icon)icon.Clone();
	}

	private static Image LoadImage() {
		using Stream stream = typeof(UiTheme).Assembly.GetManifestResourceStream("HardLinkAutoBackup.Assets.App.png")!;
		using Image image = Image.FromStream(stream);
		return new Bitmap(image);
	}

	public static void Apply(Form form) {
		form.AutoScaleDimensions = new SizeF(96, 96);
		form.AutoScaleMode = AutoScaleMode.Dpi;
		form.Font = new Font("Microsoft YaHei UI", 10);
		form.BackColor = Canvas;
		form.ForeColor = Navy;
		form.Icon = AppIcon;
		form.Shown += (_, _) => {
			// 离屏预览不调整位置；真实窗口限制在当前显示器工作区内。
			if (form.Location.X < -10000)
				return;
			Rectangle area = Screen.FromControl(form).WorkingArea;
			int width = Math.Min(form.Width, area.Width - 24);
			int height = Math.Min(form.Height, area.Height - 24);
			if (width == form.Width && height == form.Height)
				return;
			form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, width), Math.Min(form.MinimumSize.Height, height));
			form.SetBounds(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
		};
	}

	public static Button Button(string text, EventHandler action, bool primary = false) {
		var button = new Button {
			Text = text,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			MinimumSize = new Size(104, 40),
			Padding = new Padding(14, 7, 14, 7),
			FlatStyle = FlatStyle.Flat,
			BackColor = primary ? Blue : Color.White,
			ForeColor = primary ? Color.White : Navy,
			Margin = new Padding(0, 0, 8, 8),
			Cursor = Cursors.Hand,
			UseCompatibleTextRendering = false
		};
		button.FlatAppearance.BorderColor = primary ? Blue : Border;
		button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(17, 94, 163) : Color.FromArgb(239, 244, 250);
		button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(12, 78, 138) : Color.FromArgb(225, 235, 246);
		button.Click += action;
		return button;
	}

	public static Label Label(string text, Color? color = null) {
		return new Label { Text = text, AutoSize = true, ForeColor = color ?? Navy, Margin = new Padding(0, 8, 0, 8) };
	}

	public static Label Note(string text) {
		return new Label { Text = text, AutoSize = true, Dock = DockStyle.Fill, ForeColor = Muted, Margin = new Padding(0, 6, 0, 12) };
	}

	public static FlowLayoutPanel Actions() {
		return new FlowLayoutPanel {
			Dock = DockStyle.Fill,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.RightToLeft,
			Margin = Padding.Empty,
			Padding = new Padding(0, 8, 0, 0)
		};
	}
}

internal sealed class TaskGrid : DataGridView {
	public TaskGrid() {
		DoubleBuffered = true;
	}
}
