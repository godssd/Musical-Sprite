using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 技能数据（ScriptableObject）。
/// 技能与角色解耦：角色（CharacterDataSO）只持有 skillId 字符串引用，
/// 运行时通过 SkillSO.FindById(skillId) 反查到本 SO；Excel / 角色文档也只填 skillId。
/// Skill Maker 工具只读展示本 SO，不负责编排。
///
/// 大狗叫（必杀）示例字段含义见 ActiveSkillRuntime。
/// </summary>
public enum SkillInputStep
{
    Left, Down, Right, Up, A, B, C, Space
}

[CreateAssetMenu(fileName = "Skill", menuName = "Musical Sprite/Skill", order = 2)]
public class SkillSO : ScriptableObject
{
    [Header("技能标识")]
    public string skillId;            // 与 Excel 中的 skillId 对应
    public string displayName;
    [TextArea(2, 4)]
    public string description;

    [Header("消耗")]
    [Tooltip("仅 Active 需要：释放所需能量（= 角色能量上限）。其余填 0")]
    public float energyCost = 0f;

    [Header("输入方式（完全手动释放）")]
    [Tooltip("释放该技能所需的按键序列；玩家按对此序列才触发，每按对一个角色亮闪一次")]
    public SkillInputStep[] inputSequence = new SkillInputStep[] { SkillInputStep.Left, SkillInputStep.Left, SkillInputStep.Left };
    [Tooltip("序列时间窗（秒）：超过此时间未补全则重置进度")]
    public float inputWindow = 1.5f;

    [Header("附魔期（大狗叫等）")]
    [Tooltip("释放后把接下来出现的多少个音符附魔（只要有节点都算）。无时间兜底，全消费才结算")]
    public int charmedNoteCount = 6;
    [Tooltip("true = 只要不是 MISS 就算完成；false = 命中/PASS 都算")]
    public bool onlyCountNonMiss = true;

    [Header("释放效果")]
    [Tooltip("结算时按 完成数 × 该值 降低对方连击数")]
    public int reduceComboPerCharmedNote = 3;
    [Tooltip("角色变大+持续发光时的发光颜色")]
    public Color releaseGlow = new Color(1f, 0.85f, 0.2f);
    [Tooltip("音波 VFX 预制体（可选，留空则用占位方块）")]
    public GameObject shockwavePrefab;
    [Tooltip("释放音效（可选）")]
    public AudioClip releaseSfxClip;
    [Tooltip("屏幕震动幅度（0=关闭）")]
    public float screenShakeAmp = 0.3f;

    [Header("效果（P3 实现）")]
    [Tooltip("效果路由键，例如 Heal / Buff / Shield")]
    public string effectType = "None";
    [TextArea(2, 6)]
    [Tooltip("自由 JSON 参数，P3 解析")]
    public string effectParamsJSON = "{}";

    // ---- 运行时索引：skillId -> SkillSO ----
    private static Dictionary<string, SkillSO> _byId;

    /// <summary>从 Resources/Skills 下加载所有 SkillSO 并建立 skillId 索引。</summary>
    public static void RebuildIndex()
    {
        _byId = new Dictionary<string, SkillSO>();
        var all = Resources.LoadAll<SkillSO>("Skills");
        foreach (var s in all)
            if (!string.IsNullOrEmpty(s.skillId))
                _byId[s.skillId] = s;
    }

    /// <summary>按 skillId 反查 SkillSO（文档/Excel 引用用）。</summary>
    public static SkillSO FindById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId == null) RebuildIndex();
        _byId.TryGetValue(id, out var s);
        return s;
    }

    /// <summary>把输入按键映射成技能输入步骤（供 SkillInputUI 比较）。
    /// 设计：A/B/C 与 ←/↓/→ 互通，都映射到 Left/Down/Right（Player Skill 输入的"一个手指"语义）。</summary>
    public static SkillInputStep KeyToStep(KeyCode k)
    {
        switch (k)
        {
            case KeyCode.LeftArrow:
            case KeyCode.A:
                return SkillInputStep.Left;
            case KeyCode.DownArrow:
            case KeyCode.S:
            case KeyCode.B:
                return SkillInputStep.Down;
            case KeyCode.RightArrow:
            case KeyCode.D:
            case KeyCode.C:
                return SkillInputStep.Right;
            case KeyCode.UpArrow:
            case KeyCode.W:
                return SkillInputStep.Up;
            case KeyCode.Space:
                return SkillInputStep.Space;
            default: return SkillInputStep.Left;
        }
    }

    /// <summary>把角色文档「输入方式」字符串解析为输入序列。同时支持箭头脑格（←↓→↑）与字母（A/B/C/L/D/R/U/S/空格）：
    ///   ←/L/A → Left，↓/D/B → Down，→/R/C → Right，↑/U/W → Up，空格/S → Space。
    /// 空串返回空数组（被动技能无输入方式）。</summary>
    public static SkillInputStep[] ParseInputMethod(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return System.Array.Empty<SkillInputStep>();
        var list = new System.Collections.Generic.List<SkillInputStep>();
        foreach (char ch in s)
        {
            switch (ch)
            {
                case '←': case 'L': case 'A': list.Add(SkillInputStep.Left); break;
                case '↓': case 'D': case 'B': list.Add(SkillInputStep.Down); break;
                case '→': case 'R': case 'C': list.Add(SkillInputStep.Right); break;
                case '↑': case 'U': case 'W': list.Add(SkillInputStep.Up); break;
                case ' ': case 'S': list.Add(SkillInputStep.Space); break;
                default: break;
            }
        }
        return list.ToArray();
    }
}
