using UnityEngine;

/// <summary>
/// 技能数据（ScriptableObject）。
/// 由 Skill Maker 工具创建/编辑；Excel 中只填 skillId 引用，导入时反查为 SO。
/// effectType + effectParamsJSON 在 P3 实现具体效果路由。
/// </summary>
[CreateAssetMenu(fileName = "Skill", menuName = "Musical Sprite/Skill", order = 2)]
public class SkillSO : ScriptableObject
{
    [Header("技能标识")]
    public string skillId;            // 与 Excel 中的 skillId 对应
    public string displayName;
    [TextArea(2, 4)]
    public string description;

    [Header("消耗与冷却")]
    [Tooltip("冷却时间（秒）。PlayerCommand 可为 0")]
    public float cooldown = 0f;
    [Tooltip("仅 Active 需要：释放所需能量。其余填 0")]
    public float energyCost = 0f;

    [Header("效果（P3 实现）")]
    [Tooltip("效果路由键，例如 Heal / Buff / Shield")]
    public string effectType = "None";
    [TextArea(2, 6)]
    [Tooltip("自由 JSON 参数，P3 解析")]
    public string effectParamsJSON = "{}";
}
