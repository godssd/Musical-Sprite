# 连点音符（Repeat）换皮 + 命中切图 + 后退数字递减 方案

> 状态：**A/B/C/D+E 全部完成（D+E 待用户 Unity 验证：命中显示当前数→撤退→最远点数字-1切回非命中→回来循环；数字=1 命中走 CLEAR 普通命中完成）**。最后更新 2026-09-17 09:55。
> 目标：连点音符拆成「音符体 + 数字」两部分，命中有 Select 切图，后退到最远(1单位)数字-1并整组切回非命中贴图，数字=1 命中走普通命中反馈。

## 0. 现状（已确认，避免理解偏差）
- 连点音符当前是**黑圆柱**（`textured=false`），数字用 `TextMesh`（系统字体）浮在上方显示 `chainTapRemaining`。
- `Note.cs` 的 `RegisterChainTapHit` 在**命中瞬间** `chainTapRemaining--`，立即把新 remaining 传给 mover；最后一下(`remaining<=0`)调 `mover.CompleteChainTap()` 走旧「发白放大淡出」。
- `NoteMover.Update` 撤退运动：命中后以 `chainRetreatDist=1` 匀减速后退，到 `chainTapHoldDuration` 末速0、退到最远，再以 `normalSpeed` 前进回判定线。
- 贴图/库已就位：`repeat`(100×150)、`repeatSelect`(100×150)、`repeatDigits[0..9]`、`repeatDigitsSelect[0..9]` 均在 `NoteSpriteLibrary.asset` 绑定。

## 1. 需求复述（与用户对齐）
1. 连点音符拆两部分：**音符体** `Note_Repeat` + **数字** `Note_Repeat_0..9`（≥10 用两位横向排列），数字**浮在音符之上**。
2. 每次命中：除已有反馈外，切图为**命中音符贴图** `Note_Repeat_Select` + **命中数字** `Note_Repeat_N_Select`（N=当前数）。
3. 后退到最远(1单位)时：**数字下降1**，整组音符+数字**切回非命中贴图**；循环。
4. 直到**数字=1 被命中**：**不再后退**，播放**正常命中反馈**（Select 显 + 放大淡出销毁）。

## 2. 实现分块（每块可独立回退，参数 Inspector 可调）

### A. 连点音符体换皮（黑圆柱 → Note_Repeat 贴图，1:1）
- `Init` 解析 `repeat` 精灵（留空回退 `NoteSpriteLibrary.repeat`，PPU 取 `rect.pixelsPerUnit`）。
- `textured` 增加 `(isChainTap && repeatTex != null)`；mesh 用 `TapRoundedRectMesh`（与普点同套圆角平板）；材质 `MakeTapMaterial(repeatTex)`；尺寸走 `useTextureNativeSize` 1:1（→ 1.0×1.5）。
- 旧黑圆柱分支保留，取不到 repeat 时回退不报错。

### B. 数字 TextMesh → 精灵贴图（核心，替换 CreateChainCountText）—— ✅ 已完成
- 新建子物体 `digitRoot`（浮在音符之上，localPosition.y = `digitYOffset` 默认 **0.55** 局部值 ≈ 卡顶；过卡面 0.12 缩放后约 +0.066 世界）。
- 复刻原 TextMesh 朝向：`digit` 子物体 `localRotation = Euler(90,0,0)` + `localScale.z = -1`（躺平朝上、法线朝上）。
- 单数字(n<10)：一个 quad 贴 `repeatDigits[n]`；双数字(n≥10 假设 ≤99)：两个 quad 沿 **X 并排**（slot0=左=十位，slot1=右=个位）。
- 局部 scale **抵消音符非均匀缩放**（`nsx`/`nsz`，卡面 Z=1.5），数字按真实世界尺寸呈现、不被卡面拉长；`digitScaleMul`(默认1) / `digitSpacing`(默认0.06) 控制大小与间距。
- `SetDigit(n)` 切换显示 + 记录 `digitFiguresCurrent`（各位数字），预埋 `SwapDigitToSelect()`（块 C 命中切 `repeatDigitsSelect[各位]`）。
- 删除原 `chainCountText`/`chainCountRenderer`/`CreateChainCountText`；`SetAlpha`/`ChainClearCoroutine`/`MissCoroutine`/`Update` 中数字淡出/显隐改走 `SetDigitAlpha`。
- 数字材质用 `MakeTapMaterial`（URP Unlit 透明）+ `_Cull=0` 双面，避免朝向/剔除导致看不见。

### C. 命中切 Select 组（扩展现有 _selectTex 体系）
- 连点需两组：体 `_repeatBodySelectTex`(repeatSelect) + 当前数字 `repeatDigitsSelect[chainDisplayCount]`。
- 改写 `ApplySelectTexture()`：连点 → 体切 `repeatSelect`、数字 quad 切 `repeatDigitsSelect[chainDisplayCount]`；非连点 → 原有单 `_selectTex`。
- 命中反馈复用已有 pop（`ChainTapHitPopCo` 弹跳）+ Select 直接显（不闪黑），与普点一致。

### D+E. 递减时机：命中瞬间 → 最远点（核心逻辑，动 Note.cs + mover 撤退运动）
- `NoteMover.Update` 撤退中检测 `te >= chainTapHoldDuration`（=d 到达 `chainRetreatDist` 最远点）的瞬间，触发一次「到达最远」：
  - 整组切回非命中：体→`Note_Repeat`，数字→`repeatDigits[新remaining]`。
  - 回调 `note.OnChainRetreatFarthest()` 让 `chainTapRemaining--`（单一数据源仍在 Note.cs）。
- `Note.cs` `RegisterChainTapHit` 改为：
  - 命中不立即 `--`；`int displayR = chainTapRemaining;` 传 `mover.RegisterChainTapHit(displayR, isFinal: displayR==1, ...)`。
  - `displayR==1`（最后一下）→ `isHit=true`、调 `mover.PlayHitAnimation(rank)`（普通命中完成：Select 显 + 放大×1.4 + 淡出销毁）、`activeNotes.Remove`、返回 true。
  - `displayR>1` → 不递减、返回 false（留 activeNotes）；递减由 D 的最远点回调完成。
- 最后一下走 `HitCoroutine`（非旧 `ChainClearCoroutine` 发白淡出），符合「正常命中反馈」。
- 新增 `chainReachedFarthest` 标志，在 `RegisterChainTapHit` 重置。

## 3. 不改的部分（保持可回退 + 不改判定）
- 判定窗口 / `noteRadius` / `goodWindow` / `laneSpacing` 全不动。
- 连点「重新接触判定线附近 goodWindow 内可点击」的时序（`ChainTapNextContactTime`）不动——递减在更早点(最远)发生，不影响该时刻。
- Miss 路径（缩小淡出）不动，Miss 永远显示非命中贴图（符合「只有命中才变」）。

## 4. 用户拍板结论（2026-09-17 07:32）
1. 数字朝向：**躺平朝上**（与音符体同朝向，参考图：数字印在卡面中央）。粗调先落地，用户回 Unity 运行时细调后报参数，由 AI 精准烤定。
2. 双数字并排：沿 X，若左右反了翻转即可（用户确认此处理方式）。
3. 旧「进度着色」：用户无印象。实际现状 = 命中时连点体按进度黑→白渐变着色（`SetChainBodyColor`）。**决定：去掉该连续渐变**，进度由数字本身表达；保留 pop 弹跳 + Select 切图。
4. Inspector 参数：给最基础的 `digitYOffset` / `digitScaleMul` / `digitSpacing`（位移/缩放用途），支持运行时调；最终参数由 AI 按用户报数烤定。
5. D/E 语义确认：**D = 非最后命中，反馈评价 YES**（切 Select 组 + 后退 + 最远点数字-1 切回）；**E = 最后一次（数字=1）命中，评价 CLEAR，不后退，走普通命中反馈**。与现有 `rank = (chainTapRemaining<=1) ? "CLEAR" : "YES"` 逻辑一致。

## 5. 回退/安全
- 每块单独备份 `Backup/2026-09-17-repeat-A/B/C/DE/`。
- A/B/C 可独立；D+E 联动（建议一起做）。
- 双数字假设 ≤99（超 99 需三数字，当前无此需求）。
