namespace HardLinkAutoBackup;

internal static class UiTheme {
	public static readonly Color Navy = Color.FromArgb(24, 39, 62);
	public static readonly Color Blue = Color.FromArgb(37, 99, 235);
	public static readonly Color Canvas = Color.FromArgb(245, 247, 251);
	public static readonly Color Muted = Color.FromArgb(95, 108, 128);

	public static Button Button(string text, EventHandler action, bool primary = false) {
		var button = new Button {
			Text = text,
			Size = new Size(108, 36),
			FlatStyle = FlatStyle.Flat,
			BackColor = primary ? Blue : Color.White,
			ForeColor = primary ? Color.White : Navy,
			Margin = new Padding(0, 0, 10, 0),
			Cursor = Cursors.Hand
		};
		button.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(211, 218, 229);
		button.Click += action;
		return button;
	}

	public static Label Label(string text, Color? color = null) {
		return new Label { Text = text, AutoSize = true, ForeColor = color ?? Navy, Margin = new Padding(0, 8, 0, 6) };
	}
}
