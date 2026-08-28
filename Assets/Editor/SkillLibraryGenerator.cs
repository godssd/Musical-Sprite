using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
    /// 技能库生成器（一次性）。
    /// 菜单：Tools > Musical Sprite > Generate Skill Library；编辑器加载时也会自动跑一次（幂等）。
    ///
    /// 在 Assets/Resources/Skills/ 生成与功能需求表一一对应的 SkillSO 资源：
    ///   dog_howl（大狗叫）/ dudu_heal（嘟嘟治疗）/ aige_bomb（爱格炸弹雨）
    ///   / xiaohei_clear（小黑清屏）/ baby_damage_reduce（宝宝全体防御减伤）。
    ///
    /// 必须放在 Assets/Resources/ 下，是因为运行时 SkillSO.RebuildIndex() 走
    /// Resources.LoadAll<SkillSO>("Skills")，否则游戏打包后 SkillSO.FindById() 会查不到。
    /// Skill Library 窗口（Editor）走 AssetDatabase.FindAssets()，在 Resources 下也能正常列出。
    ///
    /// 这些资源是「技能库」的唯一数据源：Characters.xlsx 的 activeSkillId 列、角色文档、以及
    /// Character Importer 都只引用这里的 skillId。生成后打开 Tools > Musical Sprite > Skill Library
    /// 即可只读查看技能库。
    ///
    /// 幂等：已存在同名资源则跳过，不会覆盖你后续手动改过的参数。
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

        created += Make("dudu_heal", "嘟嘟治疗",
            "（4）将即将出现的音符（6 个）附魔，每成功完成一个音符，就对自己进行一点生命治愈（3 点生命）（必杀）",
            200f, new SkillInputStep[] { SkillInputStep.Right, SkillInputStep.Down, SkillInputStep.Right }, // CBC → →↓→
            6, 3, "Heal", "{\"healPerNote\":3,\"slowRegenSeconds\":9}", dir);

        created += Make("aige_bomb", "爱格炸弹雨",
            "（5）炸弹雨（将即将出现的音符（3 个）附魔，每完成一个音符就朝对手随机投射一颗小型炸弹（10 点伤害），造成直接生命伤害直到结算完毕）（必杀）",
            280f, new SkillInputStep[] { SkillInputStep.Left, SkillInputStep.Down, SkillInputStep.Left },    // ABA → ←↓←
            3, 3, "Bomb", "{\"bombDamage\":10}", dir);

        created += Make("xiaohei_clear", "小黑清屏",
            "（6）将身前区域的所有音符全部电没（视为完成最佳命中，按各音符最高判定计分/充能）之后释放者小黑自身陷入 N 秒沉睡；过热 / 超级过热释放时额外激活自身 b 类战力 buff（橙黄）",
            330f, new SkillInputStep[] { SkillInputStep.Down, SkillInputStep.Right, SkillInputStep.Left },   // BCA → ↓→←
            0, 0, "ClearScreen", "{}", dir,
            buffSlot: BuffSlot.B, buffCombatMult: 1.5f, clearSleepSeconds: 3f, clearBuffAsB: true);

        created += Make("baby_damage_reduce", "宝宝全体防御减伤",
            "获得 5% 的伤害减少；超级过热：获得 15% 的伤害减少",
            0f, new SkillInputStep[0],
            0, 0, "ReduceIncomingDamagePercent", "{\"percent\":0.05}", dir);

        // 宝宝双 buff（a 类，互斥顶替）：实际已在磁盘存在，此前漏在生成器外、与「唯一数据源」自述不符。补回使其可重生成。
        created += Make("baby_defense_buff", "宝宝全体防御",
            "（1）全体防御：减少 20% 受到的伤害，持续 10s（同属 a 类 buff 不可共存）",
            0f, new SkillInputStep[0],
            0, 0, "Buff", "{}", dir,
            buffSlot: BuffSlot.A, buffSubType: BuffSubType.Defense, buffCombatMult: 1f, buffDamageReduce: 0.2f, buffDuration: 10f);

        created += Make("baby_offense_buff", "宝宝全体进攻",
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
        EnchantTarget enchantTarget = EnchantTarget.Self)
    {
        string path = dir + "/" + skillId + ".asset";
        if (File.Exists(path))
        {
            // 已经在 → 检查是否需要从历史脏值（A/B/C 旧键）升级到新键 ←/↓/→
            var existing = AssetDatabase.LoadAssetAtPath<SkillSO>(path);
            if (existing != null && IsLegacyAbcSequence(existing.inputSequence) && seq != null && seq.Length > 0)
            {
                existing.inputSequence = seq;
                EditorUtility.SetDirty(existing);
                Debug.Log("[SkillLibrary] 已升级 " + skillId + ".asset 的 inputSequence 为新键（历史 A/B/C → ←/↓/→）。");
                return 1;
            }
            return 0;   // 真正的幂等
        }
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
        // 同步 buff / 清屏参数（此前生成器只设了基础字段，重生成会导致这些字段丢失）
        so.enchantTarget = enchantTarget;
        so.buffSlot = buffSlot;
        so.buffSubType = buffSubType;
        so.buffCombatMult = buffCombatMult;
        so.buffDamageReduce = buffDamageReduce;
        so.buffDuration = buffDuration;
        so.clearBandRangeMult = clearBandRangeMult;
        so.clearSleepSeconds = clearSleepSeconds;
        so.clearBuffAsB = clearBuffAsB;
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
