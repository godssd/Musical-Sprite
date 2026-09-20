# 附魔音符换色统一规则手册（RECOLOR_RULES）

> 适用：Musical-Sprite 音符换皮（同体换皮只换材质，几何不动）。
> 本文档是第 32–49 轮逐像素验证后沉淀的**唯一权威依据**。任何"批量生成换色附魔音符"都必须以本手册 + `Tools/recolor_batch.py` 为准，禁止凭记忆或近似色手改。
> 原则：**0失误 = 以生成器输出为唯一真相源，锚点取色只来自实测样本，禁止自造均值/近似色。**
> 附魔机制（名额计数 / 皮肤套用 / 逐节点·逐数字恢复规则）的权威手册见同目录 **`附魔规则.md`**；本文件只负责皮肤资产生成。

---

## 1. 完整文件清单（每皮肤 31 张）

一张皮肤 = **31 张**附魔贴图（与 003/howl 完整集结构一致）：

| 族 | 文件 | 数量 |
|---|---|---|
| 音符本体 | `Note_Tap` / `Note_Tap_Select`（=美术样本） | 2 |
| 音符本体 | `Note_Wide` / `Note_Wide_Select` | 2 |
| 音符本体 | `Note_Tap_Small` / `Note_Tap_Small_Select` | 2 |
| 数字族 | `Note_Repeat_{0..9}` / `Note_Repeat_{0..9}_Select` | 20 |
| 数字族 | `Note_Repeat` / `Note_Repeat_Select`（generic） | 2 |
| 链接条族 | `Note_Slide_Link` / `Note_Slide_Link_Select` | 2 |
| 判定条 | `Note_Slide_Judgment` | 1 |

| 角色 | 路径 |
|---|---|
| 基底模板（未换色原图） | `Assets/Art/UI/sprites/Note_*.png`（根目录） |
| 皮肤目录 | `Assets/Art/UI/sprites/00X/`（003=howl, 004=croissant 参照, 005=bomb_rain） |
| 批量生成器（统一规则+0失误校验） | `Tools/recolor_batch.py` |
| 调色板锚点 | `Tools/recolor_palette_*.json` |
| 生成器临时/验证产物 | `Tools/_gen_recolor/<skin>/` |

---

## 2. 锚点与来源模型

锚点只定义 4 个色（每皮肤）：
```
tap_normal_outer / tap_normal_inner   # Tap普通 外圈/内圈（环）
completed_outer  / completed_inner    # 完成态 外/内
```

**锚点取色规则（按样本配置分两种情况）：**
- **情况 A（004 式）**：同时有 Tap普通样本 + 独立完成数字样本 → 完成锚点取自完成数字实测（**完成系 ≠ Tap_Select**，004 实证：完成数字 (147,213,254)/(123,234,245) vs Tap_Select (103,233,198)/(103,251,207)）。
- **情况 B（005 式）**：只有 Tap普通 + Tap_Select 两张样本 → 普通锚点 = Tap普通外/内圈；完成锚点 = Tap_Select 外/内圈（完成态无深圈铁律天然成立）。

> 005 (bomb_rain) 实测锚点：普通 外(110,107,86)/内(225,224,212)；完成 外(187,183,154)/内(195,190,159)。

---

## 3. 结构对应律（004 样本逐像素实证）

| 元素 | 取色来源 | 004 实证 |
|---|---|---|
| 普通数字 | = Tap 普通 外圈/内圈 | 数字 OUT(33,112,84)=TapOUT(31,112,84)；FILL(98,188,158)=TapFILL(97,187,157) ✓ |
| 普通条 | 芯=数字外(Tap外)，边=数字内(Tap内) | 条芯(34,114,85)=数字OUT；条边(97,187,157)=数字FILL ✓ |
| 完成数字 | = 完成态专属配色（情况A）/ Tap_Select（情况B） | 004 完成数字 (147,213,254)/(123,234,245)（≠Tap_Select）✓ |
| 完成条 | 边=完成数字外（逐像素一致），芯=完成态亮色 | 004 完成条边(147,213,254)=数字OUT ✓ |

**芯边角色两态互换**（用户第41轮铁律）：普通条 边→内/芯→外；完成条 边→外/芯→内。

**完成态无深圈铁律（第49轮 howl 小tap_Select 实证）**：完成态（Select）系一律无深色外圈——数字、条、Wide_Select、Tap_Small_Select 全部如此；深环只存在于普通态本体（Tap/Wide/Tap_Small）。

---

## 4. 四大族与换色方法

### 4.1 digit 数字族（Repeat 0-9 + generic）
轴投影平涂（零渐变）。簇检测 = **digit 模式**（BFS 距离场：OUTLINE=距透明≤2px，FILL=距透明≥5px）。
```
digit normal :  outline→tap_normal_outer , fill→tap_normal_inner
digit select :  outline→completed_outer   , fill→completed_inner
```
> ⚠️ generic Repeat 的源簇与索引数字**不同**（OUT=(205,132,92)/FILL=(186,104,71)），簇检测必须逐文件做，不可硬编码源锚点。

### 4.2 bar 链接条族（Slide_Link）
簇检测 = **bar 模式**（紧裁切全不透明，距透明法失效 → 周界采样：每列上下边缘行=EDGE，列中点=CORE）。
```
bar normal :  edge→tap_normal_inner , core→tap_normal_outer   # 边=内, 芯=外
bar select :  edge→completed_outer  , core→completed_inner    # 边=外, 芯=内
```

### 4.3 ringpaw 音符本体族（Tap / Wide / Tap_Small 及 Select）

**轴投影公式**（所有轴投影共用）：
```
t = proj(pixel, sA→sB) = dot(pixel-sA, sB-sA)/|sB-sA|² ，clamp[0,1]
out = tA + t*(tB - tA)     # ✅ 正确公式（旧bug sA+…已杜绝）
```

- **Tap / Tap_Select**：美术样本直接采用，不生成。
- **Select 对（Wide_Select / Tap_Small_Select）**：**同几何逐像素色彩映射**——
  基底 `Note_Tap_Select.png` 与皮肤 Tap_Select 样本同尺寸同形状，逐像素建
  `(base_rgb → sample_rgb)` 众数映射表（未知色用最近邻回退），套用到 Wide_Select / Tap_Small_Select。
  完成态无深圈，映射天然正确。**自还原率校验必须 = 1.0000**（005 实测）。
- **Normal 对（Wide / Tap_Small / generic Repeat）**：⚠️ **正确方法 = 用「验收过的参照（howl）」做结构源，套皮肤调色板重上色（簇最近分类 + 调色板映射）**。第51轮实证：
  - 基底 `Note_*.png` 是**平涂红棕**（环与身同色 `(187,87,59)`），环无法靠颜色定位；且音符块**铺满整张图、四边贴边**（实测 `body_x[0..W-1]`/`body_y[0..H-1]`），距离场只把「透明像素」当外部源时，赤道/左右永远算不到"到透明的距离" → 环只在上/下出现或干脆不画 → **v49/v50 的 `ring_paint`/`adopt` 之路天生错误，已废弃**。
  - 而 howl 版（用户验收过）的环/身/爪是**可区分的多色结构**（如 Small 身(222,189,126)+环(116,78,35)+爪(169,134,80)；Repeat 暗胶(134,88,44)+亮胶(223,175,60)），几何与同一形状完全一致。所以直接拿 howl 当结构源做 **palette-swap**：
    1. 按 howl 真实调色板种子做最近邻分类（Small：环/爪/身 三簇；Repeat：暗/亮 两簇；Wide：环/身 两簇）；
    2. 映射：环/暗 → `tap_normal_outer`，身/亮 → `tap_normal_inner`，**爪 → 沿 howl(环→身)轴投影到皮肤(外→内)轴的对应中调**（Small bomb_rain = (167,166,149)）；
    3. alpha 掩码从 howl 源**逐像素原样保留**。
  - 实证：Small/Repeat 生成色 0 杂色（仅环/爪/身或环/身），alpha 0 不一致，环厚/爪形 = howl 原样（用户验收几何），仅换色。
  - ⚠️ **bomb_rain ≠ howl 简单重上色**：用重上色 howl 去比对 005 真样本 Tap，环像素 3855 vs 样本 1312（Δ14.87）——美术给 Tap 样本画了更精修的细环，所以 Tap 以真样本为准（不重上色）。但 Small/Repeat/generic Repeat **无 bomb_rain 真样本**，只能以验收过的 howl 为结构源换色，这是唯一可靠路径。
  - 通用生成器 `recolor_batch.py` 的 ringpaw-normal 路径目前仍是废弃的 `adopt/ring_paint`，**须改为上述 reskin-howl 方法**才能整批正确重生成（见第8节遗留）。

### 4.4 judgment 判定条（Slide_Judgment）
纯色条（9×69 单色）→ 整体填 `tap_normal_inner`（howl 实证：howl 版判定条 (222,189,126) = howl 身色）。

---

## 5. 铁律（不得违背）

1. **取色只来自实测样本锚点**，禁止自造均值/近似色（v10 百分位、v11 均值色均被用户打回过）。
2. **完成系配色独立**（情况A 时 ≠ Tap_Select）；**完成态一律无深色外圈**（铁律②扩展）。
3. **命中 / Repeat 系永远无深色外圈**（第32轮验收旧版为冻结态）。
4. **用户验收过的图 = 冻结态**：后续"替换旧文件"类指令不波及已验收图（第38/39轮教训）。
5. **条=零渐变平涂**，禁用百分位果冻曲线（第40轮教训）。
6. **每次只改一块、可回退**（旧实现隐藏不删）、参数可调、回 Unity 截图验证再下一步。
7. **0失误**：换色一律走生成器；生成器复测不符即 `exit(1)`，禁止人工覆盖。
8. **全族覆盖**：一张皮肤 = 31 张（见第1节）。数字+条只是其中 24 张，**音符本体（Wide/Tap_Small×2态）和判定条也是换色范围**（第49轮教训：只整合 digit+bar 是错的）。

---

## 6. 权威调色板

### howl (skill_3) —— 003 标准
```
tap_normal_outer = (115, 77, 34)     tap_normal_inner = (219, 186, 124)
completed_outer  = (251, 179, 91)    completed_inner  = (255, 246, 98)
```

### bomb_rain (skill_5) —— 005 标准（第49轮锚点 + 第51轮对齐真样本）
```
tap_normal_outer = (109, 107, 86)    tap_normal_inner = (225, 225, 213)   # 真样本 Tap 轮廓中位(109-112,107-108,86-87)、身(228/225,225,213)
completed_outer  = (187, 183, 154)   completed_inner  = (195, 190, 159)
```
> 第51轮生成 Small/Repeat 即用 `outer=(109,107,86)`、`inner=(225,225,213)`、`爪中调=(167,166,149)`（爪沿 howl(116,78,35→222,189,126) 轴投影到 bomb_rain(109,107,86→225,225,213) 轴）。
> ⚠️ JSON `recolor_palette_005_bomb_rain.json` 的 `anchors` 仍写 (110,107,86)/(225,224,212)，与真样本 ±1 一致，若要保持"0失误"严格一致建议同步改为 (109,107,86)/(225,225,213)。
JSON：`Tools/recolor_palette_005_bomb_rain.json`（含 ringpaw 配置）。

---

## 7. 批量生成操作步骤（新皮肤）

1. 美术提供该皮肤两张样本：`Note_Tap<suffix>.png` + `Note_Tap_Select<suffix>.png`，放入 `00X/`。
2. 实测 4 个锚点（轮廓/内部分离取中位数），复制改一份 JSON：
   ```json
   {
     "name": "myskin", "base_dir": "Assets/Art/UI/sprites",
     "output_dir": "Tools/_gen_recolor/myskin", "suffix": "_skill_X_myskin",
     "anchors": {
       "tap_normal_outer": [R,G,B], "tap_normal_inner": [R,G,B],
       "completed_outer":  [R,G,B], "completed_inner":  [R,G,B]
     },
     "generic_digit": true,
     "ringpaw": {
       "map_select_from_sample": "00X/Note_Tap_Select_skill_X_myskin.png",
       "select_targets": ["Note_Wide_Select.png", "Note_Tap_Small_Select.png"],
       "normal": [
         { "name": "Note_Wide.png",      "adopt_from": "结构正确文件.png" },
         { "name": "Note_Tap_Small.png", "ring_paint_from": "身+爪正确文件.png", "ring_solid": 2, "ring_fade": 1 }
       ]
     },
     "judgment": true
   }
   ```
3. 运行：`python Tools/recolor_batch.py Tools/recolor_palette_myskin.json`（`--dry-run` 只校验，`--out DIR` 覆盖输出）。
4. 生成器自动复测 29 张生成图（锚点容差≤5、select 自还原率≥0.995、alpha 逐像素一致、输出色⊆样本色表），任一不符即 `exit(1)`。
5. 通过后拷入 `00X/`（美术样本 2 张不覆盖）→ 00X/ 共 31 张 → 回 Unity 截图验收。

---

## 8. 验证记录

- 第46轮：howl 22 张（数字+条）Δ0 通过；历史 003 逐张漂移 Δ26 → 生成器=唯一真相源。
- 第49轮：005 全 29 张生成图 Δ0~Δ3 通过（含 generic 数字 Δ0/Δ1、ringpaw select 自还原率 1.0000、alpha 0 不一致、Small 补环后环带 Δ0）；005/ 达到 31 张完整结构。
- 第51轮：**纠正 v49/v50 的 ringpaw-normal 错误方法**。用户打回 005 的 `Note_Tap_Small`/`Note_Repeat`（v49 用 ring_paint 导致环没画上、v50 瞎画粗环+碾平果冻）。实测根因：base 平涂 + 块贴边 → 距离场取环天生失效。正确方法 = 以验收过的 howl 同形状图做结构源、簇最近分类后套 bomb_rain 调色板重上色（Small 保留爪中调 (167,166,149)、Repeat 保留 2 色果冻）。生成色 0 杂色、alpha 0 不一致、几何=howl 原样。旧两张备份于 `005_backup_2026-09-20/`（.v49 后缀），新图已拷入 `005/`。`recolor_batch.py` 的 ringpaw-normal 路径仍待改为 reskin-howl 方法（见第4.3 节）。
- 已知遗留：根目录散落旧 `skill_3_howl` ×7 / `skill_5_bomb_rain` ×5 文件（与 003/005 子目录重复、部分有缺陷如旧 Small 缺环），是否清理待用户拍板；`NoteSpriteLibrary.asset` 的 GUID 引用指向待确认；`recolor_batch.py` ringpaw-normal 须从废弃的 `adopt/ring_paint` 改为 reskin-howl 方法。
