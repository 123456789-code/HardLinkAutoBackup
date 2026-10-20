$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$project_root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $project_root 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
	$bitmap = [System.Drawing.Bitmap]::new($size, $size)
	$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
	try {
		$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
		$graphics.Clear([System.Drawing.Color]::Transparent)
		$graphics.ScaleTransform($size / 256.0, $size / 256.0)
		$back = [System.Drawing.Drawing2D.GraphicsPath]::new()
		$front = [System.Drawing.Drawing2D.GraphicsPath]::new()
		$back_brush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#54B4EF'))
		$front_brush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#0F6CBD'))
		$pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 13)
		$pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
		$pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
		try {
			$back.AddBezier(24, 68, 24, 54, 30, 48, 44, 48)
			$back.AddLine(44, 48, 92, 48)
			$back.AddLine(92, 48, 114, 68)
			$back.AddLine(114, 68, 212, 68)
			$back.AddBezier(212, 68, 226, 68, 232, 74, 232, 88)
			$back.AddLine(232, 88, 232, 194)
			$back.AddBezier(232, 194, 232, 208, 226, 216, 212, 216)
			$back.AddLine(212, 216, 44, 216)
			$back.AddBezier(44, 216, 30, 216, 24, 208, 24, 194)
			$back.CloseFigure()
			$graphics.FillPath($back_brush, $back)
			$front.AddLine(24, 96, 232, 96)
			$front.AddLine(232, 96, 232, 194)
			$front.AddBezier(232, 194, 232, 208, 226, 216, 212, 216)
			$front.AddLine(212, 216, 44, 216)
			$front.AddBezier(44, 216, 30, 216, 24, 208, 24, 194)
			$front.CloseFigure()
			$graphics.FillPath($front_brush, $front)
			$graphics.TranslateTransform(128, 152)
			$graphics.RotateTransform(-32)
			$graphics.DrawArc($pen, -65, -24, 48, 48, 90, 180)
			$graphics.DrawLine($pen, -41, -24, -9, -24)
			$graphics.DrawLine($pen, -41, 24, -9, 24)
			$graphics.DrawArc($pen, 17, -24, 48, 48, -90, 180)
			$graphics.DrawLine($pen, 9, -24, 41, -24)
			$graphics.DrawLine($pen, 9, 24, 41, 24)
			$graphics.DrawLine($pen, -25, 0, 25, 0)
			if ($size -eq 256) { $bitmap.Save((Join-Path $assets 'App.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
			$stream = [System.IO.MemoryStream]::new()
			try { $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png); $frames.Add($stream.ToArray()) } finally { $stream.Dispose() }
		} finally { $back.Dispose(); $front.Dispose(); $back_brush.Dispose(); $front_brush.Dispose(); $pen.Dispose() }
	} finally { $graphics.Dispose(); $bitmap.Dispose() }
}
$output = [System.IO.File]::Create((Join-Path $assets 'App.ico'))
$writer = [System.IO.BinaryWriter]::new($output)
try {
	$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
	$offset = 6 + 16 * $sizes.Count
	for ($index = 0; $index -lt $sizes.Count; $index++) {
		$dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
		$writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
		$writer.Write([byte]0); $writer.Write([byte]0)
		$writer.Write([uint16]1); $writer.Write([uint16]32)
		$writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
		$offset += $frames[$index].Length
	}
	foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose(); $output.Dispose() }
Write-Output '已生成 Assets/App.ico 和 Assets/App.png（16–256 像素）。'
