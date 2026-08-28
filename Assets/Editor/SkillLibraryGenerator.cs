using UnityEngine;
using UnityEditor;
using System.IO;

    /// <summary>
    /// 技能库生成器（技能定义唯一数据源，仅决定 技能ID + 功能(effectType) + 全部可调参数默认值）。
    /// 菜单：Tools > Musical Sprite > Generate Skill Library（缺时建）；
    ///       Tools > Musical Sprite > Force Regenerate Skill Library（全量重置，会覆盖手调值）。
    /// 编辑器加载时也会自动跑一次（create-if-missing）。
    ///
    /// 在 Assets/Resources/Skills/ 生成 SkillSO 资源：
    ///   dog_howl（大狗叫）/ dudu_heal（美味牛角包）/ aige_bomb（炸弹雨）
    ///   / xiaohei_clear（断弦高压）/ baby_defense_buff（全体防御）/ baby_offense_buff（全体进攻）
    ///   / xiaohei_dispel（小黑驱散）。
    ///   （baby_damage_reduce 已移除：它本是玩家减伤被动，不进技能库。）
    ///
    /// 必须放在 Assets/Resources/ 下，是因为运行时 SkillSO.RebuildIndex() 走
    /// Resources.LoadAll<SkillSO>("Skills")，否则游戏打包后 SkillSO.FindById() 会查不到。
    /// Skill Library 窗口（Editor）走 AssetDatabase.FindAssets()，在 Resources 下也能正常列出。
    ///
    /// 技能库只决定技能功能与 ID；角色数值与 技能冷却/能量需求/输入方式/技能介绍/被引用角色
    /// 由 Characters.xlsx 经 Character Importer 导入，技能库工具 Rescan 时聚合显示（不写回 SkillSO）。
    ///
    /// 缺时建（create-if-missing）：已存在的 .asset 只同步「身份」字段（skillId/名称/描述/效果），
    /// 绝不动可调参数——保住你在技能库工具里「应用改动」手调的值。全量重置用上面菜单。
    /// </summary>
public static class SkillLibraryGenerator
{
    [MenuItem("Tools/Musical Sprite/Generate Skill Library")]
    public static void Generate()
    {
        int created = GenerateAssets(silent: false);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (created > 0)
        {
            Debug.Log("[SkillLibrary] 技能库生成完成：新建 " + created + " 个 SkillSO。打开 Skill Library 查看。");
            EditorUtility.DisplayDialog("技能库生成完成",
                "已在 Assets/Resources/Skills/ 生成 " + created + " 个技能（已存在则跳过）。\n打开 Tools > Musical Sprite > Skill Library 查看技能库。", "确定");
        }
        else
        {
            Debug.Log("[SkillLibrary] 技能库已就绪（无新增）。");
        }
    }

    /// <summary>全量重置：删除全部 Skills/*.asset 后按代码默认值重建（会覆盖你在技能库工具里手调的数值）。
    /// 用于「恢复出厂默认」。日常改名/补技能用上面的 Generate（缺时建，不碰手调值）。</summary>
    [MenuItem("Tools/Musical Sprite/Force Regenerate Skill Library")]
    public static void ForceGenerate()
    {
        if (!EditorUtility.DisplayDialog("全量重置技能库",
            "这将删除 Assets/Resources/Skills/ 下全部 SkillSO 并按代码默认值重建（你在技能库工具里手调的数值会被覆盖）。\n确定继续？",
            "确定重置", "取消"))
            return;
        string dir = "Assets/Resources/Skills";
        if (Directory.Exists(dir))
        {
            foreach (var f in Directory.GetFiles(dir, "*.asset"))
                AssetDatabase.DeleteAsset(f);
        }
        int created = GenerateAssets(silent: false);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SkillLibrary] 全量重置完成：重建 " + created + " 个 SkillSO。");
    }

    /// <summary>静默版（不弹窗），给 InitializeOnLoad 自动调用。</summary>
    public static int GenerateAssets(bool silent)
    {
        // 写到 Assets/Resources/Skills/ 是关键：
        //   运行时 SkillSO.RebuildIndex() 通过 Resources.LoadAll<SkillSO>("Skills") 加载，
        //   必须放在 Resources 下才会在打包后参与 FindById 查表；
        //   SkillLibraryWindow 在 Editor 里走 AssetDatabase.FindAssets("t:SkillSO")，
        //   在 Resources 下也能正常列出。
        string dir = "Assets/Resources/Skills";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        int created = 0;
        created += Make("dog_howl", "大狗叫",
            "（3）大狗叫（将即将出现的音符附魔，每成功完成一个音符，增加分贝，结算完后发出狗叫按照分贝惊吓对手降低对方连击数）（必杀）",
            300f, new SkillInputStep[] { SkillInputStep.Left, SkillInputStep.Left, SkillInputStep.Left },   // AAA → ←←←
            6, 3, "DogHowl", "{}", dir);

        created += Make("dudu_heal", "美味牛角包",
            "（4）将即将出现的音符（6 个）附魔，每成功完成一个音符，就对自己进行一点生命治愈（3 点生命）（必杀）",
            200f, new SkillInputStep[] { SkillInputStep.Right, SkillInputStep.Down, SkillInputStep.Right }, // CBC → →↓→
            6, 3, "Heal", "{\"healPerNote\":3,\"slowRegenSeconds\":9}", dir);

        created += Make("aige_bomb", "炸弹雨",
            "（5）炸弹雨（将即将出现的音符（3 个）附魔，每完成一个音符就朝对手随机投射一颗小型炸弹（10 点伤害），造成直接生命伤害直到结算完毕）（必杀）",
            280f, new SkillInputStep[] { SkillInputStep.Left, SkillInputStep.Down, SkillInputStep.Left },    // ABA → ←↓←
            3, 3, "Bomb", "{\"bombDamage\":10}", dir);

        created += Make("xiaohei_clear", "断弦高压",
            "（6）将身前区域的所有音符全部电没（视为完成最佳命中，按各音符最高判定计分/充能）之后释放者小黑自身陷入 N 秒沉睡；过热 / 超级过热释放时额外激活自身 b 类战力 buff（橙黄）",
            330f, new SkillInputStep[] { SkillInputStep.Down, SkillInputStep.Right, SkillInputStep.Left },   // BCA → ↓→←
            0, 0, "ClearScreen", "{}", dir,
            buffSlot: BuffSlot.B, buffCombatMult: 1.5f, clearSleepSeconds: 3f, clearBuffAsB: true,
            clearOverheatRangeMult: 1.2f, clearOverheatCombatMult: 1.3f, clearOverheatBuffDuration: 30f,
            clearSuperRangeMult: 1.5f, clearSuperCombatMult: 1.8f, clearSuperBuffDuration: 30f);

        // 宝宝双 buff（a 类，互斥顶替）
        created += Make("baby_defense_buff", "全体防御",
            "（1）全体防御：减少 20% 受到的伤害，持续 10s（同属 a 类 buff 不可共存）",
            0f, new SkillInputStep[0],
            0, 0, "Buff", "{}", dir,
            buffSlot: BuffSlot.A, buffSubType: BuffSubType.Defense, buffCombatMult: 1f, buffDamageReduce: 0.2f, buffDuration: 10f);

        created += Make("baby_offense_buff", "全体进攻",
            "（2）全体进攻：整个队伍战斗力上升 40%，持续 10s（同属 a 类 buff 不可共存）",
            0f, new SkillInputStep[0],
            0, 0, "Buff", "{}", dir,
            buffSlot: BuffSlot.A, buffSubType: BuffSubType.Offense, buffCombatMult: 1.4f, buffDamageReduce: 0f, buffDuration: 10f);

        // 驱散（类附魔模块）：附魔 N 个本侧 incoming 音符，每成功结算一个即清除本侧持续性控制效果（当前=沉睡）。
        created += Make("xiaohei_dispel", "小黑驱散",
            "（7）将接下来若干音符附魔，每成功命中一个即驱散本侧持续性控制效果（解除沉睡）；附魔式释放，消耗能量后按命中结算",
            200f, new SkillInputStep[] { SkillInputStep.Left, SkillInputStep.Up, SkillInputStep.Right },   // LUR → ←↑→
            4, 3, "Dispel", "{}", dir,
            enchantTarget: EnchantTarget.Self);

        if (!silent)
            AssetDatabase.SaveAssets();
        else if (created > 0)
        {
            // 启动静默模式也可能升级了现有 .asset（inputSequence A/B/C → ←/↓/→）。
            // 必须落盘，否则下次启动会再次"升级"覆盖用户在 Inspector 里改过的参数（虽然 SetDirty 已调用，
            // SaveAssets 仍然负责把脏 .asset 写回磁盘，避免 failed to save 警告）。
            AssetDatabase.SaveAssets();
        }
        return created;
    }

    /// <summary>编辑器加载时自动运行一次（幂等：已存在则跳过），避免菜单未运行时库空。</summary>
    [InitializeOnLoadMethod]
    private static void AutoRunOnLoad()
    {
        // 推迟到 AssetDatabase 就绪之后再跑（首次导入避免竞争）
        EditorApplication.delayCall += () =>
        {
            try
            {
                int created = GenerateAssets(silent: true);
                if (created > 0)
                {
                    AssetDatabase.SaveAssets();
                    Debug.Log("[SkillLibrary] 启动时自动生成 " + created + " 个 SkillSO（首次导入 / 缺失补齐）。");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[SkillLibrary] 启动自动生成跳过：" + ex.Message);
            }
        };
    }

    static int Make(string skillId, string displayName, string desc, float energyCost,
        SkillInputStep[] seq, int charmed, int reducePer, string effectType, string effectParams, string dir,
        BuffSlot buffSlot = BuffSlot.None, BuffSubType buffSubType = BuffSubType.Offense,
        float buffCombatMult = 1.4f, float buffDamageReduce = 0.2f, float buffDuration = 10f,
        float clearBandRangeMult = 1f, float clearSleepSeconds = 3f, bool clearBuffAsB = false,
        EnchantTarget enchantTarget = EnchantTarget.Self,
        float regenInterval = 3f,
        float clearOverheatRangeMult = 1.2f, float clearOverheatCombatMult = 1.3f, float clearOverheatBuffDuration = 30f,
        float clearSuperRangeMult = 1.5f, float clearSuperCombatMult = 1.8f, float clearSuperBuffDuration = 30f)
    {
        string path = dir + "/" + skillId + ".asset";
        var existing = File.Exists(path) ? AssetDatabase.LoadAssetAtPath<SkillSO>(path) : null;
        if (existing != null)
        {
            // 已存在：只同步「身份」字段（skillId / 名称 / 描述 / 效果），绝不动可调参数——
            // 保住技能库工具里「应用改动」手调的值（选项 A：create-if-missing）。
            bool dirty = false;
            if (existing.displayName != displayName) { existing.displayName = displayName; dirty = true; }
            if (existing.description != desc) { existing.description = desc; dirty = true; }
            if (existing.effectType != effectType) { existing.effectType = effectType; dirty = true; }
            if (IsLegacyAbcSequence(existing.inputSequence) && seq != null && seq.Length > 0)
            {
                existing.inputSequence = seq; dirty = true;
                Debug.Log("[SkillLibrary] 已升级 " + skillId + ".asset 的 inputSequence（历史 A/B/C → ←/↓/→）。");
            }
            if (dirty) EditorUtility.SetDirty(existing);
            return 0;   // 已存在，不计入「新建」
        }
        // 新建：写入全部字段（含所有可调参数默认值）
        var so = ScriptableObject.CreateInstance<SkillSO>();
        so.skillId = skillId;
        so.displayName = displayName;
        so.description = desc;
        so.energyCost = energyCost;
        so.inputSequence = seq;
        so.inputWindow = 1.5f;
        so.charmedNoteCount = charmed;
        so.onlyCountNonMiss = true;
        so.reduceComboPerCharmedNote = reducePer;
        so.releaseGlow = new Color(1f, 0.85f, 0.2f);
        so.effectType = effectType;
        so.effectParamsJSON = effectParams;
        // 同步 buff / 清屏 / 附魔 参数（新建时一次性写入全部可调默认值）
        so.enchantTarget = enchantTarget;
        so.buffSlot = buffSlot;
        so.buffSubType = buffSubType;
        so.buffCombatMult = buffCombatMult;
        so.buffDamageReduce = buffDamageReduce;
        so.buffDuration = buffDuration;
        so.clearBandRangeMult = clearBandRangeMult;
        so.clearSleepSeconds = clearSleepSeconds;
        so.clearBuffAsB = clearBuffAsB;
        so.regenInterval = regenInterval;
        so.clearOverheatRangeMult = clearOverheatRangeMult;
        so.clearOverheatCombatMult = clearOverheatCombatMult;
        so.clearOverheatBuffDuration = clearOverheatBuffDuration;
        so.clearSuperRangeMult = clearSuperRangeMult;
        so.clearSuperCombatMult = clearSuperCombatMult;
        so.clearSuperBuffDuration = clearSuperBuffDuration;
        AssetDatabase.CreateAsset(so, path);
        return 1;
    }

    /// <summary>判定 inputSequence 是否全部为历史脏值键 A(4)/B(5)/C(6) — 用于触发升级。</summary>
    static bool IsLegacyAbcSequence(SkillInputStep[] seq)
    {
        if (seq == null || seq.Length == 0) return false;
        for (int i = 0; i < seq.Length; i++)
        {
            var s = seq[i];
            if (s != SkillInputStep.A && s != SkillInputStep.B && s != SkillInputStep.C)
                return false;
        }
        return true;
    }
}
