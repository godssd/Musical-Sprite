# TerrainProfile 地形配置系统 使用说明

## 一、是什么

`TerrainProfileSO`（ScriptableObject）= 一份地形的外观配置存档。**换地形 = 换一份 SO → 跑一次菜单**，地面和玩家台面的贴图、草边、描边、阴影参数全部按配置输出。

## 二、文件位置

| 内容 | 路径 |
|---|---|
| 配置数据类 | `Assets/Scripts/Environment/TerrainProfileSO.cs` |
| 一键应用工具 | `Assets/Editor/TerrainProfileApplier.cs` |
| 第一份存档（当前森林场地快照） | `Assets/TerrainProfiles/Forest.asset` |
| 地面材质（矩形模式） | `Assets/Art/Materials/M_GroundEdge_Arena.mat` |
| 台面材质（圆盘模式，A=左红 / B=右蓝） | `Assets/Art/Materials/M_GroundEdge_Stage_A / _B.mat` |
| 台面贴图 | `Assets/Art/yuantai_1_A.png`（左）/ `yuantai_1_B.png`（右） |
| 草边遮罩模板 | `Assets/Art/Textures/GroundEdge_Template.png`（地面台面共用，逻辑不变） |

## 三、怎么换地形

1. Project 窗口右键 → `Create → Musical-Sprite → Terrain Profile`（或复制 `Forest.asset` 改）；
2. 换贴图：`Ground Red/Blue Map`（地面）、`Stage Map Left/Right`（左右台面）；草边遮罩 `Grass Mask` 一般不动；
3. 选中这份 SO → 菜单 `Tools/Musical-Sprite/Terrain/Apply Selected Terrain Profile`；
4. 完成：地面 + 台面材质全部重写、场景引用自动接好。

## 四、台面（圆盘模式）的关键约定

- **贴图铺满画布**：台面贴图内容按"整个台面包围盒"映射（和 dimian 地面贴图同一套约定）。半圆盘网格只显示贴图朝场地的那一半；
- **圆心 = 台面根物体（LeftBand_Stage / RightBand_Stage）的世界原点**，半径自动取渲染器包围盒最大 XZ 半尺寸；特殊形状可在 Profile 里关掉 Auto 用手动值；
- 台面草边画在**边界内侧**（没有外扩流苏几何），`_EdgeOverhang=0` 时草尖正好落在台面边缘；
- 侧壁自动用泥土色 + 假光源，底面剔除——由世界法线判定（`normalWS.y < 0.3`）。

## 五、草边描边（新参数，两个模式通用）

`GroundEdge.shader` 新增：

| 参数 | 默认 | 说明 |
|---|---|---|
| `_OutlineColor` | (0.05, 0.05, 0.08) | 与 ToonDoodle 方块描边同色 |
| `_EdgeOutlineWidth` | 0.03（世界单位） | 草边最外圈描边带宽度；地面描在流苏外缘，台面描在台面边界 |

只改 ForwardLit 的颜色，不影响深度 / 法线 Pass 的剪影。

## 六、形状模式

`_ShapeMode`：0 = 圆角矩形（地面），1 = 圆盘（台面，圆心 `_DiscCenter` 半径 `_DiscRadius`，贴图槽 `_StageMap`）。三种 Pass（ForwardLit / DepthOnly / DepthNormals）CBUFFER 完全一致，SRP Batcher 安全。
