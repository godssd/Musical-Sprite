$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assetDir=Join-Path $projectRoot 'Assets/Art/UI/V11-Handpainted/FullRibbonStaticV1'
if(-not ('TeamHudAssetPack' -as [type])) {
    $refs=@([System.Drawing.Bitmap].Assembly.Location,[System.Drawing.Color].Assembly.Location)
    $refs += Get-ChildItem $PSHOME -Filter 'System.Private.Windows*.dll' | Select-Object -ExpandProperty FullName
    $refs += Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | Select-Object -ExpandProperty FullName
    Add-Type -Path (Join-Path $PSScriptRoot 'PackAssets.cs') -ReferencedAssemblies ($refs | Select-Object -Unique)
}
$ribbon=Join-Path $PSScriptRoot 'ribbon-source.png'
$parts=Join-Path $PSScriptRoot 'parts-source.png'
[TeamHudAssetPack]::Run($projectRoot,$ribbon,$parts,$assetDir)
[TeamHudAssetPack]::Preview($assetDir,(Join-Path $PSScriptRoot 'static-assembly-review.png'))
[TeamHudAssetPack]::Verify($assetDir) | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'asset-validation.txt') -Encoding utf8
$template=Get-Content -LiteralPath (Join-Path $projectRoot 'Assets/Art/UI/V11-Handpainted/CurveV9/hp-frame-neutral.png.meta') -Raw
foreach($file in Get-ChildItem $assetDir -Filter '*.png') {
    $metaPath=$file.FullName+'.meta'
    if(Test-Path -LiteralPath $metaPath){
        $current=Get-Content -LiteralPath $metaPath -Raw
        $current=$current -replace '(?m)^  spriteMeshType: .*','  spriteMeshType: 0'
        [System.IO.File]::WriteAllText($metaPath,$current)
        continue
    }
    $meta=$template -replace '(?m)^guid: .*',('guid: '+[guid]::NewGuid().ToString('N'))
    $meta=$meta -replace 'spriteBorder: \{[^}]*\}','spriteBorder: {x: 0, y: 0, z: 0, w: 0}'
    $meta=$meta -replace '(?m)^  spriteMeshType: .*','  spriteMeshType: 0'
    $meta=$meta -replace '(?m)^  spriteGenerateFallbackPhysicsShape: .*','  spriteGenerateFallbackPhysicsShape: 0'
    $meta=$meta -replace '(?m)^  spriteID: .*',('  spriteID: '+[guid]::NewGuid().ToString('N'))
    [System.IO.File]::WriteAllText($metaPath,$meta)
}
$records=@()
foreach($file in Get-ChildItem $assetDir -Filter '*.png'){
    $b=[System.Drawing.Bitmap]::FromFile($file.FullName)
    $records+=@{file=$file.Name;width=$b.Width;height=$b.Height;alphaCorner=$b.GetPixel(0,0).A}
    $b.Dispose()
}
$records | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'asset-inventory.json') -Encoding utf8
Write-Output ('Prepared '+$records.Count+' PNG sprites; preview and inventory saved.')
