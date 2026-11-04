Add-Type -AssemblyName System.Drawing
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assetDir = Join-Path $projectRoot 'Assets/Art/UI/V11-Handpainted/ConnectedV5'
[System.IO.Directory]::CreateDirectory($assetDir) | Out-Null
$oldDir = Join-Path $projectRoot 'Assets/Art/UI/V11-Handpainted'
$metaTemplate = Get-Content -LiteralPath (Join-Path $oldDir 'RibbonV2/hp-fill-aligned-v2.png.meta') -Raw
$script:boundsCache = @{}
function Alpha-Bounds($bmp) {
    $data=$bmp.LockBits([System.Drawing.Rectangle]::new(0,0,$bmp.Width,$bmp.Height),[System.Drawing.Imaging.ImageLockMode]::ReadOnly,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes=[byte[]]::new($data.Stride*$data.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0,$bytes,0,$bytes.Length)
    $minX=$bmp.Width; $minY=$bmp.Height; $maxX=0; $maxY=0
    for($y=0;$y -lt $bmp.Height;$y++) {for($x=0;$x -lt $bmp.Width;$x++) {
        if($bytes[$y*$data.Stride+$x*4+3] -gt 32) {
            $minX=[Math]::Min($minX,$x);$maxX=[Math]::Max($maxX,$x);$minY=[Math]::Min($minY,$y);$maxY=[Math]::Max($maxY,$y)
        }
    }}
    $bmp.UnlockBits($data)
    return [System.Drawing.Rectangle]::new($minX,$minY,$maxX-$minX+1,$maxY-$minY+1)
}
function Save-Sprite($bmp,$name,$left=0,$right=0) {
    $path=Join-Path $assetDir $name
    $bmp.Save($path,[System.Drawing.Imaging.ImageFormat]::Png)
    if(-not (Test-Path -LiteralPath ($path+'.meta'))) {
        $meta=$metaTemplate -replace '(?m)^guid: .*',('guid: '+[guid]::NewGuid().ToString('N'))
        $meta=$meta -replace 'spriteBorder: \{x: 0, y: 0, z: 0, w: 0\}',("spriteBorder: {x: $left, y: 0, z: $right, w: 0}")
        [System.IO.File]::WriteAllText($path+'.meta',$meta)
    }
}
function Tint($bmp,$color) {
    $out=[System.Drawing.Bitmap]::new($bmp.Width,$bmp.Height)
    $g=[System.Drawing.Graphics]::FromImage($out)
    $matrix=[System.Drawing.Imaging.ColorMatrix]::new()
    $matrix.Matrix00=$color.R/255.0;$matrix.Matrix11=$color.G/255.0;$matrix.Matrix22=$color.B/255.0
    $attrs=[System.Drawing.Imaging.ImageAttributes]::new();$attrs.SetColorMatrix($matrix)
    $g.DrawImage($bmp,[System.Drawing.Rectangle]::new(0,0,$bmp.Width,$bmp.Height),0,0,$bmp.Width,$bmp.Height,[System.Drawing.GraphicsUnit]::Pixel,$attrs)
    $g.Dispose();$attrs.Dispose();return ,$out
}
$red=[System.Drawing.Color]::FromArgb(255,81,75)
$blue=[System.Drawing.Color]::FromArgb(122,163,255)
$cream=[System.Drawing.Color]::FromArgb(255,247,222)
$sources=@{
    'frame'='C:/Users/Administrator/.codex/generated_images/01a123cd-6eaf-7fe3-bf0e-8f1720bef4b2/exec-71a825b1-cc5a-4d32-bc50-a201a7b16098.png'
    'fill'='C:/Users/Administrator/.codex/generated_images/01a123cd-6eaf-7fe3-bf0e-8f1720bef4b2/exec-d9607fb4-8d0d-4d99-8dcb-41d126a99f50.png'
}
$materials=@{};$records=@()
foreach($key in @('frame','fill')) {
    $src=[System.Drawing.Bitmap]::FromFile($sources[$key])
    $bounds=Alpha-Bounds $src
    $trim=$src.Clone($bounds,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $name=if($key -eq 'frame') {'hp-connected-neutral.png'} else {'hp-capsule-neutral.png'}
    $left=if($key -eq 'frame') {600} else {0};$right=if($key -eq 'frame') {280} else {0}
    Save-Sprite $trim $name $left $right
    $materials[$key]=$trim
    $records+=@{file=$name;sourceCanvas=@($src.Width,$src.Height);trimRect=@($bounds.X,$bounds.Y,$bounds.Width,$bounds.Height);borderLeft=$left;borderRight=$right}
    Copy-Item -LiteralPath $sources[$key] -Destination (Join-Path $PSScriptRoot ($key+'-source.png'))
    $src.Dispose()
}
foreach($team in @('red','blue')) {
    $color=if($team -eq 'red') {$red} else {$blue}
    foreach($key in @('frame','fill')) {
        $variant=Tint $materials[$key] $color
        $left=if($key -eq 'frame') {600} else {0};$right=if($key -eq 'frame') {280} else {0}
        if($team -eq 'blue') {$variant.RotateFlip([System.Drawing.RotateFlipType]::RotateNoneFlipX);$swap=$left;$left=$right;$right=$swap}
        Save-Sprite $variant ('hp-'+$key+'-'+$team+'.png') $left $right
        $variant.Dispose()
    }
}
$mirroredBody=$materials.fill.Clone()
$mirroredBody.RotateFlip([System.Drawing.RotateFlipType]::RotateNoneFlipX)
Save-Sprite $mirroredBody 'hp-capsule-blue-neutral.png'
$mirroredBody.Dispose()
$frame=$materials.frame
$slices=@(@{name='left';x=0;w=600},@{name='middle';x=600;w=$frame.Width-880},@{name='right';x=$frame.Width-280;w=280})
foreach($slice in $slices) {
    $part=$frame.Clone([System.Drawing.Rectangle]::new($slice.x,0,$slice.w,$frame.Height),[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    Save-Sprite $part ('hp-connected-'+$slice.name+'-neutral.png')
    $part.Dispose()
}
foreach($part in @('leaves.png','hp-strokes.png','hp-dot.png','hp.png')) {
    $bmp=[System.Drawing.Bitmap]::FromFile((Join-Path $oldDir $part));Save-Sprite $bmp $part;$bmp.Dispose()
}
@{version=5;purpose='Connected crescent and extendable score underline';materials=$records;existingGlyphs='../0.png through ../9.png';redGrowth='right';blueGrowth='left';frameSizing='Horizontal 3-slice only. Maintain native aspect of left/right caps. Match strip seam in source. Do not scale the complete frame horizontally.'} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $assetDir 'layout.json') -Encoding utf8

# Reference composition uses actual production sprite glyphs; this is not a game screenshot.
$glyphs=@{};$glyphBounds=@{}
foreach($n in 0..9) {$bmp=[System.Drawing.Bitmap]::FromFile((Join-Path $oldDir ($n.ToString()+'.png')));$glyphs[$n.ToString()]=$bmp;$glyphBounds[$n.ToString()]=Alpha-Bounds $bmp}
$hp=[System.Drawing.Bitmap]::FromFile((Join-Path $oldDir 'hp.png'));$hpBounds=Alpha-Bounds $hp
$leaf=[System.Drawing.Bitmap]::FromFile((Join-Path $oldDir 'leaves.png'));$leafBounds=Alpha-Bounds $leaf
$marks=[System.Drawing.Bitmap]::FromFile((Join-Path $oldDir 'hp-strokes.png'));$marksBounds=Alpha-Bounds $marks
function Draw-Part($g,$bmp,$src,$x,$y,$w,$h,$color) {
    $matrix=[System.Drawing.Imaging.ColorMatrix]::new();$matrix.Matrix00=$color.R/255.;$matrix.Matrix11=$color.G/255.;$matrix.Matrix22=$color.B/255.
    $attrs=[System.Drawing.Imaging.ImageAttributes]::new();$attrs.SetColorMatrix($matrix)
    $g.DrawImage($bmp,[System.Drawing.Rectangle]::new([int]$x,[int]$y,[int]$w,[int]$h),$src.X,$src.Y,$src.Width,$src.Height,[System.Drawing.GraphicsUnit]::Pixel,$attrs);$attrs.Dispose()
}
function Number-Width($value,$height) {
    $w=0.0;foreach($ch in $value.ToCharArray()) {$r=$glyphBounds[$ch.ToString()];$w+=$height*$r.Width/$r.Height+4};return $w-4
}
function Draw-Number($g,$value,$x,$y,$height,$color) {
    foreach($ch in $value.ToCharArray()) {$key=$ch.ToString();$r=$glyphBounds[$key];$w=$height*$r.Width/$r.Height;Draw-Part $g $glyphs[$key] $r $x $y $w $height $color;$x+=$w+4}
}
$sheet=[System.Drawing.Bitmap]::new(1900,1000);$g=[System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::FromArgb(17,33,49));$g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font=[System.Drawing.Font]::new('Microsoft YaHei',21);$small=[System.Drawing.Font]::new('Microsoft YaHei',14);$brush=[System.Drawing.SolidBrush]::new($cream)
$g.DrawString('连体月牙与底线 / 手绘素材组合预览',$font,$brush,38,24)
$g.DrawString('红方向右增长，蓝方向左增长；字高固定，仅延长线段并移动卷钩与叶片。',$small,$brush,40,68)
$frame=$materials.frame;$body=$materials.fill
$frameRect=[System.Drawing.Rectangle]::new(0,0,$frame.Width,$frame.Height);$bodyRect=[System.Drawing.Rectangle]::new(0,0,$body.Width,$body.Height)
$row=0
foreach($value in @('58','5842','584200')) {
    $y=140+$row*264
    $g.DrawString(('分数 '+$value),$small,$brush,40,$y-20)
    for($side=0;$side -lt 2;$side++) {
        $color=if($side -eq 0) {$red} else {$blue}
        $scoreW=Number-Width $value 72
        $frameW=[int](400+$scoreW+44)
        $h=[int]($frame.Height*0.30);$leftW=180;$rightW=84;$midW=$frameW-$leftW-$rightW
        $layer=[System.Drawing.Bitmap]::new($frameW+44,200);$lg=[System.Drawing.Graphics]::FromImage($layer);$lg.InterpolationMode=$g.InterpolationMode
        Draw-Part $lg $frame ([System.Drawing.Rectangle]::new(0,0,600,$frame.Height)) 0 50 $leftW $h $color
        Draw-Part $lg $frame ([System.Drawing.Rectangle]::new(600,0,$frame.Width-880,$frame.Height)) $leftW 50 $midW $h $color
        Draw-Part $lg $frame ([System.Drawing.Rectangle]::new($frame.Width-280,0,280,$frame.Height)) ($leftW+$midW) 50 $rightW $h $color
        Draw-Part $lg $body $bodyRect 62 70 286 37 $color
        Draw-Part $lg $leaf $leafBounds 0 0 52 60 ([System.Drawing.Color]::White)
        Draw-Part $lg $leaf $leafBounds ($frameW-12) 12 43 50 ([System.Drawing.Color]::White)
        Draw-Part $lg $marks $marksBounds 358 69 30 18 $color
        $lg.Dispose()
        if($side -eq 1) {$layer.RotateFlip([System.Drawing.RotateFlipType]::RotateNoneFlipX)}
        $origin=if($side -eq 0) {40} else {1860-$layer.Width}
        $g.DrawImageUnscaled($layer,[int]$origin,[int]$y)
        $hpX=if($side -eq 0) {$origin+66} else {$origin+$layer.Width-66-148}
        Draw-Part $g $hp $hpBounds $hpX ($y+29) 47 30 $cream
        Draw-Number $g '326' ($hpX+55) ($y+22) 38 $color
        $scoreX=if($side -eq 0) {$origin+400} else {$origin+$layer.Width-400-$scoreW}
        Draw-Number $g $value $scoreX ($y+1) 72 $cream
        $layer.Dispose()
    }
    $row++
}
$g.DrawString('均为满血；数字使用现有 0–9 图片字库。素材合成示意，非 Unity 实机画面。',$small,$brush,40,948)
$g.Dispose();$sheet.Save((Join-Path $PSScriptRoot 'connected-materials-preview.png'),[System.Drawing.Imaging.ImageFormat]::Png);$sheet.Dispose()
foreach($bmp in $materials.Values) {$bmp.Dispose()};foreach($bmp in $glyphs.Values) {$bmp.Dispose()};$hp.Dispose();$leaf.Dispose();$marks.Dispose();$font.Dispose();$small.Dispose();$brush.Dispose()


