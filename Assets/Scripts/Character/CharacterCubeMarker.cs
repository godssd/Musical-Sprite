using UnityEngine;
using System.Collections;

/// <summary>
/// 占位 cube 标记组件（P2）。挂在角色占位 cube 上：
/// - side 0 = 左侧玩家阵营 / 1 = 右侧对手阵营
/// - laneIndex -1 = 玩家自身角色（NPC）/ 非负数 = 该侧对应音轨上的队伍角色
///
/// FeverVFXPlaceholder 通过 FindObjectsByType&lt;CharacterCubeMarker&gt; 取代 Tag 查找。
/// 新场景在角色创建时直接挂 Marker；旧场景由 CharacterBattleSystem 按乐队层级迁移。
///
/// 简易按键反馈：Flash() 让 cube 临时放大（popScale=1.25）后回弹，颜色短暂提亮，
/// 由 BattleVisualsController.OnLanePress 触发，与 LaneIndicator.Flash() 并列。
///
/// 主动技能释放表现（大狗叫等）：GrowGlow() 让角色变大 + 持续发光；ShrinkUnglow() 缩回并熄灭。
/// </summary>
public class CharacterCubeMarker : MonoBehaviour
{
    [Tooltip("0 = 左侧玩家阵营 / 1 = 右侧对手阵营")]
    public int side = 0;

    [Tooltip("-1 = 玩家自身角色（不占音轨） / 非负数 = 队伍角色对应音轨")]
    public int laneIndex = -1;

    public bool IsPlayer => laneIndex < 0;

    [Header("按键反馈（P3）")]
    [Tooltip("Flash() 时缩放倍数（弹跳峰值 = base × popScale）")]
    public float popScale = 1.25f;
    [Tooltip("Flash() 时颜色提亮系数（base + identityColor × boostAmount）")]
    [Range(0f, 1f)] public float colorBoost = 0.6f;
    [Tooltip("Flash() 总时长（秒）")]
    public float flashDuration = 0.18f;

    [Header("释放表现（主动技能）")]
    [Tooltip("GrowGlow() 时放大的倍数")]
    public float growScale = 1.5f;
    [Tooltip("GrowGlow() 持续发光颜色")]
    public Color releaseGlow = new Color(1f, 0.85f, 0.2f);

    private Vector3 baseScale;
    private Coroutine flashCo;
    private Coroutine glowCo;
    private Coroutine pulseCo;

    void Awake()
    {
        baseScale = transform.localScale;
    }

    /// <summary>
    /// 由 BattleVisualsController 调用：按下该侧对应 lane 时，让这个 cube 弹跳并提亮一下。
    /// 也用于主动技能「每按对一个键，对应角色亮闪一次」。
    /// </summary>
    public void Flash()
    {
        if (!isActiveAndEnabled) return;
        if (flashCo != null) StopCoroutine(flashCo);
        flashCo = StartCoroutine(FlashCo());
    }

    private System.Collections.IEnumerator FlashCo()
    {
        float t = 0f;
        float half = flashDuration * 0.5f;
        var rend = GetComponent<Renderer>();
        Color baseColor = (rend != null && rend.material != null) ? rend.material.color : Color.white;
        Color boostColor = baseColor + baseColor * colorBoost;

        while (t < half)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / half);
            transform.localScale = baseScale * Mathf.Lerp(1f, popScale, k);
            if (rend != null && rend.material != null) rend.material.color = Color.Lerp(baseColor, boostColor, k);
            yield return null;
        }
        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / half);
            transform.localScale = baseScale * Mathf.Lerp(popScale, 1f, k);
            if (rend != null && rend.material != null) rend.material.color = Color.Lerp(boostColor, baseColor, k);
            yield return null;
        }

        transform.localScale = baseScale;
        if (rend != null && rend.material != null) rend.material.color = baseColor;
        flashCo = null;
    }

    /// <summary>主动技能释放开始：角色变大 + 持续发光（保持到 ShrinkUnglow）。</summary>
    public void GrowGlow()
    {
        if (!isActiveAndEnabled) return;
        if (flashCo != null) StopCoroutine(flashCo);
        if (glowCo != null) StopCoroutine(glowCo);
        glowCo = StartCoroutine(GrowCo());
    }

    /// <summary>主动技能释放结束：缩回原大小并熄灭发光。</summary>
    public void ShrinkUnglow()
    {
        if (!isActiveAndEnabled) return;
        if (glowCo != null) StopCoroutine(glowCo);
        glowCo = StartCoroutine(ShrinkCo());
    }

    private System.Collections.IEnumerator GrowCo()
    {
        float t = 0f;
        float dur = 0.15f;
        Vector3 from = transform.localScale;
        // 附魔期间「平时不亮」，只保持变大；命中闪烁由 PulseGlow 负责（Issue 1）。
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            transform.localScale = Vector3.Lerp(from, baseScale * growScale, k);
            yield return null;
        }
        transform.localScale = baseScale * growScale;
        glowCo = null;
    }

    private System.Collections.IEnumerator ShrinkCo()
    {
        float t = 0f;
        float dur = 0.15f;
        Vector3 from = transform.localScale;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            transform.localScale = Vector3.Lerp(from, baseScale, k);
            yield return null;
        }
        transform.localScale = baseScale;
        SetEmission(Color.black);
        glowCo = null;
    }

    private void SetEmission(Color c)
    {
        var rend = GetComponent<Renderer>();
        if (rend == null || rend.material == null) return;
        rend.material.EnableKeyword("_EMISSION");
        rend.material.SetColor("_EmissionColor", c);
    }

    /// <summary>
    /// 主动技能"附魔音符命中"时的高亮脉冲：仅短暂提亮发光（emission），不动 scale。
    /// 用于变大/发光中的大狗被命中时闪一下，而不会把放大中的狗缩回原尺寸。
    /// </summary>
    public void PulseGlow()
    {
        if (!isActiveAndEnabled) return;
        if (pulseCo != null) StopCoroutine(pulseCo);
        pulseCo = StartCoroutine(PulseCo());
    }

    private System.Collections.IEnumerator PulseCo()
    {
        // 命中闪烁：在黑色（平时不亮）基础上短暂提亮，再回落到黑色。
        SetEmission(releaseGlow * 2.6f);
        yield return new WaitForSeconds(0.09f);
        SetEmission(Color.black);
        pulseCo = null;
    }
}
