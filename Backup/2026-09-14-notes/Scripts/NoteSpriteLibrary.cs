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
}
