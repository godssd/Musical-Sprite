# Composites the exported source sprites for inspection. This is not a Unity game screenshot.
Add-Type -AssemblyName System.Drawing
$root=Join-Path $PSScriptRoot '../../Assets/Art/UI/V11-Handpainted'
$canvas=[Drawing.Bitmap]::new(1600,330)
$g=[Drawing.Graphics]::FromImage($canvas)
$g.Clear([Drawing.Color]::FromArgb(255,24,40,54))
$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
$cream=[Drawing.Color]::FromArgb(255,255,255,255)
$red=[Drawing.Color]::FromArgb(255,255,82,77)
$blue=[Drawing.Color]::FromArgb(255,122,163,255)
$font=[Drawing.Font]::new('Segoe UI',22,[Drawing.FontStyle]::Regular)
$brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,174,193,206))
function Caption($label,$x,$y){$g.DrawString($label,$font,$brush,[single]$x,[single]$y)}
function Sprite($name,$x,$y,$height,$tint,$width=0,$mirror=$false){
 $image=[Drawing.Bitmap]::FromFile((Join-Path $root ($name+'.png')))
 try{
  if($mirror){$image.RotateFlip([Drawing.RotateFlipType]::RotateNoneFlipX)}
  if($width -le 0){$width=$image.Width*$height/$image.Height}
  $matrix=[Drawing.Imaging.ColorMatrix]::new()
  $matrix.Matrix00=$tint.R/255.0; $matrix.Matrix11=$tint.G/255.0; $matrix.Matrix22=$tint.B/255.0
  $attributes=[Drawing.Imaging.ImageAttributes]::new();$attributes.SetColorMatrix($matrix)
  try{$g.DrawImage($image,[Drawing.Rectangle]::new([int]$x,[int]$y,[int]$width,[int]$height),0,0,$image.Width,$image.Height,[Drawing.GraphicsUnit]::Pixel,$attributes)}finally{$attributes.Dispose()}
  return [double]$width
 }finally{$image.Dispose()}
}
function Number($number,$x,$y,$height,$tint){
 foreach($ch in $number.ToCharArray()){$width=Sprite $ch.ToString() $x $y $height $tint;$x+=$width+5}
 return $x
}
Caption 'HP / DETAIL SPRITE COMPOSITION - NOT A UNITY SCREENSHOT' 50 25
$null=Sprite 'hp' 145 101 44 $cream
$null=Number '326' 222 90 58 $red
$null=Sprite 'hp-body' 110 175 32 $red 350
$null=Sprite 'hp-tail' 90 160 65 $red 640
$null=Sprite 'leaves' 85 72 42 $cream
$null=Sprite 'hp-strokes' 480 145 18 $red 52
$null=Sprite 'hp-dot' 550 165 7 $red
$null=Sprite 'leaves' 710 140 24 $cream
$null=Sprite 'hp' 1290 101 44 $cream
$null=Number '226' 1367 90 58 $blue
$null=Sprite 'hp-body' 1140 175 32 $blue 350
$null=Sprite 'hp-tail' 870 160 65 $blue 640 $true
$null=Sprite 'leaves' 1540 72 42 $cream 0 $true
$null=Sprite 'hp-strokes' 1068 145 18 $blue 52 $true
$null=Sprite 'hp-dot' 1043 165 7 $blue
$null=Sprite 'leaves' 870 140 24 $cream 0 $true
Caption 'Fixed leaves / three brush marks / paint dot / elongated ribbon tail' 50 273
$canvas.Save((Join-Path $PSScriptRoot 'hp-details-material-preview-v1.png'),[Drawing.Imaging.ImageFormat]::Png)
$brush.Dispose();$font.Dispose();$g.Dispose();$canvas.Dispose()
