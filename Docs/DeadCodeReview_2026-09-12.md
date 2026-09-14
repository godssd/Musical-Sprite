# 死代码 / 疑似废弃工具 筛查报告（2026-09-12）

> 背景：本轮任务包含「清除旧 AI 代码 + 筛查删除无用工具和死代码」。
> 你的铁律：**疑似废弃和可能还需的内容先都保留，展开细说让你判断；其余按计划执行。**
> 结论先行：**经全仓引用检索，下列项目绝大多数并非死代码，而是带 `[MenuItem]` 的可用编辑器工具或仍被活动功能引用的资源。本次一律保留，未删除任何文件。** 唯一需要你拍板的是 `ScenePropSetup` 与 `ScenePropSpawner` 的功能重叠。

---

## 检索方法

对以下标识符在 `Assets/` 全仓（`.cs/.shader/.mat/.prefab/.unity/.asset`，排除 `.meta`）做引用检索，并额外检查各 Editor 工具是否含 `[MenuItem]`、是否被运行时脚本引用。

候选清单（来自你的需求）：ToonDoodle 三件套、GrassFringe 三件套、GroundEdgeMaterialSetup、TerrainProfileApplier、TerrainProfileSO、HPBarLiquidSetup、HPBarFxPreviewWindow、ScenePropSetup、ScenePropSpawner、FeverVFXPlaceholder。

---

## 1. ToonDoodle 三件套（shader + BatchConvert + MaterialSetup）

| 文件 | 角色 |
|---|---|
| `Assets/Shaders/ToonDoodle.shader` | 手写卡通描边 Shader |
| `Assets/Editor/ToonDoodleBatchConvert.cs` | MenuItem：把选中/场景材质转 ToonDoodle |
| `Assets/Editor/ToonDoodleMaterialSetup.cs` | MenuItem：建 ToonDoodle 样例材质 |

**引用证据**：`ToonDoodleBatchConvert.cs`、`ToonDoodleMaterialSetup.cs`、`Scripts/Environment/TerrainProfileSO.cs` 均引用该 shader；无 `.mat` 直接引用（由工具运行时生成）。
**判定**：**保留**。这是地形/地面卡通描边管线的一环，被 `TerrainProfileSO`（地形 Profile 数据）注释关联，且 `GroundEdgeMaterialSetup` 也走该批量转换。属 COTL 手绘卡通风格落地的工具链。

---

## 2. GrassFringe 三件套（shader + M_GrassFringe.mat + GrassFringe.asset 网格）

| 文件 | 角色 |
|---|---|
| `Assets/Shaders/GrassFringe.shader` | 草丛流苏 Shader |
| `Assets/Art/Materials/M_GrassFringe.mat` | 使用该 Shader 的材质 |
| `Assets/Art/Meshes/GrassFringe.asset` | 流苏网格 |

**引用证据**：`Scripts/BattleCenterLine.cs` 第 46 行有序列化字段 tooltip：「草丛流苏材质（M_GrassFringe）。赋值后会跟随 groundMaterial 一起设置 _CenterLineX，使草丛颜色随粉杠变化。」→ 战斗中线（粉杠）的草丛描边效果正在用它。
**判定**：**保留（活动）**。这是战斗中线视觉的一部分，被 `BattleCenterLine` 实时引用。

---

## 3. GroundEdgeMaterialSetup.cs（含 GroundEdge.shader）

**角色**：一系列地面/舞台边缘材质与程序化网格的编辑器工具。
**引用证据**：含 6 个 `[MenuItem]`（建 GroundEdge 竞技场材质 / 生成程序化地面网格 / 替换 Demo 地面 / 更新地面贴图 / 加舞台悬檐唇 / 去悬檐唇）；引用 `GroundEdge.shader` 与 `ToonDoodleBatchConvert`。
**判定**：**保留（活动）**。地面美术资产替换工作线（用终稿替换 demo 程序化占位）的核心工具，你近期还在用。

---

## 4. TerrainProfileApplier.cs + TerrainProfileSO.cs（地形 Profile 管线）

| 文件 | 角色 |
|---|---|
| `Scripts/Environment/TerrainProfileSO.cs` | 地形 Profile ScriptableObject 数据 |
| `Editor/TerrainProfileApplier.cs` | 把 Profile 应用到选中地形 |

**引用证据**：`TerrainProfileApplier` 有 `[MenuItem("Tools/Musical-Sprite/Terrain/Apply Selected Terrain Profile")]`；`TerrainProfileSO` 被 `TerrainProfileApplier` 与 `ToonDoodle` 工具链引用。
**判定**：**保留（活动）**。地形描边/分期几何的正式管线，菜单可调用。非死代码。

---

## 5. HPBarLiquidSetup.cs + HPBarFxPreviewWindow.cs（液槽 HP 条）

| 文件 | 角色 |
|---|---|
| `Editor/HPBarLiquidSetup.cs` | 液槽 HP 条一键布置/校验/红蓝同步 |
| `Editor/HPBarFxPreviewWindow.cs` | HP 条特效预览窗口 |
| `Scripts/UI/HPBarLiquid.cs` | 运行时液槽 HP 条脚本（引用预览窗口） |

**引用证据**：`HPBarFxPreviewWindow` 被 `Scripts/UI/HPBarLiquid.cs` 引用；两者均有 `[MenuItem]`（Setup Liquid HP Bars / Check HP Bars / Sync Blue←Red / HP Bar FX Preview）。
**判定**：**保留（活动，正在进行）**。这正是你当前并行的「液槽 HP 条」工作线（需验证 sprite/plane 1:1 原图尺寸）。属于活跃开发，不要动。

---

## 6. FeverVFXPlaceholder.cs（过热 VFX 占位）

**角色**：过热（Fever）状态 VFX 占位脚本。
**引用证据**：被 `CharacterCubeMarker.cs`、`FeverBanner.cs`、`FeverManager.cs` 引用（过热系统三处都在用）。
**判定**：**保留（活动）**。过热系统是已上线功能，占位脚本仍被调用。

---

## 7. ScenePropSetup.cs vs ScenePropSpawner.cs（场景道具工具 —— 唯一需你判断的重叠项）

| 文件 | MenuItem | 功能 |
|---|---|---|
| `Editor/ScenePropSetup.cs` | Create Scene Prop From PNG / From FBX / List Scene Props | 从 PNG 或 FBX 创建场景道具 + 列出道具 |
| `Editor/ScenePropSpawner.cs` | 场景道具/生成测试树木 (tree_2~5) | 生成测试树木面片 |

**引用证据**：两者均无被其他脚本引用（仅自身），且都通过 `[MenuItem]` 供你手动调用。
**判定**：**保留两者，但请你拍板**——它们功能有重叠（都是"场景道具生成"），`ScenePropSetup` 是通用 PNG/FBX 道具创建器，`ScenePropSpawner` 是特定测试树木（tree_2~5）生成器。可能 `ScenePropSpawner` 是被 `ScenePropSetup` 取代前的早期版本，也可能 tree 测试仍依赖它。建议：若 tree_2~5 测试已结束，可只留 `ScenePropSetup`；若还要反复测树木面片，则两个都留。

---

## 汇总建议

| 项目 | 是否死代码 | 本次动作 |
|---|---|---|
| ToonDoodle 三件套 | 否（工具链） | 保留 |
| GrassFringe 三件套 | 否（BattleCenterLine 引用） | 保留 |
| GroundEdgeMaterialSetup | 否（6 个 MenuItem，地面替换线） | 保留 |
| TerrainProfileApplier/SO | 否（地形描边管线） | 保留 |
| HPBarLiquidSetup/FxPreview | 否（液槽 HP 条线，活跃） | 保留 |
| FeverVFXPlaceholder | 否（过热系统引用） | 保留 |
| ScenePropSetup vs ScenePropSpawner | 重叠，需判断 | 均保留，等你定 |

**本次实际删除的只有 3 组 `.bak` 冗余备份**（GroundEdge_Template.png.bak 及其 .meta、SampleScene.unity.bak.20260912 及其 .meta、SampleScene.unity.bak.stageoverhang 及其 .meta），符合计划中"删 .bak 备份"的要求。

> 如果你确认 `ScenePropSpawner` 可弃，或想进一步清理（例如 ToonDoodle 若确定不走卡通描边），告诉我具体哪一项，我再按"可回退"原则处理（旧实现隐藏而非删除）。
