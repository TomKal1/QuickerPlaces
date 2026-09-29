# Regenerates src/QuickerPlaces/Resources/AppIcon.ico: a Brand green tile
# (#1F5C4D) with an off-white "QP" in TASA Orbiter ExtraBold and a thin
# silver edge (#C6CBC8), matching the monogram in the main window header.
# PNG frames at 16-256 px packed into one .ico. Run from any folder.
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$fontFile = Join-Path $root 'src\QuickerPlaces\Resources\Fonts\TASAOrbiter-ExtraBold.ttf'
$outFile = Join-Path $root 'src\QuickerPlaces\Resources\AppIcon.ico'

$fonts = New-Object System.Drawing.Text.PrivateFontCollection
$fonts.AddFontFile($fontFile)
$family = $fonts.Families[0]
$green = [System.Drawing.Color]::FromArgb(255, 0x1F, 0x5C, 0x4D)
$silver = [System.Drawing.Color]::FromArgb(255, 0xC6, 0xCB, 0xC8)
$ink = [System.Drawing.Color]::FromArgb(255, 0xF2, 0xF1, 0xEC)

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $inset = [Math]::Max(0.5, $s / 64)
    $w = $s - 2 * $inset
    $d = [Math]::Max(3, $s * 0.36)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($inset, $inset, $d, $d, 180, 90)
    $path.AddArc($inset + $w - $d, $inset, $d, $d, 270, 90)
    $path.AddArc($inset + $w - $d, $inset + $w - $d, $d, $d, 0, 90)
    $path.AddArc($inset, $inset + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $green), $path)
    $g.DrawPath((New-Object System.Drawing.Pen $silver, ([Math]::Max(1, $s / 48))), $path)
    $font = New-Object System.Drawing.Font $family, ([single]($s * 0.42)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString('QP', $font, (New-Object System.Drawing.SolidBrush $ink), (New-Object System.Drawing.RectangleF 0, ([single]($s * 0.02)), $s, $s), $format)
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , $stream.ToArray()
    $g.Dispose(); $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $side = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$side); $writer.Write([byte]$side); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frames[$i].Length); $writer.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
[System.IO.File]::WriteAllBytes($outFile, $out.ToArray())
"Wrote $outFile ($($out.Length) bytes, $($sizes.Count) frames)"
