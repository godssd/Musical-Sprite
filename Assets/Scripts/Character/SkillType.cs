/// <summary>
/// 技能类型。
/// - PlayerCommand：玩家专属指令技能，无需能量（按键直接触发）
/// - Active：队伍角色主动技能，需要能量（命中音轨音符充能，方向键序列输入释放）
/// - Passive：被动技能，对局中持续生效
/// </summary>
public enum SkillType
{
    PlayerCommand,
    Active,
    Passive
}
