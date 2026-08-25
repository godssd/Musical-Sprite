using System;
using System.Collections.Generic;

/// <summary>
/// 角色花名册（静态注册表）。
/// P2 战斗初始化时把 CharacterDataSO 装配为 CharacterClass 并注册到这里；
/// ScoreManager 按 (side, lane) 查队伍角色以累加能量；VFX 订阅角色事件。
/// P1 阶段无角色注册时，所有查询安全返回 null / 空，不影响现有玩法。
/// </summary>
public static class CharacterRoster
{
    public static event Action OnRosterChanged;

    // key = side * 10 + lane
    private static readonly Dictionary<int, CharacterClass> teamChars = new Dictionary<int, CharacterClass>();
    private static readonly Dictionary<int, CharacterClass> playerChars = new Dictionary<int, CharacterClass>();

    private static int Key(int side, int lane) => side * 10 + lane;

    public static void RegisterTeam(int side, int lane, CharacterClass c)
    {
        teamChars[Key(side, lane)] = c;
        OnRosterChanged?.Invoke();
    }

    public static void RegisterPlayer(int side, CharacterClass c)
    {
        playerChars[side] = c;
        OnRosterChanged?.Invoke();
    }

    public static CharacterClass GetTeam(int side, int lane)
    {
        teamChars.TryGetValue(Key(side, lane), out var c);
        return c;
    }

    public static CharacterClass GetPlayer(int side)
    {
        playerChars.TryGetValue(side, out var c);
        return c;
    }

    public static IEnumerable<CharacterClass> AllTeamCharacters()
    {
        return teamChars.Values;
    }

    public static void Clear()
    {
        teamChars.Clear();
        playerChars.Clear();
        OnRosterChanged?.Invoke();
    }
}
