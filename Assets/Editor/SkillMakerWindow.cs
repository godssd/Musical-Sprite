using UnityEngine;
using UnityEditor;

/// <summary>
    /// 技能库（Skill Library，原 Skill Maker）。
    /// 菜单：Tools > Musical Sprite > Skill Library。
    ///
    /// 只读展示工程内所有 SkillSO（Assets/Resources/Skills，由 SkillLibraryGenerator 在编辑器加载时自动生成）的
    /// skillId / 名称 / 参数，供 Characters.xlsx 与角色文档引用。本窗口不提供任何写操作。
    ///
    /// 资源位于 Assets/Resources/Skills/，是因为运行时 SkillSO.RebuildIndex() 走
    /// Resources.LoadAll<SkillSO>("Skills")，必须放 Resources 下游戏打包后 FindById 才能查到。
    /// </summary>
public class SkillMakerWindow : EditorWindow
{
    private Vector2 scroll;
    private SkillSO[] skills;

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
            "只读展示工程内所有 SkillSO 的 skillId 与参数，供 Characters.xlsx / 角色文档引用。\n" +
            "技能数据位于 Assets/Resources/Skills/（编辑器加载时自动生成，也可在 Tools > Musical Sprite > Generate Skill Library 手跑）。\n" +
            "本窗口不提供编辑；要改技能请直接改对应 .asset，重跑生成器不会覆盖已存在的 .asset。",
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
        var guids = AssetDatabase.FindAssets("t:SkillSO");
        var list = new System.Collections.Generic.List<SkillSO>();
        foreach (var g in guids)
        {
            var so = AssetDatabase.LoadAssetAtPath<SkillSO>(AssetDatabase.GUIDToAssetPath(g));
            if (so != null) list.Add(so);
        }
        list.Sort((a, b) => string.Compare(a.skillId, b.skillId));
        skills = list.ToArray();
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
        EditorGUILayout.LabelField("能量需求", s.energyCost > 0 ? s.energyCost.ToString() : "无（被动/玩家）");
        EditorGUILayout.LabelField("输入方式", InputString(s.inputSequence));
        EditorGUILayout.LabelField("附魔音符数", s.charmedNoteCount.ToString());
        EditorGUILayout.LabelField("每完成削减连击", "× " + s.reduceComboPerCharmedNote);
        EditorGUILayout.LabelField("效果路由", s.effectType);
        EditorGUILayout.LabelField("资源路径", AssetDatabase.GetAssetPath(s));

        EditorGUILayout.EndVertical();
        GUILayout.Space(6);
    }

    private static string InputString(SkillInputStep[] seq)
    {
        if (seq == null || seq.Length == 0) return "（无 / 被动）";
        var sb = new System.Text.StringBuilder();
        foreach (var step in seq)
        {
            switch (step)
            {
                case SkillInputStep.A: sb.Append("A "); break;
                case SkillInputStep.B: sb.Append("B "); break;
                case SkillInputStep.C: sb.Append("C "); break;
                case SkillInputStep.Left: sb.Append("← "); break;
                case SkillInputStep.Down: sb.Append("↓ "); break;
                case SkillInputStep.Right: sb.Append("→ "); break;
                case SkillInputStep.Up: sb.Append("↑ "); break;
                case SkillInputStep.Space: sb.Append("␣ "); break;
            }
        }
        return sb.ToString().Trim();
    }
}
