# 世界外观接入回退点（2026-10-16）

本目录保存了本轮修改前的 `SampleScene.unity`、`BattleCenterLine.cs`、`FeverManager.cs`、`ScenePropSprite.shader` 等原件，以及开始前的 `git status`。现有炸弹、冲击波、地面材质和用户新画的 PNG 不属于本轮回退范围。

`SampleScene.before-visibility-fix.unity` 另外保存了第一次接入后的场景，可用于只比较第二次可见性调整。完整撤销仍使用下述脚本。

需要撤销本轮接入时，先关闭 Unity，然后在项目根目录运行：

```powershell
& '.\CodexRollback\world-visuals-20261016\rollback.ps1'
```

脚本会先保存回退前的当前文件副本，再恢复旧场景和旧代码；仅删除本轮新建的脚本、配置资产及其 `.meta`。用户原有 PNG 和此前未提交的 VFX 工作不会被删除。

注意：回退会撤销之后在 `SampleScene.unity` 中继续做的摆放调整。脚本保存的 `pre-rollback-*` 目录可用于找回这些后续调整。
