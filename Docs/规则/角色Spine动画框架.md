# Musical-Sprite 角色动画框架文档

> 版本：2026-09-13  
> 适用：Unity 6000 + URP + Spine 4.3  
> 目的：把「接新角色」变成只填数据、零角色专属代码。

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
| `PlaySuperFever` | loop | `03_Play_Fever` | 3 | 超级过热（暂复用过热资源） |
| `Special` | oneshot | `05_Special` | 4 | 进入过热/超级过热时爆发 |
| `TargetNormal` | oneshot | `06_Target_Normal` | 2 | 普通命中 |
| `TargetFever` | oneshot | `07_Target_Fever` | 5 | 过热命中 |
| `Hit` | oneshot | `08_Hit` | 8 | 受击 |
| `Dizziness` | oneshot | `09_Dizziness` | 8 | 晕眩（暂不用） |
| `Decadent` | oneshot | `10_Decadent` | 2 | 过热断连颓废 |
| `SkillSelect` | oneshot | `11_Skill_Select` | 9 | 呼号选中 |
| `SkillStart` | oneshot | `12_Skill_Start` | 10 | 释放技能起手 |
| `SkillLoop` | loop | `13_Skill_Loop` | 10 | 技能准备攻击循环 |
| `SkillAttak` | oneshot | `14_Skill_Attak` | 12 | 附魔音符命中结算 |
| `SkillEnd` | oneshot | `15_Skill_End` | 11 | 释放技能结束 |
| `Victory` | loop | `16_Victory01` | 20 | 胜利终态 |
| `Fail` | loop | `17_Fail` | 20 | 失败终态 |
| `Select` | oneshot | `04_Select` | 0 | 暂不用 |
| `Idle` | oneshot | `00_Idle` | 0 | 暂不用 |

### 3.1 优先级规则

- 数字越高越优先。
- oneshot 播放期间会**打断**低优先级 loop/oneshot；等 oneshot 播完后 `Update` 自动接回当前 loop。
- 若当前动画优先级 **≥** 新请求，则新请求**本次作废**（不播）。
- `SkillLoop` 期间通过 `SetSkillLoopLock(true)` 锁定，普通/过热 loop 无法切走。

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
- `SetLoopState(state)`：切换持续循环状态。
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
- 当前已验证的动画：屎屎的 19 个 Spine 动画均与 SlotTable 后缀匹配。
- 后续角色接入时，建议先跑一局战斗，确认 `Opening → PlayNormal → 命中 → 技能起手 → SkillLoop → SkillAttak → SkillEnd` 全链路正常。
