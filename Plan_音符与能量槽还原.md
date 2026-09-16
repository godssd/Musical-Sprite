# Musical-Sprite 音符与能量槽还原计划（总文档）

> 用途：本项目的「音符表现 + 世界空间能量槽」还原工作的**唯一长期计划文档**。
> 所有需求、教训、执行切分都汇总在此，避免跨会话因时间推移而遗忘。
> 最后更新：2026-09-14（基于本轮「回退 SpriteRenderer 路线 + 重新对齐」结论）
> 工作铁律（贯穿全文）：plan-first / verify-then-execute；改动可回退；参数 Inspector 可调；小步验证（每步回 Unity 截图确认）。

---

## 0. 当前基线状态（2026-09-14）

- 音符三脚本（`NoteMover.cs` / `HoldNote.cs` / `NoteSpawner.cs`）已 `git checkout HEAD`，回到 `ai实装` 提交的 **MeshRenderer 原始黑色几何音符**（稳定、尺寸/判定数学已验证）。这是「之前的音符效果」基线。
- 能量槽 `EnergyBarWorldSpace.cs` 已是世界空间 mesh 胶囊，尺寸对、位置错（漂浮在 marker 根高度）。旧 `EnergyBarUIController`（ScreenSpaceOverlay）保留未删，可回退。
- SpriteRenderer 时代产物（`NoteSpriteLibrary.cs` / `AdditiveSprite.shader` / `Art/UI/sprites/` / `NoteSpriteLibrary.asset`）留盘未删，当前音符已不引用，可供新方案复用。

---

## 1. 核心教训（来自本次 SpriteRenderer 路线失败复盘）

用户总结的两个硬伤，是后续一切做法的约束来源：

| 硬伤 | 含义 | 对做法的约束 |
|---|---|---|
| **① 制作工艺与已有稳定效果差异过大** | SpriteRenderer 重写等于把渲染管线整个换掉，不是「换皮」而是「重建」，导致大量不可控错误（过曝、尺寸爆长、flip、连线粗…） | **只能在「同渲染体（MeshRenderer+mesh）」上换皮，绝不能重建管线**。任何新做法第一个判断标准：是不是只换了材质/贴图，几何与跟随逻辑一行不动。 |
| **② 效果非常扁平，没有立体感** | 平躺板 + Unlit 纯色贴图 = 一张纸，俯视下读不出体积 | 需要「轻立体化」：厚度/描边/接触阴影/受光，但仍须满足①（不能为此重建管线）。 |

**由此导出的两条铁律：**
1. 音符 = **同体换皮**（same primitive, swap material/texture），不是 reconstruction。
2. 立体感 = **增量叠加**（材质受光 + 描边 + 阴影 + 可选微厚度），每加一层都是独立可验证的小步。

---

## 2. 总需求全景

### A. 音符系统（视觉 + 手感）
| # | 需求 | 状态 |
|---|---|---|
| A1 | 视觉 = 手绘卡通（COTL 风），按效果图还原 | ⏸ 本次只做普通点击 |
| A2 | 类型：普通点击 / 小点击 / 跨轨 / 连点 / Slide 连线 / 按住 | ⏸ 分批 |
| A3 | **尺寸铁律**：除跨轨外所有音符视觉不超当前轨；跨轨只跨 2 轨 | ★ 普通点击 |
| A4 | 显形：整枚越过粉杠（中线）后才出现 | ★ 原始已实现 |
| A5 | 命中：整体放大≈1.15、减速前进淡出、不停止 | ★ 普通点击 |
| A6 | 靠近判定线：仅微微变长变亮一点点 | ⏸ |
| A7 | Miss：整体均匀缩小、先慢后快加速、轻微减速前进 | ★ 普通点击 |
| A8 | 小点击图片方向正确（之前反了） | ⏸ |
| A9 | Slide 连线：方案 A 程序化柔光带；发光竖杠仅按住出现、断连即消失 | ⏸ |
| A10 | 按住音符：节点可读、连线细、保护竖杠收敛 | ⏸ |
| A11 | 连点数字：独立 sprite，命中旧缩新放、末击闪烁 | ⏸ |
| A12 | 音符阵营色：左橙黄/右偏蓝 | ⏸ 暂不管 |
| A13 | 加法发光颜色按效果图；需 URP 兼容材质（自定义 shader URP 未加载→过曝是根因） | ⏸ |

### B. 能量槽系统 ★
| # | 需求 | 状态 |
|---|---|---|
| B1 | 世界空间、浮角色脚下 | 已落地 |
| B2 | 尺寸：当前已对（胶囊大小） | 保持 |
| B3 | 位置：压在脚下，**当前位置不对（漂浮在 marker 根高度）** | ★ 本次修 |
| B4 | CD 态：进 CD 暗黄、结束回黄；**充能不受影响**（CD 只影响释放） | ★ 本次复核 |
| B5 | 阵营色：左黄/右蓝，当前不随阵营变 | ⏸ 暂不做 |
| B6 | 满/可使用态加发光 | ⏸ |

### C. 其他并行工作线（仅记录，本次完全不涉及）
- 液槽 HP 条（sprite/plane 1:1 原图）
- COTL 体积雾（深度遮挡渐变，非屏幕空间后处理、非 rim）
- 卡通 VFX 方案（Plan A/B/C + 对比表）

---

## 3. 本次范围与执行切分（小步、每步回 Unity 验证、可单独回退）

### 能量槽（只做 B3 位置 + 复核 B4，B5 不做）
- **切分 E1**：把位置锚点改成「脚底接触点 + footLift」，调准使其压在脚下。
- **切分 E2**：复核 CD 暗黄 + 充能不受影响（进度条仍在涨）。

### 普通点击音符（只做 A1 贴图 + A3 尺寸 + A4 显形 + A5/A7 反馈；其余类型后续）
- **切分 N1（换皮）**：同体换皮——同一 mesh，材质换成 URP Lit/Transparent + 卡通贴图 `Note_Tap.png`，行为全不变 → 确认「卡通爪印 at 正确尺寸/位置」。
- **切分 N2（受光立体感）**：材质改 URP Lit 让顶面受光（解决扁平），或加法线贴图微浮雕。
- **切分 N3（描边）**：加 COTL 粗黑描边（放大深色背面体 / 或贴图自带描边）。
- **切分 N4（命中反馈）**：整体放大≈1.15 + 减速前进淡出（A5）。
- **切分 N5（Miss 反馈）**：整体缩小先慢后快 + 减速前进（A7）。
- **切分 N6（接触阴影，可选）**：音符下加贴地暗斑增强立体/落地感。

> 顺序原则：先 N1 跑通（满足①同体换皮），再按 N2→N3→N6 逐步加立体感（满足②），N4/N5 反馈单独。任一刀不满意，`git checkout HEAD --` 即回黑色稳定版。

---

## 4. 能量槽：位置准确还原方案（详）

### 4.1 老位置为何不能直接复用
旧 `EnergyBarUIController` 是 **ScreenSpaceOverlay**：每帧把 `CharacterCubeMarker.transform.position`（marker 根）投影到屏幕，下移 `verticalOffset=60px`。它的「正确」是相对 marker 根投影的屏幕偏移，世界坐标无法直接搬。

新 `EnergyBarWorldSpace` 是 **世界空间 mesh**，当前用：
```
barY = marker.position.y + markerFootYOffset(默认0) + feetYOffset(0.03)
```
问题：Spine 接入后占位 cube 渲染器被禁用，旧 `FeetWorldY` 用包围盒的方式失效；marker 根的 pivot 高度未知（cube 时代中心在地面上方约 0.5，Spine 子物体 pivot 更不确定），所以 bar 漂在错误高度。

### 4.2 推荐锚点：脚底接触点（最稳、pivot 无关）
每个 `CharacterCubeMarker` 都挂了 `BlobShadow`（`Assets/Scripts/Effects/BlobShadow.cs`），它每帧从角色根向下 raycast 到地面、把阴影贴在**脚底接触点**（`hit.point.y`）。这就是「脚底」的物理位置。

方案：
- 给 `BlobShadow` 加一个 **public `float lastGroundY`**（每帧 `UpdateShadowPositionAndScale` 里写入 `hit.point.y`）。
- `EnergyBarWorldSpace.FeetWorldY` 改为：
  ```
  if (anchorToFoot) {
      var bs = marker.GetComponent<BlobShadow>();
      if (bs != null) return bs.lastGroundY + footLift;   // footLift 默认 ~0.08
  }
  return marker.position.y + markerFootYOffset + offset;   // 兜底
  ```
- 优点：不受 Spine 开关、cube 禁用、角色高度、marker pivot 影响 → **任何角色都自动压在脚下**，即「准确」。

### 4.3 调试校准（让用户亲眼确认「准」）
- `EnergyBarWorldSpace` 加 `public bool showAnchorDebug`：开启时在运行时于「计算出的 bar 中心 Y」处生成一个**薄圆盘 + 竖直参考线**的小 GameObject（或 `OnDrawGizmos` 画），玩家进 Play 直接看 bar 是否贴在脚底；调 `footLift` 到贴合后关闭 debug。
- 这是对「我希望能够准确把位置调好」的直接回应：不是盲调一个数，而是有可视化参照物校准。

### 4.4 能量槽参数表（Inspector 可调）
| 字段 | 默认 | 说明 |
|---|---|---|
| `anchorToFoot` | true | 用 BlobShadow 脚底锚（推荐） |
| `footLift` | 0.08 | 脚底接触点之上的抬升 |
| `markerFootYOffset` | 0 | 兜底用，anchorToFoot 关闭时生效 |
| `feetYOffset` | 0.03 | 原微调量，保留 |
| `showAnchorDebug` | false | 校准可视化，调好后关 |
| `barWorldWidth/Height` | 0.85 / 0.12 | 尺寸（已对，保持） |

---

## 5. 普通点击音符：推荐做法（详）

### 5.1 同体换皮（解决硬伤①）
- **几何一行不动**：复用原始 `RoundedRectMesh` 平躺板 + 已验证的尺寸/位置/粉杠显形数学。
- **只换材质层**：把「纯黑材质」换成 **URP 内置材质**（Lit 或 Lit+Transparent），把 `Note_Tap.png` 当 `_MainTex` 贴上。
- 为什么稳：绕开「自定义 `AdditiveSprite.shader`（CGPROGRAM）URP17 未加载→光晕退化过曝」这个总根因；尺寸由几何决定、贴图只是画上去，不会放大溢出（满足 A3 不超轨）。
- 这就是「换皮不是重建」——和硬伤①直接对着干。

### 5.2 轻立体化三件套（解决硬伤②，逐层叠加）
| 层 | 做法 | 解决什么 | 是否换管线 |
|---|---|---|---|
| ① 受光 | 材质用 **URP Lit**（非 Unlit），顶面接收方向光 → 有明暗 | 平躺板不再是一张均匀纸 | 否（内置 shader） |
| ② 描边 | COTL 粗黑描边：放大深色背面体（经典 toon outline）或贴图自带描边 | 卡通辨识度 + 边缘体积感 | 否（仅多一个 pass/网格） |
| ③ 接触阴影 | 音符下加贴地暗斑（复用 BlobShadow 思路或简化 quad） | 落地感、强化立体 | 否 |
| （可选）微厚度 | 给音符 mesh 少量挤出/倒角，产生真实侧面 | 极强立体感；代价是动了 mesh 数据，但仍同 MeshRenderer 体 | 否（仍同 primitive） |

> 优先级：先 ① 受光（N2），再 ② 描边（N3），仍扁再加 ③/微厚度（N6）。每步独立验证。

### 5.3 命中 / Miss 反馈（复用原始 tint 逻辑）
- 命中：材质 tint 白 + 整体 ×1.15 + 沿轨减速淡出（A5）。
- Miss：材质 tint 黑 + 整体缩小先慢后快 + 减速前进（A7）。
- 原始 `color` 逻辑几乎零改动即可驱动（tint 乘在贴图上）。

### 5.4 普通点击音符参数/文件清单（仅讨论，未执行）
- 新增材质：`Assets/Art/UI/Materials/M_NoteTap_Lit.mat`（URP Lit + `Note_Tap.png` 作 `_MainTex`）。
- 改 `NoteMover.cs`：Init 里 `noteMaterial = new Material(noteTapLitMat)` 替换原纯黑材质；命中/Miss 改 tint（原始已是改 `color`，基本不动）。
- 不改：`NoteSpawner` 几何/判定数学、能量槽、`RuntimeSpriteUtility`（能量槽还在用）。
- 回退：不满意 `git checkout HEAD --` 立刻回黑色稳定版。

---

## 6. 待用户确认项（对齐用，确认后才执行）
1. 能量槽锚点：**同意用 BlobShadow 脚底接触点 + footLift 作为准确定位方案**（而非盲调 markerFootYOffset）？
2. 能量槽是否要我顺手给 `BlobShadow` 加 `lastGroundY` 公开字段（N1 的前提）？
3. 普通点击音符：确认 **同体换皮（MeshRenderer+URP Lit 贴图）** 路线，且立体感按 ①受光→②描边→③阴影 顺序叠？
4. 普通点击贴图底板：直接复用现有 `RoundedRectMesh` 圆角板当画布（最简），还是要一块真·Quad？
5. 执行顺序：先 E1（能量槽位置）→ E2（CD 复核）→ N1（换皮）→ N2… 是否认可？

---

## 7. 回退与备份约定
- 所有改动前先 `cp` 当前文件到 `Backup/YYYY-MM-DD-<描述>/`，可整体回退。
- 音符任一切分不满意：`git checkout HEAD -- Assets/Scripts/NoteMover.cs Assets/Scripts/HoldNote.cs Assets/Scripts/NoteSpawner.cs` 回黑色稳定版。
- 能量槽回退：`FeverManager.EnsureEnergyBar()` 改回调用 `EnergyBarUIController.EnsureExists()`（旧文件保留）。
- 自定义 shader（`AdditiveSprite.shader`）在本轮回溯中确认 URP17 下未加载，**新方案一律不依赖它**；发光如需加法，改用 URP 内置 Additive 材质或 `RuntimeSpriteUtility.AdditiveGlow`（已确认可加载）。

---

## 8. 执行记录（2026-09-14 晚）

> 用户澄清：「点击音效」是口误，实际要做的是**普通点击音符的换皮（视觉替换）**，音效暂时不做。范围收紧：只做「普通点击换皮」+「能量槽位置」，其他音符类型一律不动，先各自跑通。

### 8.1 已执行：普通点击音符换皮（N1）
- 改 `NoteMover.cs`：
  - 新增 `tapTexture`(Texture2D) / `tapNoteMaterial`(Material) 字段；判定 `isPlainTap`（非小点击/非连点/单轨）且能取到贴图（优先 Inspector 的 `tapTexture`，否则回退 `NoteSpriteLibrary.Instance.tap.texture`）才 `textured=true`。
  - 换皮音符改用 `RoundedRectMesh` 圆角平板当画布（`CreateRoundedRectMesh` 补了 UV，0..1 铺满贴图）。
  - 材质：`textured` 时用成品 `tapNoteMaterial` 或运行时 `MakeTapMaterial()`（URP Unlit + Transparent + `_BaseMap`=爪印）；非换皮音符保持原黑底（行为不变）。
  - 新增 `SetNoteTint/GetNoteTint` 统一着色（URP Unlit 用 `_BaseColor`，旧材质用 `_Color`），把原 `noteMaterial.color` 全部改走 helper，**其他音符类型的颜色行为不变**。
  - 可见态基础色：换皮=白（显贴图），非换皮=黑。
- 关键约束：只作用于普通点击；小点击/跨轨/连点/按住/Slide 全部走原黑底逻辑，未碰。

### 8.2 已执行：能量槽位置（E1，脚底锚定）
- 改 `BlobShadow.cs`：新增公开字段 `lastGroundY`（射线命中地面 Y），供能量槽复用。
- 改 `EnergyBarWorldSpace.cs`：
  - 新增 `anchorToFoot=true / footLift=0.03 / footOffsetZ=0 / showAnchorDebug=false`。
  - `FeetWorldY` 优先用 `BlobShadow.lastGroundY + footLift`，回退包围盒 / `markerFootYOffset`。
  - `Follow` 与初始定位的 Z 加 `footOffsetZ`（锁视角下把槽推到角色正下方）。
  - `showAnchorDebug` 开启时画青色线框球 Gizmo，便于确认是否压在脚下。

### 8.3 备份
- `Backup/2026-09-14-tap-replace/`：改前的 `NoteMover.cs` / `EnergyBarWorldSpace.cs` / `BlobShadow.cs` 三份。

### 8.4 待回 Unity 验证
1. 普通点击 = 卡通爪印（贴地、过粉杠才显、命中黑→白放大消失、Miss 缩小消失）；其他音符仍是黑色。
2. 能量槽贴在角色脚下/屏幕下方；若还想更靠下，调 `footOffsetZ`（正/负看锁视角方向）。
3. 若爪印不显示：确认 `NoteSpriteLibrary.tap` 已赋值，或在 NoteMover 上直接拖 `tapTexture`；若爪印太亮/方向不对，反馈再调。

---

## 9. 执行记录（2026-09-15：能量槽绑定音轨 + 普通点击视觉收窄）

> 用户拍板：① 音符用**方法 B**（只加 `tapVisualScaleMul` 视觉倍数，不动 `noteRadius`、不影响判定窗口、只缩普通点击）；② 能量槽**绑定音轨固定锚点**、不绑角色，位置钉死；③ 玩家能量槽 `lane=-1` 自动取玩家标记位置；④ 能量槽整体 X 缩放 = 用户运行时调好的 `0.7792`。

### 9.1 已执行：普通点击视觉收窄（方法 B）
- `NoteMover.cs` 新增：
  - `public float tapVisualScaleMul = 0.687f;`（≈ 用户把 scale.x 由 1.2 调到 0.824 的倍数，只缩视觉）。
  - `public float tapAlpha = 1f;`（半透明准备参数，需透明背景 PNG 才生效；默认 1 = 不变）。
  - `private float visibleAlpha;` 记录可见态不透明度，换皮时 = `tapAlpha`。
- `Init` 里 `isPlainTap` 判定后，对普通点击整体（X/Z 等比）乘 `tapVisualScaleMul` → 收窄成胶囊且不超当前音轨；`noteRadius` 与 `goodWindow` 判定窗口完全不变。
- 半透明：`SetAlpha` 改为 `c.a = visibleAlpha * alpha`，换皮音符可见态不透明度 = `tapAlpha`。
- **未碰**：小点击/跨轨/连点/按住/Slide 的颜色与尺寸逻辑；命中/Miss 反馈逻辑。

### 9.2 已执行：能量槽绑定音轨（E1 修订 → 音轨锚定）
- `EnergyBarWorldSpace.cs`：
  - 新增可序列化 `LaneAnchor` 类（side / lane / position / useThis）。
  - 新增 `laneAnchors` 默认 4 条（side=0 的 lane0~3 坐标，即用户给的 EBW_S0_L0~L3 世界坐标）。
  - 新增 `barScaleX = 0.7792f`（整体收窄，等同用户运行时 scale.x:1→0.7792）。
  - `BuildForCharacter` 改用 `GetAnchorPosition(side,lane)` 取固定坐标，`root.localScale.x = barScaleX`，**位置不再追角色**。
  - `WorldEnergyBar.Follow()` 改为：普通音轨位置钉死在锚点；仅 `lane==-1`（玩家）晚于 BuildAll 生成时吸附到玩家标记（放玩家身下）。
  - 删除旧「脚底锚定」字段（`anchorToFoot/footLift/footOffsetZ/useRendererBounds/markerFootYOffset/feetYOffset/followLerp`）与 `FeetWorldY`（已被锚点方案取代）。
  - `EnergyTheme` 与 CD/充能状态机、阵营主题色逻辑**未改**（用户本次说先不动色）。
- 数据读取仍走 `CharacterRoster.GetTeam(side,lane)`（位置不绑角色、但能量/CD 数据绑角色：角色换人后下次 `BuildAll`/OnRosterChanged 重建即更新）。

### 9.3 备份
- `Backup/2026-09-15-energy-lane/`：改前的 `NoteMover.cs` / `EnergyBarWorldSpace.cs`。

### 9.4 待回 Unity 验证
1. 普通点击明显变窄（约一半），仍是胶囊、不超当前音轨；其他音符尺寸不变。
2. 4 条 side=0 能量槽钉在你给的锚点坐标上（不再随角色漂）；整体更窄（×0.7792）。
3. 玩家能量槽：当主角有能量（energyCost>0）时出现在玩家身下；当前主角 energyCost=0 不显示属正常。
4. 若某条能量槽位置仍偏：直接在 `EnergyBarWorldSpace.laneAnchors` 里拖该 (side,lane) 的 Position 即可（Inspector 改完运行时生效并保存）。
5. 半透明要等透明背景 PNG：替换 `Note_Tap.png` 为爪印外 alpha=0 的版本后，把 `tapAlpha` 调到 ~0.7 即半透。

---

## 10. 执行记录（2026-09-15 第二轮修正：Z 轴保持 + 透明材质加固 + 锚点角色对应修正）

> 用户反馈：① 普通点击收窄时 Z 轴也被缩，要求只压 X 保持 Z；② 爪印贴图透明区域仍不透明；③ 4 条能量槽位置全错，lane 与角色对应关系反了。
>
> 用户确认贴图原图透明背景正确（棋盘格），说明 PNG 本身有 Alpha，问题在材质未强制 Transparent。

### 10.1 已执行：普通点击只缩 X、Z 轴保持
- `NoteMover.cs`：
  - `Init` 里 `isPlainTap` 分支从 `new Vector3(xDiameter * tapVisualScaleMul, 0.12f, zDiameter * tapVisualScaleMul)` 改为只乘 X：
    `new Vector3(xDiameter * tapVisualScaleMul, 0.12f, zDiameter);`
  - 效果：X 按 0.687 缩到约 0.824，Z 保持原长度（约 1.2），判定窗口仍不变。
  - 代价/注意：贴图爪印在 Z 方向会被相对拉长，因为几何压 X 没压 Z；如要图案不变形，需美术出窄比例的源图。

### 10.2 已执行：透明材质强制设置
- `NoteMover.cs` `MakeTapMaterial()` 加固：
  - 去掉 `Unlit/Color` 兜底（避免不透明 fallback）。
  - 找不到 `Universal Render Pipeline/Unlit` 时只 fallback 到 `Unlit/Transparent`。
  - 强制 `mat.SetFloat("_Surface", 1f)`、`EnableKeyword("_SURFACE_TYPE_TRANSPARENT")`。
  - 强制 `mat.SetFloat("_Blend", 1f)`（Alpha 混合）。
  - 关闭 AlphaClip：`mat.SetFloat("_AlphaClip", 0f)`、`mat.DisableKeyword("_ALPHATEST_ON")`。
  - 保持 `renderQueue = Transparent`。
- 前提：`Note_Tap.png` 本身必须有 Alpha 通道（用户确认有）。

### 10.3 已执行：能量槽锚点角色对应修正
- `EnergyBarWorldSpace.cs` 更新 `laneAnchors` 默认值（side=0）：
  - lane 0：大狗 → `(-7.20, 0.01, -2.71)`
  - lane 1：屎屎 → `(-6.51, 0.06, -1.30)`
  - lane 2：布姆 → `(-6.42, 0.17, 0.14)`
  - lane 3：小黑 → `(-7.14, 0.06, 1.81)`
- 说明：之前把用户图片顺序（先发的是 lane 3）误填成 lane 0~3，现已按用户表修正。

### 10.4 备份
- `Backup/2026-09-15-tap-fix/`：改前的 `NoteMover.cs` / `EnergyBarWorldSpace.cs`。

### 10.5 待回 Unity 验证
1. 普通点击：X 变窄、Z 长度不变，整体呈窄胶囊；爪印外透明区域正确透明。
2. 能量槽：4 条槽分别压在大狗/屎屎/布姆/小黑脚下对应坐标；整体 ×0.7792。
3. 玩家槽：主角有能量时出现在身下灰色大方块处。
4. 若位置仍有偏差：在 `EnergyBarWorldSpace.laneAnchors` 里直接拖对应 `(side,lane)` 的 `Position`。

---

## 11. 讨论（2026-09-15 第三轮：aibo 样式靠效果图 + 玩家槽对齐）

> 用户：① aibo（非玩家角色）能量槽样式往效果图形象靠；② 玩家槽对齐之前讨论方案（主角无能量，显示 CD 态）；③ 优化方案先看，不执行。

### 11.1 文档检索结论
- 能量槽方案唯一存档 = 本 Plan 文档 §状态机（aibo 4 态 / hero 3 态）。
- `BattleScene_Blueprint_对照说明.md` 仅含能量烟雾 VFX 占位（`VFX_EnergySmoke`），**无能量槽 UI 节点**。
- `CultOfTheLamb_技术拆解.md` 卡通风格依据：粗黑描边是**独立图层**、Doodle UV 抖动着色器、水彩渗色、Toon 光照。
- **缺失**：效果图本身的量化参数（颜色/形状/填充动画），需用户补参照（图或文字描述）。

### 11.2 玩家槽对齐（hero 3 态修正）
- 当前缺口：主角 `energyCost=0` → `maxEnergies` 全 0 → `BuildForCharacter` 的 `n==0 return` → 不建条；即便建条，无能量时 `ratio=0` 永远空，不会触发"可使用"银白闪烁。
- 修正方案：
  - `BuildForCharacter` 对 `c.isPlayer || lane == -1` 用 `activeSlots.Count` 作槽数（即使 `maxEnergies` 全 0 也建条）。
  - `WorldEnergyBar.Tick` 玩家分支：用 `!skillBusyArr[slot]`（技能不在 CD = 可使用）驱动 `ReadyHero`（银白↔暗灰闪烁 0.8s）；CD 中走暗灰空槽；**不依赖能量满判断**。
  - 位置 `GetAnchorPosition(-1)` 取玩家标记（灰大方块）身下。
- 双主动 = 左右 2 槽各独立 3 态（Plan 原方案）。

### 11.3 Aibo 槽往效果图靠（视觉增强，待效果图参照）
- 当前视觉：圆角长条 + 黄框黄条（扁平生成），4 态状态机已实现，但不够卡通。
- 增强方向（COTL 风格，按优先级）：
  1. **粗黑描边**：BG 外加一层深色描边 Sprite（COTL 标志，独立图层）。
  2. **填充调色**：高饱和 Aura 黄 `(1,0.85,0.20)` 作充盈/一般，更深棕黄作 CD 暗部（对齐 `BattleScene_Blueprint` 的 Aura 色）。
  3. **圆角更大**（上一轮方案 0.35）贴合卡通。
  4. **可选 Doodle 抖动**：边缘 UV 抖动（ToonDoodle_Shader），需验证 URP 兼容（之前自定义 shader 有加载失败先例，谨慎）。
  5. **脉冲/闪烁参数**（`aiboPulseAmount`/`aiboGlowCycle`/`heroBlinkCycle`）按效果图微调。
- 需用户补：效果图里 aibo 槽的颜色 / 形状 / 填充动画，否则按 COTL 通用风格给默认增强。

### 11.4 待确认（对齐用）
1. "2 角色"= aibo（非玩家）理解对吗？
2. 玩家槽：双主动显示 2 槽（左/右各一）还是 1 个汇总条？状态用"技能 CD"驱动 OK？
3. aibo 槽：先按 COTL 风格加描边 + 调色（不引入新 Shader），还是等用户发效果图精确对齐？
4. 是否引入 Doodle 抖动 Shader（需验证 URP 加载）？

---

## 12. 执行记录（2026-09-15 第四轮：能量槽样式往效果图靠）

> 用户拍板：① aibo 与 player 能量槽样式都往效果图靠（粗黑描边、近黑底、亮框、胶囊形）；② 玩家双主动左右各一槽（确认）；③ 抖动不加；④ 只改能量槽，不动音符。

### 12.1 形状：圆角矩形 → 胶囊 + 三层
- `GenBarSprite` 重写：新增 `cornerFrac`（圆角占半高比例，≈1=胶囊）、`outline`（粗黑描边色）、`outlineT`（描边像素）、`innerShrink`（填充内缩）参数。
- 生成逻辑改为**外粗描边 → 内框 → 填充**三层（SDF 近似）。
- 纹理从 128×32 升到 128×48（更高精度描边，显示尺寸仍由 barWorldWidth/Height 决定）。
- 新增 Inspector 参数：`barOutlinePixels=5`（粗黑描边）、`barFramePixels=3`（内框）、`barCornerRadiusFrac=0.95`（椭圆/胶囊）、`barFillShrink=2`（填充内缩防盖框）。

### 12.2 加粗黑描边 + 调色往效果图靠
- `EnergyTheme` 加 `aiboOutline` / `heroOutline`（默认近黑 `0.04,0.04,0.04`）。
- side0 主题色调整：aibo 黄框 `1,0.82,0.10` + 近黑底 `0.08,0.06,0.02` + 黄填充；player 银白框 `0.90,0.90,0.95` + 近黑底 + 银白填充；充盈/可用亮色已对齐。

### 12.3 玩家槽强制显示（CD 驱动）
- `BuildForCharacter`：`isPlayer`（lane==-1 或 c.isPlayer）用 `activeSlots.Count` 作槽数（双主动=2，至少 1），**不再因 maxEnergies 全 0 而 return**。
- `WorldEnergyBar.Tick` 玩家分支不再看能量：`!busy` → `ReadyHero`（银白↔暗灰闪烁、满）；`busy` → `ChargingHero`（暗灰空槽）。
- `ChargingHero` 玩家用 `releaseCol`（暗灰）而非亮银，区分 CD 态。
- 位置 `GetAnchorPosition(-1)` 取玩家标记（灰大方块）身下，左右两槽按 `totalW` 对称分布。

### 12.4 备份
- `Backup/2026-09-15-bar-style/Character/EnergyBarWorldSpace.cs`

### 12.5 待回 Unity 验证
1. 4 条 aibo 槽：粗黑描边 + 黄框 + 近黑底 + 黄填充，胶囊形；充盈闪烁、CD 暗黄。
2. 玩家（小熊）脚下左右各一银白框槽：技能可用=银白闪烁、CD=暗灰空槽。
3. 若描边太粗/太细：调 `barOutlinePixels`；若不够圆：调 `barCornerRadiusFrac`（↑更圆）。
4. 若玩家 CD 态也想让整槽变暗（连框）：需给 bg 加 tint（当前仅 fill 受色，后续小步再加）。
5. 抖动（Doodle）按用户要求未加。


## 13. 执行记录（2026-09-15 第五轮：跨轨换皮 + 无描边三层 Sprite + 竖杠）

### 拍板内容
1. 拉伸修复：`tapVisualScaleMul` 等比作用于 X/Z（不再单独压 X 破坏贴图比例）。
2. 能量槽贴图放 `Assets/Art/UI/EnergyBar/`，加载改 **Inspector 拖引用**（弃 Resources.Load）。
3. 样式**无描边**，三层：槽(胶囊环) / 条(填充) / 底色(内部衬底)。
4. 玩家双主动槽中间竖杠：**独立 BG_Dual 贴图**（环+竖杠一体，对齐永不错位）。

### 改动清单
- **NoteMover.cs**：+`wideTexture`、`isWideTap`、`activeTex`；跨轨点击换皮 Note_Wide.png（尺寸暂不动先看效果）；tapVisualScaleMul 改 XZ 同乘。
- **EnergyBarWorldSpace.cs**：Sprite 字段扩为 4（BG / BG_Dual / Track / Fill）；`BuildVisuals` 三层排序 Track(底)→Fill(中)→BG环(顶)；修 `ApplyRatios` 复合缩小 bug（基准 scale + 左缘钉死，兼容任意 pivot Sprite）；Resources 兜底删除改警告。
- **生成 4 张灰度 Sprite**（256×96，标准圆角矩形 SDF，白色/中灰、颜色由主题色 tint）：
  `EnergyBar_BG.png`(白环) / `EnergyBar_BG_Dual.png`(白环+竖杠) / `EnergyBar_Track.png`(灰底) / `EnergyBar_Fill.png`(白条)。
- 备份：`Backup/2026-09-15-ui-bar/`。

### 待 Unity 操作与验证
1. **必须**：把 4 张 PNG 拖入 EnergyBarWorldSpace 的 `barBgSprite` / `barBgDualSprite` / `barTrackSprite` / `barFillSprite` 字段（空引用会在 Console 打警告）。
2. 跨轨音符显示 Note_Wide 贴图（拉伸/比例下一轮调）。
3. 普通点击不再拉伸（贴图比例 X/Z + 等比 mul）。
4. 能量槽三层显示、颜色正确 tint；玩家 1 条 2 段带竖杠；释放只清条。

---

## §10 第十轮：闪烁白黄 + 隐藏黑底 + 玩家双槽 Fill_Dual + 固定锚点 + 真实三维遮挡

### 用户拍板
1. 充满闪烁亮端 `aiboFullA` 改纯白 → 白↔黄忽闪（与黄差异明显）。
2. 黑底(Track)加开关 `showTrack`（默认 false），只留槽环 + 条；可勾回。
3. 玩家双槽填充条用专门 `EnergyBar_Fill_Dual`（整条含左右两段的图）：运行时 `Sprite.Create` 从正中切两半、原尺寸显示、各管一段。aibo 单条继续用 `EnergyBar_Fill`。
4. 位置：新增 6 条固定锚点（左玩家 side0/-1、右玩家 side1/-1、右侧 4 轨 side1/0~3，坐标按用户截图 Transform）；左侧 4 条 aibo 不动。玩家条固定不跟随（`Follow` 在有固定锚点时跳过）。
5. 遮挡用真实三维：`Start` 把 `Camera.main` 设 `Custom` 透明排序（axis = 相机朝向）；三层 + Glow 同 `sortingOrder`、靠 barRoot 局部 Z 微偏移（layerZTrack/Fill/BG/Glow）分层，与 spine 同层按相机距离前后遮挡。

### 执行落地（EnergyBarWorldSpace.cs，备份 Backup/2026-09-15-fill-dual-occlusion/）
- `themeSide0.aiboFullA` = (1,1,1)；新增字段 `showTrack`、`barFillDualSprite`、`layerZTrack/Fill/BG/Glow`。
- 缓存 `_cachedFillDualSprite` + `Resources.Load("EnergyBar/EnergyBar_Fill_Dual")`；`EnergyBar_Fill_Dual.png.meta` 补 `textureType:8/spriteMode:1/alphaIsTransparency:1`。
- laneAnchors 扩成 10 条；`GetAnchorPosition` 含 -1；新增 `HasFixedAnchor`。
- `Follow`：固定锚点则钉死。
- `BuildVisuals`：Track 用 `if(showTrack)` 包裹；playDual 切左右半 Fill（pivot 左中、PPU 同原图）；缩放段 dual=原尺寸(fillScaleX=1)、左段 fillLeftX=-designW/2、右段=0；三层+Glow 去 sortingOrder 差异、各设 localPosition.z = layerZ*。
- `ApplyRatios` 保持 Fill 的 z = layerZFill。
- `Start` 调 `SetupTransparencySort()`；`sortingOrder` 默认 0。

### 待 Unity 验证
- 无黑底；充满白↔黄闪烁；玩家双槽用 Fill_Dual 显示两段、释放清单段；6 个新位置正确；玩家条钉死；spine 与条按真实距离前后遮挡。
- 若 Fill_Dual 竖杠不居中，加 `dualSplitRatio`（默认 0.5 居中切）。

