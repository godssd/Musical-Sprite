using UnityEngine;

/// <summary>Shared, persistent tuning for the game and the isolated rail review.</summary>
[CreateAssetMenu(menuName = "Musical-Sprite/Judgement Rail Style")]
public sealed class JudgementRailStyle : ScriptableObject
{
    public const string ResourcePath = "BattleVisualV11/RailStyle";

    [Header("常态几何（世界单位，修改后实时更新）")]
    [Min(0.005f), InspectorName("判定带宽度")] public float railWidth = 0.065f;
    [Min(0.001f), InspectorName("辅助线半宽")] public float guideHalfWidth = 0.013f;
    [Min(0f), InspectorName("段间留隙半宽")] public float segmentGap = 0.018f;
    [Range(0f, 1f), InspectorName("辅助线延伸比例")] public float guideReach = 0.72f;
    [Range(0f, 1f), InspectorName("常态判定带不透明度")] public float idleOpacity = 0.90f;
    [Range(0f, 3f), InspectorName("常态颜色亮度倍率")] public float idleBrightness = 1f;
    [Range(0f, 1f), InspectorName("辅助线不透明度")] public float guideOpacity = 0.18f;

    [Header("命中与按下：范围、强度")]
    [Range(0f, 1f), InspectorName("前向光延伸比例")] public float lightReach = 0.45f;
    [Min(0f), InspectorName("中央分界前留白")] public float seamPadding = 0.25f;
    [Min(0.01f), InspectorName("判定段光晕半宽")] public float railHaloWidth = 0.25f;
    [Min(0.01f), InspectorName("上下边光晕半宽")] public float edgeHaloWidth = 0.23f;
    [Range(0f, 3f), InspectorName("按下判定段亮度倍率")] public float pressStrength = 0.58f;
    [Range(0f, 3f), InspectorName("命中判定段亮度倍率")] public float hitStrength = 1f;
    [Range(0f, 3f), InspectorName("命中上下边亮度倍率")] public float edgeStrength = 1f;
    [Range(0f, 3f), InspectorName("按下内侧渐变强度")] public float pressWashStrength = 0.30f;
    [Range(0f, 1f), InspectorName("按下内侧渐变不透明度")] public float pressWashOpacity = 0.14f;
    [Range(0f, 1f), InspectorName("命中内侧渐变不透明度")] public float hitWashOpacity = 0.21f;

    [Header("渐变曲线（指数越小，远处保留越多）")]
    [Range(0.1f, 5f), InspectorName("纵向衰减指数")] public float falloffPower = 1.55f;
    [Range(0f, 0.98f), InspectorName("尾部羽化开始位置")] public float tailFadeStart = 0.70f;
    [Range(0f, 0.9f), InspectorName("边光远端收窄比例")] public float edgeTaper = 0.62f;
    [Range(0f, 1f), InspectorName("远端色相混合量")] public float hueAmount = 0.78f;
    [Range(0f, 0.9f), InspectorName("色相变化起点")] public float hueStart = 0.04f;
    [Range(0.01f, 1f), InspectorName("色相变化终点")] public float hueEnd = 0.90f;

    [Header("细芯与柔晕（指数越小越宽）")]
    [Range(0f, 3f), InspectorName("判定段细芯强度")] public float railCoreIntensity = 0.96f;
    [Range(0f, 3f), InspectorName("判定段柔晕强度")] public float railHaloIntensity = 0.26f;
    [Range(2f, 400f), InspectorName("判定段细芯收束指数")] public float railCorePower = 125f;
    [Range(1f, 20f), InspectorName("判定段柔晕收束指数")] public float railHaloPower = 5.5f;
    [Range(0f, 3f), InspectorName("上下边细芯强度")] public float edgeCoreIntensity = 0.68f;
    [Range(0f, 3f), InspectorName("上下边柔晕强度")] public float edgeHaloIntensity = 0.30f;
    [Range(2f, 400f), InspectorName("上下边细芯收束指数")] public float edgeCorePower = 190f;
    [Range(1f, 20f), InspectorName("上下边柔晕收束指数")] public float edgeHaloPower = 5f;

    [Header("时间（秒）")]
    [Min(0f), InspectorName("命中峰值停留")] public float hitPeakHold = 0f;
    [Min(0.01f), InspectorName("命中衰减时长")] public float hitFadeDuration = 0.28f;
    [Min(0.01f), InspectorName("松开衰减时长")] public float releaseDuration = 0.10f;

    [Header("队伍配色（左右共用同一几何）")]
    public Color idleRed = new Color(0.73f, 0.32f, 0.31f);
    public Color idleBlue = new Color(0.38f, 0.50f, 0.73f);
    public Color hitRed = new Color(1f, 0.34f, 0.30f);
    public Color hitBlue = new Color(0.35f, 0.62f, 1f);
    public Color coreRed = new Color(1f, 0.94f, 0.84f);
    public Color coreBlue = new Color(0.86f, 0.97f, 1f);
    public Color farColor = new Color(0.60f, 0.49f, 0.78f);

    [System.NonSerialized] private int revision = 1;
    public int Revision => revision;
    public void NotifyChanged() { unchecked { revision++; } }
    private void OnValidate() => NotifyChanged();

    public void ApplyTo(Material material, int side)
    {
        bool idle = material.GetFloat("_Kind") < 0.5f;
        material.SetColor("_NearColor", idle ? (side == 0 ? idleRed : idleBlue) : (side == 0 ? hitRed : hitBlue));
        material.SetColor("_CoreColor", side == 0 ? coreRed : coreBlue);
        material.SetColor("_FarColor", farColor);
        material.SetVector("_IdleParams", new Vector4(idleOpacity, idleBrightness, guideOpacity, 0f));
        material.SetVector("_RailShape", new Vector4(railCoreIntensity, railHaloIntensity, railCorePower, railHaloPower));
        material.SetVector("_EdgeShape", new Vector4(edgeCoreIntensity, edgeHaloIntensity, edgeCorePower, edgeHaloPower));
        material.SetVector("_FadeParams", new Vector4(Mathf.Max(0.1f, falloffPower), Mathf.Min(0.98f, tailFadeStart), edgeTaper, 0f));
        material.SetVector("_HueParams", new Vector4(hueAmount, hueStart, Mathf.Max(hueStart + 0.001f, hueEnd), 0f));
        material.SetVector("_WashParams", new Vector4(pressWashOpacity, hitWashOpacity, 0f, 0f));
    }
}
