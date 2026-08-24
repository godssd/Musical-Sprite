using UnityEngine;
using System.Collections;

/// <summary>
/// 控制音符（圆柱）从生成点沿轨道移动到判定线并穿过判定线，越过中线后变为可见。
/// 命中时：变黑 → 白 → 放大 → 消失。
/// Miss 时：越过判定线后快速缩小消失（缩小完成才由 NoteSpawner 出 MISS 反馈）。
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
    private float noteRadius = 0.45f; // 圆柱半径（X 方向半长，用于重叠/穿透计算）

    private MeshRenderer rend;
    private Material noteMaterial;
    private Color originalColor;

    private bool animationPlaying = false;
    private bool missing = false;     // 已进入漏击缩小状态
    private bool missComplete = false; // 缩小动画结束，可出 MISS

    // 圆柱网格（共享缓存，运行时把方块换成圆柱，不必重建预制体）
    private static Mesh _cylinderMesh;
    private static Mesh CylinderMesh
    {
        get
        {
            if (_cylinderMesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _cylinderMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(tmp);
            }
            return _cylinderMesh;
        }
    }

    /// <summary>音符是否已经完整穿过判定线。</summary>
    public bool hasFullyPassed { get; private set; }

    /// <summary>音符是否已进入漏击缩小状态（不再可被命中）。</summary>
    public bool IsMissing => missing;

    /// <summary>漏击缩小动画是否已播放完毕（可触发 MISS 反馈并销毁）。</summary>
    public bool MissShrinkComplete => missComplete;

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
                     Conductor conductor, BattleCenterLine centerLine, float noteRadius = 0.45f)
    {
        this.spawnPos = spawnPos;
        this.hitPos = hitPos;
        this.hitTime = hitTime;
        this.leadTime = leadTime;
        this.conductor = conductor;
        this.centerLine = centerLine;
        this.noteRadius = noteRadius;

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

        // 换成圆柱网格（与预制体无关，运行时保证是圆柱）
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = CylinderMesh;

        // 移除可能存在的碰撞体，音符不需要物理
        Collider col = GetComponent<Collider>();
        if (col != null) Destroy(col);

        // 圆柱：Unity 内置圆柱基准半径 0.5、高度 1；缩放到目标半径与薄高度
        // 半径扩大 50%（原方块半宽 0.3 → 0.45），高度与判定线同高
        float radiusScale = noteRadius / 0.5f;
        transform.localScale = new Vector3(radiusScale, 0.12f, radiusScale);
        noteHalfSize = noteRadius; // 圆柱 X 方向半长即半径
        // hitPoint 表示音符中心高度；音符底面贴地时中心应为自身高度的一半。
        rideY = hitPos.y;

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
    /// 进入漏击状态：停止移动、快速缩小，缩小完成前不可被命中。
    /// 缩完由 MissShrinkComplete 暴露，由 NoteSpawner 负责出 MISS 反馈并销毁。
    /// </summary>
    public void BeginMiss()
    {
        if (animationPlaying || missing) return;
        missing = true;
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
        float duration = 0.18f; // 快速变小，但不立刻消失
        float timer = 0f;
        Vector3 startScale = transform.localScale;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = timer / duration;

            if (noteMaterial != null)
            {
                Color c = noteMaterial.color;
                c.a = Mathf.Lerp(1f, 0f, t);
                noteMaterial.color = c;
            }

            // 缩小到 0，配合淡出形成“穿过后快速消失”的反馈
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
            yield return null;
        }

        // 缩小完成：标记由 NoteSpawner 出 MISS 并销毁（不在此直接 Destroy，
        // 避免与判定逻辑争夺对音符生命周期的掌控）
        missComplete = true;
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
