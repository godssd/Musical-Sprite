# GroundEdge Shader 使用说明

> 目标：红蓝两方拼成**一整块静态地面**，四边（外缘）生成有机草地锯齿，**中缝不出现草边**。
> 文件：`Assets/Shaders/GroundEdge.shader`
> 配套：`Assets/Editor/GroundEdgeMaterialSetup.cs`
> 模型：`Assets/Art/Models/ArenaGround_New.fbx`（来自 `dimian.blend`，左右两半在 x=0 直边无缝拼接）
> 贴图：`Assets/Art/dimian_1_A.png`（红方完整图）、`dimian_1_B.png`（蓝方完整图）

---

## 一、地面模型与"不变化"的约定

- 红蓝是**一整块不变的地面**：`ArenaLeft`(世界 X[-8,0]) + `ArenaRight`(X[0,8]) 在 x=0 **直边拼接**，视觉上是一块 16×7.5 的场地。
- 两半都是静态的，**不会被缩放/移动**（`BattleCenterLine` 已改为只移动粉杠，不再动地面）。
- "我的地面随优势变大、显示更多"通过材质 `_Reveal` 实现（见第三节），**不改几何、不拉伸贴图**。

## 二、Shader 做了什么

- **中间区域**：按世界坐标把贴图映射到 0..1（每方默认只显示半场），正常显示，完全不变。
- **边缘带**（靠近外缘 `_EdgeWidth` 世界米内）：用值噪声做 Alpha Cutoff，形成有机锯齿；外缘染 `_EdgeColor`（草绿）。
- **中缝不生成草**：边缘判断用**世界坐标 + 边缘开关 `_EdgeMask`**。红方右=中缝、蓝方左=中缝 都设为 0，所以中间那条接缝绝不会长草。
- 可选叠加**草边纹理**（Alpha=草形）让锯齿更像真草尖。

## 三、怎么看到效果（两步菜单）

1. 编译完脚本后，菜单 → `Tools/Musical-Sprite/Create GroundEdge Red & Blue Materials`
   → 生成 `M_GroundEdge_Red` / `M_GroundEdge_Blue`（已接好 `dimian_1_A/B`、边缘开关、半场范围）。
2. 菜单 → `Tools/Musical-Sprite/Replace Demo Ground With New Model`
   → 自动把场景里的 `ArenaLeft`/`ArenaRight` 换成新 FBX 网格、归位、赋上这两个材质。
3. 进 Scene / Game 看：四边（外缘）出草齿，**中间无缝、无草**。

> 若想手动：把红材质挂 `ArenaLeft`、蓝材质挂 `ArenaRight`，两物体位置设 (0,-0.05,0)、缩放 (1,1,1)。

## 四、关键参数

| 参数 | 含义 | 红方默认 | 蓝方默认 |
|---|---|---|---|
| `Edge Mask Left` / `Edge Mask Right` / `Edge Mask Bottom` / `Edge Mask Top` | 各边是否长草（1/0） | 左1 右0 前1 后1 | 左0 右1 前1 后1 |
| `Ground Min` / `Ground Max` | 该半场世界范围，用于贴图映射 | (-8,-3.75)/(0,3.75) | (0,-3.75)/(8,3.75) |
| `Reveal` | 0.5=半场，1=显示整张图（占满），0=不显示 | 0.5 | 0.5 |
| `Edge Width` | 草齿带宽（世界米） | 0.35 | 0.35 |
| `Edge Noise Scale` | 草尖粗细（高=细密） | 45 | 45 |
| `Edge Cutoff` | 咬多深 | 0.5 | 0.5 |
| `Edge Tint` | 外缘染色 | 草绿 | 草绿 |

### 调参顺序（逼近截图草地边）
1. `Edge Width` 0.1~0.5 → 草带宽窄。
2. `Edge Noise Scale` 20~80 → 低=大块锯齿，高=细密草尖（截图草尖感调高）。
3. `Edge Cutoff` 0.3~0.7 → 咬多深。
4. `Edge Tint` → 自然草绿。
5. `Edge Noise Speed` 0 先不动，想要边缘轻微抖动再开（像 Doodle）。

### 进阶：用草边纹理做"真草尖"
1. 准备一张图，Alpha=草的轮廓（底部实、顶部一排草尖透明）。
2. 勾 `Use Edge Texture`，把图拖进 `Edge Texture`，调 `Edge Texture Strength`(1=完全用纹理形状)。

## 五、优势"覆盖"机制（当前实现）

`BattleCenterLine` 每帧计算优势 `a∈[-1,1]`（红占优为正），并写入材质：
- `M_GroundEdge_Red._Reveal = 0.5 + 0.5*a`
- `M_GroundEdge_Blue._Reveal = 0.5 - 0.5*a`

效果：均势时双方各显示半场；某方占优时其地面显现更多（最多整张），劣势方收缩。
**注意**：这只是"各自显现自己贴图更多/更少"，还不是"占优方贴图真正铺到对方半场"。若要做到后者（对方地面被我的颜色真正覆盖），需要把红蓝做成**单一连续地面 + 一张贴图按移动边界混合**，属下一步设计，等你看过当前效果再定。

## 六、验证清单（按顺序做）

### 阶段 1：确认 Shader 能编译
1. 回 Unity 后看 Console。
2. 如果只剩 `GroundEdge.shader` 相关错误，全选该文件 → 右键 **Reimport**（强制刷新）。
3. 目标：Console 里**不再出现** `GroundEdge.shader` 的 `Parse error`。

### 阶段 2：生成材质并挂到地面
1. 菜单 `Tools/Musical-Sprite/Create GroundEdge Red & Blue Materials`。
2. 菜单 `Tools/Musical-Sprite/Replace Demo Ground With New Model`。
3. 选中 `ArenaLeft`，Inspector 里 MeshRenderer 的材质应是 `M_GroundEdge_Red`；`ArenaRight` 应是 `M_GroundEdge_Blue`。
4. 若材质显示为洋红色（magenta），说明 Shader 还有编译错误，截图给我。

### 阶段 3：观察边缘效果
1. 切到 **Scene 视图** → 上方按钮选 **Shaded**（不要 Wireframe）。
2. 从顶视图（按 Scene 视图右上角坐标轴，点 Y 轴）俯视地面。
3. 确认：
   - 中间 x=0 处**没有草齿**（中缝干净）。
   - 左右外侧、前后两边**有草齿**。
   - 贴图没有拉伸变形，默认显示各半场。
4. 截图四张：顶视图全貌、左侧外缘特写、右侧外缘特写、中缝特写。

### 阶段 4：验证"优势显现"（可选）
1. 选中 `CenterLine` 物体，找到 `BattleCenterLine` 脚本。
2. 改 `Test Bias`（如果有）或直接进 Play 模式制造分差，看哪方 `_Reveal` 变大。
3. 或在运行时 Inspector 里手动拖 `M_GroundEdge_Red` 的 `Reveal` 从 0.5 到 1，看红方是否显示更多贴图。

## 七、已知限制
- 阴影：未做裁剪阴影 Pass，地面自身不投带锯齿的影（影响不大）。
- 效果固化：我看不到渲染，草形粗细/深浅需你截图反馈，我再帮你把数值调优或写死。
