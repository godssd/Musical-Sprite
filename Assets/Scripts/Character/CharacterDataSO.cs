using UnityEngine;

/// <summary>
/// 角色数据（ScriptableObject）。
/// 由 Character Importer 从 Excel 导入生成；也可在 Project 窗口右键创建手动填。
///
/// 玩家自身角色 isPlayer=true（不占音轨、无能量充能、走 PlayerCommand 类技能）；
/// 队伍角色 isPlayer=false 且 laneIndex 对应其音轨（0=最底、3=最顶）。
///
/// 数据来源：飞书「角色系统」表 1:1（编号 / 角色 / 职业 / hp / 战斗力 / 能力1 / 能量需求 / 过热状态 / 技能冷却）。
/// 运行时 HP 上限 = (玩家 hp) + (lane0 hp) + (lane1 hp) + (lane2 hp) + (lane3 hp) 之和；
///       战斗力 = 同样的五个 combatPower 之和（见 CharacterBattleSystem.InitializeFromData）。
///
/// 「上场角色不能重复」的不变量由 CharacterBattleSystem.InitializeFromData 的"每个 lane
/// 取首个匹配"保证——同一 characterId 不会被两个 lane 同时占据。
/// </summary>
[CreateAssetMenu(fileName = "Character", menuName = "Musical Sprite/Character", order = 3)]
public class CharacterDataSO : ScriptableObject
{
    [Header("基础")]
    [Tooltip("1~N；玩家自身通常=1，靠 isPlayer 区分")]
    public int characterId;
    [Tooltip("显示名（飞书「角色」列）")]
    public string displayName;
    [Tooltip("true = 玩家自身角色（不占音轨，无能量充能）")]
    public bool isPlayer = false;
    [Tooltip("职业（飞书「职业」列）。玩家=「演奏者」，队伍角色留空")]
    public string profession;

    [Header("视觉")]
    [Tooltip("代表该角色的方块颜色（身份色：宝宝灰 / 大狗橙 / 嘟嘟绿 / 爱格白 / 小黑黑）。场景方块按此色上色；由引导/导入器按 characterId 自动写入")]
    public Color blockColor = new Color(0.62f, 0.62f, 0.62f);

    [Header("战斗属性（飞书 hp / 战斗力 列）")]
    [Tooltip("仅 isPlayer=false 时有效：0~3 对应四条音轨")]
    public int laneIndex = 0;
    [Tooltip("角色血量上限（1hp=1hp 换为全队血量）")]
    public int maxHP = 100;
    [Tooltip("战斗力数值（100 点战斗力视为 10% 分数换位比，参与「扣血=（分差超出×100×0.001）× 优势方战斗力总和」）")]
    public float combatPower = 10f;

    [Header("技能（飞书「能力1」主动 + 「过热状态」被动）")]
    [Tooltip("主动技能运行时引用（P3 路由层用）。如已用 Skill Maker 建了 SkillSO 可填；可空")]
    public SkillSO activeSkill;
    [Tooltip("主动技能 ID 引用（与 Skill Maker / 角色文档对应）。运行时若 activeSkill 为空则用此 ID 反查 SkillSO.FindById")]
    public string skillId;
    [Tooltip("主动技能文字描述（飞书「能力1」原文，给玩家看的。运行时此字段只读，不参与判定）")]
    [TextArea(2, 6)]
    public string activeSkillDescription;
    [Tooltip("主动技能能量需求（玩家=0 / 队伍=100 以上整数；飞书「能量需求」列）")]
    public int activeEnergyCost = 100;

    [Tooltip("被动/过热技能运行时引用（P3 路由层用）。可空")]
    public SkillSO passiveSkill;
    [Tooltip("被动/过热技能文字描述（飞书「过热状态」原文，给玩家看的）")]
    [TextArea(2, 4)]
    public string passiveSkillDescription;

    [Header("技能冷却（飞书「技能冷却」列，表格可调）")]
    [Tooltip("释放后进入冷却的秒数。完全由角色文档决定（技能库 SkillSO 不再持有 cooldown）。0 / 未填 = 用兜底 20s。由 Character Importer 从 Characters.xlsx 的「技能冷却」列写入")]
    public float skillCooldown = 20f;

    [Header("过热")]
    [Tooltip("预留：是否参与该 side 的过热连击统计（P1 暂未使用，默认 true）")]
    public bool feverEligible = true;
}
