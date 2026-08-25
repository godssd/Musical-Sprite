# Characters.xlsx 使用说明（飞书原表 1:1）

本项目用 Excel 填表 → 引擎内 `Character Importer` 导入，批量生成 `CharacterDataSO`。
**本表的列名直接对齐飞书「角色系统」原表**（编号 / 角色 / 职业 / hp / 战斗力 / 能力1 / 能量需求 / 过热状态），打开 Excel 就是飞书样式，方便录入与对照。

## 0. 角色身份色（场景方块按此上色）

两侧（玩家 / 对手）共用同一套色板，一眼区分「谁是谁」：

| characterId | 角色 | 方块颜色 |
|---:|---|---|
| 1 | 宝宝（玩家自身，大块） | 灰 |
| 2 | 大狗（lane0） | 橙 |
| 3 | 嘟嘟（lane1） | 绿 |
| 4 | 爱格（lane2，原表误写为“爱情”） | 白 |
| 5 | 小黑（lane3） | 黑（近黑，避免暗场不可见） |

颜色由 `CharacterPalette.GetColor(id)` 按 id 统一决定，导入器 / 默认引导都会自动写入 `CharacterDataSO.blockColor`；运行时 `CharacterBattleSystem.ColorAllMarkers()` 给场景方块上色，`FeverVFXPlaceholder` 过热时仅向主题色混合 60%，不会冲掉身份色。

---

## 1. 怎么生成 / 修改这张表

- 修改 Excel：直接在 `Assets/Data/Characters/Characters.xlsx` 里编辑表头与 5 行角色数据；保存。
- 重新生成（清空旧表，按飞书原表重新铺一次）：
  - 跑 `Tools/gen_characters_xlsx.py`（Python 标准库，无第三方依赖）。要求：先关掉 Excel 释放文件锁，否则覆盖失败。
- 导入角色到 Unity：
  1. 跑 `Tools > Musical Sprite > Skill Maker`（如已有 SkillSO 可跳过）；
  2. 跑 `Tools > Musical Sprite > Character Importer`：选 xlsx → 「预览」看一遍 → 「导入」即可。

---

## 2. 列含义（完全对齐飞书原表）

| 列 | 飞书原列名 | 字段名 | 说明 |
|----|------|------|------|
| A | 编号 | `characterId` | 玩家必须=1；队伍=2~5。**上场不能重复**：每个 characterId 在两侧只能各出现一次 |
| B | 角色 | `displayName` | 显示名（宝宝 / 大狗 / 嘟嘟 / 爱格 / 小黑） |
| C | 职业 | `profession` | 玩家=「演奏者」；队伍角色一般留空 |
| D | hp（1hp=1hp换为全队血量） | `maxHP` | 角色血量上限 |
| E | 战斗力（100点战斗力视为10%分数换位比） | `combatPower` | 战斗力数值，参与「保底扣血」 |
| F | 能力1 | `activeSkillDescription` | 主动技能描述（长文本，直观阅读；P3 路由前只展示不入引擎判定） |
| G | 能量需求（10分=1能量） | `activeEnergyCost` | 玩家填「无」或 0；队伍=100 以上整数（飞书原值：300/200/280/330） |
| H | 过热状态 | `passiveSkillDescription` | 主动施放后/过热期间的被动效果描述 |
| I | 备注 | `notes` | 可选，仅展示，不入 SO |

可选进阶列（不写也能导入）：
- `activeSkillId` / `passiveSkillId`：若已用 Skill Maker 建了 SkillSO，填入对应 `skillId`，导入器会反查成 SO 引用（留空也 OK，会用占位 SO 兜底）
- `type=player|team`、`lane=0..3`：手动指定 isPlayer/laneIndex；不写时 importer 按「id=1 → 玩家，id=2..5 → lane=id-2」推断

---

## 3. 上场规则（对局内不变量）

每个 side（一侧的对战方）上场 = **1 个玩家角色（isPlayer=true）+ 4 个队伍角色（isPlayer=false，lane=0..3）**：

- 玩家侧（side=0）：必须从角色池的「玩家角色」挑 1 个
- 队伍侧（side=0 的 4 队 + side=1 的 4 队）：从角色池的「队伍角色」挑 4 个，分别占 lane 0/1/2/3
- **同一 characterId 不能在两个 lane / 两个 side 重复上场**（由 `CharacterBattleSystem.InitializeFromData` 的 HashSet 检查兜底）

### 对局内 HP 上限 / 战斗力（自动求和，引擎内不再手填）

```
对局内 MaxHP   = 玩家.hp + lane0.hp + lane1.hp + lane2.hp + lane3.hp
对局内 战斗力  = 玩家.combat + lane0.combat + lane1.combat + lane2.combat + lane3.combat
```

按飞书原表（玩家：宝宝 100/25；队伍：大狗 25/35、嘟嘟 88/3、爱格 45/20、小黑 68/15），双侧总数值都是：

| 指标 | 数值 | 验证 |
|---|---|---|
| 全队 HP | 100 + 25 + 88 + 45 + 68 = **326** | 默认产线下 HPBar 显示 `326/326` |
| 全队战斗力 | 25 + 35 + 3 + 20 + 15 = **98** | 默认产线下 `[ScoreManager] 保底` 日志里 combatSum=98 |

---

## 4. 完整行示例（按飞书原表生成）

| 编号 | 角色 | 职业 | HP | 战斗力 | 能力1 | 能量需求 | 过热状态 |
|---|---|---|---|---|---|---|---|
| 1 | 宝宝 | 演奏者 | 100 | 25 | 全体防御（获取分数能力下降 80% 来抵御一切负面效果）持续 4s | 无 | 获得 5% 的伤害减少 |
| 2 | 大狗 | | 25 | 35 | （3）大狗叫（将即将出现的音符附魔，每成功完成一个音符增加分贝，结算完后发出狗叫按分贝惊吓对手降低对方连击数）（必杀） | 300 | 狗叫后追加一次狗叫 |
| 3 | 嘟嘟 | | 88 | 3 | （4）（即将将出现的音符（6 个）附魔，每成功完成一个音符就对自己进行一点生命治愈（3 点生命））（必杀） | 200 | 获得治疗后进入缓慢回复（大招之后每三秒根据收集音符数量 ×(1) 回复生命，持续一段时间（9s）） |
| 4 | 爱格 | | 45 | 20 | （5）炸弹雨（即将将出现的音符（3 个）附魔，每完成一个音符就朝对手随机投射一颗小型炸弹（10 点伤害），造成直接生命伤害直到结算完毕）（必杀） | 280 | 生成更多音符 (+2) |
| 5 | 小黑 | | 68 | 15 | （6）将身前区域的所有音符全部电没（视力完成最佳击中己方获得所有大招充能）之后陷入 3 秒沉睡 | 330 | 范围加大，此后一段时间（30s）全队战斗力提升 |

---

## 5. 编辑器内快速复位

若旧 ScriptableObject 资产还在、改表想重新生成：Project 窗口 → `Assets/Data/Characters/` → 全选 `.asset` → Delete。改完 Excel 后再跑导入器即可重建。
