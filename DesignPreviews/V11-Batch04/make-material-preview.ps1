# Composites the exported source sprites for inspection. This is not a Unity game screenshot.
Add-Type -AssemblyName System.Drawing
$root=Join-Path $PSScriptRoot '../../Assets/Art/UI/V11-Handpainted'
$canvas=[Drawing.Bitmap]::new(1600,1280)
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
Caption 'HANDPAINTED HUD / ACTUAL EXPORTED SPRITES' 50 25
Caption 'Shared 0-9 atlas - ivory / coral red / soft blue' 50 66
for($i=0;$i -lt 10;$i++){
 $null=Sprite $i.ToString() (50+$i*151) 130 118 $cream
 $null=Sprite $i.ToString() (50+$i*151) 300 118 $red
 $null=Sprite $i.ToString() (50+$i*151) 470 118 $blue
}
Caption 'HP - body and fixed tail are independent; green healing keeps the same mask' 50 645
$null=Sprite 'hp' 120 705 46 $cream
$null=Number '326' 205 700 54 $red
$null=Sprite 'hp-body' 75 775 58 $red 585
$null=Sprite 'hp-tail' 55 785 91 $red 640
$null=Sprite 'hp' 970 705 46 $cream
$null=Number '176' 1055 700 54 $blue
$null=Sprite 'hp-body' 925 775 58 $blue 585
$null=Sprite 'hp-tail' 905 785 91 $blue 640 $true
Caption 'COMBO - image digits, simple side arcs and just two small leaves' 50 925
$null=Sprite 'combo' 280 991 39 $cream
$end=Number '55' 288 1049 130 $red
$null=Sprite 'accents' 250 1084 75 $red 21 $true
$null=Sprite 'accents' ($end+6) 1084 75 $red 21
$null=Sprite 'leaves' ($end-20) 1193 26 $cream
$null=Sprite 'combo' 1110 991 39 $cream
$end=Number '3' 1155 1049 130 $blue
$null=Sprite 'accents' 1118 1084 75 $blue 21 $true
$null=Sprite 'accents' ($end+6) 1084 75 $blue 21
$null=Sprite 'leaves' ($end-16) 1193 26 $cream
$canvas.Save((Join-Path $PSScriptRoot 'handpainted-material-sheet-v1.png'),[Drawing.Imaging.ImageFormat]::Png)
$brush.Dispose();$font.Dispose();$g.Dispose();$canvas.Dispose()
