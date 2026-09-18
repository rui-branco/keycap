param(
    [string]$Out = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSCommandPath)) "assets\keycap.ico")
)

Add-Type -AssemblyName System.Drawing

# A solid accent tile with the command mark knocked out of it.
#
# The earlier version drew a thin outline glyph on a dark tile, which washed
# out next to the solid, saturated icons either side of it on the taskbar. A
# filled tile carries the same visual weight as its neighbours, and the mark is
# both filled and stroked so its loops stay legible down at 24px.

function New-Icon([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.InterpolationMode = 'HighQualityBicubic'

    function Rounded([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r * 2
        if ($d -gt $w) { $d = $w }
        if ($d -gt $h) { $d = $h }
        $p.AddArc($x, $y, $d, $d, 180, 90)
        $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
        $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
        $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $p.CloseFigure()
        return $p
    }

    # --- tile: fills almost the whole canvas so it matches neighbouring icons
    $inset = [single]($S * 0.03)
    $side = [single]($S - $inset * 2)
    $tile = Rounded $inset $inset $side $side ([single]($S * 0.23))
    $rect = New-Object System.Drawing.RectangleF($inset, $inset, $side, $side)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 125, 211, 255),
        [System.Drawing.Color]::FromArgb(255, 44, 138, 214), 90.0)
    $g.FillPath($brush, $tile)

    # --- command mark, knocked out in the dark ground colour
    $fam = New-Object System.Drawing.FontFamily("Segoe UI Symbol")
    $fmt = [System.Drawing.StringFormat]::GenericTypographic
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gp.AddString([char]0x2318, $fam, 0, 200.0, (New-Object System.Drawing.PointF(0, 0)), $fmt)
    $b = $gp.GetBounds()

    $target = [single]($side * 0.62)
    $scale = [Math]::Min($target / $b.Width, $target / $b.Height)
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate([single]($S / 2.0), [single]($S / 2.0))
    $m.Scale([single]$scale, [single]$scale)
    $m.Translate([single](-($b.X + $b.Width / 2.0)), [single](-($b.Y + $b.Height / 2.0)))
    $gp.Transform($m)

    $ink = [System.Drawing.Color]::FromArgb(255, 16, 34, 51)
    $fill = New-Object System.Drawing.SolidBrush $ink
    # Stroking as well as filling fattens the loops, so they survive at 24px.
    $pen = New-Object System.Drawing.Pen $ink, ([single]([Math]::Max(1.0, $S * 0.045)))
    $pen.LineJoin = 'Round'
    $g.DrawPath($pen, $gp)
    $g.FillPath($fill, $gp)

    $g.Dispose()
    return $bmp
}

$sizes = @(256, 128, 64, 48, 32, 24, 16)
$pngs = @()
foreach ($s in $sizes) {
    $bm = New-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $bm.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,($ms.ToArray())
    $bm.Dispose(); $ms.Dispose()
}

$dir = Split-Path -Parent $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = $(if ($s -ge 256) { 0 } else { $s })
    $bw.Write([Byte]$dim); $bw.Write([Byte]$dim)
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush(); $bw.Close(); $fs.Close()
"written: $Out  ($((Get-Item $Out).Length) bytes, $($sizes.Count) sizes)"
