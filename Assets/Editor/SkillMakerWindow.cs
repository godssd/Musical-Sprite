using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 技能库（Skill Library，原 Skill Maker）。
/// 菜单：Tools > Musical Sprite > Skill Library。
///
/// 展示工程内所有 SkillSO（Assets/Resources/Skills）的 skillId / 名称 / 效果 / 效果路由，
/// 并**聚合角色文档引用**（遍历 Assets/Data/Characters 下所有 CharacterDataSO，对每个 skillId
/// 聚合「持有人 / 能量需求 / 技能冷却 / 输入方式 / 过热状态 / 超级过热状态」——这些值来自角色文档，
/// 技能库只展示、不决定）。
///
/// 同时提供**可调参数面板**：effectParamsJSON / 附魔音符数 / 每完成削减连击 可直接编辑并即时存回 SkillSO，
/// 改完下次 Play 即生效（无需改代码）。技能逻辑参数由代码 / Skill Library Generator 实现，这里只暴露数值调参入口。
///
/// 资源位于 Assets/Resources/Skills/（由 SkillLibraryGenerator 在编辑器加载时自动生成）。
/// </summary>
public class SkillMakerWindow : EditorWindow
{
    private Vector2 scroll;
    private SkillSO[] skills;
    // skillId -> 该技能被角色文档引用的聚合信息（Rescan 时构建一次，避免每帧重复扫描）
    private Dictionary<string, List<RefInfo>> _refCache = new Dictionary<string, List<RefInfo>>();

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
            "展示工程内所有 SkillSO 的 skillId / 名称 / 效果，并聚合角色文档里对该技能的引用（持有人 / 能量需求 / 技能冷却 / 输入方式 / 过热·超级过热状态）。\n" +
            "能量需求 / 技能冷却 / 输入方式 等数值**来自角色文档**（库只展示，不决定）；要改这些值请去 Characters.xlsx / 角色文档。\n" +
            "下方「可调参数」可直接编辑技能自身的效果参数（effectParamsJSON 等），即时存盘，Play 即生效。",
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

    private void Rescan()
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
                lst.Add(new RefInfo
                {
                    holder = cso.displayName + (cso.isPlayer ? "（玩家）" : "（队伍 L" + cso.laneIndex + "）"),
                    energy = slot.energyCost <= 0 ? "无（被动/玩家）" : slot.energyCost.ToString(),
                    cooldown = slot.cooldown > 0f ? slot.cooldown + " s" : "无冷却",
                    input = string.IsNullOrWhiteSpace(slot.inputMethod) ? "（无 / 被动）" : slot.inputMethod,
                    overheat = slot.overheatDesc,
                    super = slot.superOverheatDesc,
                });
            }
        }
    }

    // 角色文档引用聚合信息
    private class RefInfo
    {
        public string holder;
        public string energy;
        public string cooldown;
        public string input;
        public string overheat;
        public string super;
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

        // 文档引用聚合（持有人 / 能量需求 / 冷却 / 输入 / 过热 / 超级过热）—— 来自角色文档，库只展示
        _refCache.TryGetValue(s.skillId, out var refs);
        int refCount = refs != null ? refs.Count : 0;
        EditorGUILayout.LabelField("被角色文档引用", refCount + " 个角色");
        if (refs != null)
        {
            foreach (var r in refs)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("  持有人", r.holder);
                EditorGUILayout.LabelField("    能量需求", r.energy);
                EditorGUILayout.LabelField("    技能冷却", r.cooldown);
                EditorGUILayout.LabelField("    输入方式", r.input);
                if (!string.IsNullOrEmpty(r.overheat)) EditorGUILayout.LabelField("    过热状态", r.overheat);
                if (!string.IsNullOrEmpty(r.super)) EditorGUILayout.LabelField("    超级过热状态", r.super);
                EditorGUILayout.EndVertical();
            }
        }
        if (refCount == 0)
            EditorGUILayout.HelpBox("当前没有任何角色文档引用此 skillId（检查 Characters.xlsx 的「技能引用ID」列）。", MessageType.None);

        // 可调参数面板（编辑后即时存回 SkillSO）
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("可调参数（修改即时存盘，Play 即生效）", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        int charmed = EditorGUILayout.IntField("附魔音符数", s.charmedNoteCount);
        int reduce = EditorGUILayout.IntField("每完成削减连击", s.reduceComboPerCharmedNote);
        float comboRate = EditorGUILayout.FloatField("连击转HP伤害系数(大狗叫)", s.comboHpDamageRate);
        float feverDelay = EditorGUILayout.FloatField("过热连叫间隔(大狗叫)", s.feverExtraDelay);
        string json = EditorGUILayout.TextArea(s.effectParamsJSON, GUILayout.Height(60));
        if (EditorGUI.EndChangeCheck())
        {
            s.charmedNoteCount = charmed;
            s.reduceComboPerCharmedNote = reduce;
            s.comboHpDamageRate = comboRate;
            s.feverExtraDelay = feverDelay;
            s.effectParamsJSON = json;
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
        }

        EditorGUILayout.LabelField("资源路径", AssetDatabase.GetAssetPath(s));
        EditorGUILayout.EndVertical();
        GUILayout.Space(6);
    }
}
