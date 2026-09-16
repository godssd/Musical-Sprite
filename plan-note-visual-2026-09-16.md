# 音符视觉迭代方案（对齐版 · 不执行）【已废弃】

> ⚠️ 方向已调整（2026-09-16 16:12）：音符整体改走 **Spine 骨骼**，不再用多层平面 quad。
> 最新单一事实源见 `plan-note-spine-2026-09-16.md`，本文件仅留作历史参考。

> 日期：2026-09-16 · 范围：仅普通点击音符（plain tap）的视觉层迭代
> 纪律：先对齐不执行、每步独立备份可回退、参数 Inspector 可调、旧实现隐藏不删

## 0. 当前代码锚点（已核实）
- 网格：`TapRoundedRectMesh` 是带侧壁的厚板（顶点 y=±0.5，`localScale.y=0.12`）→ “厚度”来源
- 材质：`MakeTapMaterial` = URP Unlit Transparent + `_BaseMap`，`renderQueue=Transparent`（透明已支持）
- 渲染：单 MeshRenderer + 单材质，`rend.enabled` 控制显隐；`SetNoteTint` 只改 `_BaseColor`
- 尺寸：`noteRadius` → xDiameter/zDiameter，`isPlainTap` 按贴图比例；与轨宽无关联
- 轨宽：判定用 `laneSpacing`，视觉轨道 quad 另算（两套参数）

## 1. 第3点：只显贴图、不显模型（根因 + 落地）
**根因**：厚板 mesh 有 0.12 侧壁 + 平板画布比胶囊大 → 即使贴图边缘透明，侧壁几何 + 画布轮廓仍显形。
**落地**：
- 网格换零厚度单面 quad（4 顶点、XZ 平面、无侧壁），胶囊外透明区天然不渲染
- `localScale.y`→0（仅留极小 layering 偏移）；贴图比例逻辑保留
- 材质加 `ZWrite=false` + renderQueue 高于地面（不与地面 z-fight、透明区不写深度）
- 旧厚板用 `useFlatQuad` 开关保留，可回退
- 验收：贴图外完全不可见，只看见胶囊

## 2. 第1点：音轨场景内可拖动
- 新建 `NoteTrackRig` MonoBehaviour，持有视觉轨宽 `trackVisualWidthScale`（与判定 laneSpacing 解耦）
- 配套 `Editor`（`[CustomEditor]` + `OnSceneGUI`）：场景画可拖动手柄（左右轨缘 `Handles.Slider`），拖动实时写回并刷新轨道 quad
- Inspector 同步暴露（含 Play 可改）
- 流程：你拖→截图→报数→我定死默认

## 3. 第2点：大小 Play 下可调 + 可读
- 新增 `noteSizeScale`（live 倍率），Init 算完基准尺寸 × 它；`ApplyVisualScale()` 变更即时套用
- 与轨宽解耦：加 `tapLaneRatio`（音符宽 = 轨宽 × ratio），调轨宽音符自动跟
- `tapVisualScaleMul` 保留兼容
- 流程：你 Play 拖→截图→报数→我烤进默认

## 4. 第4点：分层结构（你画图，我实现）
- `NoteVisual` 容器下挂 N 个 layer quad（你拆几部分 = 几层）
- 数据驱动：`NoteLayerDef { tex; localOffset; blendMode(Alpha/Additive); alpha; renderQueue }`
- 默认层（映射你画的部件）：L0 阴影 / L1 光晕(Additive) / L2 本体(透地) / L3 高光(描边)
- 每层独立材质实例 + MaterialPropertyBlock；`SetNoteTint/SetAlpha` 改遍历层
- 立体感来自 localOffset 的 Y/Z 微差 + 缩放，非 mesh 厚度
- 显隐/命中协程操作对象改挂 `NoteVisual` 根（唯一重构风险点）

## 5. 第5点：Spine 是否引入（权衡）
- 利：骨骼动画做 squash/jiggle/复杂命中强；项目已用 spine-unity
- 弊：note 躺平 world XZ，spine 是 screen-facing 2D 需 −90°X 旋转；真 3D 深度难做（第4点受限）；每 note 一 skeleton 开销 > quad
- 建议：普通 tap 用分层 quad；Spine 只留给特殊音符（美味牛角包/BOSS/hero 主音符）

## 6. 第6点：透明（材质实现）
- Alpha 层：`ZWrite=false, Blend SrcAlpha/OneMinusSrcAlpha, renderQueue 高于地面`
- Additive 层：`Blend One/One, ZWrite=false`
- 本体 alpha 0.7~0.85 透出地面；地面 opaque 写深度，note 层 ZTest LEqual + 微 Y 偏移防 z-fight
- `SetNoteTint` 兼容（`_BaseColor` 不变）

## 7. 第7点：命中/消失新表现（先定接口）
- 保留 HitCoroutine/MissCoroutine 骨架，操作对象改 `NoteVisual`
- 候选：命中=光晕爆亮 + 本体 squash(XZ 弹开)；Miss=去饱和 + 缩小
- 具体等你描述

## 8. 实施顺序（每步独立备份、可回退、逐个验证）
A 厚度/尺寸/轨宽(1·2·3几何) → B 分层结构(4) → C 组件动态 → D 透明排序(6) → E 命中(7)
Spine 决策为独立分支。

## 9. 待你拍板
1. 轨道拖动手柄：`Handles.Slider` 边缘拖，还是直接给 `trackVisualWidthScale` 你拉 Inspector？
2. 大小 live 缩放接受每帧套用吗（几十 note 无压力）？
3. 分层贴图层数/命名：你拆几部分？每部分职责（阴影/光晕/本体/高光）？
4. Spine：全量 / 混合 / 纯 quad？
5. 透明程度：本体 alpha 目标值？
6. 命中表现具体设计（爆裂/水花/其他）？

## 10. 第二轮对齐记录（2026-09-16 15:49）
- 第3点更新：用户给体积参考草图（顶面+前侧可见的果冻体块），要"有体积的感觉"而非纯平面片。
  - 方案一 手绘 2.5D 单图：一张图自带顶面+侧面，零厚度面片展示（推荐主体）
  - 方案二 分层错位：体块层+顶面层 Y 微错位，命中时层间视差（与第4点拆层同一件事）
  - 方案三 真3D果冻网格+半透明shader（后手，不先做）
  - **推荐：一+二组合**；依据=俯视锁视角相机角度恒定，假体积观感稳定
- Q1 细说：Scene 视图轨带左右缘各画一个 Handles.Slider 手柄，鼠标拖动→轨带 quad 实时变宽→Inspector 的 trackVisualWidthScale 同步写回；laneSpacing（判定点）不动，玩法零风险。用户拖好截图报数→我定死默认。
- Q2 展开：新增 noteSizeScale live 旋钮；音符显示尺寸=基准尺寸×noteSizeScale；值变化时才刷新（非每帧白算）。默认只缩"显示"不缩判定范围(noteRadius)；用户 Play 拖→截图报数→我烤进默认后显示与判定重新对齐。
- Q3：用户拆好图后给清单（每张的职责/偏移/混合）。
- Q4：Spine 混用拍板——分层 quad 与 spine 音符共存，均挂 NoteVisual 根、同一套 renderQueue 排序与显隐接口；spine 的颜色/透明经 skeleton color 适配进 SetNoteTint。冲突点仅排序与着色两处，接口层解决。
- Q5：果冻感透明=本体 alpha 约 0.75~0.85 + additive 边缘光，具体跟效果图调。
- Q6：命中效果参考图有方向，细节制作时细聊；保留 HitCoroutine/MissCoroutine 骨架换内容。
