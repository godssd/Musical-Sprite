# V11 第一批：判定 UI 小样与基线

日期：2026-10-24。状态：**代码已接入，离线 C# 编译通过；实际 Unity 渲染尚未验证**。

## 本批交付

- 保存改动前的相关场景、脚本、材质、设置和 .meta，并记录 SHA-256：`.codex-backups/v11-batch01-20261024-092018/manifest.json`。备份保留了进入本轮前已有的未提交改动，不是从 Git HEAD 覆盖。
- 新 `UnifiedJudgementRail` 读取实际 Spawner 的 hitPoint、laneSpacing，生成每半场四段平面判定带、五条共享辅助边。
- Press / Held 为整段柔白和较弱前向渐变；Hit 增强对应段及上下边，向场内渐隐并轻微转色。红蓝共用 Shader、几何和时间。
- 相邻 Hit 的共享边采用最大包络；高频命中不累加无限亮度。松手后衰减，沉睡清除该轨 held / hit。
- 保留原 LaneIndicator 的输入 / 命中 / 沉睡入口，绑定到新轨道；停止创建重复旧灯。旧判定线与 hitPoint 分属两个对象，已单独隐藏旧线。
- 去掉 `BattleVisualsController.Start` 无条件生成局部雾的调用。未修改已保存场景或现有全局雾设置。
- 提供隔离的 Unity 审查窗口和批量出图 / 布景材质诊断入口。

## 查看入口

Unity 导入脚本完成后：

**Tools → Musical-Sprite → V11 → 01 Judgement UI Review**

1. 点击“载入 / 刷新小样”。窗口读取真实战斗相机和原地面，建立独立预览场景。
2. 用工具栏切换“常态 / 按住 / 单轨命中 / 相邻命中 / 四轨命中”。左右会显示相同轨道和状态，便于比较。
3. 切换轨道 0–3 后点击“重播当前状态”。轨道编号跟随原 Spawner，不代表画面从上到下的固定顺序。
4. 关闭“播放衰减”可停在峰值比较；打开后观察 Hit 收尾。此窗口用于单项小样，不包含新 HUD 或新背景。
5. 在 SampleScene 正常 Play 时，既有按键和判定事件也已使用同一组件，可观察角色 / 音符在场时的遮挡。

**Tools → Musical-Sprite → V11 → 02 Capture Rail Samples And Diagnostics**

生成五种状态的实际 PNG，以及当前布景 Sprite / 材质 / Shader 支持和编译消息清单，写入本目录。它不保存 SampleScene；若需要临时加载场景，结束后关闭其加载副本。当前尚无成功生成的 PNG，不能把菜单存在当作渲染已经通过。

## 本批审查重点

| 项目 | 看什么 |
|---|---|
| 常态 | 四段薄色带、五条淡辅助线；没有黑灯座、塑料高光、中轴刻度和封闭格子 |
| 按下 | 整段明显响应，场内出现弱柔光；不是只亮一个小灯珠 |
| 命中 | 比按下更亮，轨道上下边同步增强；近亮远淡、尾端归零，路径保持直线 |
| 红蓝 | 同一状态下结构、长度、衰减相同，仅方向和颜色有区别 |
| 相邻命中 | 共享边没有比其他边额外叠亮一倍 |
| 收尾 | Hit 退回 Held / Idle；反复按松不留常亮或闪成两个重叠对象 |

这批的光强、细芯宽度、晕宽和长度是首版参数，仍需实际相机下校准。主要配置位于运行时 `JudgementUI_Red_V11` / `JudgementUI_Blue_V11` 的 `UnifiedJudgementRail`。几何宽度在配置时建立，调整宽度或间距后需重新进入 Play / 刷新小样，不能假定所有 Inspector 字段都实时重建网格。

## 验证证据和限制

- 使用本工程 Unity 6000.0.82f1 自带 Roslyn，基于缓存的实际 Editor 编译响应文件，编译当前运行时代码与编辑器代码，并加入新文件；两者退出码均为 0。日志位于基线目录下的 `compile/runtime-compile.log`、`compile/editor-compile.log`。
- 编译存在项目原有的过时 API / 未使用字段警告；本批没有因此改动其他模块。
- Unity 批处理第一次因受限环境中 Package Manager IPC 失败退出；用所需本机权限重试后 Package Manager 已连接，但 Unity 报 `No valid Unity Editor license found`，退出码 198。记录为 `unity-batch-authorized.log`。
- 因许可证问题，**尚未运行审查窗口、渲染新 Shader、生成实际截图或复现粉色布景**。离线 C# 编译不覆盖 Shader 编译、资源导入、运行生命周期和画面质量。
- 完整 Linked / Hold 节点 span 展示接入、MISS / BREAK 统一、Combo 撕碎、HP、技能及新过热素材属于后续批次。本批保留原玩法结果，不能据此判定这些需求已完成。
- 本批不修改旧 `LaneGraphic` / `LaneLampGlow` Shader，避免牵连仍使用它们的其他效果；新判定光使用独立 `JudgementSoftLight`。

## 回退

先退出 Play。恢复本批备份中的 `BattleVisualsController.cs` 和 `LaneIndicator.cs` 可恢复旧运行时入口；新增文件可保留为未接入小样。不要把备份场景覆盖到之后已经手动编辑的场景上；若要恢复其他内容，逐项与 manifest 对照。本批没有提交或推送 Git。
