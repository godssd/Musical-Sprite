using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HPDecorationStyle))]
public sealed class HPDecorationStyleEditor : Editor
{
    [MenuItem("Tools/Musical-Sprite/V11/05 HP Detail Parameters")]
    public static void Open()
    {
        Selection.activeObject = Resources.Load<HPDecorationStyle>("BattleVisualV11/HPDecorationStyle");
        if (Selection.activeObject != null) EditorGUIUtility.PingObject(Selection.activeObject);
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("红蓝及 03 预览共用配置。X/Y 单位为原 Canvas 像素；悬停字段可看偏移方向。", MessageType.Info);
        serializedObject.Update();
        Field("showDetails", "显示装饰");
        Field("tailExtension", "向分数延伸长度");
        Field("tailOpacity", "细尾透明度");
        Field("tailHeight", "细尾高度");
        Field("tailYOffset", "细尾上下偏移");
        Field("outerLeafSize", "外侧叶片尺寸");
        Field("outerLeafOffset", "外侧叶片偏移");
        Field("strokesSize", "三笔触尺寸");
        Field("strokesOffset", "三笔触偏移");
        Field("dotSize", "圆点大小");
        Field("dotOffset", "圆点偏移");
        Field("innerLeafSize", "末端叶片尺寸");
        Field("innerLeafOffset", "末端叶片偏移");
        serializedObject.ApplyModifiedProperties();
        if (GUILayout.Button("保存血条细节参数（退出 Play 后保留）"))
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
        }
    }

    private void Field(string name, string label)
    {
        var property = serializedObject.FindProperty(name);
        EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip));
    }
}
