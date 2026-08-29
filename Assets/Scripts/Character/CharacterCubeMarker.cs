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
    [Tooltip("GrowGlow() 起手闪烁时的发光颜色")]
    public Color releaseGlow = new Color(1f, 0.85f, 0.2f);

    [Header("命中跳跃（P3）")]
    [Tooltip("Jump() 跳跃峰值高度（本地坐标单位）")]
    public float jumpHeight = 0.6f;
    [Tooltip("Jump() 跳跃总时长（秒）")]
    public float jumpDuration = 0.3f;

    private Vector3 baseScale;
    private Vector3 baseLocalPos;
    private Coroutine flashCo;
    private Coroutine glowCo;
    private Coroutine pulseCo;
    private Coroutine jumpCo;
    private Color sleepBaseColor = Color.white;
    private bool sleepVisualOn = false;
    [Tooltip("沉睡时方块缩放（相对 baseScale）；睡眠异常(表现)时角色方块变小")]
    public float sleepShrinkScale = 0.6f;

    /// <summary>按 (side, lane) 索引的全局角色标记表，供普通命中时按音轨查找对应角色跳跃。</summary>
    private static System.Collections.Generic.Dictionary<int, CharacterCubeMarker> Registry = new System.Collections.Generic.Dictionary<int, CharacterCubeMarker>();
    private static int RegKey(int side, int lane) => side * 4 + lane;
    /// <summary>按 side/lane 取得对应音轨角色标记（无则返回 null）。</summary>
    public static CharacterCubeMarker GetAt(int side, int lane)
    {
        Registry.TryGetValue(RegKey(side, lane), out var m);
        return m;
    }

    void Awake()
    {
        baseScale = transform.localScale;
        baseLocalPos = transform.localPosition;
        Registry[RegKey(side, laneIndex)] = this;
    }

    void OnDestroy()
    {
        Registry.Remove(RegKey(side, laneIndex));
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
        // 起手先快速闪烁（提亮）一下，对应「闪烁变大」的"闪烁"阶段；闪完即熄灭。
        SetEmission(releaseGlow * 2.6f);
        float blink = 0.15f;
        float tb = 0f;
        while (tb < blink)
        {
            tb += Time.deltaTime;
            yield return null;
        }
        SetEmission(Color.black);  // 闪烁结束：进入"只变大、不持续发光"状态

        // 附魔期间平时不发光，只保持变大；命中闪烁由 PulseGlow 负责。
        float t = 0f;
        float dur = 0.15f;
        Vector3 from = transform.localScale;
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
        transform.localScale = sleepVisualOn ? baseScale * sleepShrinkScale : baseScale;
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

    /// <summary>
    /// 普通音符命中时，对应音轨角色向上跳一下（仅本地位移 hop，不影响 scale）。
    /// 释放主动技能期间由 BattleVisualsController 屏蔽调用。
    /// </summary>
    public void Jump()
    {
        if (!isActiveAndEnabled) return;
        if (jumpCo != null) StopCoroutine(jumpCo);
        jumpCo = StartCoroutine(JumpCo());
    }

    private System.Collections.IEnumerator JumpCo()
    {
        float t = 0f;
        while (t < jumpDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / jumpDuration);
            float h = Mathf.Sin(k * Mathf.PI) * jumpHeight;   // 0 -> 峰 -> 0 的抛物线 hop
            transform.localPosition = baseLocalPos + Vector3.up * h;
            yield return null;
        }
        transform.localPosition = baseLocalPos;
        jumpCo = null;
    }

    /// <summary>沉睡视觉：on=true 时方块变灰 + 熄灯；on=false 时恢复身份色（沉睡解除）。</summary>
    public void ApplySleepVisual(bool on)
    {
        var rend = GetComponent<Renderer>();
        if (rend == null || rend.material == null) return;
        if (on)
        {
            if (glowCo != null) StopCoroutine(glowCo);
            sleepBaseColor = rend.material.color;
            rend.material.color = new Color(0.35f, 0.35f, 0.35f); // 灰：沉睡
            SetEmission(Color.black);                              // 黑灯
            transform.localScale = baseScale * sleepShrinkScale;   // 变小
            sleepVisualOn = true;
        }
        else if (sleepVisualOn)
        {
            if (glowCo != null) StopCoroutine(glowCo);
            rend.material.color = sleepBaseColor;
            SetEmission(Color.black);
            transform.localScale = baseScale;                      // 恢复
            sleepVisualOn = false;
        }
    }
}
