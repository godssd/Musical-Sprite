using UnityEngine;

/// <summary>
/// 默认角色 / 技能 引导（Bootstrap）。
///
/// 当场景里 CharacterBattleSystem.allCharacters 为空时，自动造 "飞书原表 1:1" 的 5 个玩家角色 + 5 个对侧角色：
///   - 玩家（isPlayer=true）：宝宝 (id=1, lane=-1, hp=100, combat=25, 能量 0)
///   - 队伍 0~3（lane 0=大狗, 1=嘟嘟, 2=爱格, 3=小黑）
/// - 对侧用同样的 5 个数据，但 lane=-1/0..3（同样的 characterId 跨 side 各注册一次，互不冲突）
///
/// 技能 skillId 与功能需求表 / 技能库（Skill Maker）保持一致：
///   大狗=dog_howl / 嘟嘟=dudu_heal / 爱格=aige_bomb / 小黑=xiaohei_clear / 宝宝被动=baby_damage_reduce。
/// 输入方式按功能需求表（A/B/C 三键）：大狗 AAA / 嘟嘟 CBC / 爱格 ABA / 小黑 BCA。
///
/// 用途：未在 Editor 里手动配置 / 未跑 Character Importer 时，让游戏仍可启动并跑出符合
/// 「HP 上限=100+25+88+45+68=326 ; 战斗力=25+35+3+20+15=98」的稳定数值。
/// </summary>
public static class DefaultSkillBootstrap
{
    /// <summary> 飞书原数据 1:1 表；按 characterId 索引 [id=玩家/队伍索引]。</summary>
    public static readonly (string name, string profession, int hp, float combat, string activeDesc, int activeCost, string passiveDesc, string activeSkillId, string inputMethod)[] DefaultRows = new (string, string, int, float, string, int, string, string, string)[]
    {
        // id=1：宝宝（玩家），演奏者
        ("宝宝", "演奏者", 100, 25f,
         "全体防御（获取分数能力下降 80%）来抵御一切负面效果）持续 4s",
         0,
         "获得 5% 的伤害减少",
         "",            // 玩家主动技能走 PlayerCommand，无 SkillSO
         ""),
        // id=2：队伍 lane0 大狗
        ("大狗", "", 25, 35f,
         "（3）大狗叫（将即将出现的音符附魔，每成功完成一个音符，增加分贝，结算完后发出狗叫按照分贝惊吓对手降低对方连击数）（必杀）",
         300,
         "狗叫后追加一次狗叫",
         "dog_howl",
         "AAA"),
        // id=3：队伍 lane1 嘟嘟
        ("嘟嘟", "", 88, 3f,
         "（4）（即将将出现的音符（6 个）附魔，每成功完成一个音符，就对自己进行一点生命治愈（3 点生命））（必杀）",
         200,
         "获得治疗后进入缓慢回复（大招之后每三秒根据收集音符数量 ×(1) 回复生命，持续一段时间（9s））",
         "dudu_heal",
         "CBC"),
        // id=4：队伍 lane2 爱格（原表误写为“爱情”）
        ("爱格", "", 45, 20f,
         "（5）炸弹雨（即将将出现的音符（3 个）附魔，每完成一个音符就朝对手随机投射一颗小型炸弹（10 点伤害），造成直接生命伤害直到结算完毕）（必杀）",
         280,
         "生成更多音符 (+2)",
         "aige_bomb",
         "ABA"),
        // id=5：队伍 lane3 小黑
        ("小黑", "", 68, 15f,
         "（6）将身前区域的所有音符全部电没（视力完成最佳击中己方获得所有大招充能）之后陷入 3 秒沉睡",
         330,
         "范围加大，此后一段时间（30s）全队战斗力提升",
         "xiaohei_clear",
         "BCA"),
    };

    /// <summary>默认「能力2」占位数据（与飞书表 1:1）。按 characterId-1 索引。
    /// 团队角色（大狗/嘟嘟/爱格）的 能力2 是被动（输入方式留空）；玩家（宝宝）的 能力2 是第二个主动（全体进攻，AAB，无能量）。小黑无 能力2。</summary>
    public static readonly (string desc, string inputMethod, string overheat, string super)[] DefaultAbility2 = new (string, string, string, string)[]
    {
        // id=1 宝宝：第二个主动（全体进攻）AAB，无能量
        ("（2）全体进攻（整个队伍战斗力上升（20%）但受到伤害增加20%）持续4s", "AAB", "战斗力额外提升5%", "战斗力额外提升15%"),
        // id=2 大狗：被动（每次扣血微增必杀积累）
        ("每次扣除生命都会微量增加必杀技积累", "", "", ""),
        // id=3 嘟嘟：被动（全体防御下释放必杀回复更强）
        ("全体防御下释放必杀回复效果更强", "", "", ""),
        // id=4 爱格：被动（全体进攻下间隔附魔投射炸弹）
        ("全体进攻下每隔一段时间会对一个音符进行附魔，成功完成后会投射一颗小型炸弹（解除进攻重置间隔）", "", "", ""),
        // id=5 小黑：无 能力2
        ("", "", "", ""),
    };

    /// <summary>构造一个技能槽（能力N 列组）。skillId 为空则 skill=null（纯文案/玩家技能，P3 路由）。</summary>
    private static SkillSlot MakeSlot(string skillId, int energyCost, float cooldown, string inputMethod, string desc, string overheat, string super, SkillSO resolved = null)
    {
        return new SkillSlot
        {
            skillId = skillId,
            energyCost = energyCost,
            cooldown = cooldown,
            inputMethod = inputMethod,
            description = desc,
            overheatDesc = overheat,
            superOverheatDesc = super,
            skill = string.IsNullOrEmpty(skillId) ? null : (resolved ?? SkillSO.FindById(skillId)),
        };
    }


    public static SkillSO MakeDefaultActiveSkill(string display, string desc, int energyCost, string effectType, string paramsJson, string skillId = "")
    {
        var s = ScriptableObject.CreateInstance<SkillSO>();
        s.skillId = string.IsNullOrEmpty(skillId) ? ("default_active_" + display.Replace(" ", "_")) : skillId;
        s.displayName = display + " (占位)";
        s.description = desc;
        s.energyCost = energyCost;
        s.effectType = effectType;
        s.effectParamsJSON = paramsJson;
        return s;
    }

    public static SkillSO MakeDefaultPassiveSkill(string display, string desc, string effectType, string paramsJson, string skillId = "")
    {
        var s = ScriptableObject.CreateInstance<SkillSO>();
        s.skillId = string.IsNullOrEmpty(skillId) ? ("default_passive_" + display.Replace(" ", "_")) : skillId;
        s.displayName = display + " (占位)";
        s.description = desc;
        s.energyCost = 0f;
        s.effectType = effectType;
        s.effectParamsJSON = paramsJson;
        return s;
    }

    /// <summary>把功能需求表的输入方式字符串（如 "AAA" / "CBC"）解析为 SkillInputStep 序列。
    /// 约定：表内 A/B/C 分别表示 ←/↓/→，与 SkillInputUI 的方向键标签一致。</summary>
    public static SkillInputStep[] ParseInputMethod(string s)
    {
        var list = new System.Collections.Generic.List<SkillInputStep>();
        if (!string.IsNullOrEmpty(s))
        {
            foreach (char ch in s)
            {
                switch (char.ToUpper(ch))
                {
                    case 'A': list.Add(SkillInputStep.Left); break;   // A → ←
                    case 'B': list.Add(SkillInputStep.Down); break;   // B → ↓
                    case 'C': list.Add(SkillInputStep.Right); break;  // C → →
                    case 'L': list.Add(SkillInputStep.Left); break;
                    case 'D': list.Add(SkillInputStep.Down); break;
                    case 'R': list.Add(SkillInputStep.Right); break;
                    case 'U': list.Add(SkillInputStep.Up); break;
                    case 'S': list.Add(SkillInputStep.Space); break;
                    default: break;
                }
            }
        }
        return list.Count > 0 ? list.ToArray() : new SkillInputStep[] { SkillInputStep.Left, SkillInputStep.Left, SkillInputStep.Left };
    }

    /// <summary> 玩家自身角色 (id=1, lane=-1, isPlayer=true) </summary>
    public static CharacterDataSO MakeDefaultPlayer(int side)
    {
        var row = DefaultRows[0];
        var c = ScriptableObject.CreateInstance<CharacterDataSO>();
        c.characterId = 1;
        c.displayName = row.name + (side == 0 ? "" : " (R)");
        c.profession = row.profession;
        c.isPlayer = true;
        c.laneIndex = -1;
        c.maxHP = row.hp;
        c.combatPower = row.combat;
        c.activeSkillDescription = row.activeDesc;
        c.activeEnergyCost = row.activeCost;
        c.passiveSkillDescription = row.passiveDesc;
        c.activeSkill = null; // 玩家走 PlayerCommand 类走另一条路（暂未实现）
        c.passiveSkill = MakeDefaultPassiveSkill("damage_reduce_5", "过热时获得 5% 伤害减少", "ReduceIncomingDamagePercent", "{\"percent\":0.05}", "baby_damage_reduce");
        // 多技能槽：玩家（宝宝）有两个无能量主动（能力1=↓↓← 全体防御；能力2=AAB 全体进攻），无被动。
        c.skills = new SkillSlot[5];
        c.skills[0] = MakeSlot("", 0, 0f, "↓↓←", row.activeDesc, row.passiveDesc, "", null);
        c.skills[1] = MakeSlot("", 0, 0f, "AAB", DefaultAbility2[0].desc, DefaultAbility2[0].overheat, DefaultAbility2[0].super, null);
        c.feverEligible = true;
        c.blockColor = CharacterPalette.GetColor(c.characterId);
        return c;
    }

    /// <summary> 队伍角色 (id=1+(lane), isPlayer=false)。lane=0/1/2/3 对应大狗/嘟嘟/爱格/小黑。 </summary>
    public static CharacterDataSO MakeDefaultTeam(int side, int laneIndex)
    {
        var rowIdx = laneIndex + 1; // lane 0 → row 1 (大狗)
        if (rowIdx < 0 || rowIdx >= DefaultRows.Length) rowIdx = 1;
        var row = DefaultRows[rowIdx];

        var c = ScriptableObject.CreateInstance<CharacterDataSO>();
        c.characterId = rowIdx + 1;
        c.displayName = row.name + (side == 0 ? "" : " (R)");
        c.profession = row.profession;
        c.isPlayer = false;
        c.laneIndex = laneIndex;
        c.maxHP = row.hp;
        c.combatPower = row.combat;
        c.activeSkillDescription = row.activeDesc;
        c.activeEnergyCost = row.activeCost;
        c.passiveSkillDescription = row.passiveDesc;
        c.activeSkill = MakeDefaultActiveSkill(row.name, row.activeDesc, row.activeCost, "PendingP3Routing", "{}", row.activeSkillId);
        c.passiveSkill = MakeDefaultPassiveSkill(row.name, row.passiveDesc, "PendingP3Routing", "{}", "default_passive_" + row.name);
        // 队伍技能统一参数（占位；正式技能由 Skill Maker 建 SkillSO 经 skillId 引用覆盖）
        if (c.activeSkill != null)
        {
            c.activeSkill.inputSequence = ParseInputMethod(row.inputMethod);
            c.activeSkill.charmedNoteCount = 6;
            c.activeSkill.reduceComboPerCharmedNote = 3;
            if (!string.IsNullOrEmpty(row.activeSkillId)) c.activeSkill.skillId = row.activeSkillId;
        }
        // 多技能槽：能力1=主动（有能量门槛）；能力2=被动（输入方式留空→满足条件持续生效）。
        var a2 = DefaultAbility2[rowIdx];
        c.skills = new SkillSlot[5];
        c.skills[0] = MakeSlot(row.activeSkillId, row.activeCost, 0f, row.inputMethod, row.activeDesc, row.passiveDesc, "", c.activeSkill);
        // 能力2：有输入方式=主动（玩家用），否则=被动；默认团队数据均为被动。
        bool a2Active = !string.IsNullOrWhiteSpace(a2.inputMethod);
        c.skills[1] = MakeSlot("", 0, 0f, a2.inputMethod, a2.desc, a2.overheat, a2.super, null);
        if (!a2Active && string.IsNullOrWhiteSpace(a2.desc)) c.skills[1] = null; // 小黑等无能力2 → 空槽
        c.feverEligible = true;
        c.blockColor = CharacterPalette.GetColor(c.characterId);
        return c;
    }
}
