# Uygulama ikonunu (Assets\DockHub.ico) üretir. PowerShell 7+ (Windows) gerektirir.
# Kullanım: pwsh tools/generate-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\CustomDock\Assets\DockHub.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$s) {
    $bmp = [System.Drawing.Bitmap]::new($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [single]([Math]::Max(0.5, $s * 0.04))
    $body = New-RoundedPath $pad $pad ($s - 2 * $pad) ($s - 2 * $pad) ([single]($s * 0.22))
    $grad = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new(0, 0), [System.Drawing.PointF]::new(0, $s),
        [System.Drawing.Color]::FromArgb(255, 58, 58, 66), [System.Drawing.Color]::FromArgb(255, 18, 18, 22))
    $g.FillPath($grad, $body)

    # "Dock" çubuğu
    $barH = [single]($s * 0.34); $barY = [single]($s * 0.52); $barX = [single]($s * 0.14); $barW = [single]($s * 0.72)
    $bar = New-RoundedPath $barX $barY $barW $barH ([single]($barH * 0.3))
    $g.FillPath([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), $bar)

    # Üç widget kutusu
    $colors = @(
        [System.Drawing.Color]::FromArgb(255, 52, 199, 89),
        [System.Drawing.Color]::FromArgb(255, 255, 159, 10),
        [System.Drawing.Color]::FromArgb(255, 10, 132, 255))
    $inner = [single]($barH * 0.18)
    $cell = [single](($barW - 4 * $inner) / 3)
    for ($i = 0; $i -lt 3; $i++) {
        $cx = [single]($barX + $inner + $i * ($cell + $inner))
        $cy = [single]($barY + $inner)
        $ch = [single]($barH - 2 * $inner)
        $cp = New-RoundedPath $cx $cy $cell $ch ([single]([Math]::Max(0.5, $ch * 0.25)))
        $g.FillPath([System.Drawing.SolidBrush]::new($colors[$i]), $cp)
    }

    # Üstte saat çizgisi
    $lineY = [single]($s * 0.26)
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(230, 255, 255, 255), [single]([Math]::Max(1, $s * 0.07)))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLine($pen, [single]($s * 0.24), $lineY, [single]($s * 0.56), $lineY)
    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)
    $bw.Write([uint32]40); $bw.Write([int32]$s); $bw.Write([int32]($s * 2))
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
    $bw.Write([uint32]0); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Floor(($s + 31) / 32) * 4)
    $bw.Write([byte[]]::new($maskRow * $s))
    $bw.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -ge 256) {
        $ms = [System.IO.MemoryStream]::new()
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        , $ms.ToArray()
    } else {
        , [byte[]](Get-DibBytes $bmp)
    }
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $out)).Path + '\' + (Split-Path $out -Leaf))
$w = [System.IO.BinaryWriter]::new($fs)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write([byte[]]$img) }
$w.Dispose()
Write-Host "Icon written: $out"
