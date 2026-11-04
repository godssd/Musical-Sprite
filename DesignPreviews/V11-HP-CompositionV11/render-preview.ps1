Add-Type -AssemblyName System.Drawing
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$svg=[xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Assets/Art/UI/V11-Handpainted/CurveV9/hp-frame-red.svg') -Raw)
$d=$svg.DocumentElement.FirstChild.GetAttribute('d')
$segments=@();$previous=@(140.,12.)
foreach($match in [regex]::Matches($d,'C ([^CZ]+)')) {
 $values=@($match.Groups[1].Value.Trim() -split '\s+' | ForEach-Object {[single]::Parse($_,[Globalization.CultureInfo]::InvariantCulture)})
 $segments+=,@($previous[0],$previous[1],$values[0],$values[1],$values[2],$values[3],$values[4],$values[5])
 $previous=@($values[4],$values[5])
}
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
function Upper-Line-Y($targetX) {
 $low=0.;$high=1.
 for($iteration=0;$iteration -lt 26;$iteration++) {
  $parameter=($low+$high)*.5;$s=1-$parameter
  $sx=$s*$s*$s*280+3*$s*$s*$parameter*700+3*$s*$parameter*$parameter*1120+$parameter*$parameter*$parameter*1540
  $u=($sx+40)/1900;$t=[Math]::Clamp(($u-360/1900.)/((1900-300-360)/1900.),0.,1.)
  $blend=$t*$t*$t*(10+$t*(-15+6*$t));$mapped=$u*$baseWidth+$extra*$blend
  if($mapped -lt $targetX) {$low=$parameter} else {$high=$parameter}
 }
 $parameter=($low+$high)*.5;$s=1-$parameter
 $sy=$s*$s*$s*266+3*$s*$s*$parameter*246+3*$s*$parameter*$parameter*180+$parameter*$parameter*$parameter*145
 return (50+($sy+20)*65/380)
}
function Band-Point($x,$angle) {
 $lineY=Upper-Line-Y $x
 $slope=((Upper-Line-Y ($x+.1))-(Upper-Line-Y ($x-.1)))/.2
 $norm=[Math]::Sqrt(1+$slope*$slope)
 $radius=13-($x-73)/204
 $offset=8+$radius
 $cx=$x+$offset*$slope/$norm;$cy=$lineY-$offset/$norm
 $cos=[Math]::Cos($angle);$sin=[Math]::Sin($angle)
 $px=$cx+$radius*($slope*$cos+$sin)/$norm
 $py=$cy+$radius*(-$cos+$slope*$sin)/$norm
 if($side -eq 1) {$px=$groupWidth-$px}
 return [Drawing.PointF]::new(($origin+$px),($y+$py))
}
$sourcePath=[Drawing.Drawing2D.GraphicsPath]::new()
foreach($s in $segments) {$sourcePath.AddBezier([single]$s[0],[single]$s[1],[single]$s[2],[single]$s[3],[single]$s[4],[single]$s[5],[single]$s[6],[single]$s[7])}
$sourcePath.CloseFigure();$sourcePath.Flatten([Drawing.Drawing2D.Matrix]::new(),0.02)
$flat=$sourcePath.PathPoints
$sheet=[Drawing.Bitmap]::new(1400,880);$g=[Drawing.Graphics]::FromImage($sheet)
$g.Clear([Drawing.Color]::FromArgb(17,33,49));$g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias;$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font=[Drawing.Font]::new('Microsoft YaHei',22);$small=[Drawing.Font]::new('Microsoft YaHei',14);$cream=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,247,222))
$g.DrawString('V11 / 血槽与装饰线共用走势',$font,$cream,40,26)
$g.DrawString('血槽由同一条曲线向内偏移形成，保持间隙；不再叠放独立的横向胶囊。',$small,$cream,40,76)
$row=0
foreach($value in @('58','5842','584200')) {
 $y=148+$row*220;$scoreWidth=Glyph-Width $value 54;$frameWidth=320+$scoreWidth*1.04+28;$groupWidth=$frameWidth+30
 $g.DrawString(('分数 '+$value),$small,$cream,40,$y-24)
 for($side=0;$side -lt 2;$side++) {
  $color=if($side -eq 0) {[Drawing.Color]::FromArgb(255,81,75)} else {[Drawing.Color]::FromArgb(122,163,255)}
  $origin=if($side -eq 0) {40} else {1360-$groupWidth}
  $points=[Collections.Generic.List[Drawing.PointF]]::new()
  $baseWidth=[Math]::Min($frameWidth,1900*65/380);$extra=$frameWidth-$baseWidth
  foreach($p in $flat) {
   $u=($p.X+40)/1900;$t=[Math]::Clamp(($u-360/1900.)/((1900-300-360)/1900.),0.,1.)
   $blend=$t*$t*$t*(10+$t*(-15+6*$t));$x=$u*$baseWidth+$extra*$blend
   if($side -eq 1) {$x=$groupWidth-$x}
   $points.Add([Drawing.PointF]::new(($origin+$x),($y+50+($p.Y+20)*65/380)))
  }
  $shape=[Drawing.Drawing2D.GraphicsPath]::new();$shape.AddLines($points.ToArray());$shape.CloseFigure()
  $brush=[Drawing.SolidBrush]::new($color);$g.FillPath($brush,$shape);$shape.Dispose();$brush.Dispose()
  $bandPoints=[Collections.Generic.List[Drawing.PointF]]::new()
  for($sample=0;$sample -le 64;$sample++) {$bandPoints.Add((Band-Point (73+204*$sample/64.) 0.))}
  for($sample=1;$sample -le 24;$sample++) {$bandPoints.Add((Band-Point 277. ($sample*[Math]::PI/24)))}
  for($sample=63;$sample -ge 0;$sample--) {$bandPoints.Add((Band-Point (73+204*$sample/64.) ([Math]::PI)))}
  for($sample=1;$sample -le 24;$sample++) {$bandPoints.Add((Band-Point 73. ([Math]::PI+$sample*[Math]::PI/24)))}
  $bandPath=[Drawing.Drawing2D.GraphicsPath]::new();$bandPath.AddLines($bandPoints.ToArray());$bandPath.CloseFigure()
  $bandBrush=[Drawing.SolidBrush]::new($color);$g.FillPath($bandBrush,$bandPath);$bandBrush.Dispose();$bandPath.Dispose()
  $hpX=if($side -eq 0) {$origin+64} else {$origin+$groupWidth-64-128}
  Draw-Region $g $labels $layout.hp $hpX ($y+29) 38 24 ([Drawing.Color]::White)
  Number $g '326' ($hpX+46) ($y+23) 30 $color
  $scoreX=if($side -eq 0) {$origin+320} else {$origin+$groupWidth-320-$scoreWidth}
  Number $g $value $scoreX ($y+10) 54 ([Drawing.Color]::White)
  $outerX=if($side -eq 0) {$origin+14} else {$origin+$groupWidth-14-34}
  Draw-Region $g $labels $layout.leaves $outerX ($y+4) 34 42 ([Drawing.Color]::White)
  $leafX=if($side -eq 0) {$origin+$frameWidth-5} else {$origin+$groupWidth-$frameWidth+5-30}
  Draw-Region $g $labels $layout.leaves $leafX ($y+17) 30 35 ([Drawing.Color]::White)
 }
 $row++
}
$g.DrawString('整体排布审查稿，字库与素材保持现有风格；非实机截图，未修改游戏绑定。',$small,$cream,40,834)
$g.Dispose();$sheet.Save((Join-Path $PSScriptRoot 'composition-v11-preview.png'),[Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose();$sourcePath.Dispose();$atlas.Dispose();$labels.Dispose();$marks.Dispose();$body.Dispose();$font.Dispose();$small.Dispose();$cream.Dispose()




