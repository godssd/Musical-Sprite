using UnityEngine;

/// <summary>
/// 角色身份色板（按 characterId 返回该角色代表方块的颜色）。
/// 与飞书「角色系统」表一一对应：
///   1 小熊 → 灰
///   2 大狗 → 橙
///   3 嘟嘟 → 绿
///   4 布姆 → 白
///   5 小黑 → 黑
/// 默认（未列出 id）→ 黄（占位）。
///
/// 两侧（玩家/对手）共用同一套色板：小熊永远是灰、大狗永远是橙……便于玩家一眼区分「谁是谁」。
/// </summary>
public static class CharacterPalette
{
    public static Color GetColor(int characterId)
    {
        switch (characterId)
        {
            case 1: return new Color(0.62f, 0.62f, 0.62f); // 小熊 灰
            case 2: return new Color(1.00f, 0.55f, 0.05f); // 大狗 橙
            case 3: return new Color(0.20f, 0.80f, 0.25f); // 嘟嘟 绿
            case 4: return new Color(1.00f, 1.00f, 1.00f); // 布姆 白
            case 5: return new Color(0.05f, 0.05f, 0.05f); // 小黑 黑（近黑，避免纯黑在暗场不可见）
            default: return Color.yellow;
        }
    }

    public static string ColorName(int characterId)
    {
        switch (characterId)
        {
            case 1: return "灰";
            case 2: return "橙";
            case 3: return "绿";
            case 4: return "白";
            case 5: return "黑";
            default: return "黄";
        }
    }
}
