using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HPDecorationStyle))]
public sealed class HPDecorationStyleEditor : Editor
{
    private bool showLayout;
    [MenuItem("Tools/Musical-Sprite/V11/05 HP Detail Parameters")]
    public static void Open()
    {
        Selection.activeObject = Resources.Load<HPDecorationStyle>("BattleVisualV11/HPDecorationStyle");
        if (Selection.activeObject != null) EditorGUIUtility.PingObject(Selection.activeObject);
    }

    public override void OnInspectorGUI()
    {
        var connected = Resources.Load<ConnectedHeaderStyle>(ConnectedHeaderStyle.ResourcePath);
        if (connected != null && connected.useConnectedHeader)
        {
            EditorGUILayout.HelpBox("游戏当前使用连体月牙新版；本页参数仅用于旧版回退与旧小样。新版请打开 06。", MessageType.Info);
            if (GUILayout.Button("打开 06 连体血条与分数参数")) ConnectedHeaderStyleEditor.Open();
        }
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
        showLayout = EditorGUILayout.Foldout(showLayout, "顶部 HP＋分数组合摆放", true);
        if (showLayout)
        {
            Field("headerSize", "单侧组合尺寸");
            Field("headerScale", "组合整体缩放");
            Field("screenInset", "画面边距");
            Field("hpRootOffset", "HP 容器位置");
            Field("hpRootSize", "HP 容器尺寸");
            Field("bodyOffset", "填充主体位置");
            Field("bodySize", "填充主体尺寸");
            Field("numberOffset", "HP 数字位置");
            Field("numberSize", "HP 数字区域");
            Field("hpDigitHeight", "血量数字高度");
            Field("hpLabelHeightRatio", "HP 字样高度比例");
            Field("hpLabelGap", "HP 与数字间距");
            Field("scoreOffset", "分数位置");
            Field("scoreSize", "分数区域");
            Field("scoreDigitHeight", "分数数字高度");
        }
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
