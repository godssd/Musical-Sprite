Add-Type -AssemblyName System.Drawing
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assetDir=Join-Path $projectRoot 'Assets/Art/UI/V11-Handpainted/CurveV9'
[IO.Directory]::CreateDirectory($assetDir) | Out-Null
# Absolute control points, SVG coordinates (Y down). Long upper/lower edges are each one cubic.
$segments=@(
 @(140,12,95,85,80,150,110,206),
 @(110,206,144,268,213,269.190476,280,266),
 @(280,266,700,246,1120,180,1540,145),
 @(1540,145,1652,135.666667,1766,137,1770,196),
 @(1770,196,1773,231,1738,260,1707,253),
 @(1707,253,1747,226,1734,183,1685,174),
 @(1685,174,1640,165.735,1590,160.833333,1540,165),
 @(1540,165,1120,200,700,274,280,294),
 @(280,294,213,297.190476,120,338,52,300),
 @(52,300,-16,262,5,123,140,12)
)
$path=[Drawing.Drawing2D.GraphicsPath]::new()
foreach($s in $segments) {$path.AddBezier([single]$s[0],[single]$s[1],[single]$s[2],[single]$s[3],[single]$s[4],[single]$s[5],[single]$s[6],[single]$s[7])}
$path.CloseFigure()
$metaTemplate=Get-Content -LiteralPath (Join-Path $projectRoot 'Assets/Art/UI/V11-Handpainted/CurveV8/hp-frame-red.png.meta') -Raw
function Save-Png($bmp,$name,$left,$right) {
 $target=Join-Path $assetDir $name
 $bmp.Save($target,[Drawing.Imaging.ImageFormat]::Png)
 if(-not (Test-Path -LiteralPath ($target+'.meta'))) {
  $meta=$metaTemplate -replace '(?m)^guid: .*',('guid: '+[guid]::NewGuid().ToString('N'))
  $meta=$meta -replace 'spriteBorder: \{[^}]+\}',("spriteBorder: {x: $left, y: 0, z: $right, w: 0}")
  [IO.File]::WriteAllText($target+'.meta',$meta)
 }
}
$teams=@(@{name='red';hex='#FF514B';color=[Drawing.Color]::FromArgb(255,81,75)},@{name='blue';hex='#7AA3FF';color=[Drawing.Color]::FromArgb(122,163,255)},@{name='neutral';hex='#FFFFFF';color=[Drawing.Color]::White})
$svgPath='M 140 12 '+(($segments | ForEach-Object {'C '+($_[2..7] -join ' ')}) -join ' ')+' Z'
foreach($team in $teams) {
 $large=[Drawing.Bitmap]::new(3800,760)
 $g=[Drawing.Graphics]::FromImage($large)
 $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
 $g.ScaleTransform(2,2);$g.TranslateTransform(40,20)
 $brush=[Drawing.SolidBrush]::new($team.color);$g.FillPath($brush,$path)
 $g.Dispose();$brush.Dispose()
 $bmp=[Drawing.Bitmap]::new(1900,380);$g=[Drawing.Graphics]::FromImage($bmp)
 $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
 $g.CompositingMode=[Drawing.Drawing2D.CompositingMode]::SourceCopy
 $g.DrawImage($large,[Drawing.Rectangle]::new(0,0,1900,380));$g.Dispose();$large.Dispose()
 $left=360;$right=300
 if($team.name -eq 'blue') {$bmp.RotateFlip([Drawing.RotateFlipType]::RotateNoneFlipX);$left=300;$right=360}
 Save-Png $bmp ('hp-frame-'+$team.name+'.png') $left $right
 $bmp.Dispose()
 $transform=if($team.name -eq 'blue') {'translate(1900 0) scale(-1 1) translate(40 20)'} else {'translate(40 20)'}
 $svg='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1900 380"><path transform="'+$transform+'" fill="'+$team.hex+'" d="'+$svgPath+'"/></svg>'
 [IO.File]::WriteAllText((Join-Path $assetDir ('hp-frame-'+$team.name+'.svg')),$svg)
}
$path.Dispose()
@{version=9;canvas=@(1900,380);source='Analytic cubic Bezier vector path, not generated raster';longUpper=@(@(280,266),@(700,246),@(1120,180),@(1540,145));longLower=@(@(280,294),@(700,274),@(1120,200),@(1540,165));mainCenterline=@(@(280,280),@(700,260),@(1120,190),@(1540,155));redBorder=@(360,300);blueBorder=@(300,360);curveRule='Centerline dx/dt positive, dy/dt negative throughout. No interior stationary point or reversal.'} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $assetDir 'curve-layout.json') -Encoding utf8
