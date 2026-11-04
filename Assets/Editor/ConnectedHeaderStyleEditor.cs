using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ConnectedHeaderStyle))]
public sealed class ConnectedHeaderStyleEditor : Editor
{
    [MenuItem("Tools/Musical-Sprite/V11/06 Connected HP And Score Parameters")]
    public static void Open()
    {
        Selection.activeObject = Resources.Load<ConnectedHeaderStyle>(ConnectedHeaderStyle.ResourcePath);
        if (Selection.activeObject != null) EditorGUIUtility.PingObject(Selection.activeObject);
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("当前连体月牙版配置。红蓝共用位置参数、自动镜像；修改即时生效，点击保存可跨 Play 保留。切换新版/旧版需重新进入 Play。旧版 05 配置仍保留。", MessageType.Info);
        DrawDefaultInspector();
        if (GUILayout.Button("保存连体血条与分数参数"))
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
        }
    }
}
