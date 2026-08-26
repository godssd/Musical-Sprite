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
            "（6）将身前区域的所有音符全部电没（视为完成最佳击中自己获得所有大招充能）之后陷入 3 秒沉睡",
            330f, new SkillInputStep[] { SkillInputStep.Down, SkillInputStep.Right, SkillInputStep.Left },   // BCA → ↓→←
            0, 0, "ClearScreen", "{\"sleepSeconds\":3}", dir);

        created += Make("baby_damage_reduce", "宝宝全体防御减伤",
            "获得 5% 的伤害减少；超级过热：获得 15% 的伤害减少",
            0f, new SkillInputStep[0],
            0, 0, "ReduceIncomingDamagePercent", "{\"percent\":0.05}", dir);

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
        SkillInputStep[] seq, int charmed, int reducePer, string effectType, string effectParams, string dir)
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
