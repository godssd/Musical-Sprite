using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// 技能制作器（Skill Maker）。
/// 菜单：Tools > Musical Sprite > Skill Maker。
/// 创建/编辑 SkillSO（按 skillId 命名），保存到 Assets/Data/Skills/。
/// Excel 中只填 skillId 引用，导入时反查为 SO。
/// </summary>
public class SkillMakerWindow : EditorWindow
{
    private SkillSO selected;
    private Vector2 scroll;
    private string newSkillId = "skill_new";

    [MenuItem("Tools/Musical Sprite/Skill Maker")]
    public static void ShowWindow()
    {
        GetWindow<SkillMakerWindow>("Skill Maker");
    }

    private void OnGUI()
    {
        GUILayout.Label("技能制作器 (Skill Maker)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("创建/编辑主动/被动/指令技能，保存为 SkillSO 到 Assets/Data/Skills/。Excel 中只填 skillId 引用。", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("新建技能", GUILayout.Width(100)))
            CreateNew();
        newSkillId = EditorGUILayout.TextField("新技能 ID", newSkillId);
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(8);
        DrawSkillList();

        GUILayout.Space(8);
        DrawSelected();
    }

    private void DrawSkillList()
    {
        var guids = AssetDatabase.FindAssets("t:SkillSO");
        if (guids.Length == 0)
        {
            EditorGUILayout.HelpBox("暂无技能，点击「新建技能」创建。", MessageType.None);
            return;
        }
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(160));
        foreach (var g in guids)
        {
            var so = AssetDatabase.LoadAssetAtPath<SkillSO>(AssetDatabase.GUIDToAssetPath(g));
            if (so == null) continue;
            if (GUILayout.Button(so.skillId + "  (" + so.displayName + ")", EditorStyles.label))
                selected = so;
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawSelected()
    {
        if (selected == null)
        {
            EditorGUILayout.HelpBox("未选择技能。", MessageType.None);
            return;
        }

        // 注意：SerializedObject 会缓存，切换 selected 需重建
        var so = new SerializedObject(selected);
        so.Update();
        EditorGUILayout.PropertyField(so.FindProperty("skillId"));
        EditorGUILayout.PropertyField(so.FindProperty("displayName"));
        EditorGUILayout.PropertyField(so.FindProperty("description"));
        EditorGUILayout.PropertyField(so.FindProperty("cooldown"));
        EditorGUILayout.PropertyField(so.FindProperty("energyCost"));
        EditorGUILayout.PropertyField(so.FindProperty("effectType"));
        EditorGUILayout.PropertyField(so.FindProperty("effectParamsJSON"));
        so.ApplyModifiedProperties();

        if (GUILayout.Button("保存", GUILayout.Height(30)))
        {
            // 若 skillId 与文件名不一致，重命名 asset
            string ap = AssetDatabase.GetAssetPath(selected);
            string currentName = Path.GetFileNameWithoutExtension(ap);
            if (currentName != selected.skillId && !string.IsNullOrEmpty(selected.skillId))
                AssetDatabase.RenameAsset(ap, selected.skillId);
            EditorUtility.SetDirty(selected);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SkillMaker] 已保存技能 " + selected.skillId);
        }
    }

    private void CreateNew()
    {
        string dir = "Assets/Data/Skills";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + newSkillId + ".asset");
        var so = ScriptableObject.CreateInstance<SkillSO>();
        so.skillId = newSkillId;
        so.displayName = newSkillId;
        AssetDatabase.CreateAsset(so, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        selected = so;
        Debug.Log("[SkillMaker] 已创建技能 " + path);
    }
}
