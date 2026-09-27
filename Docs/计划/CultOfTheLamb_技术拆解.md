# Cult of the Lamb 技术拆解

> 分析对象：`F:\raunjian\steam\steamapps\common\Cult of the Lamb`
> 分析时间：2026-09-04
> 工具：UnityPy + Python 脚本

---

## 一、整体技术栈

| 层面 | 结论 |
|---|---|
| 引擎 | Unity（Mono 后端） |
| 渲染管线 | Built-in / URP（从自研着色器数量看，更可能是深度定制的 Built-in） |
| 着色器工具 | Amplify Shader Editor |
| 调色 | Amplify Color |
| 雾/氛围 | Boxophobic AtmosphericHeightFog |
| 2D 动画 | Spine（角色、树、Follower 等） |
| 补间动画 | DOTween |
| 寻路 | A* Pathfinding Project |
| 音频 | FMOD |
| UI 增强 | Coffee UI（软遮罩 / UI 粒子）、LeTai TranslucentImage（毛玻璃） |

---

## 二、场景是怎么搭的：2.5D 精灵拼贴

从 `resources.assets` 对象数量可直接看出架构：

| 对象类型 | 数量 | 含义 |
|---|---|---|
| GameObject | 7614 | 场景总对象 |
| **SpriteRenderer** | **2070** | 世界由 2D 精灵构成 |
| **Sprite** | 1902 | — |
| RectTransform / CanvasRenderer | 1705 / 1364 | UI 极重 |
| **ParticleSystem** | **1209** | 特效几乎全靠粒子 |
| Mesh / MeshFilter | 22 / 742 | **真正的 3D 模型只有 22 个** |
| Material / Shader / Texture2D | 444 / 268 / 789 | 自研材质/着色器很多 |

### 关键结论

- **COTL 是 2.5D，不是 3D**。整个世界是 **2D 精灵层叠**出来的，靠正交/斜视相机营造立体感。
- 你的 `Musical-Sprite` 是**真 3D 锁视角**，所以**架构不能照搬，但观感可以借鉴**。

### 具体手法

1. **房间/场景 = 精灵拼装**
   - `Background`(50 个) 多层做视差背景。
   - `Room Back Sprite`、`Dungeon1` 是场景预制体。
   - 蘑菇/树精灵：`Tree1`、`MushroomPatch` 等。

2. **地形边缘用 SpriteShape**
   - 不是方块，是有机曲线。

3. **角色和树用 Spine 骨骼动画**
   - `Spine GameObject (Follower/Tree1)`、`SkeletonGraphic`。
   - 动作流畅、可变形的"活"感来自 Spine。

4. **灯光 = 1000+ 个 StencilLighting 体积光精灵**
   - `StencilLighting_Volumetric` 大量出现。
   - 不是真打一千盏灯，而是 2D 精灵面片做彩色辉光池。

5. **粗黑描边是独立图层**
   - `Lamb_Outline`、`Goat_Outline`、`Outline`(59 个)。
   - 描边是单独画的，叠在角色下层/上层。

6. **2D 物理**
   - 291 个 CircleCollider2D、90 个 BoxCollider2D、52 个 Rigidbody2D。

---

## 三、手绘涂鸦风的渲染实现

COTL 的"手绘墨水"感主要来自三种自研着色器：

### 1. Doodle 抖动着色器（招牌效果）

- 着色器名举例：`UI_UberShader_Doodle_Intense`、`Particle_DoodleUV_A`
- 抓到的典型参数：
  - `_DOODLE_UV = 1`（开启抖动）
  - `_DoodleSize = 8.1`
  - `_DoodleSpeed = 9.2`
- **原理**：对 UV 做随时间变化的噪声扰动，让边缘像手绘墨水一样持续轻微抖动。

### 2. 水彩着色器

- 着色器名：`Watercolor_*`
- 典型参数：
  - `_BlotchMultiply ≈ 3.7`
  - `_BlotchSubtract ≈ 3.2`
  - `_PaperStrength = 1`
  - `_InkColor`
- **原理**：模拟水彩在纸上的渗开、墨晕斑块。

### 3. 卡通 / Toon 光照

- Rim（边缘光）、Fresnel、ShadowOverlay、SSS（次表面散射）参数。
- 角色和粒子都是 Cel-Shade 风格。

---

## 四、纹理与资源规格

| 类型 | 发现 |
|---|---|
| 草地 tileset | `Grass_Dungeon1` 2048×1024，可平铺 |
| 角色贴图 | `Follower` 8192、`player-main` 4096、`skeleton` 4096 —— 主角手绘精度很高 |
| 风格 | 2 的幂、可平铺、手绘感强 |

---

## 五、对 Musical-Sprite 的落地建议

| COTL 的做法 | Musical-Sprite（3D URP）该怎么对齐 |
|---|---|
| 2D 精灵层叠世界 | 你已用 3D 模型 + 锁视角，方向正确 |
| Doodle UV 噪声抖动 | 自研 URP Toon Shader：反向外扩描边 + 噪声抖动轮廓 + 阶梯色阶。本项目的液体血条 Shader 就是自研 shader 的经验基础 |
| 水彩/墨晕 | 地面/装饰用**手绘可平铺纹理** |
| 1000+ Stencil 体积光 | URP 用 **Bloom + 少量点光 + 自发光贴花** 假体积光，不要真打大量实时光 |
| 粗黑描边独立层 | 3D 里用**反向外扩法线描边（inverted hull）**实现 |
| 1209 粒子做特效 | VFX 走 doodle/水彩观感，参数化暴露（VfxFactory） |
| Spine 角色动画 | 你的角色是 3D 模型，不适用 Spine，靠模型动画 / blend shape |

**一句话总结**：美术方向（手绘卡通 + 粗黑边 + 暗部 + 高饱和）和 COTL 一致，缺的"抖动活感"靠**自研 Toon 着色器的噪声扰动**补上，而不是抄它的精灵架构。

---

## 六、参考资产路径

- 本机：`F:\raunjian\steam\steamapps\common\Cult of the Lamb`
- UnityPy 分析脚本位置：`C:\Users\Administrator\WorkBuddy\2026-08-30-16-26-26\cotl_analysis\`
