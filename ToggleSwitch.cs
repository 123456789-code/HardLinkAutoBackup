using System.Drawing.Drawing2D;

namespace HardLinkAutoBackup;

// 保留 CheckBox 的键盘与无障碍切换行为，只绘制滑块外观。
internal sealed class ToggleSwitch : CheckBox {
	public ToggleSwitch() {
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
		AutoSize = true;
		Cursor = Cursors.Hand;
		BackColor = Color.Transparent;
		AccessibleRole = AccessibleRole.CheckButton;
	}

	private int TrackHeight {
		get { return Math.Max(22, (int)Math.Ceiling(Font.Height * 1.1)); }
	}

	public override Size GetPreferredSize(Size proposed_size) {
		Size text_size = TextRenderer.MeasureText(Text + "  开启", Font, Size.Empty, TextFormatFlags.SingleLine);
		int height = TrackHeight;
		return new Size(height * 2 + height / 2 + text_size.Width + Padding.Horizontal + 4,
			Math.Max(height, text_size.Height) + Padding.Vertical + 8);
	}

	protected override void OnCheckedChanged(EventArgs args) {
		base.OnCheckedChanged(args);
		Invalidate();
	}

	protected override void OnPaint(PaintEventArgs args) {
		Graphics graphics = args.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		int height = TrackHeight;
		var track = new Rectangle(Padding.Left + 2, (Height - height) / 2, height * 2, height);
		Color accent = SystemInformation.HighContrast ? SystemColors.Highlight : UiTheme.Blue;
		Color inactive = SystemInformation.HighContrast ? SystemColors.WindowText : UiTheme.Muted;
		Color color = Enabled ? (Checked ? accent : inactive) : SystemColors.GrayText;
		using var outline = new GraphicsPath();
		outline.AddArc(track.Left, track.Top, height, height, 90, 180);
		outline.AddArc(track.Right - height, track.Top, height, height, 270, 180);
		outline.CloseFigure();
		using var brush = new SolidBrush(color);
		if (Checked)
			graphics.FillPath(brush, outline);
		else {
			using var pen = new Pen(color, Math.Max(1, height / 16f));
			graphics.DrawPath(pen, outline);
		}
		int inset = Math.Max(4, height / 5);
		int diameter = height - inset * 2;
		int thumb_left = Checked ? track.Right - inset - diameter : track.Left + inset;
		using var thumb = new SolidBrush(Checked ? (SystemInformation.HighContrast ? SystemColors.HighlightText : Color.White) : color);
		graphics.FillEllipse(thumb, thumb_left, track.Top + inset, diameter, diameter);
		var text_bounds = new Rectangle(track.Right + height / 2, Padding.Top, Math.Max(0, Width - track.Right - height / 2 - Padding.Right), Height - Padding.Vertical);
		TextRenderer.DrawText(graphics, Text + (Checked ? "  开启" : "  关闭"), Font, text_bounds,
			Enabled ? ForeColor : SystemColors.GrayText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
		if (Focused && ShowFocusCues)
			ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
	}
}
