# 生成 GlintWin 应用图标：渐变圆角底 + 「译」字，多尺寸 PNG 打包为 .ico
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'

$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 56
$path.AddArc(0, 0, $r, $r, 180, 90)
$path.AddArc($size - $r, 0, $r, $r, 270, 90)
$path.AddArc($size - $r, $size - $r, $r, $r, 0, 90)
$path.AddArc(0, $size - $r, $r, $r, 90, 90)
$path.CloseFigure()

$rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, `
    ([System.Drawing.Color]::FromArgb(255, 91, 124, 250)), `
    ([System.Drawing.Color]::FromArgb(255, 155, 91, 250)), 45
$g.FillPath($brush, $path)

$f = New-Object System.Drawing.Font('Microsoft YaHei UI', 122, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = 'Center'
$sf.LineAlignment = 'Center'
$g.DrawString([string][char]0x8BD1, $f, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, -8, $size, $size), $sf)
$g.Dispose()

$sizes = 256, 128, 64, 48, 32, 16
$pngs = @()
foreach ($s in $sizes) {
  $b = New-Object System.Drawing.Bitmap $s, $s
  $g2 = [System.Drawing.Graphics]::FromImage($b)
  $g2.InterpolationMode = 'HighQualityBicubic'
  $g2.DrawImage($bmp, 0, 0, $s, $s)
  $g2.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += ,@{ Size = $s; Bytes = $ms.ToArray() }
  $b.Dispose()
}
$bmp.Dispose()

$out = 'D:\Development\ZCode_workplace\GlintWin\app.ico'
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
  $bw.Write([byte]($p.Size -band 0xFF))   # 256 -> 0
  $bw.Write([byte]($p.Size -band 0xFF))
  $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([uint16]1); $bw.Write([uint16]32)
  $bw.Write([uint32]$p.Bytes.Length)
  $bw.Write([uint32]$offset)
  $offset += $p.Bytes.Length
}
foreach ($p in $pngs) { $bw.Write($p.Bytes) }
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Output ("icon written: " + $out + " (" + (Get-Item $out).Length + " bytes)")
