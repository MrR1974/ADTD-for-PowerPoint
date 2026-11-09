<#
.SYNOPSIS
  Draws the app's icon (src\Adtd.App\Adtd.ico): a domain tree, one box above two joined by elbow connectors
  as on the domains slide, in white on a blue tile, at 16–256 px. Run again after changing the design; the
  .ico is committed.
#>
# Adapted from Recast's tools/New-Icon.ps1.
param([string] $Out = (Join-Path $PSScriptRoot '..\src\Adtd.App\Adtd.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function RoundRect([System.Drawing.Graphics] $g, [System.Drawing.Brush] $brush, [double] $x, [double] $y, [double] $w, [double] $h, [double] $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $p.CloseFigure()
    $g.FillPath($brush, $p)
}

function Draw([int] $n) {
    $bmp = New-Object System.Drawing.Bitmap $n, $n
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $n / 16.0
    # Tile: a rounded square in directory blue.
    RoundRect $g (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x1F, 0x5A, 0xA6))) 0 0 ($n - 1) ($n - 1) ([Math]::Max(1, 3 * $u))
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1, 1.1 * $u))
    $r = [Math]::Max(0.5, 0.8 * $u)
    # The forest root, and two child domains below it.
    RoundRect $g $white (5 * $u) (2.5 * $u) (6 * $u) (3.5 * $u) $r
    RoundRect $g $white (1.5 * $u) (10 * $u) (5.5 * $u) (3.5 * $u) $r
    RoundRect $g $white (9 * $u) (10 * $u) (5.5 * $u) (3.5 * $u) $r
    # Elbow connectors: down from the root, across, and down onto each child.
    $mid = 8 * $u
    $g.DrawLine($pen, 8 * $u, 6 * $u, 8 * $u, $mid)
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(4.25 * $u, 10 * $u), [System.Drawing.PointF]::new(4.25 * $u, $mid),
        [System.Drawing.PointF]::new(11.75 * $u, $mid), [System.Drawing.PointF]::new(11.75 * $u, 10 * $u)))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}

# ICO: header, one directory entry per size, then each image as PNG (valid since Windows Vista).
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) { , (Draw $s) }
$o = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $o
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path (Resolve-Path (Split-Path $Out)) (Split-Path $Out -Leaf)), $o.ToArray())
# A 256 px PNG beside it, for the README and for checking the design.
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot '..\assets\app-icon.png'), $images[-1])
Write-Host "Wrote $Out ($($o.Length) bytes)"
