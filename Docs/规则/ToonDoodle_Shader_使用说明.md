# ToonDoodle Shader 使用说明

> 基于《Cult of the Lamb》的 Doodle 参数复刻：粗黑描边 + UV 抖动 + Toon 色阶。
> 文件位置：`Assets/Shaders/ToonDoodle.shader`

---

## 一、为什么用 HLSL 手写而不是 Shader Graph

项目当前 `Packages/manifest.json` 没有安装 **Shader Graph** 包。直接生成 `.shadergraph` 文件 Unity 无法打开，所以先用 **URP HLSL 手写 Shader** 实现等价效果：

- 参数全部暴露在 Inspector，可在运行时/编辑态实时调参。
- 不需要额外安装包，只要项目已导入 URP 即可。
- 后续如果你装了 Shader Graph，可以把它翻译成 Sub Graph。

---

## 二、怎么看到效果（3 步）

### 1. 生成示例材质球

Unity 菜单 → `Tools → Musical-Sprite → Create ToonDoodle Sample Material`

会在 `Assets/Art/Materials/` 下生成 `M_ToonDoodle_Sample.mat`。

### 2. 挂到模型上

把 `M_ToonDoodle_Sample.mat` 拖到任意 3D Mesh Renderer 的材质槽上。

### 3. 调参看效果

选中材质球，Inspector 里会出现以下参数。重点调：

| 参数 | 作用 | 建议范围 | COTL 参考值 |
|---|---|---|---|
| `启用 Doodle 抖动` | 开关整体抖动（作用于 UV 采样） | 0 / 1 | 1 |
| `抖动尺寸` | UV 噪声网格密度，越大抖动越细碎 | 1 ~ 30 | 8.1 |
| `抖动速度` | 每秒切换多少格噪声 | 0 ~ 30 | 9.2 |
| `抖动强度` | UV 偏移幅度，太大画面会散 | 0 ~ 0.02 | 0.005 |
| `色阶层数` | Toon 阴影的硬阶层数 | 1 ~ 8 | 4 |
| `描边宽度` | 外扩描边粗细（**世界单位**，视空间沿法线外扩） | 0 ~ 0.1 | 0.03 |
| `描边颜色` | 通常黑/深蓝黑 | - | (0.05,0.05,0.08) |

> 注：`描边也抖动`（_OutlineDoodle）已于 2026-09-11 废弃移除——旧实现把抖动加在裁剪空间顶点上，随距离放大且逐帧随机，导致描边壳乱飞（黑色不稳定方块）并随机切片盖住模型（模型被截断）。现在描边是稳定的视空间法线外扩，手绘感由 ForwardLit 的 UV 抖动独立承担。

---

## 三、参数调参顺序（建议）

1. **先把 `启用 Doodle 抖动` 关掉**，确认基础 Toon 色阶和描边正常。
2. **调 `描边宽度` + `描边颜色`**，让轮廓粗黑明显。
3. **打开 Doodle**，先设 `抖动强度 = 0.02` 看能不能明显抖起来，再慢慢降到 `0.005` 左右自然。
4. **调 `抖动尺寸` 和 `抖动速度`**：尺寸大=细碎抖动，尺寸小=大块偏移；速度高=抖得快像水，速度低=慢抽搐。
5. **最后调 `色阶层数`**：数字越小阴影越硬，越大越接近渐变。

---

## 四、已知限制

- **描边是 Inverted Hull 方法**：只适合封闭式 3D Mesh，薄片/平面物体（指示器、标记条）会整片出黑面——批量转换工具已按对象名（CenterLine / Indicator / HitLine / HPBar / Frame）与材质类型（UI / 粒子 / 文本 / 包内置默认材质）自动跳过。
- **多光源**：目前只采样主方向光（Main Light），额外点光未接入。
- **雾**：已接入 URP 雾。
- **透明/裁剪**：当前是 Opaque，如需 Alpha Cutout 需额外加一个 `_Cutoff` 参数和 clip。

## 四点五、Pass 清单（2026-09-11 补齐）

| Pass | LightMode | 作用 |
|---|---|---|
| ForwardLit | UniversalForward | Toon + Doodle 主着色 |
| Outline | SRPDefaultUnlit | 稳定版反壳描边（视空间法线外扩） |
| ShadowCaster | ShadowCaster | 写主光阴影贴图（地面实时阴影的来源） |
| DepthOnly | DepthOnly | 相机深度纹理（深度 prepass / 移动端） |
| DepthNormals | DepthNormals | 深度 + 法线（PC 端 SSAO prepass） |

5 个 Pass 的 `UnityPerMaterial` CBUFFER 为全属性并集且完全一致（SRP Batcher 约束）。

---

## 五、后续升级路线

| 阶段 | 内容 |
|---|---|
| V1（当前） | HLSL 手写：基础 Toon + Doodle + 描边 |
| V2 | 接入额外实时光、Rim Light、边缘光 |
| V3 | 加入水彩墨晕（Watercolor blot）效果 |
| V4 | 装 Shader Graph 后转成可视化节点，方便非程序员美术调参 |
