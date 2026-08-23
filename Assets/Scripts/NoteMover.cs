using UnityEngine;
using System.Collections;

/// <summary>
/// 控制音符从生成点沿轨道移动到判定线并穿过判定线，越过中线后变为可见。
/// 命中时：变黑方块 → 白方块 → 变大 → 消失。
/// Miss 时：音符完全穿过判定线（leftband）后变小消失。
/// </summary>
[RequireComponent(typeof(Note))]
public class NoteMover : MonoBehaviour
{
    private Note note;
    private Conductor conductor;
    private BattleCenterLine centerLine;

    private Vector3 spawnPos;
    private Vector3 hitPos;
    private Vector3 exitPos;
    private float hitTime;
    private float leadTime;
    private float startTime;
    private float exitLeadTime; // 从生成到完全穿过判定线所需时间
    private float noteHalfSize;
    private float rideY; // 音符贴着判定线飞行时的 Y 高度

    private MeshRenderer rend;
    private Material noteMaterial;
    private Color originalColor;

    private bool animationPlaying = false;

    /// <summary>音符是否已经完整穿过判定线。</summary>
    public bool hasFullyPassed { get; private set; }

    public Vector3 HitPosition => hitPos;

    /// <summary>音符在移动方向上的边长（按 X 轴 scale 计）。</summary>
    public float NoteEdgeLength => transform.localScale.x;

    [Tooltip("判定线（hit line cube）在移动方向上的半厚度")]
    public float judgeLineHalfThickness = 0.05f;

    /// <summary>当前音符是否与判定线发生了几何重叠。</summary>
    public bool IsOverlappingHitLine()
    {
        return Mathf.Abs(transform.position.x - hitPos.x) <= noteHalfSize + judgeLineHalfThickness;
    }

    /// <summary>音符中心到判定线中心的 X 轴距离。</summary>
    public float CenterDistanceToHit()
    {
        return Mathf.Abs(transform.position.x - hitPos.x);
    }

    public void Init(Vector3 spawnPos, Vector3 hitPos, float hitTime, float leadTime,
                     Conductor conductor, BattleCenterLine centerLine)
    {
        this.spawnPos = spawnPos;
        this.hitPos = hitPos;
        this.hitTime = hitTime;
        this.leadTime = leadTime;
        this.conductor = conductor;
        this.centerLine = centerLine;

        note = GetComponent<Note>();
        note.hitTime = hitTime;
        startTime = hitTime - leadTime;

        // 计算越过判定线的终点（再往前 1.5 个单位，保证能完全穿过）
        Vector3 moveDir = (hitPos - spawnPos).normalized;
        exitPos = hitPos + moveDir * 1.5f;

        // 音符应在 hitTime 时到达 hitPos，之后继续滑向 exitPos
        float hitDistance = Vector3.Distance(spawnPos, hitPos);
        float exitDistance = hitDistance + 1.5f;
        exitLeadTime = hitDistance > 0.001f ? leadTime * (exitDistance / hitDistance) : leadTime + 0.5f;

        // 压扁为薄板，使音符高度与判定线一致（X/Z 不变，仅降低 Y）
        transform.localScale = new Vector3(0.6f, 0.12f, 0.6f);
        noteHalfSize = transform.localScale.x * 0.5f;
        // 判定线中心在 hitPos.y - 0.05 处，音符贴着它飞行
        rideY = hitPos.y - 0.05f;

        rend = GetComponentInChildren<MeshRenderer>();
        if (rend != null)
        {
            // 复制一份材质实例，避免影响其他音符
            noteMaterial = new Material(rend.material);
            rend.material = noteMaterial;
            originalColor = Color.black;
            noteMaterial.color = Color.black;
            // 初始不可见：越过粉杠前完全隐藏
            rend.enabled = false;
        }
    }

    void Update()
    {
        if (conductor == null || animationPlaying) return;

        // 按歌曲时间做线性插值：从生成点 -> 判定线 -> 穿出
        float t = (conductor.songPosition - startTime) / exitLeadTime;
        t = Mathf.Clamp01(t);
        Vector3 pos = Vector3.Lerp(spawnPos, exitPos, t);
        pos.y = rideY; // 让音符与判定线同高
        transform.position = pos;

        // 过中线后变为可见
        float centerX = centerLine != null ? centerLine.currentX : 0f;
        bool shouldBeVisible = false;

        if (note.side == 0) // 左玩家：从右向左飞，X < 中线时进入自己半场
        {
            shouldBeVisible = transform.position.x < centerX;
        }
        else // 右玩家：从左向右飞，X > 中线时进入自己半场
        {
            shouldBeVisible = transform.position.x > centerX;
        }

        if (shouldBeVisible && !note.isVisible)
        {
            note.isVisible = true;
            if (rend != null) rend.enabled = true;
            SetAlpha(1f);
        }

        // 检测是否已完全穿过判定线：必须后缘也越过判定线，才算“彻底穿过”
        if (!hasFullyPassed)
        {
            float noteBack = note.side == 0
                ? transform.position.x + noteHalfSize   // 向左飞，后缘是右侧
                : transform.position.x - noteHalfSize;  // 向右飞，后缘是左侧

            if (note.side == 0)
                hasFullyPassed = noteBack < hitPos.x;
            else
                hasFullyPassed = noteBack > hitPos.x;
        }
    }

    /// <summary>
    /// 播放命中反馈：变黑 → 白 → 变大 → 消失。
    /// </summary>
    public void PlayHitAnimation(string rank)
    {
        if (animationPlaying) return;
        animationPlaying = true;
        StopAllCoroutines();
        StartCoroutine(HitCoroutine(rank));
    }

    /// <summary>
    /// 播放 Miss 消失动画。
    /// </summary>
    public void PlayMissAnimation()
    {
        if (animationPlaying) return;
        animationPlaying = true;
        StopAllCoroutines();
        StartCoroutine(MissCoroutine());
    }

    private IEnumerator HitCoroutine(string rank)
    {
        float duration = 0.25f;
        float timer = 0f;

        // 第一阶段：变黑（强调被击中）
        if (noteMaterial != null)
        {
            noteMaterial.color = Color.black;
        }

        yield return new WaitForSeconds(0.05f);

        // 第二阶段：变白并放大
        Vector3 startScale = transform.localScale;
        Vector3 endScale = startScale * 1.4f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = timer / duration;

            if (noteMaterial != null)
            {
                noteMaterial.color = Color.Lerp(Color.white, new Color(1f, 1f, 1f, 0f), t);
            }

            transform.localScale = Vector3.Lerp(startScale, endScale, Mathf.Sin(t * Mathf.PI * 0.5f));
            yield return null;
        }

        Destroy(gameObject);
    }

    private IEnumerator MissCoroutine()
    {
        float duration = 0.15f;
        float timer = 0f;
        Vector3 startScale = transform.localScale;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = timer / duration;

            if (noteMaterial != null)
            {
                Color c = Color.Lerp(Color.black, new Color(0.2f, 0.2f, 0.2f, 0f), t);
                noteMaterial.color = c;
            }

            transform.localScale = Vector3.Lerp(startScale, startScale * 0.3f, t);
            yield return null;
        }

        Destroy(gameObject);
    }

    private void SetAlpha(float alpha)
    {
        if (noteMaterial == null) return;

        Color c = Color.black;
        c.a = alpha;
        noteMaterial.color = c;

        if (rend != null && note.isVisible)
        {
            rend.enabled = alpha > 0.01f;
        }
    }
}
