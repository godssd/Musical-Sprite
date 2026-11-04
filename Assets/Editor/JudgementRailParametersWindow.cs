using UnityEditor;
using UnityEngine;

/// <summary>Resizable full-height parameter editing independent of preview availability.</summary>
public sealed class JudgementRailParametersWindow : EditorWindow
{
    private Editor parameterEditor;
    private Vector2 scroll;

    public static void Open()
    {
        var window = GetWindow<JudgementRailParametersWindow>("V11 判定参数");
        window.minSize = new Vector2(560f, 420f);
        if (window.position.width < 680f || window.position.height < 600f)
            window.position = new Rect(window.position.x, window.position.y, 780f, 760f);
        window.Show(); window.Focus();
    }

    private void OnGUI()
    {
        var style = Resources.Load<JudgementRailStyle>(JudgementRailStyle.ResourcePath);
        if (style == null)
        {
            EditorGUILayout.HelpBox("未找到 Assets/Resources/BattleVisualV11/RailStyle.asset，请先让 Unity 导入参数资产。", MessageType.Warning);
            return;
        }
        Editor.CreateCachedEditor(style, typeof(JudgementRailStyleEditor), ref parameterEditor);
        var inspector = (JudgementRailStyleEditor)parameterEditor;
        EditorGUILayout.LabelField("游戏与预览共用配置；无需加载预览就能调参。", EditorStyles.wordWrappedMiniLabel);
        inspector.DrawToolbar();
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        inspector.DrawParameterFields(position.width - 24f);
        EditorGUILayout.EndScrollView();
        inspector.DrawHelp();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("悬停名称看说明，? 展开帮助；保存后退出 Play 仍保留。", EditorStyles.miniLabel);
            if (GUILayout.Button("定位配置资产", GUILayout.Width(110)))
            { Selection.activeObject = style; EditorGUIUtility.PingObject(style); }
        }
    }

    private void OnDisable()
    {
        if (parameterEditor != null) DestroyImmediate(parameterEditor);
    }
}
