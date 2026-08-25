using UnityEngine;
using System.Collections;

/// <summary>
/// 占位 cube 标记组件（P2）。挂在角色占位 cube 上：
/// - side 0 = 左侧玩家阵营 / 1 = 右侧对手阵营
/// - laneIndex -1 = 玩家自身角色（NPC）/ 0~3 = 该侧第 N 个音轨上的队伍角色
///
/// FeverVFXPlaceholder 通过 FindObjectsByType&lt;CharacterCubeMarker&gt; 取代 Tag 查找。
/// 场景里临时补 marker：MS Debug 工具 → "为场景 cube 自动补 marker"按钮（按 transform.x 与 laneSpacing 估算）。
///
/// 简易按键反馈：Flash() 让 cube 临时放大（popScale=1.25）后回弹，颜色短暂提亮，
/// 由 BattleVisualsController.OnLanePress 触发，与 LaneIndicator.Flash() 并列。
/// </summary>
public class CharacterCubeMarker : MonoBehaviour
{
    [Tooltip("0 = 左侧玩家阵营 / 1 = 右侧对手阵营")]
    public int side = 0;

    [Tooltip("-1 = 玩家自身角色（不占音轨） / 0~3 = 队伍角色对应音轨")]
    public int laneIndex = -1;

    public bool IsPlayer => laneIndex < 0;

    [Header("按键反馈（P3）")]
    [Tooltip("Flash() 时缩放倍数（弹跳峰值 = base × popScale）")]
    public float popScale = 1.25f;
    [Tooltip("Flash() 时颜色提亮系数（base + identityColor × boostAmount）")]
    [Range(0f, 1f)] public float colorBoost = 0.6f;
    [Tooltip("Flash() 总时长（秒）")]
    public float flashDuration = 0.18f;

    private Vector3 baseScale;
    private Coroutine flashCo;

    void Awake()
    {
        baseScale = transform.localScale;
    }

    /// <summary>
    /// 由 BattleVisualsController 调用：按下该侧对应 lane 时，让这个 cube 弹跳并提亮一下。
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

        // 前半段：放大 + 提亮
        while (t < half)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / half);
            transform.localScale = baseScale * Mathf.Lerp(1f, popScale, k);
            if (rend != null && rend.material != null) rend.material.color = Color.Lerp(baseColor, boostColor, k);
            yield return null;
        }
        // 后半段：回弹 + 颜色还原
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
}
