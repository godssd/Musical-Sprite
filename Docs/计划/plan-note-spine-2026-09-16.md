# 音符视觉方案（Spine 版）· 计划表

> 状态：对齐完成，待执行（每步独立备份、可回退、参数 Inspector 可调、旧实现隐藏不删）
> 创建：2026-09-16 · 适用范围：仅普通点击音符（plain tap）视觉层
> 单一事实源：本文件取代旧 `plan-note-visual-2026-09-16.md`（旧文档已废弃，方向见 §0）

## 0. 方向总决策
- **放弃**「多层平面 quad 分层」方案
- 音符整体改用 **Spine 骨骼动画**（spine-unity 4.3 + urp-shaders 模块，项目已装）
- 理由：拆分部件在 Spine 里是天然 slot/bone，比手写多层 quad 更顺、好维护；动态/透明/命中全在同一套骨骼里
- 配套：轨道（几何/宽度）仍用 Unity 原生 quad + 场景手柄；Spine 只管音符本体

## 1. 关键可行性结论（原问：Spine 能否给特定部件加不同材质）
**可以，且是 spine-unity 4.3 标准机制（已核实）。**
- 机制：`SkeletonRenderSeparator` + `SkeletonPartsRenderer` 按 slot 分组拆成多个独立 MeshRenderer，每个 part 有独立 Material → 可设不同混合/着色
- 三种做法：
  1. 不同混合模式（发光 Additive / 半透明 Alpha / 噪点 Multiply）：Spine 编辑器给 slot 设 blend mode，SkeletonDataAsset 挂 `Default BlendModeMaterials` 资源 → 加载即正确材质（最省事）
  2. 整 part 换材质/Shader（噪点 Shader Graph / 溶解）：给对应 `SkeletonPartsRenderer.MeshRenderer.material` 赋自定义 URP 材质
  3. 同 Shader 仅改属性（颜色/强度）：`MaterialPropertyBlock` 设到 part 的 MeshRenderer（如 `_FillColor`）
- 另：单 slot 换材质可用 `SkeletonRenderer.CustomSlotMaterials[slot] = mat`
- 排序：`SkeletonPartsRenderer.MeshRenderer.sortingOrder` 控制层叠（地面/阴影/光晕/本体/高光）
- 已知坑（4.3）：重导入后主 MeshRenderer 偶尔需重 toggle 刷新；多材质 submesh 警告属提示级，可忽略
- 结论：发光/半透明/噪点/其他全部可行；美术在 Spine 编辑器分层 + 设 blend，代码侧只做一次 Separator 配置

## 2. 原始 7 点需求 → Spine 方案映射表
| # | 用户需求 | Spine 实现 | 负责人 | 状态 |
|---|---|---|---|---|
| 1 | 音轨宽度场景内可拖动 | 轨道 quad 独立 `trackVisualWidthScale` + 场景 Handles.Slider 手柄（判定 laneSpacing 不动） | AI | 待执行 |
| 2 | 音符大小 Play 可调+可读 | Spine 根物体 scale 用 live 旋钮 `noteSizeScale`，Play 拖值实时缩放，报数烤默认 | AI | 待执行 |
| 3 | 去厚度/只显贴图/有体积 | Spine 骨骼无 3D 厚板；体积感由美术在骨架画（顶面+侧）+ 命中 squash 骨骼动画；去掉 NoteMover 里 Y=0.12 厚板 mesh | 用户(画)+AI | 待执行 |
| 4 | 拆多组件空间分层立体 | Spine 多 slot/bone，美术在编辑器分层；空间排列=bone 位置/深度 | 用户(画)+AI(挂) | 待执行 |
| 5 | 部分组件动态 | Spine 骨骼动画（呼吸/抖/扫光）美术 author，或运行时驱动 bone | 用户(动画)+AI | 待执行 |
| 6 | 半透明透地 | Separator 把本体 slot 组设 Alpha 半透明材质（alpha~0.8）+ 边缘光 slot 设 Additive；ZWrite 处理 | AI | 待执行 |
| 7 | 命中/消失新表现 | Spine 命中动画（爆亮/弹开）美术 author；AI 在 HitCoroutine 里 Play 对应 animation；旧 tint 协程废弃 | 用户(动画)+AI | 待执行 |
| 补充 | 不同部件不同材质 | SkeletonRenderSeparator + SkeletonPartsRenderer（见 §1） | AI | 已确认可行 |

## 2.5 音符尺寸原则（贴图 1:1，已拍板 2026-09-16）
**用户拍板：音符模型完全按贴图大小显示（1:1）可行。**
- 口径：**保持贴图导入 PPU=100**（`Assets/Art/UI/sprites/Note_Tap.png.meta` 的 `spritePixelsToUnits: 100`）。`localScale.X = 贴图宽/PPU`、`localScale.Z = 贴图高/PPU`，美术改像素即改尺寸，代码零干预 → 真正所见即所得。
- 判定圈对齐（已拍板）：`noteRadius = 贴图半宽世界尺寸`（普通 tap = 0.98/2 ≈ 0.49），视觉与判定圈一致，避免“看着大但判定圈小”误判。
- 当前实测数据（PPU=100 原生世界尺寸）：
  - Note_Tap 98×195px → **0.98 × 1.95**；当前烤定 `tapTargetScale=(0.845,1.374)` 比原生略小，切换后会变大（X+16% / Z+42%）
  - Note_Wide 101×287px → **1.01 × 2.87**；当前 `wideTapTargetScale=(0.778,3.02)`（X 偏小、Z 偏大）；wide 已有独立贴图，无需拉伸普通贴图
- 配套必做（否则“按贴图”翻车）：
  1. 厚板 mesh → **零厚度 quad**（消除胶囊外侧壁轮廓；NoteMover `TapRoundedRectMesh` 的 Y=0.12 改为平面 quad）
  2. `noteRadius` 按贴图半宽重算（见上）
  3. 跨轨 wide 用独立贴图（已有，原生 2.87 长匹配 span=2，不需拉伸）
- **A2 执行输入 · 拉伸明细与目标像素（2026-09-16 实测）**：
  - 普通点击 tap：当前代码 `localScale=(0.845, 1.374)`（X/Z 世界单位），相对 PPU=100 原生(0.98,1.95) 为 **X 压 14% / Z 压 30%**（非等比，Z 压更多）；判定圈 `noteRadius=0.45`。→ 美术按 **85×137px** 重出 Note_Tap（PPU=100 不变），1:1 即还原 0.845×1.374，判定圈同步设 `noteRadius=0.845/2≈0.42`。
  - 跨轨 wide：当前 `localScale=(0.778, 3.02)`，相对原生(1.01,2.87) 为 **X 压 23% / Z 放 5%**；span=2 恒用 `noteRadius×2` 算宽。→ 美术按 **78×302px** 重出 Note_Wide，1:1 即还原 0.778×3.02；判定圈维持 `noteRadius=0.45`（wide 宽=0.9 vs 显示宽 0.778，A2 时再对齐或保留）。
  - 内容占比：tap 宽100%/高99%、wide 宽99%/高100% → 贴图几乎无留白，包围盒≈爪印本体，改图时无需额外扣留白。
  - 形状提醒：目标像素宽高比须≈当前显示比（tap≈0.62、wide≈0.26），**不能简单等比缩放原图**（原图比更瘦长会变形）。
  4. 旧手调 `tapTargetScale`/`wideTapTargetScale` **不删**：加 `useTextureNativeSize` 开关（开=按贴图自动算，关=走手调 scale），可回退
  - **执行状态（2026-09-16 21:28）**：`useTextureNativeSize` 已实现并默认开；运行时抓 `NoteSpriteLibrary` 的 Sprite，用 `rect`/`pixelsPerUnit` 算 1:1 世界尺寸（非 100 PPU 也准）。美术实际导出：Note_Tap **84×137**（非 85，差 1px 忽略）→ 1:1=0.84×1.37；Note_Wide **78×302** → 1:1=0.78×3.02。观感与旧烤定值一致。A2a 完成；A2b（零厚度 quad + noteRadius 对齐）待执行。
- 与 Spine 计划（§0）不冲突：Spine 下“按贴图大小”= 在 Spine 编辑器设 skeleton 尺寸（贴图 import 尺寸），原则一致
- 执行归属：当前 quad 版本先落地验证手感（阶段 A2），Spine 阶段（B）沿用同原则

## 3. Spine 部件 → 材质规划表（美术拆图后补全）
| 部件(slot) | 角色 | 混合/材质 | 备注 |
|---|---|---|---|
| 阴影 | 地面椭圆 | Alpha 半透明（程序/画） | 落地感 |
| 光晕 | 底光 | Additive 发光 | 动态呼吸 |
| 本体 | 爪印主体 | Alpha ~0.8 | 透地、体积感 |
| 高光/边缘 | 描边提亮 | Additive | 立体 |
| 噪点层（可选） | 待定 | 自定义 Shader Graph | 你决定是否要 |
> 用户拆图后填入具体 slot 名与参数

## 4. 实施阶段表（每步独立备份可回退）
| 阶段 | 内容 | 关键产出 | 负责人 | 回退 |
|---|---|---|---|---|
| A | 轨道可拖动手柄 + 大小 live 旋钮 | NoteTrackRig + Editor + noteSizeScale | AI | 备份 |
<!-- A 阶段首步已落地：临时可视化调参 Rig 已建（纯新增、零玩法依赖、调完可删） -->
<!-- A 阶段·轨道宽度已执行（2026-09-16 18:05）：laneSpacing 1.5→1.7（两侧）；判定线视觉 scale.z 7.5→6.5；8 提示灯移到新 lane 中心(±2.55/±0.85)且 thick 1.4；TouchZone(红方) centerX -7→-6 / halfWidth 1→3 / halfDepth 0.65→0.75（X∈[-9,-3]）。备份 Backup/2026-09-16-track-width/。大小 live 旋钮(noteSizeScale)为下一批，未做。 -->
<!--   - Assets/Scripts/NoteTrackRig.cs：OnDrawGizmos 画玩家/蓝侧轨道带 + 中央判定线 + 音符半径参考圆 -->
<!--   - Assets/Editor/NoteTrackRigEditor.cs：轨道带 Z 边缘手柄→laneSpacing；判定线 X 手柄→judgeLineX；菜单 Tools/音符/创建音轨调参 Rig -->
<!--   用法：菜单建 Rig → 选中 → Scene 拖手柄 → 读 Inspector 的 laneSpacing / judgeLineX 报 AI → AI 烤进 NoteSpawner.laneSpacing(两侧)+BattleCenterLine静止位+两侧hitPoint.x -->
| A2a | 尺寸按贴图 1:1（PPU 任意值都准，抓 Sprite.rect/pixelsPerUnit）+ useTextureNativeSize 开关 | NoteMover 改 scale 来源 | AI | ✅ 2026-09-16 21:28 已执行；开关回退旧 tapTargetScale 保留 |
| A2b | 换零厚度 quad（消 Y=0.12 侧壁，实现“只显贴图不显模型”）+ noteRadius 按贴图半宽对齐 | NoteMover mesh 改零厚度 quad、判定半径重算 | AI | ⏳ 待执行（对应点③厚度 + 判定圈一致性） |
| B | 音符挂 Spine 骨架替换厚板；去 Y=0.12 厚度；useSpine 开关 | NoteMover 实例化 SkeletonAnimation，旧 mesh 路径保留 | AI | 开关回退 |
| C | Separator 多材质（半透明/发光/噪点） | SkeletonRenderSeparator 配置 + 材质（§1） | AI | 备份 |
| D | 动态（呼吸/抖）接入 | Spine 动画 or 运行时 bone 驱动 | 用户+AI | — |
| E | 命中/消失 Spine 动画 + HitCoroutine 换 Play | 动画 + 接口 | 用户+AI | 旧协程保留 |

## 5. 待你拍板 / 提供
| 项 | 说明 | 状态 |
|---|---|---|
| Spine 骨架谁来建 | 你画美术+骨架导出，还是我先搭占位骨架你再替换 | 待定 |
| 部件清单 | 拆图后给 slot 名 + 每部件材质意图 | 待你给 |
| 音轨手柄形式 | Handles.Slider 拖 vs Inspector 数值 | 待定 |
| 大小缩放范围 | 默认只缩显示 vs 连判定一起 | 默认只缩显示（待确认） |
| 噪点层 | 是否要（影响材质复杂度） | 待定 |
| 透明目标值 | 本体 alpha（建议 0.75~0.85） | 待定 |
| 命中具体表现 | 参考图有方向，细节制作时聊 | 后议 |

## 6. 关联文件
- `Assets/Scripts/NoteMover.cs`：mesh 替换、scale、HitCoroutine 改造点
- `Assets/Scripts/NoteSpawner.cs`：spawn 时挂 Spine 骨架
- `Assets/Editor/MusicalSpriteDebugWindow.cs`：调参入口（可选加 Spine 选项）
- 旧方案 `plan-note-visual-2026-09-16.md`：已废弃，方向见本节 §0
