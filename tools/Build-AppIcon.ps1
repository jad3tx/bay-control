#Requires -Version 5.1
# Rasterize the Bay Control mark into host/Assets/app.ico
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-BayIconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(255, 15, 42, 28))

    $scale = $size / 128.0
    $S = { param($v) [int][Math]::Round($v * $scale) }

    $gold = [System.Drawing.Color]::FromArgb(255, 240, 199, 94)
    $g1 = [System.Drawing.Color]::FromArgb(255, 26, 74, 46)
    $g2 = [System.Drawing.Color]::FromArgb(255, 47, 122, 74)
    $g3 = [System.Drawing.Color]::FromArgb(255, 59, 143, 88)
    $cream = [System.Drawing.Color]::FromArgb(255, 243, 247, 240)
    $pole = [System.Drawing.Color]::FromArgb(255, 232, 242, 228)

    $penW = [Math]::Max(1, (& $S 3.5))
    $penGold = New-Object System.Drawing.Pen $gold, $penW
    $g.DrawEllipse($penGold, (& $S 12), (& $S 40), (& $S 104), (& $S 76))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $g1), (& $S 20), (& $S 46), (& $S 88), (& $S 64))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $g2), (& $S 34), (& $S 56), (& $S 60), (& $S 44))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $g3), (& $S 49), (& $S 67), (& $S 30), (& $S 22))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $cream), (& $S 68), (& $S 66), (& $S 16), (& $S 16))

    $poleW = [Math]::Max(2, (& $S 6))
    $penPole = New-Object System.Drawing.Pen $pole, $poleW
    $penPole.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $penPole.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($penPole, (& $S 64), (& $S 22), (& $S 64), (& $S 72))

    $flag = New-Object System.Drawing.SolidBrush $cream
    $pts = @(
        (New-Object System.Drawing.Point ((& $S 64)), ((& $S 22))),
        (New-Object System.Drawing.Point ((& $S 94)), ((& $S 22))),
        (New-Object System.Drawing.Point ((& $S 85)), ((& $S 34))),
        (New-Object System.Drawing.Point ((& $S 94)), ((& $S 46))),
        (New-Object System.Drawing.Point ((& $S 64)), ((& $S 46)))
    )
    $g.FillPolygon($flag, $pts)
    $g.Dispose()
    return $bmp
}

function Save-Icon([System.Drawing.Bitmap[]]$bmps, [string]$path) {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$bmps.Count)

    $imageStreams = New-Object System.Collections.Generic.List[byte[]]
    foreach ($b in $bmps) {
        $w = $b.Width
        $h = $b.Height
        $raw = New-Object byte[] ($w * $h * 4)
        for ($y = 0; $y -lt $h; $y++) {
            for ($x = 0; $x -lt $w; $x++) {
                $c = $b.GetPixel($x, $h - 1 - $y)
                $i = ($y * $w + $x) * 4
                $raw[$i] = $c.B
                $raw[$i + 1] = $c.G
                $raw[$i + 2] = $c.R
                $raw[$i + 3] = $c.A
            }
        }
        $andRow = [int][Math]::Ceiling($w / 32.0) * 4
        $andSize = $andRow * $h
        $xorSize = $raw.Length
        $img = New-Object System.IO.MemoryStream
        $ibw = New-Object System.IO.BinaryWriter $img
        $ibw.Write([int32]40)
        $ibw.Write([int32]$w)
        $ibw.Write([int32]($h * 2))
        $ibw.Write([int16]1)
        $ibw.Write([int16]32)
        $ibw.Write([int32]0)
        $ibw.Write([int32]($xorSize + $andSize))
        $ibw.Write([int32]0)
        $ibw.Write([int32]0)
        $ibw.Write([int32]0)
        $ibw.Write([int32]0)
        $ibw.Write($raw)
        $ibw.Write((New-Object byte[] $andSize))
        $ibw.Flush()
        [void]$imageStreams.Add($img.ToArray())
        $ibw.Dispose()
        $img.Dispose()
    }

    $offset = 6 + (16 * $bmps.Count)
    for ($i = 0; $i -lt $bmps.Count; $i++) {
        $b = $bmps[$i]
        $wByte = if ($b.Width -ge 256) { [byte]0 } else { [byte]$b.Width }
        $hByte = if ($b.Height -ge 256) { [byte]0 } else { [byte]$b.Height }
        $bw.Write($wByte)
        $bw.Write($hByte)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$imageStreams[$i].Length)
        $bw.Write([uint32]$offset)
        $offset += $imageStreams[$i].Length
    }
    foreach ($bytes in $imageStreams) { $bw.Write($bytes) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
    $bw.Dispose()
    $ms.Dispose()
}

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

$icoPath = Join-Path $root "host\Assets\app.ico"
$pngPath = Join-Path $root "docs\brand\icon-final-256.png"
New-Item -ItemType Directory -Force -Path (Split-Path $icoPath) | Out-Null

$sizes = @(16, 32, 48, 256)
$bitmaps = @()
foreach ($s in $sizes) { $bitmaps += New-BayIconBitmap $s }
Save-Icon $bitmaps $icoPath

$png = New-BayIconBitmap 256
$png.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()
foreach ($b in $bitmaps) { $b.Dispose() }

Write-Host "Wrote $icoPath"
Write-Host "Wrote $pngPath"
