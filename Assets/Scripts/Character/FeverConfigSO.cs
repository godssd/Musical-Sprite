using UnityEngine;

/// <summary>
/// 过热（Fever / Super Fever）配置。
/// 存放在 ScriptableObject 中，MS Debug 工具可实时改写并立即生效。
/// </summary>
[CreateAssetMenu(fileName = "FeverConfig", menuName = "Musical Sprite/Fever Config", order = 1)]
public class FeverConfigSO : ScriptableObject
{
    [Header("过热阈值（连击数）")]
    [Tooltip("玩家侧连击达到该值进入 Fever")]
    public int feverComboThreshold = 20;
    [Tooltip("Fever 状态下连击继续累加到该值进入 Super Fever（独立阈值，非累加）")]
    public int superFeverComboThreshold = 50;

    [Header("得分加成系数")]
    [Tooltip("Fever 状态下，该 side 命中得分乘数（1.05 = 基础分 +5%）")]
    public float feverScoreMultiplier = 1.05f;
    [Tooltip("SuperFever 状态下，该 side 命中得分乘数（1.10 = 基础分 +10%）")]
    public float superFeverScoreMultiplier = 1.10f;
}
