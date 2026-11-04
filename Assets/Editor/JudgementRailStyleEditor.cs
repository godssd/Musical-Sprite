using System;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(JudgementRailStyle))]
public sealed class JudgementRailStyleEditor : Editor
{
    private string search = "";
    private int category;
    private bool showDescriptions;
    private string selectedHelp;
    private readonly bool[] expanded = { true, true, true, true, true, true };
    private static readonly string[] Categories = { "全部", "常态", "范围/强度", "渐变", "光芯/光晕", "时间", "配色" };
    private static readonly string[] GroupNames = { "常态判定带与辅助线", "按下 / 命中的范围与强度", "从判定带向场内的渐变", "细芯与外围柔和光晕", "动画时间（秒）", "队伍与远端配色" };

    private sealed class Parameter
    {
        public readonly int Group;
        public readonly string Name, Label, Help;
        public readonly float Min, Max;
        public readonly bool Slider, Color;
        public Parameter(int group, string name, string label, string help, float min = 0f,
            float max = 0f, bool slider = false, bool color = false)
        { Group = group; Name = name; Label = label; Help = help; Min = min; Max = max; Slider = slider; Color = color; }
        public bool Matches(string term) => string.IsNullOrWhiteSpace(term) ||
            Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
            Label.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
            Help.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Explicit labels replace the English names previously produced by DrawDefaultInspector.
    private static readonly Parameter[] Parameters =
    {
        new Parameter(0, nameof(JudgementRailStyle.railWidth), "判定带宽度", "整条平面判定带的完整宽度，单位为世界单位。增大更粗；不直接改变光晕宽度。", 0.005f),
        new Parameter(0, nameof(JudgementRailStyle.guideHalfWidth), "辅助线半宽", "常态五条辅助线的半宽。完整宽度约为此值的两倍；越大越容易看清四轨。", 0.001f),
        new Parameter(0, nameof(JudgementRailStyle.segmentGap), "段间留隙半宽", "相邻判定段的总空隙约为两倍此值；0 表示常态段相接。", 0),
        new Parameter(0, nameof(JudgementRailStyle.guideReach), "辅助线延伸比例", "判定带到中央分界可用距离的比例。0 不延伸，1 接近中央留白边界，不改变真实判定位置。", 0, 1, true),
        new Parameter(0, nameof(JudgementRailStyle.idleOpacity), "常态判定带不透明度", "0 透明、1 不透明。调高更实；若颜色仍暗，应调亮度或常态颜色。", 0, 1, true),
        new Parameter(0, nameof(JudgementRailStyle.idleBrightness), "常态颜色亮度倍率", "1 为原颜色亮度，0 为黑色，超过 1 提亮。只作用于常态判定带。", 0, 3, true),
        new Parameter(0, nameof(JudgementRailStyle.guideOpacity), "辅助线不透明度", "常态辅助线近端的基础透明度，远端仍叠加纵向衰减。", 0, 1, true),

        new Parameter(1, nameof(JudgementRailStyle.lightReach), "前向光延伸比例", "按下内侧渐变和命中上下边光的几何长度，相对半场可用距离。实际可见长度还受衰减影响。", 0, 1, true),
        new Parameter(1, nameof(JudgementRailStyle.seamPadding), "中央分界前留白", "先从可用半场长度扣除的距离，单位为世界单位。越大，延伸光越早停在中央分界前。", 0),
        new Parameter(1, nameof(JudgementRailStyle.railHaloWidth), "判定段光晕半宽", "判定带两侧光晕的覆盖半宽。增大扩大柔光范围，亮度由强度参数另调。", 0.01f),
        new Parameter(1, nameof(JudgementRailStyle.edgeHaloWidth), "上下边光晕半宽", "命中时沿音轨上下边界的光晕覆盖半宽。增大让边光更柔、更有面积。", 0.01f),
        new Parameter(1, nameof(JudgementRailStyle.pressStrength), "按下判定段亮度倍率", "空按 / 按住时整段的亮度。命中另有独立倍率，通常应比按下更亮。", 0, 3, true),
        new Parameter(1, nameof(JudgementRailStyle.hitStrength), "命中判定段亮度倍率", "非 MISS 命中时整段亮度，1 为首版基准。超过 1 增强发光，不改变持续时间。", 0, 3, true),
        new Parameter(1, nameof(JudgementRailStyle.edgeStrength), "命中上下边亮度倍率", "音轨上下两条共享边的整体亮度，不改变判定段。相邻命中仍取最大包络。", 0, 3, true),
        new Parameter(1, nameof(JudgementRailStyle.pressWashStrength), "按下内侧渐变强度", "按下时轨道内部大片渐变的包络强度，与按下渐变不透明度共同作用。", 0, 3, true),
        new Parameter(1, nameof(JudgementRailStyle.pressWashOpacity), "按下渐变不透明度", "按下时内侧大片渐变的基础透明度。过高会盖住地面细节，不控制细亮边。", 0, 1, true),
        new Parameter(1, nameof(JudgementRailStyle.hitWashOpacity), "命中渐变不透明度", "命中时轨道内部染色的基础透明度。可比按下更强，仍需保留地面可读性。", 0, 1, true),

        new Parameter(2, nameof(JudgementRailStyle.falloffPower), "纵向衰减指数", "沿光长的亮度衰减约为 (1-距离比例)^指数。越小远端越亮，越大越集中在判定带附近。", 0.1f, 5, true),
        new Parameter(2, nameof(JudgementRailStyle.tailFadeStart), "尾部羽化开始位置", "0.7 表示最后 30% 额外淡到零。增大推迟尾部羽化；前面的纵向衰减仍存在。", 0, 0.98f, true),
        new Parameter(2, nameof(JudgementRailStyle.edgeTaper), "边光远端收窄比例", "越大，上下边光越往远处越窄；0 不额外收窄。光的中心路径始终笔直。", 0, 0.9f, true),
        new Parameter(2, nameof(JudgementRailStyle.hueAmount), "远端色相混合量", "0 保留近端队伍色，1 更多过渡到远端颜色。色相和透明度分别控制。", 0, 1, true),
        new Parameter(2, nameof(JudgementRailStyle.hueStart), "色相变化起点", "从光长的哪个比例开始向远端颜色过渡。越小越早变色。", 0, 0.9f, true),
        new Parameter(2, nameof(JudgementRailStyle.hueEnd), "色相变化终点", "色相过渡完成的位置，按光长比例计算。需大于起点；不控制透明度。", 0.01f, 1, true),

        new Parameter(3, nameof(JudgementRailStyle.railCoreIntensity), "判定段细芯强度", "判定带中间细亮芯的强度，外围晕光由柔晕强度控制。", 0, 3, true),
        new Parameter(3, nameof(JudgementRailStyle.railHaloIntensity), "判定段柔晕强度", "判定带外围柔光强度。要更柔，可配合加宽光晕，不必只提高此值。", 0, 3, true),
        new Parameter(3, nameof(JudgementRailStyle.railCorePower), "判定段细芯收束指数", "横向收束指数：越大细芯越细，越小越宽。它不是亮度系数。", 2, 400, true),
        new Parameter(3, nameof(JudgementRailStyle.railHaloPower), "判定段柔晕收束指数", "越小外围光晕铺得越宽，越大越集中；范围仍受光晕半宽限制。", 1, 20, true),
        new Parameter(3, nameof(JudgementRailStyle.edgeCoreIntensity), "上下边细芯强度", "音轨上下边界里细亮线的强度，与外围柔晕独立调整。", 0, 3, true),
        new Parameter(3, nameof(JudgementRailStyle.edgeHaloIntensity), "上下边柔晕强度", "上下边细亮线周围柔光的强度。提高面积感，注意别冲白地面。", 0, 3, true),
        new Parameter(3, nameof(JudgementRailStyle.edgeCorePower), "上下边细芯收束指数", "越大边界细芯越细，越小越宽。不改变直线路径和延伸长度。", 2, 400, true),
        new Parameter(3, nameof(JudgementRailStyle.edgeCoreReach), "上下边细芯延伸比例", "细亮核心独立于宽光晕提前消退；比例基于前向光长度，降低后可避免整轨出现硬亮框。", .1f, 1, true),
        new Parameter(3, nameof(JudgementRailStyle.edgeCoreFalloff), "上下边细芯独立衰减", "只控制细亮核心向场内的衰减，宽色光继续使用纵向衰减指数。", .2f, 4, true),
        new Parameter(3, nameof(JudgementRailStyle.edgeHaloPower), "上下边柔晕收束指数", "越小外围晕光越铺开，越大越集中，与上下边光晕半宽共同决定柔和程度。", 1, 20, true),

        new Parameter(4, nameof(JudgementRailStyle.hitPeakHold), "命中峰值停留（秒）", "命中保持最亮状态多久，再开始衰减。0 表示立即衰减。", 0),
        new Parameter(4, nameof(JudgementRailStyle.hitFadeDuration), "命中衰减时长（秒）", "峰值后的退出时间。总命中时间 = 峰值停留 + 衰减时长。", 0.01f),
        new Parameter(4, nameof(JudgementRailStyle.releaseDuration), "松开衰减时长（秒）", "松开后的输入光退出时间，不控制命中光；仍按住时 Hit 结束会回到按下态。", 0.01f),

        new Parameter(5, nameof(JudgementRailStyle.idleRed), "红方常态颜色", "红方平面色带的原色，还会乘常态亮度。透明度请调专门的不透明度参数。", color: true),
        new Parameter(5, nameof(JudgementRailStyle.idleBlue), "蓝方常态颜色", "蓝方平面色带的原色。几何和时间仍与红方一致。", color: true),
        new Parameter(5, nameof(JudgementRailStyle.hitRed), "红方命中光晕颜色", "红方命中外晕和内侧染色的近端队伍色，白亮芯另有颜色参数。", color: true),
        new Parameter(5, nameof(JudgementRailStyle.hitBlue), "蓝方命中光晕颜色", "蓝方命中外晕和内侧染色的近端队伍色。", color: true),
        new Parameter(5, nameof(JudgementRailStyle.coreRed), "红方细芯颜色", "红方最亮细芯的近白色，减少黄色分量可得到更中性的暖白。", color: true),
        new Parameter(5, nameof(JudgementRailStyle.coreBlue), "蓝方细芯颜色", "蓝方最亮细芯可用轻微冷白，不必涂成饱和蓝。", color: true),
        new Parameter(5, nameof(JudgementRailStyle.farColor), "远端过渡颜色", "红蓝共用的远端目标色，配合色相混合量与变化起止位置使用。", color: true)
    };

    [MenuItem("Tools/Musical-Sprite/V11/00 Judgement UI Parameters")]
    public static void SelectStyle() => JudgementRailParametersWindow.Open();

    public override void OnInspectorGUI()
    {
        DrawToolbar();
        DrawParameterFields();
        DrawHelp();
    }

    public void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("保存参数", "保存资产，退出 Play 后保留。")))
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssetIfDirty(target);
                if (EditorWindow.focusedWindow != null)
                    EditorWindow.focusedWindow.ShowNotification(new GUIContent("判定参数已保存"));
            }
            if (GUILayout.Button("独立参数窗口")) JudgementRailParametersWindow.Open();
            if (GUILayout.Button(new GUIContent("建议起点", "替换为上次分析建议值，会改变参数，支持 Undo。"))) ApplySuggested();
        }
        search = EditorGUILayout.TextField(new GUIContent("查找参数", "可搜索中文、英文变量名或用途。"), search);
        category = EditorGUILayout.Popup("参数分类", category, Categories);
        showDescriptions = EditorGUILayout.ToggleLeft("显示每项说明（也可悬停名称或点 ?）", showDescriptions);
    }

    public void DrawParameterFields(float availableWidth = 0f)
    {
        serializedObject.Update();
        float oldLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = Mathf.Clamp((availableWidth > 0 ? availableWidth : EditorGUIUtility.currentViewWidth) * 0.46f, 160f, 260f);
        try
        {
            bool matchedAny = false;
            for (int group = 0; group < GroupNames.Length; group++)
            {
                if (category > 0 && category - 1 != group) continue;
                bool hasMatch = false;
                foreach (var field in Parameters)
                    if (field.Group == group && field.Matches(search)) { hasMatch = true; break; }
                if (!hasMatch) continue;
                matchedAny = true;
                expanded[group] = EditorGUILayout.Foldout(expanded[group], GroupNames[group], true, EditorStyles.foldoutHeader);
                if (!expanded[group] && string.IsNullOrWhiteSpace(search)) continue;
                foreach (var field in Parameters)
                {
                    if (field.Group != group || !field.Matches(search)) continue;
                    var property = serializedObject.FindProperty(field.Name);
                    if (property == null) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var content = new GUIContent(field.Label, field.Name + "\n" + field.Help);
                        EditorGUI.BeginChangeCheck();
                        if (field.Color)
                        {
                            Color value = EditorGUILayout.ColorField(content, property.colorValue, true, false, false);
                            if (EditorGUI.EndChangeCheck()) property.colorValue = value;
                        }
                        else
                        {
                            float value = field.Slider ? EditorGUILayout.Slider(content, property.floatValue, field.Min, field.Max)
                                : EditorGUILayout.FloatField(content, property.floatValue);
                            if (EditorGUI.EndChangeCheck()) property.floatValue = Mathf.Max(field.Min, value);
                        }
                        if (GUILayout.Button(new GUIContent("?", field.Help), GUILayout.Width(24))) selectedHelp = field.Name;
                    }
                    if (showDescriptions)
                        EditorGUILayout.LabelField(field.Name + "：" + field.Help, EditorStyles.wordWrappedMiniLabel);
                }
                EditorGUILayout.Space(6f);
            }
            if (!matchedAny) EditorGUILayout.HelpBox("没有匹配的参数，请清空搜索或切换“全部”。", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
        }
        finally { EditorGUIUtility.labelWidth = oldLabelWidth; }
    }

    public void DrawHelp()
    {
        if (string.IsNullOrEmpty(selectedHelp)) return;
        foreach (var field in Parameters)
        {
            if (field.Name != selectedHelp) continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(field.Label + "（" + field.Name + "）\n" + field.Help, MessageType.Info);
                if (GUILayout.Button("×", GUILayout.Width(24))) selectedHelp = null;
            }
            break;
        }
    }

    private void ApplySuggested()
    {
        var style = (JudgementRailStyle)target;
        Undo.RecordObject(style, "Apply V11 rail suggested tuning");
        style.railWidth = 0.105f; style.idleBrightness = 1.2f;
        style.guideOpacity = 0.22f; style.guideReach = 0.90f; style.lightReach = 0.80f;
        style.railHaloWidth = 0.35f; style.edgeHaloWidth = 0.34f;
        style.railHaloIntensity = 0.34f; style.edgeHaloIntensity = 0.40f;
        style.falloffPower = 0.8f; style.tailFadeStart = 0.75f;
        style.hitPeakHold = 0.04f; style.hitFadeDuration = 0.30f;
        style.coreRed = new Color(1f, 0.97f, 0.94f);
        style.coreBlue = new Color(0.90f, 0.97f, 1f);
        style.NotifyChanged();
        EditorUtility.SetDirty(style);
        serializedObject.Update();
    }
}
