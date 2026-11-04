# Mechanical atlas bounds/import preparation. Original image pixels are never modified.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$probe=[Drawing.Bitmap]::new(1,1)
$probe.Dispose()
$drawingReferences=@([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -match '^System\.(Drawing|Private\.Windows)' } | ForEach-Object { $_.Location } | Sort-Object -Unique)
Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class HudAlphaBoundsV1 {
 public static int[] Scan(string path, int columns, int rows) {
  using (var image = new Bitmap(path)) {
   var bits = image.LockBits(new Rectangle(0,0,image.Width,image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
   var data = new byte[Math.Abs(bits.Stride)*image.Height];
   Marshal.Copy(bits.Scan0,data,0,data.Length);
   image.UnlockBits(bits);
   var result = new int[columns*rows*4];
   for(int row=0;row<rows;row++) for(int col=0;col<columns;col++) {
    int x0=col*image.Width/columns,x1=(col+1)*image.Width/columns;
    int y0=row*image.Height/rows,y1=(row+1)*image.Height/rows;
    int left=x1,right=x0,top=y1,bottom=y0;
    for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)
     if(data[y*bits.Stride+x*4+3]>8){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
    if(left>right||top>bottom)throw new Exception("Empty atlas cell "+row+","+col);
    left=Math.Max(x0,left-2);right=Math.Min(x1-1,right+2);
    top=Math.Max(y0,top-2);bottom=Math.Min(y1-1,bottom+2);
    int n=(row*columns+col)*4;
    result[n]=left;result[n+1]=image.Height-bottom-1;result[n+2]=right-left+1;result[n+3]=bottom-top+1;
   }
   return result;
  }
 }
}
'@ -ReferencedAssemblies $drawingReferences
$root=Join-Path $PSScriptRoot '../../Assets/Resources/BattleVisualV11/HudAssets'
function Region($array,$i) { [ordered]@{x=$array[$i*4]; y=$array[$i*4+1]; width=$array[$i*4+2]; height=$array[$i*4+3]} }
$digits=[HudAlphaBoundsV1]::Scan((Join-Path $root 'digits-v1.png'),5,2)
$labels=[HudAlphaBoundsV1]::Scan((Join-Path $root 'labels-v1.png'),2,2)
$ribbon=[HudAlphaBoundsV1]::Scan((Join-Path $root 'ribbon-v1.png'),1,2)
$data=[ordered]@{digits=@(0..9 | ForEach-Object {Region $digits $_}); hp=(Region $labels 0); combo=(Region $labels 1); leaves=(Region $labels 2); accents=(Region $labels 3); body=(Region $ribbon 0); tail=(Region $ribbon 1)}
[IO.File]::WriteAllText((Join-Path $root 'layout-v1.json'),($data | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
foreach($file in @('digits-v1.png','labels-v1.png','ribbon-v1.png')) {
 $meta=Join-Path $root ($file+'.meta')
 if(Test-Path -LiteralPath $meta){continue}
 $guid=[guid]::NewGuid().ToString('N')
 @"
fileFormatVersion: 2
guid: $guid
TextureImporter:
  serializedVersion: 13
  mipmaps:
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  maxTextureSize: 2048
  textureType: 0
  textureShape: 1
  alphaSource: 1
  alphaIsTransparency: 1
  npotScale: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    overridden: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content -LiteralPath $meta -Encoding UTF8
}
$data | ConvertTo-Json -Depth 6

# Export independent transparent sprites for artists / Inspector assignment; runtime keeps one atlas.
$exportRoot=Join-Path $PSScriptRoot '../../Assets/Art/UI/V11-Handpainted'
New-Item -ItemType Directory -Path $exportRoot -Force | Out-Null
function Export-Region($atlas,$region,$name) {
 $source=[Drawing.Bitmap]::FromFile((Join-Path $root $atlas))
 try {
  $rectangle=[Drawing.Rectangle]::new($region.x,($source.Height-$region.y-$region.height),$region.width,$region.height)
  $crop=$source.Clone($rectangle,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
  try { $crop.Save((Join-Path $exportRoot ($name+'.png')),[Drawing.Imaging.ImageFormat]::Png) } finally {$crop.Dispose()}
 } finally {$source.Dispose()}
 $meta=Join-Path $exportRoot ($name+'.png.meta')
 if(!(Test-Path -LiteralPath $meta)) {
  $guid=[guid]::NewGuid().ToString('N')
  @"
fileFormatVersion: 2
guid: $guid
TextureImporter:
  serializedVersion: 13
  mipmaps:
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    wrapU: 1
    wrapV: 1
    wrapW: 1
  maxTextureSize: 2048
  textureType: 8
  textureShape: 1
  spriteMode: 1
  spritePixelsToUnits: 100
  spriteMeshType: 0
  alphaSource: 1
  alphaIsTransparency: 1
  npotScale: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    textureFormat: -1
    textureCompression: 0
    overridden: 0
"@ | Set-Content -LiteralPath $meta -Encoding UTF8
 }
}
for($i=0;$i -lt 10;$i++){ Export-Region 'digits-v1.png' $data.digits[$i] $i.ToString() }
foreach($name in @('hp','combo','leaves','accents')){ Export-Region 'labels-v1.png' $data[$name] $name }
foreach($name in @('body','tail')){ Export-Region 'ribbon-v1.png' $data[$name] ('hp-'+$name) }
