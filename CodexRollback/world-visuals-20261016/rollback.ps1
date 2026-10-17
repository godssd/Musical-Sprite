$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$expected = 'D:\unity\plan go\Musical Sprite'
if ($project -ne $expected) { throw "项目路径不符：$project" }
if (Get-Process Unity -ErrorAction SilentlyContinue) { throw '请先关闭 Unity，再回退场景文件。' }

$modified = @(
    'Assets\Scenes\SampleScene.unity',
    'Assets\Scripts\BattleCenterLine.cs',
    'Assets\Scripts\Character\FeverManager.cs',
    'Assets\Shaders\ScenePropSprite.shader'
)
$created = @(
    'Assets\Scripts\Environment\WorldVisualOwnership.cs',
    'Assets\Scripts\Environment\PlayerWorldVisualSetSO.cs',
    'Assets\Scripts\Environment\ArenaWorldVisualBinder.cs',
    'Assets\Scripts\Environment\FeverSceneryController.cs',
    'Assets\Editor\WorldVisualSceneInstaller.cs',
    'Assets\WorldVisualSets\PlayerWorld_A.asset',
    'Assets\WorldVisualSets\PlayerWorld_B.asset'
)
$snapshot = Join-Path $PSScriptRoot ('pre-rollback-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $snapshot -Force | Out-Null
foreach ($relative in ($modified + $created)) {
    foreach ($suffix in @('', '.meta')) {
        $path = Join-Path $project ($relative + $suffix)
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $destination = Join-Path $snapshot ($relative + $suffix)
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath $path -Destination $destination -Force
    }
}
foreach ($relative in $modified) {
    $source = Join-Path $PSScriptRoot $relative
    $destination = Join-Path $project $relative
    if (-not (Test-Path -LiteralPath $source)) { throw "缺少备份：$source" }
    Copy-Item -LiteralPath $source -Destination $destination -Force
}
foreach ($relative in $created) {
    foreach ($suffix in @('', '.meta')) {
        $path = Join-Path $project ($relative + $suffix)
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
}
$setFolder = Join-Path $project 'Assets\WorldVisualSets'
if (Test-Path -LiteralPath $setFolder) {
    # 只在文件夹为空时删除，不触碰之后用户放入的其他资产。
    if (@(Get-ChildItem -LiteralPath $setFolder -Force).Count -eq 0) {
        Remove-Item -LiteralPath $setFolder
        $folderMeta = $setFolder + '.meta'
        if (Test-Path -LiteralPath $folderMeta) { Remove-Item -LiteralPath $folderMeta -Force }
    }
}
Write-Output "已回退本轮世界外观接入。回退前文件保存在 $snapshot"
