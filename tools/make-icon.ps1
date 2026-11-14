# Generates app.ico (icon of the exe and the installer): a white and green battery on a dark background.
# Only needs to be run again to change the icon artwork.
param(
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) 'app.ico'),
    [string]$PreviewPng = ''
)
Add-Type -AssemblyName System.Drawing

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'Half'
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $size / 16.0

    # Background
    $bg = New-RoundRect 0 0 $size $size (3.6 * $u)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(28, 33, 46))), $bg)

    # Battery
    $pw = [Math]::Max(1.0, [Math]::Round(1.1 * $u))
    $bx = [Math]::Round(2 * $u) + $pw / 2; $by = [Math]::Round(4.5 * $u) + $pw / 2
    $bw = [Math]::Round(10 * $u); $bh = [Math]::Round(6 * $u)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $pw
    $g.DrawPath($pen, (New-RoundRect $bx $by $bw $bh (1.4 * $u)))
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillPath($white, (New-RoundRect ($bx + $bw + $pw / 2) ($by + $bh / 2 - 1.3 * $u) (1.3 * $u) (2.6 * $u) (0.4 * $u)))

    # Level
    $inset = $pw / 2 + [Math]::Max(1.0, [Math]::Round(0.8 * $u))
    $green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(52, 199, 89))
    $g.FillRectangle($green, [single]($bx + $inset), [single]($by + $inset), [single](($bw - 2 * $inset) * 0.72), [single]($bh - 2 * $inset))
    $g.Dispose()
    return $bmp
}

# BMP entries (32-bit DIB + mask) for small sizes, PNG for 256 px.
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle 0, 0, $n, $n
    $data = $bmp.LockBits($rect, 'ReadOnly', 'Format32bppArgb')
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $maskRow = [int]([Math]::Floor(($n + 31) / 32) * 4)
    $bw.Write([int]40); $bw.Write([int]$n); $bw.Write([int]($n * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]0); $bw.Write([int]($n * $n * 4 + $maskRow * $n)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    $row = New-Object byte[] ($n * 4)
    for ($y = $n - 1; $y -ge 0; $y--) {
        [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($data.Scan0, $y * $data.Stride), $row, 0, $row.Length)
        $bw.Write($row)
    }
    $bw.Write((New-Object byte[] ($maskRow * $n)))
    $bmp.UnlockBits($data)
    $bw.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$entries = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -eq 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
        if ($PreviewPng) { [System.IO.File]::WriteAllBytes($PreviewPng, $bytes) }
    } else {
        $bytes = Get-DibBytes $bmp
    }
    $entries += , @{ Size = $s; Bytes = $bytes }
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$e.Bytes.Length); $w.Write([int]$offset)
    $offset += $e.Bytes.Length
}
foreach ($e in $entries) { $w.Write([byte[]]$e.Bytes) }
$w.Close()
"OK: $Out"
