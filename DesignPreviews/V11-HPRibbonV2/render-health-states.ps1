Add-Type -AssemblyName System.Drawing
$assetDir = Join-Path $PSScriptRoot '../../Assets/Art/UI/V11-Handpainted/RibbonV2'
$fixed = [System.Drawing.Bitmap]::FromFile((Join-Path $assetDir 'hp-fixed-neutral-aligned-v2.png'))
$fill = [System.Drawing.Bitmap]::FromFile((Join-Path $assetDir 'hp-fill-aligned-v2.png'))
$sheet = [System.Drawing.Bitmap]::new(1600, 1020)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::FromArgb(25,43,56))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font = [System.Drawing.Font]::new('Microsoft YaHei',22)
$small = [System.Drawing.Font]::new('Microsoft YaHei',14)
$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(246,236,214))
$g.DrawString('新版血条 · 静态血量对比', $font, $brush, 42, 26)
$g.DrawString('同一素材与尺寸 / 红方与蓝方镜像 / 未加入扣血、回血过渡', $small, $brush, 44, 72)

function Draw-Tinted($graphics, $source, $color, $cropWidth) {
    $matrix = [System.Drawing.Imaging.ColorMatrix]::new()
    $matrix.Matrix00 = $color.R / 255.0
    $matrix.Matrix11 = $color.G / 255.0
    $matrix.Matrix22 = $color.B / 255.0
    $attrs = [System.Drawing.Imaging.ImageAttributes]::new()
    $attrs.SetColorMatrix($matrix)
    if ($cropWidth -gt 0) {
        $graphics.SetClip([System.Drawing.Rectangle]::new(0,0,$cropWidth,724))
    }
    $graphics.DrawImage($source,[System.Drawing.Rectangle]::new(0,0,2172,724),0,0,2172,724,[System.Drawing.GraphicsUnit]::Pixel,$attrs)
    $graphics.ResetClip()
    $attrs.Dispose()
}

$states = @(@{label='满血'; value=1.0}, @{label='半血'; value=0.5}, @{label='残血'; value=0.15})
for ($row=0; $row -lt 3; $row++) {
    $state = $states[$row]
    $y = 128 + $row * 282
    $g.DrawString(('{0}  {1:0}%' -f $state.label, ($state.value*100)), $font, $brush, 44, $y)
    for ($side=0; $side -lt 2; $side++) {
        $layer = [System.Drawing.Bitmap]::new(2172,724)
        $lg = [System.Drawing.Graphics]::FromImage($layer)
        $color = if ($side -eq 0) {[System.Drawing.Color]::FromArgb(255,81,75)} else {[System.Drawing.Color]::FromArgb(122,163,255)}
        Draw-Tinted $lg $fixed $color 0
        $edge = [int](234 + 851 * $state.value)
        Draw-Tinted $lg $fill $color $edge
        $lg.Dispose()
        if ($side -eq 1) {$layer.RotateFlip([System.Drawing.RotateFlipType]::RotateNoneFlipX)}
        $x = if ($side -eq 0) {38} else {824}
        $g.DrawImage($layer,[System.Drawing.Rectangle]::new($x,($y+58),740,116),70,218,2040,320,[System.Drawing.GraphicsUnit]::Pixel)
        $layer.Dispose()
    }
}
$g.DrawString('素材模拟预览，非 Unity 实机截图；残血以 15% 示意。', $small, $brush, 44, 970)
$g.Dispose()
$sheet.Save((Join-Path $PSScriptRoot 'hp-health-states-v2.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose(); $fixed.Dispose(); $fill.Dispose(); $font.Dispose(); $small.Dispose(); $brush.Dispose()

