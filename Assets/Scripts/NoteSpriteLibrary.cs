using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 音符美术资源库。在 Resources/NoteSpriteLibrary.asset 中配置所有 Sprite，
/// 运行时由 NoteMover / HoldNote 读取。
/// </summary>
[CreateAssetMenu(fileName = "NoteSpriteLibrary", menuName = "Musical Sprite/Note Sprite Library")]
public class NoteSpriteLibrary : ScriptableObject
{
    [Header("Tap")] public Sprite tap;
    [Header("Tap Select")] public Sprite tapSelect;
    [Header("Small Tap")] public Sprite smallTap;
    [Header("Small Tap Select")] public Sprite smallTapSelect;
    [Header("Wide")] public Sprite wide;
    [Header("Wide Select")] public Sprite wideSelect;

    [Header("Repeat")] public Sprite repeat;
    [Header("Repeat Select")] public Sprite repeatSelect;
    [Header("Repeat Digits 0-9")] public Sprite[] repeatDigits = new Sprite[10];
    [Header("Repeat Digits Select 0-9")] public Sprite[] repeatDigitsSelect = new Sprite[10];

    [Header("Slide")] public Sprite slideLink;
    [Header("Slide Link Select")] public Sprite slideLinkSelect;
    [Header("Slide Judgment")] public Sprite slideJudgment;
    [Header("Slide Judgment Select")] public Sprite slideJudgmentSelect;

    [Header("命中光束")] public Sprite beam;
    [Header("光碎")] public Sprite sparkle;

    private static NoteSpriteLibrary _instance;
    public static NoteSpriteLibrary Instance
    {
        get
        {
            if (_instance == null)
                _instance = Resources.Load<NoteSpriteLibrary>("NoteSpriteLibrary");
            return _instance;
        }
    }

    // ---- 附魔皮肤查找（P0：skillId -> 皮肤贴图集） ----
    // 皮肤按 Resources/EnchantSkins/{skillId}/{kind}_{skillId}.png 存放；
    // kind ∈ {Note_Tap, Note_Tap_Small, Note_Wide, Note_Repeat0..9, Note_Slide_Link, Note_Slide_Judgment}。
    // 取不到（该技能没有皮肤集）返回 null —— 调用方回退到原发光染色，保证其它技能不受影响、改动可逆。
    // 注意：皮肤 PNG 以 textureType=0(Default) 导入，故直接 Resources.Load<Texture2D>；
    //       若某个皮肤被改以 Sprite(2D/UI) 导入导致 Load<Texture2D> 为 null，则回退试 Load<Sprite>.texture。
    private static Dictionary<string, Texture2D> _enchantCache = new Dictionary<string, Texture2D>();
    public static Texture2D GetEnchantSkin(string skillId, string kind)
    {
        if (string.IsNullOrEmpty(skillId) || string.IsNullOrEmpty(kind)) return null;
        string key = skillId + "|" + kind;
        if (_enchantCache.TryGetValue(key, out var cached)) return cached;
        string path = $"EnchantSkins/{skillId}/{kind}_{skillId}";
        Texture2D tex = Resources.Load<Texture2D>(path);
        if (tex == null)
        {
            Sprite sp = Resources.Load<Sprite>(path);   // 兼容以 Sprite 导入的皮肤
            tex = sp != null ? sp.texture : null;
        }
        _enchantCache[key] = tex; // 可能为 null，缓存避免重复 IO
        return tex;
    }
}
