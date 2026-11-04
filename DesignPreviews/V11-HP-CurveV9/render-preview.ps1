. (Join-Path $PSScriptRoot 'build-frame.ps1')
$root=$projectRoot
$hud=Join-Path $root 'Assets/Resources/BattleVisualV11/HudAssets'
$layout=Get-Content -LiteralPath (Join-Path $hud 'layout-v1.json') -Raw | ConvertFrom-Json
$details=Get-Content -LiteralPath (Join-Path $hud 'hp-details-v1.json') -Raw | ConvertFrom-Json
$atlas=[Drawing.Bitmap]::FromFile((Join-Path $hud 'digits-v1.png'))
$labels=[Drawing.Bitmap]::FromFile((Join-Path $hud 'labels-v1.png'))
$marks=[Drawing.Bitmap]::FromFile((Join-Path $hud 'hp-marks-v1.png'))
$body=[Drawing.Bitmap]::FromFile((Join-Path $root 'Assets/Art/UI/V11-Handpainted/CurveV8/hp-capsule-neutral.png'))
function Draw-Region($g,$bitmap,$region,$x,$y,$w,$h,$color) {
 $cm=[Drawing.Imaging.ColorMatrix]::new();$cm.Matrix00=$color.R/255.;$cm.Matrix11=$color.G/255.;$cm.Matrix22=$color.B/255.
 $attr=[Drawing.Imaging.ImageAttributes]::new();$attr.SetColorMatrix($cm)
 $g.DrawImage($bitmap,[Drawing.Rectangle]::new([int]$x,[int]$y,[int]$w,[int]$h),[single]$region.x,[single]($bitmap.Height-$region.y-$region.height),[single]$region.width,[single]$region.height,[Drawing.GraphicsUnit]::Pixel,$attr)
 $attr.Dispose()
}
function Glyph-Width($s,$height) {$w=0.;foreach($ch in $s.ToCharArray()) {$r=$layout.digits[[int]::Parse($ch)];$w+=$r.width/$r.height*$height+3};return $w-3}
function Number($g,$s,$x,$y,$height,$color) {foreach($ch in $s.ToCharArray()) {$r=$layout.digits[[int]::Parse($ch)];$w=$r.width/$r.height*$height;Draw-Region $g $atlas $r $x $y $w $height $color;$x+=$w+3}}
$sourcePath=[Drawing.Drawing2D.GraphicsPath]::new()
foreach($s in $segments) {$sourcePath.AddBezier([single]$s[0],[single]$s[1],[single]$s[2],[single]$s[3],[single]$s[4],[single]$s[5],[single]$s[6],[single]$s[7])}
$sourcePath.CloseFigure();$sourcePath.Flatten([Drawing.Drawing2D.Matrix]::new(),0.02)
$flat=$sourcePath.PathPoints
$sheet=[Drawing.Bitmap]::new(1900,950);$g=[Drawing.Graphics]::FromImage($sheet)
$g.Clear([Drawing.Color]::FromArgb(17,33,49));$g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias;$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font=[Drawing.Font]::new('Microsoft YaHei',22);$small=[Drawing.Font]::new('Microsoft YaHei',14);$cream=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,247,222))
$g.DrawString('V9 / 月牙顺势展开，中段持续上扬',$font,$cream,40,26)
$g.DrawString('贝塞尔母版 + 连续伸缩；红蓝共用同一轮廓镜像，示意三种分数长度。',$small,$cream,40,76)
$row=0
foreach($value in @('58','5842','584200')) {
 $y=142+$row*250;$scoreWidth=Glyph-Width $value 72;$frameWidth=400+$scoreWidth*1.04+44;$groupWidth=$frameWidth+40
 $g.DrawString(('分数 '+$value),$small,$cream,40,$y-24)
 for($side=0;$side -lt 2;$side++) {
  $color=if($side -eq 0) {[Drawing.Color]::FromArgb(255,81,75)} else {[Drawing.Color]::FromArgb(122,163,255)}
  $origin=if($side -eq 0) {40} else {1860-$groupWidth}
  $points=[Collections.Generic.List[Drawing.PointF]]::new()
  $baseWidth=[Math]::Min($frameWidth,1900*100/380);$extra=$frameWidth-$baseWidth
  foreach($p in $flat) {
   $u=($p.X+40)/1900;$t=[Math]::Clamp(($u-360/1900.)/((1900-300-360)/1900.),0.,1.)
   $blend=$t*$t*$t*(10+$t*(-15+6*$t));$x=$u*$baseWidth+$extra*$blend
   if($side -eq 1) {$x=$groupWidth-$x}
   $points.Add([Drawing.PointF]::new(($origin+$x),($y+50+($p.Y+20)*100/380)))
  }
  $shape=[Drawing.Drawing2D.GraphicsPath]::new();$shape.AddLines($points.ToArray());$shape.CloseFigure()
  $brush=[Drawing.SolidBrush]::new($color);$g.FillPath($brush,$shape);$shape.Dispose();$brush.Dispose()
  $bodyX=if($side -eq 0) {$origin+62} else {$origin+$groupWidth-62-286}
  $bodyRegion=[pscustomobject]@{x=0;y=0;width=$body.Width;height=$body.Height}
  Draw-Region $g $body $bodyRegion $bodyX ($y+70) 286 37 $color
  $hpX=if($side -eq 0) {$origin+66} else {$origin+$groupWidth-66-156}
  Draw-Region $g $labels $layout.hp $hpX ($y+29) 47 30 ([Drawing.Color]::White)
  Number $g '326' ($hpX+57) ($y+22) 38 $color
  $scoreX=if($side -eq 0) {$origin+400} else {$origin+$groupWidth-400-$scoreWidth}
  Number $g $value $scoreX ($y+4) 72 ([Drawing.Color]::White)
  $outerX=if($side -eq 0) {$origin} else {$origin+$groupWidth-52}
  Draw-Region $g $labels $layout.leaves $outerX $y 52 60 ([Drawing.Color]::White)
  $leafX=if($side -eq 0) {$origin+$frameWidth-12} else {$origin+$groupWidth-$frameWidth+12-43}
  Draw-Region $g $labels $layout.leaves $leafX ($y+12) 43 50 ([Drawing.Color]::White)
 }
 $row++
}
$g.DrawString('实际字库与素材组合；装饰线按运行时同一连续映射绘制。非 Unity 实机截图。',$small,$cream,40,902)
$g.Dispose();$sheet.Save((Join-Path $PSScriptRoot 'curve-v9-preview.png'),[Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose();$sourcePath.Dispose();$atlas.Dispose();$labels.Dispose();$marks.Dispose();$body.Dispose();$font.Dispose();$small.Dispose();$cream.Dispose()


