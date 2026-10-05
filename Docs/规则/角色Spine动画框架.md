# Musical-Sprite 角色动画框架文档

> 版本：2026-10-04  
> 适用：Unity 6000 + URP + Spine 4.3  
> 目的：把「接新角色」变成只填数据、零角色专属代码。  
> **动画编号标准以 Aibo_1_Bigdog（大狗）导出为唯一事实源，所有新角色 Spine 资源必须遵循此后缀。**

---

## 1. 核心设计目标

- **换角 / 加角 = 只改数据**：每个新角色只需准备 `CharacterDataSO` + Spine 资源，战斗、命中、技能动画由统一框架自动驱动。
- **统一优先级模型**：所有角色共用同一套动画状态与优先级表，避免角色专属分支。
- **向后兼容**：`modelPrefab` 为空时仍走原 cube 占位，美术资源未到位的角色也能正常战斗。
- **自检友好**：运行时自动诊断缺失的 Spine 动画名，Console 直接给出补全清单。

---

## 2. 命名约定

### 2.1 角色编号规则

| 类型 | 前缀 | 编号 | 示例 |
|------|------|------|------|
| 玩家角色 | `Player_` | `01 / 02 / ...` | `Player_01_Bear` |
| 队伍角色 | `Aibo_` | `1 / 2 / 3 / ...` | `Aibo_2_Shit` |

- 玩家角色：`Player_XX_名字`（如 `Player_01_Bear`）。
- 伙伴角色：`Aibo_X_名字`（如 `Aibo_1_Bigdog`、`Aibo_2_Shit`、`Aibo_3_Boom`、`Aibo_4_Black`）。

### 2.2 Spine 动画名规则

运行时完整动画名 = **前缀 + "_" + 标准槽位后缀**

```
Aibo_2_Shit_02_Play_Normal
Player_01_Bear_12_Skill_Start
```

前缀来自 `CharacterDataSO.animationPrefix`，后缀由 `CharacterAnimator.SlotTable` 统一维护，**所有角色通用**。

---

## 3. 标准动画状态表

| 状态枚举 | 类型 | 槽位后缀 | 优先级 | 触发时机 |
|----------|------|----------|--------|----------|
| `Opening` | oneshot | `01_Opening` | 20 | 角色生成时强制播放一次，结束后自动接回 loop |
| `PlayNormal` | loop | `02_Play_Normal` | 1 | 正常演奏循环 |
| `PlayFever` | loop | `03_Play_Fever` | 3 | 过热演奏循环 |
| `PlaySuperFever` | loop | `04_Play_Superfever` | 6 | 超级过热演奏循环 |
| `TargetNormal` | oneshot | `06_Target_Normal` | 2 | 普通命中 |
| `TargetFever` | oneshot | `07_Target_Fever` | 5 | 过热命中 |
| `TargetSuperFever` | oneshot | `08_Target_Superfever` | 7 | 超级过热命中 |
| `Hit` | oneshot | `09_Hit` | 8 | 受击 |
| `Dizziness` | **loop（受控时长）** | `10_Dizziness` | 8 | 晕眩/睡眠：循环播放，停留时长由**控制时间**决定（如小黑睡眠 3 秒）；到时自动回退触发前的 loop |
| `SkillSelect` | oneshot | `11_Skill_Select` | 9 | 呼号选中 |
| `SkillStart` | oneshot | `12_Skill_Start` | 10 | 释放技能起手 |
| `SkillLoop` | loop | `13_Skill_Loop` | 10 | 技能准备攻击循环 |
| `SkillAttak` | oneshot | `14_Skill_Attak` | 12 | 释放技能攻击（行动行为，见 §3.2） |
| `SkillEnd` | oneshot | `15_Skill_End` | 11 | 释放技能结束 |
| `Victory` | loop | `16_Victory` | 20 | 胜利终态 |
| `Fail` | loop | `17_Fail` | 20 | 失败终态 |
| `Select` | oneshot | `04_Select` | 0 | 暂不用 |
| `Idle` | oneshot | `00_Idle` | 0 | 暂不用 |

### 3.1 优先级规则

- 数字越高越优先。
- oneshot 播放期间会**打断**低优先级 loop/oneshot；等 oneshot 播完后 `Update` 自动接回当前 loop。
- 若当前动画优先级 **≥** 新请求，则新请求**本次作废**（不播）。
- `SkillLoop` 期间通过 `SetSkillLoopLock(true)` 锁定，普通/过热 loop 无法切走。

#### 3.1.1 受控时长循环（Dizziness 类）

`Dizziness` 是 **loop** 状态，但它的停留时长不是常驻，而是**由外部控制时间决定**（如眩晕 buff 持续 3 秒、小黑睡眠 3 秒）。机制：

- 触发：调用 `PlayTimedLoop(Dizziness, duration)`（CharacterCubeMarker 封装为 `PlayDizziness(duration)`），**duration 秒后自动回退到触发前的 loop 状态**（如 PlayNormal）。
- 不要走 `PlayOnce(Dizziness)` / `SetLoopState(Dizziness)`：二者对 loop 状态会直接**常驻**，不会回退（这是旧版"东倒西歪/卡死在晕眩"类 bug 的根因）。
- `duration <= 0` 视为常驻（由外部显式 `CancelTimedLoop` / `SetLoopState` 结束），便于"提前唤醒"场景。
- 受控时长循环进行中若被更高优先级 oneshot（如 Hit）打断，oneshot 播完仍会自动接回 Dizziness loop，倒计时继续；到时回退。

### 3.2 技能释放通用流程（12345 状态机）【底层规则 · 所有技能必须遵循】

> **一句话**：任何主动技能，无论带不带附魔、什么效果，统一走同一套
> `① SkillStart → ② SkillLoop → ③ SkillAttak → ④ SkillLoop → ⑤ SkillEnd` 状态机。

| 步 | 状态 | 类型 / 优先级 | 含义 | 播放时机 |
|----|------|---------------|------|----------|
| ① | `SkillStart` | oneshot / 10 | 释放启动（呼喊式起手） | 进入释放即刻播一次（强制，高优先级） |
| ② | `SkillLoop` | loop / 10 | 技能准备攻击循环（**底环**） | 进入释放即 `SetLoopState(SkillLoop)` + `SetSkillLoopLock(true)` |
| ③ | `SkillAttak` | oneshot / 12 | **释放技能攻击＝行动行为**（投弹 / 回血 / 射电流 / 施加 buff…） | 攻击 / 效果释放那一刻播一次 |
| ④ | `SkillLoop` | loop / 10 | **与 ② 完全相同**的状态（③ 播完自动回此） | ③ 播完由 `CharacterAnimator.Update` 自动接回，**无需手写** |
| ⑤ | `SkillEnd` | oneshot / 11 | 释放收尾 | 技能彻底结束播一次，后切回过热 / 普通 loop |

**★ 之前最易理解错、必须刻进脑子的点（逐条）：**

1. **② 和 ④ 是同一个 `SkillLoop` 资源 / 状态，不是两个不同的「准备段」**。它们只是同一条循环在「攻击前」与「攻击后」两个时刻的称呼。**绝不要为 ②、④ 准备两套动画。**
2. **③ `SkillAttak` 才是真正的「攻击 / 行动行为」**，是技能效果释放的视觉代表——投弹、回血、射出电流、施加 buff 等「行动」都应当与 ③ 的播放对齐（代码在触发效果处调用 `PlaySkillStep(SkillAttak)`）。
3. **「播放 0s 也算播放」**：即便某技能逻辑上「没有攻击动作」（如纯 buff、纯清屏），② / ④ 依然存在（`SkillLoop` 一直在播，0s 的攻击也视为流程成立），**不得「跳过」② 或 ④**。即**所有技能统一走完整 12345 流程**，不存在「无附魔技能省掉某步」的特例。
4. **优先级链**：`SkillAttak(12) > SkillEnd(11) > SkillStart/SkillLoop(10) > SkillSelect(9)`。因为 ③(12) 高于 ⑤(11)，所以 **③ 必须在 ⑤ 之前播放，且二者之间必须有 `yield` 间隔**（至少让 ③ 起播），否则同帧 ⑤ 会被 ③ 压制而**整段跳过不播**。
5. **oneshot 自动回环**：③、⑤ 播完由 `CharacterAnimator.Update` 自动接回 `currentLoopState`（技能进行中始终 = `SkillLoop`）。**无需、也不应在代码里写「显式回 SkillLoop」的逻辑**——这正是 ④ 自动产生的机制。
6. **无附魔类（断弦高压 / 纯 buff 技能等）视觉只见 1→3→5**：因为 ② / ④ 是同一条底环、看不出切换；但 ② / ④ 在流程上依旧存在（0s 合法）。代码上这些技能走 `ClearScreenSequence` / `PureBuffSequence`，**必须补 `PlaySkillStep(SkillAttak)`（③）后才能进 `EndSkillAnim`（⑤）**——缺 ③ 属于流程不完整的 bug（2026-09-27 已修正）。

**代码落点对照（实现者按此自查）：**

| 步 | 代码位置 | 说明 |
|----|----------|------|
| ① | `ActiveSkillRuntime.BeginCast` | 进入释放即播；若被更高优先级动画（Opening/Victory/Fail=20）挡住没播出来，视为「技能未释放」并撤销 |
| ② | `BeginCast`（`SetLoopState(SkillLoop)` + `SetSkillLoopLock(true)`） | 底环，锁定期间不吃控制 / 状态切换 |
| ③ | 附魔类：`Settle`（最终效果释放）+ `OnPerCharmSuccess`（逐音符命中各播一次）；无附魔类：`ClearScreenSequence` / `PureBuffSequence`（在行动行为处补播） | **所有路径都必须有这一行**，否则流程缺 ③ |
| ④ | `CharacterAnimator.Update`（`if (cur.IsComplete) PlayLoop(currentLoopState)`） | 自动接回，无需手写 |
| ⑤ | `EndSkillAnim`（被 `ClearScreenSequence` / `PureBuffSequence` / `EndCastSequence` / `FireSequence` 末尾调用） | 收尾后切回过热 / 普通 loop |

**设计意图**：统一状态机让所有技能（带 / 不带附魔、任何效果）共用同一套动画驱动；新技能只需提供对应的 ③ 攻击动作资源（或复用 `SkillLoop`），框架自动串起 12345，杜绝「缺状态 / 流程错位 / 无附魔类省步骤」等问题。屎屎（首个验证角色）即凭此一套流程驱动当前所有主动技能。

---

## 4. 接入新角色的四步流程

### 步骤 1：准备 Spine 资源

1. 导出角色 Spine 资源到 `Assets/Art/Characters/{名字}/`。
2. 确保 `SkeletonData.asset` 里的动画名严格遵循：`前缀_编号_语义`，例如：
   - `Aibo_2_Shit_01_Opening`
   - `Aibo_2_Shit_02_Play_Normal`
   - `Aibo_2_Shit_12_Skill_Start`
3. 把 Spine 预制体做成 prefab（参考 `Assets/Art/Characters/Shit/`）。

### 步骤 2：创建 CharacterDataSO

在 `Assets/Data/Characters/` 右键 → `Create / Musical Sprite / Character`，填写关键字段：

| 字段 | 填写说明 |
|------|----------|
| `characterId` | 1~N，玩家=1，队伍角色按编号 |
| `displayName` | 显示名（如"屎屎"） |
| `isPlayer` | 玩家角色=true；队伍角色=false |
| `laneIndex` | 队伍角色填 0~3（最底到最顶）；玩家填 -1 |
| `blockColor` | 身份色，用于场景 cube 上色 |
| `modelPrefab` | 角色 Spine prefab |
| `animationPrefix` | 与 Spine 动画前缀完全一致，如 `Aibo_2_Shit` |
| `activeSkill` / `skillId` | 主动技能引用（可选） |
| `passiveSkill` | 被动/过热技能引用（可选） |

### 步骤 3：把角色放进战斗场景

- 战斗场景角色由 `CharacterBattleSystem.InitializeFromData` 按乐队层级自动装配。
- 它会读取 `CharacterDataSO`，为每个上场角色生成 `CharacterCubeMarker`，并调用 `SetModelPrefab()` 注入 Spine 外观与前缀。
- 玩家角色和 4 个队伍角色的 SO 填好即可，**无需手动摆 prefab**。

### 步骤 4：验证动画是否齐全

1. 进 Play。
2. 看 Console 是否有 `[CharacterAnimator] prefix=... 在 SkeletonData 中未找到以下动画` 警告。
3. 如果有警告，按列表补 Spine 动画名；如果没有警告，说明所有标准动画已注册。

---

## 5. 关键代码组件说明

### 5.1 CharacterCubeMarker

挂在角色占位 cube 上的核心桥接组件：

- 维护 `side`（0 玩家 / 1 对手）和 `laneIndex`（-1 玩家 / 0~3 队伍音轨）。
- 全局 `Registry` 按 `(side, laneIndex)` 索引，供命中、受击查找。
- `SetModelPrefab()`：实例化 Spine 模型、隐藏 cube、挂载 `CharacterAnimator` 并注入 `animationPrefix`。
- 统一动画 API：`PlayTarget` / `PlayHit` / `EnterFever` / `PlaySkillStep` / `PlayVictory` / `PlayFail` 等。

### 5.2 CharacterAnimator

Spine 动画驱动器，数据驱动：

- `Rebuild()`：按 `animationPrefix + SlotTable 后缀` 在 SkeletonData 中自动发现动画，存在才注册。
- `PlayOnce(state)`：触发 oneshot，按优先级打断；返回 bool 表示是否实际播放。
- `SetLoopState(state)`：切换持续循环状态（常驻）。
- `PlayTimedLoop(state, duration)`：播放**受控时长**循环（如 Dizziness），duration 秒后自动回退触发前的 loop；duration<=0 常驻。
- `CancelTimedLoop(fallback)`：立即取消受控时长循环并回退到指定 loop。
- `SetSkillLoopLock(bool)`：技能期间锁定 loop。

### 5.3 CharacterBattleSystem

- 从 `CharacterDataSO[]` 初始化乐队。
- 自动调用 `CharacterCubeMarker.SetModelPrefab()` 完成美术接入。
- 旧场景迁移：`MigrateLegacyBand` → `ConfigureMarker` 动态创建 marker。

---

## 6. 当前接入状态

| 角色 | SO 文件 | Spine 资源 | 接入状态 |
|------|---------|------------|----------|
| 小熊（主角） | `Player_01_Bear.asset` | 待接入 | 仅占位 |
| 大狗 | `Aibo_1_Bigdog.asset` | 待接入 | 仅占位 |
| 屎屎 | `Aibo_2_Shit.asset` | `Assets/Art/Characters/Shit/` | **已接入，作为首个验证角色** |
| 布姆 | `Aibo_3_Boom.asset` | 待接入 | 仅占位 |
| 小黑 | `Aibo_4_Black.asset` | 待接入 | 仅占位 |

---

## 7. 常见错误与自检清单

### 7.1 命中/技能动画没反应

1. 打开 Console，搜索 `[CharacterAnimator] prefix=... 未找到以下动画`。
2. 检查 Spine 导出的动画名是否严格等于 `前缀_编号_语义`（注意大小写、下划线、0 填充）。
3. 选中 Spine 子物体 → 右键 `Diagnostic/Dump Spine Animations`，对比 SlotTable 后缀。

### 7.2 角色位置/大小不对

1. 不进 Play，打开菜单 `Tools / Musical-Sprite / 角色静帧预览`。
2. 选择角色和状态，在 Scene 视图里直接调整 Spine prefab 的本地位置/旋转/缩放。
3. 调整结果会保存到 Spine prefab 本身。

### 7.3 技能起手没播就结束 / 每命中不播 SkillAttak

- 检查 `ActiveSkillRuntime`：
  - `BeginCast` 会判定 `SkillStart` 是否真正播出，没播出则撤销释放。
  - `OnPerCharmSuccess` 每命中会触发 `SkillAttak`。
  - `TrySettleFromCharm` 会等待所有附魔音符结算（含即将生成的）。

### 7.4 Registry 撞键 / 普通命中跳到别的角色

- `CharacterCubeMarker.RegKey = side * 100 + laneIndex`，避免 side=1 玩家（lane=-1）与 side=0 lane=3 撞键。
- 如再出现，检查 `CharacterBattleSystem` 是否在角色生成后调用了 `marker.Register()`。

---

## 8. 扩展：新增动画状态

若未来需要新动画类目（如"睡觉"、"挑衅"）：

1. 在 `CharacterAnimator.CharacterAnimationState` 枚举中新增状态。
2. 在 `SlotTable` 中新增一条：`{ State, "XX_NewName", priority, loop }`。
3. 在 `CharacterCubeMarker` 中新增一个转发方法（可选）。
4.  Spine 资源按新后缀命名即可，**不需要改任何角色专属代码**。

---

## 9. 相关文件路径速查

| 文件 | 路径 |
|------|------|
| 角色数据 SO | `Assets/Data/Characters/` |
| 角色动画驱动器 | `Assets/Scripts/Character/CharacterAnimator.cs` |
| 角色标记组件 | `Assets/Scripts/Character/CharacterCubeMarker.cs` |
| 战斗装配系统 | `Assets/Scripts/Character/CharacterBattleSystem.cs` |
| 静帧预览窗口 | `Assets/Editor/CharacterStaticPreviewWindow.cs` |
| 角色导入器 | `Assets/Editor/CharacterImporterWindow.cs` |
| 技能运行时 | `Assets/Scripts/Skill/ActiveSkillRuntime.cs` |
| 当前已接入 Spine 资源 | `Assets/Art/Characters/Shit/` |

---

## 10. 备忘

- 所有战斗数值（伤害 / 回血 / HP）一律**向上取整为整数**，不保留小数。
- 2026-10-04 动画标准迁移：删除 `Special`、`Decadent`；新增 `TargetSuperFever`；以 Aibo_1_Bigdog（大狗）导出编号为新标准（`04_Play_Superfever`、`08_Target_Superfever`、`09_Hit`、`10_Dizziness`、`16_Victory`）。屎屎旧命名需同步调整。
- 2026-10-04 修正：`Dizziness` 实为 **loop（受控时长）**，非 oneshot——停留时长由控制时间决定（如小黑睡眠 3 秒）。新增 `PlayTimedLoop` / `CancelTimedLoop`（CharacterAnimator）+ `PlayDizziness(duration)`（CharacterCubeMarker）。触发必须用 `PlayDizziness`/`PlayTimedLoop`，禁止 `PlayOnce`/`SetLoopState`（会常驻不回退）。
- 后续角色接入时，建议先跑一局战斗，确认 `Opening → PlayNormal → 命中 → 技能起手 → SkillLoop → SkillAttak → SkillEnd` 全链路正常。
