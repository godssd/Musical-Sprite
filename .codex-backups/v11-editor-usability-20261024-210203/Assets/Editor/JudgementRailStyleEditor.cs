using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(JudgementRailStyle))]
public sealed class JudgementRailStyleEditor : Editor
{
    [MenuItem("Tools/Musical-Sprite/V11/00 Judgement UI Parameters")]
    public static void SelectStyle()
    {
        var style = Resources.Load<JudgementRailStyle>(JudgementRailStyle.ResourcePath);
        if (style == null) { Debug.LogError("Missing Assets/Resources/BattleVisualV11/RailStyle.asset"); return; }
        Selection.activeObject = style;
        EditorGUIUtility.PingObject(style);
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("游戏内红蓝判定带与 V11 审查窗口共用此资产。修改实时生效；可在 Play 中编辑并保存，退出 Play 后保留。指数越小，光越宽 / 远端越亮。", MessageType.Info);
        DrawDefaultInspector();
        var style = (JudgementRailStyle)target;
        if (GUILayout.Button("保存参数（退出 Play 后保留）"))
        {
            EditorUtility.SetDirty(style);
            AssetDatabase.SaveAssetIfDirty(style);
        }
        if (GUILayout.Button("使用上次分析的建议起点（可 Undo）"))
        {
            Undo.RecordObject(style, "Apply V11 rail suggested tuning");
            style.railWidth = 0.105f;
            style.idleBrightness = 1.2f;
            style.guideOpacity = 0.22f;
            style.guideReach = 0.90f;
            style.lightReach = 0.80f;
            style.railHaloWidth = 0.35f;
            style.edgeHaloWidth = 0.34f;
            style.railHaloIntensity = 0.34f;
            style.edgeHaloIntensity = 0.40f;
            style.falloffPower = 0.8f;
            style.tailFadeStart = 0.75f;
            style.hitPeakHold = 0.04f;
            style.hitFadeDuration = 0.30f;
            style.coreRed = new Color(1f, 0.97f, 0.94f);
            style.coreBlue = new Color(0.90f, 0.97f, 1f);
            style.NotifyChanged();
            EditorUtility.SetDirty(style);
            serializedObject.Update();
        }
    }
}
