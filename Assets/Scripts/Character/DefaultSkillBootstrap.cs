using UnityEngine;

/// <summary>
/// 默认角色 / 技能 引导（Bootstrap）。
///
/// 当场景里 CharacterBattleSystem.allCharacters 为空时，自动造 "飞书原表 1:1" 的 5 个玩家角色 + 5 个对侧角色：
    ///   - 玩家（isPlayer=true）：宝宝 (id=1, lane=-1, hp=100, combat=25, 能量 0)
    ///   - 队伍 0~3（lane 0=大狗, 1=嘟嘟, 2=爱格, 3=小黑）
///   - 对侧用同样的 5 个数据，但 lane=-1/0..3（同样的 characterId 跨 side 各注册一次，互不冲突）
///
/// 用途：未在 Editor 里手动配置 / 未跑 Character Importer 时，让游戏仍可启动并跑出符合
/// 「HP 上限=100+25+88+45+68=326 ; 战斗力=25+35+3+20+15=98」的稳定数值。
/// </summary>
public static class DefaultSkillBootstrap
{
    /// <summary> 飞书原数据 1:1 表；按 characterId 索引 [id=玩家/队伍索引]。</summary>
    public static readonly (string name, string profession, int hp, float combat, string activeDesc, int activeCost, string passiveDesc)[] DefaultRows = new (string, string, int, float, string, int, string)[]
    {
        // id=1：宝宝（玩家），演奏者
        ("宝宝", "演奏者", 100, 25f,
         "全体防御（获取分数能力下降 80%）来抵御一切负面效果）持续 4s",
         0,
         "获得 5% 的伤害减少"),
        // id=2：队伍 lane0 大狗
        ("大狗", "", 25, 35f,
         "（3）大狗叫（将即将出现的音符附魔，每成功完成一个音符，增加分贝，结算完后发出狗叫按照分贝惊吓对手降低对方连击数）（必杀）",
         300,
         "狗叫后追加一次狗叫"),
        // id=3：队伍 lane1 嘟嘟
        ("嘟嘟", "", 88, 3f,
         "（4）（即将将出现的音符（6 个）附魔，每成功完成一个音符，就对自己进行一点生命治愈（3 点生命））（必杀）",
         200,
         "获得治疗后进入缓慢回复（大招之后每三秒根据收集音符数量 ×(1) 回复生命，持续一段时间（9s））"),
        // id=4：队伍 lane2 爱格（原表误写为“爱情”）
        ("爱格", "", 45, 20f,
         "（5）炸弹雨（即将将出现的音符（3 个）附魔，每完成一个音符就朝对手随机投射一颗小型炸弹（10 点伤害），造成直接生命伤害直到结算完毕）（必杀）",
         280,
         "生成更多音符 (+2)"),
        // id=5：队伍 lane3 小黑
        ("小黑", "", 68, 15f,
         "（6）将身前区域的所有音符全部电没（视力完成最佳击中己方获得所有大招充能）之后陷入 3 秒沉睡",
         330,
         "范围加大，此后一段时间（30s）全队战斗力提升"),
    };

    public static SkillSO MakeDefaultActiveSkill(string display, string desc, int energyCost, string effectType, string paramsJson)
    {
        var s = ScriptableObject.CreateInstance<SkillSO>();
        s.skillId = "default_active_" + display.Replace(" ", "_");
        s.displayName = display + " (占位)";
        s.description = desc;
        s.cooldown = 0f;
        s.energyCost = energyCost;
        s.effectType = effectType;
        s.effectParamsJSON = paramsJson;
        return s;
    }

    public static SkillSO MakeDefaultPassiveSkill(string display, string desc, string effectType, string paramsJson)
    {
        var s = ScriptableObject.CreateInstance<SkillSO>();
        s.skillId = "default_passive_" + display.Replace(" ", "_");
        s.displayName = display + " (占位)";
        s.description = desc;
        s.cooldown = 0f;
        s.energyCost = 0f;
        s.effectType = effectType;
        s.effectParamsJSON = paramsJson;
        return s;
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
        c.passiveSkill = MakeDefaultPassiveSkill("damage_reduce_5", "过热时获得 5% 伤害减少", "ReduceIncomingDamagePercent", "{\"percent\":0.05}");
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
        c.activeSkill = MakeDefaultActiveSkill(row.name, row.activeDesc, row.activeCost, "PendingP3Routing", "{}");
        c.passiveSkill = MakeDefaultPassiveSkill(row.name, row.passiveDesc, "PendingP3Routing", "{}");
        c.feverEligible = true;
        c.blockColor = CharacterPalette.GetColor(c.characterId);
        return c;
    }
}
