using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 技能库（Skill Library，原 Skill Maker）。
/// 菜单：Tools > Musical Sprite > Skill Library。
///
/// 展示工程内所有 SkillSO（Assets/Resources/Skills）的 skillId / 名称 / 效果 / 效果路由，
/// 并**聚合角色文档引用**（遍历 Assets/Data/Characters 下所有 CharacterDataSO，对每个 skillId
/// 聚合「持有人 / 能量需求 / 技能冷却 / 输入方式 / 技能介绍 / 过热状态 / 超级过热状态」——
/// 这些值来自角色文档，技能库只展示、不决定，由 Rescan 实时同步）。
///
/// 可调参数面板：按 effectType 分支显示**该技能自己的**可调参数（不再硬编码大狗叫那套），
/// 编辑后点「应用改动」才写回 SkillSO 并存盘（方便随时调控，且不会被编辑器重载覆盖——见 SkillLibraryGenerator 的 create-if-missing）。
///
/// 资源位于 Assets/Resources/Skills/（由 SkillLibraryGenerator 在编辑器加载时自动生成）。
/// </summary>
public class SkillMakerWindow : EditorWindow
{
    private Vector2 scroll;
    private SkillSO[] skills;
    // skillId -> 该技能被角色文档引用的聚合信息（Rescan 时构建一次，避免每帧重复扫描）
    private Dictionary<string, List<RefInfo>> _refCache = new Dictionary<string, List<RefInfo>>();
    // skillId -> 未提交的临时编辑值（点「应用改动」才写回 SkillSO）
    private Dictionary<string, SkillTmp> _tmp = new Dictionary<string, SkillTmp>();

    [MenuItem("Tools/Musical Sprite/Skill Library")]
    public static void ShowWindow()
    {
        GetWindow<SkillMakerWindow>("Skill Library 技能库");
    }

    private void OnEnable() { Rescan(); }

    private void OnGUI()
    {
        GUILayout.Label("技能库 (Skill Library)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "展示工程内所有 SkillSO 的 skillId / 名称 / 效果，并聚合角色文档里对该技能的引用（持有人 / 能量需求 / 技能冷却 / 输入方式 / 技能介绍 / 过热·超级过热状态）。\n" +
            "能量需求 / 技能冷却 / 输入方式 / 技能介绍 等数值**来自角色文档**（库只展示，不决定）；要改这些值请去 Characters.xlsx / 角色文档后重新导入。\n" +
            "下方「可调参数」按技能效果分支显示，**编辑后点「应用改动」写回并存盘**（Play 即生效）。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("刷新", GUILayout.Width(80))) Rescan();
        GUILayout.Label("共 " + (skills != null ? skills.Length : 0) + " 个技能", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(6);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (skills != null && skills.Length > 0)
        {
            foreach (var s in skills) DrawSkill(s);
        }
        else
        {
            EditorGUILayout.HelpBox("未找到任何 SkillSO。第一次打开脚本后会自动生成；如果还出现这条警告，请手跑 Tools > Musical Sprite > Generate Skill Library。", MessageType.Warning);
        }
        EditorGUILayout.EndScrollView();
    }

    /// <summary>重新扫描技能列表 + 聚合角色文档引用。public static 供 Character Importer 在导入后调用，实现「导入即同步」。
    /// 仅当技能库窗口已打开时才刷新其显示（不强制弹出窗口）；窗口未开时无需操作（下次打开 OnEnable 会 Rescan）。</summary>
    public static void Rescan()
    {
        if (HasOpenInstances<SkillMakerWindow>())
            GetWindow<SkillMakerWindow>().RescanInternal();
    }

    private void RescanInternal()
    {
        // 技能列表
        var guids = AssetDatabase.FindAssets("t:SkillSO");
        var list = new List<SkillSO>();
        foreach (var g in guids)
        {
            var so = AssetDatabase.LoadAssetAtPath<SkillSO>(AssetDatabase.GUIDToAssetPath(g));
            if (so != null) list.Add(so);
        }
        list.Sort((a, b) => string.Compare(a.skillId, b.skillId));
        skills = list.ToArray();

        // 聚合角色文档引用（扫描一次，建 skillId -> refs 缓存）
        _refCache = new Dictionary<string, List<RefInfo>>();
        var cGuids = AssetDatabase.FindAssets("t:CharacterDataSO", new[] { "Assets/Data/Characters" });
        foreach (var cg in cGuids)
        {
            var cso = AssetDatabase.LoadAssetAtPath<CharacterDataSO>(AssetDatabase.GUIDToAssetPath(cg));
            if (cso == null || cso.skills == null) continue;
            foreach (var slot in cso.skills)
            {
                if (slot == null || string.IsNullOrEmpty(slot.skillId)) continue;
                if (!_refCache.TryGetValue(slot.skillId, out var lst))
                {
                    lst = new List<RefInfo>();
                    _refCache[slot.skillId] = lst;
                }
                // 类型判定严格按角色文档规则（CharacterDataSO.IsPassive，2026-09-26 修正）：
                //   主动技能 = 输入方式 和 能量需求 都不为空；被动 = 输入方式 为空。
                //   能量需求 空/无 在导入时均解析为 energyCost=0，故可靠判别信号是 输入方式 是否为空。
                //   主动且能量需求=无/0 → 无能量主动技能（如全体防御/全体进攻：有输入+有冷却，无能量流程）。
                //   判定不再依赖 技能冷却；被动技能也可带冷却（冷却内完全失效，详见规则文档 §1.4）。
                string type = slot.IsPassive ? "被动技能"
                    : (slot.energyCost <= 0 ? "主动技能（无能量）" : "主动技能（能量门槛 " + slot.energyCost + "）");
                lst.Add(new RefInfo
                {
                    holder = cso.displayName + (cso.isPlayer ? "（玩家）" : "（队伍 L" + cso.laneIndex + "）"),
                    type = type,
                    energy = slot.IsPassive ? "无（被动）"
                        : (slot.energyCost <= 0 ? "无（主动·无能量）" : slot.energyCost + " 能量"),
                    cooldown = slot.cooldown > 0f ? slot.cooldown + " s"
                        : (slot.IsPassive ? "无冷却（被动）" : "无冷却"),
                    input = slot.IsPassive ? "（无 / 被动）"
                        : (string.IsNullOrWhiteSpace(slot.inputMethod) ? "（无输入）" : slot.inputMethod),
                    intro = slot.description,
                    overheat = slot.overheatDesc,
                    super = slot.superOverheatDesc,
                });
            }
        }
        Repaint();
    }

    // 角色文档引用聚合信息
    private class RefInfo
    {
        public string holder;
        public string type;
        public string energy;
        public string cooldown;
        public string input;
        public string intro;
        public string overheat;
        public string super;
    }

    // 未提交的临时编辑值（点「应用改动」才写回）
    private class SkillTmp
    {
        public int charmedNoteCount;
        public int reduceComboPerCharmedNote;
        public int charmBonusFever;
        public int charmBonusSuperFever;
        public float comboHpDamageRate;
        public float feverExtraDelay;
        public float bombDamageRate;
        public float healHpRate;
        public float healCombatRate;
        public float regenPerTickHpRate;
        public float regenInterval;
        public float buffCombatMult;
        public float buffDamageReduce;
        public float buffDuration;
        public float clearBandRangeMult;
        public float clearSleepSeconds;
        public float clearOverheatRangeMult;
        public float clearOverheatCombatMult;
        public float clearOverheatBuffDuration;
        public float clearSuperRangeMult;
        public float clearSuperCombatMult;
        public float clearSuperBuffDuration;
    }

    private SkillTmp GetTmp(SkillSO s)
    {
        if (!_tmp.TryGetValue(s.skillId, out var t))
        {
            t = new SkillTmp
            {
                charmedNoteCount = s.charmedNoteCount,
                reduceComboPerCharmedNote = s.reduceComboPerCharmedNote,
                charmBonusFever = s.charmBonusFever,
                charmBonusSuperFever = s.charmBonusSuperFever,
                comboHpDamageRate = s.comboHpDamageRate,
                feverExtraDelay = s.feverExtraDelay,
                bombDamageRate = s.bombDamageRate,
                healHpRate = s.healHpRate,
                healCombatRate = s.healCombatRate,
                regenPerTickHpRate = s.regenPerTickHpRate,
                regenInterval = s.regenInterval,
                buffCombatMult = s.buffCombatMult,
                buffDamageReduce = s.buffDamageReduce,
                buffDuration = s.buffDuration,
                clearBandRangeMult = s.clearBandRangeMult,
                clearSleepSeconds = s.clearSleepSeconds,
                clearOverheatRangeMult = s.clearOverheatRangeMult,
                clearOverheatCombatMult = s.clearOverheatCombatMult,
                clearOverheatBuffDuration = s.clearOverheatBuffDuration,
                clearSuperRangeMult = s.clearSuperRangeMult,
                clearSuperCombatMult = s.clearSuperCombatMult,
                clearSuperBuffDuration = s.clearSuperBuffDuration,
            };
            _tmp[s.skillId] = t;
        }
        return t;
    }

    private void DrawSkill(SkillSO s)
    {
        if (s == null) return;
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("skillId: " + s.skillId, EditorStyles.boldLabel);
        if (GUILayout.Button("复制 ID", GUILayout.Width(70)))
            EditorGUIUtility.systemCopyBuffer = s.skillId;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("名称", s.displayName);
        EditorGUILayout.HelpBox(s.description, MessageType.None);
        EditorGUILayout.LabelField("效果路由", s.effectType);

        // 文档引用聚合（持有人 / 能量需求 / 冷却 / 输入 / 技能介绍 / 过热 / 超级过热）—— 来自角色文档，库只展示，不构成逻辑
        _refCache.TryGetValue(s.skillId, out var refs);
        int refCount = refs != null ? refs.Count : 0;
        EditorGUILayout.LabelField("被角色文档引用（文档信息·仅供阅读）", refCount + " 个角色");
        if (refs != null && refCount > 0)
        {
            foreach (var r in refs)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("  持有人", r.holder);
                EditorGUILayout.LabelField("    技能类型", r.type);
                EditorGUILayout.LabelField("    能量需求", r.energy);
                EditorGUILayout.LabelField("    技能冷却", r.cooldown);
                EditorGUILayout.LabelField("    输入方式", r.input);
                if (!string.IsNullOrEmpty(r.intro)) EditorGUILayout.LabelField("    技能介绍", r.intro);
                if (!string.IsNullOrEmpty(r.overheat)) EditorGUILayout.LabelField("    过热状态", r.overheat);
                if (!string.IsNullOrEmpty(r.super)) EditorGUILayout.LabelField("    超级过热状态", r.super);
                EditorGUILayout.EndVertical();
            }
        }
        else
        {
            // 未被任何角色文档引用 → 这部分文档信息显示为空（非技能数值由 Characters.xlsx 决定，库只展示）
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("    技能类型", "空");
            EditorGUILayout.LabelField("    能量需求", "空");
            EditorGUILayout.LabelField("    技能冷却", "空");
            EditorGUILayout.LabelField("    输入方式", "空");
            EditorGUILayout.LabelField("    技能介绍", "空");
            EditorGUILayout.LabelField("    过热状态", "空");
            EditorGUILayout.LabelField("    超级过热状态", "空");
            EditorGUILayout.EndVertical();
            EditorGUILayout.HelpBox("未引用：以上文档信息为空（检查 Characters.xlsx 的「技能引用ID」列）。", MessageType.None);
        }

        // 可调参数面板（按 effectType 分支；编辑后点「应用改动」写回）
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("可调参数（按技能效果显示，编辑后点「应用改动」）", EditorStyles.boldLabel);
        var t = GetTmp(s);
        DrawTunables(s, t);
        DrawNonTunableInfo(s);
        if (GUILayout.Button("应用改动", GUILayout.Width(100)))
        {
            s.charmedNoteCount = t.charmedNoteCount;
            s.reduceComboPerCharmedNote = t.reduceComboPerCharmedNote;
            s.charmBonusFever = t.charmBonusFever;
            s.charmBonusSuperFever = t.charmBonusSuperFever;
            s.comboHpDamageRate = t.comboHpDamageRate;
            s.feverExtraDelay = t.feverExtraDelay;
            s.bombDamageRate = t.bombDamageRate;
            s.healHpRate = t.healHpRate;
            s.healCombatRate = t.healCombatRate;
            s.regenPerTickHpRate = t.regenPerTickHpRate;
            s.regenInterval = t.regenInterval;
            s.buffCombatMult = t.buffCombatMult;
            s.buffDamageReduce = t.buffDamageReduce;
            s.buffDuration = t.buffDuration;
            s.clearBandRangeMult = t.clearBandRangeMult;
            s.clearSleepSeconds = t.clearSleepSeconds;
            s.clearOverheatRangeMult = t.clearOverheatRangeMult;
            s.clearOverheatCombatMult = t.clearOverheatCombatMult;
            s.clearOverheatBuffDuration = t.clearOverheatBuffDuration;
            s.clearSuperRangeMult = t.clearSuperRangeMult;
            s.clearSuperCombatMult = t.clearSuperCombatMult;
            s.clearSuperBuffDuration = t.clearSuperBuffDuration;
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
            Debug.Log("[SkillLibrary] 已应用改动：" + s.skillId);
        }

        EditorGUILayout.LabelField("资源路径", AssetDatabase.GetAssetPath(s));
        EditorGUILayout.EndVertical();
        GUILayout.Space(6);
    }

    /// <summary>按 effectType 显示该技能自己的可调参数（解决「炸弹雨显示大狗叫参数」的 bug）。</summary>
    private void DrawTunables(SkillSO s, SkillTmp t)
    {
        switch (s.effectType)
        {
            case "DogHowl":
                t.charmedNoteCount = EditorGUILayout.IntField("附魔音符数", t.charmedNoteCount);
                t.reduceComboPerCharmedNote = EditorGUILayout.IntField("每完成削减连击", t.reduceComboPerCharmedNote);
                t.charmBonusFever = EditorGUILayout.IntField("过热额外附魔数", t.charmBonusFever);
                t.charmBonusSuperFever = EditorGUILayout.IntField("超级过热额外附魔数", t.charmBonusSuperFever);
                t.comboHpDamageRate = EditorGUILayout.FloatField("连击转HP伤害系数", t.comboHpDamageRate);
                t.feverExtraDelay = EditorGUILayout.FloatField("过热连叫间隔(s)", t.feverExtraDelay);
                break;
            case "Bomb":
                t.bombDamageRate = EditorGUILayout.FloatField("伤害参数(战斗力总和占比)", t.bombDamageRate);
                t.charmedNoteCount = EditorGUILayout.IntField("附魔符文数量", t.charmedNoteCount);
                t.charmBonusFever = EditorGUILayout.IntField("过热附魔符文数量", t.charmBonusFever);
                t.charmBonusSuperFever = EditorGUILayout.IntField("超级过热附魔符文数量", t.charmBonusSuperFever);
                break;
            case "Heal":
                t.healCombatRate = EditorGUILayout.FloatField("每音符恢复·战力系数", t.healCombatRate);
                t.healHpRate = EditorGUILayout.FloatField("每音符恢复·生命系数", t.healHpRate);
                t.charmedNoteCount = EditorGUILayout.IntField("附魔音符数量", t.charmedNoteCount);
                t.regenInterval = EditorGUILayout.FloatField("过热恢复间隔(s)", t.regenInterval);
                t.regenPerTickHpRate = EditorGUILayout.FloatField("缓慢恢复量系数(每跳)", t.regenPerTickHpRate);
                break;
            case "Buff":
                t.buffCombatMult = EditorGUILayout.FloatField("攻击力加成(乘子)", t.buffCombatMult);
                t.buffDamageReduce = EditorGUILayout.FloatField("减伤数值(0~1)", t.buffDamageReduce);
                t.buffDuration = EditorGUILayout.FloatField("持续时间(s)", t.buffDuration);
                break;
            case "ClearScreen":
                t.clearBandRangeMult = EditorGUILayout.FloatField("消灭范围(基础倍率)", t.clearBandRangeMult);
                t.clearSleepSeconds = EditorGUILayout.FloatField("沉睡秒数", t.clearSleepSeconds);
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("— 过热状态 —", EditorStyles.miniLabel);
                t.clearOverheatRangeMult = EditorGUILayout.FloatField("  范围加成", t.clearOverheatRangeMult);
                t.clearOverheatCombatMult = EditorGUILayout.FloatField("  攻击力提升", t.clearOverheatCombatMult);
                t.clearOverheatBuffDuration = EditorGUILayout.FloatField("  持续时间(s)", t.clearOverheatBuffDuration);
                EditorGUILayout.LabelField("— 超级过热状态 —", EditorStyles.miniLabel);
                t.clearSuperRangeMult = EditorGUILayout.FloatField("  范围加成", t.clearSuperRangeMult);
                t.clearSuperCombatMult = EditorGUILayout.FloatField("  攻击力提升", t.clearSuperCombatMult);
                t.clearSuperBuffDuration = EditorGUILayout.FloatField("  持续时间(s)", t.clearSuperBuffDuration);
                break;
            case "Dispel":
                t.charmedNoteCount = EditorGUILayout.IntField("附魔音符数量", t.charmedNoteCount);
                t.reduceComboPerCharmedNote = EditorGUILayout.IntField("每完成削减连击", t.reduceComboPerCharmedNote);
                break;
                default:
                    EditorGUILayout.LabelField("（该效果类型暂无可调参数面板）");
                    break;
        }
    }

    /// <summary>可调参数之外、不适合在面板直接编辑的技能逻辑参数（枚举/布尔）。
    /// 仅作信息提示，让使用者知悉其存在；如需改动请告知开发修改 SkillSO 字段（不要在此擅自做成可编辑控件，避免与「技能库决定逻辑」职责混淆）。</summary>
    private void DrawNonTunableInfo(SkillSO s)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("逻辑参数（不在面板编辑，需改动时告知开发）", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("  附魔目标(enchantTarget)", s.enchantTarget.ToString());
        EditorGUILayout.LabelField("  仅未MISS计入(onlyCountNonMiss)", s.onlyCountNonMiss.ToString());
        EditorGUILayout.LabelField("  buff 槽位(buffSlot)", s.buffSlot.ToString());
        EditorGUILayout.LabelField("  buff 子类型(buffSubType)", s.buffSubType.ToString());
        EditorGUILayout.LabelField("  清屏激活b类(clearBuffAsB)", s.clearBuffAsB.ToString());
        EditorGUILayout.HelpBox(
            "以上为技能逻辑参数（枚举/布尔），不适合在面板直接编辑。如需改动请告知开发修改 SkillSO 字段。\n" +
            "非技能数值（能量需求 / 技能冷却 / 输入方式 / 技能引用ID）来自角色文档，见上方「文档引用信息」，不在此处。",
            MessageType.None);
    }
}
