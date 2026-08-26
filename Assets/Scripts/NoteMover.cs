using UnityEngine;
using System.Collections;

/// <summary>
/// 控制点击音符从生成点沿轨道移动到判定线并穿过判定线，越过中线后变为可见。
/// 普通点击使用圆柱，连轨点击使用带圆角的扁平矩形。
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
    private bool isChainTap = false;
    private TextMesh chainCountText;
    private MeshRenderer chainCountRenderer;
    private float chainTapDeadline = -1f;
    private float chainTapHoldDuration = 0.4f;

    public float ChainTapDeadline => chainTapDeadline;

    public void SetChainTapHoldDuration(float seconds)
    {
        chainTapHoldDuration = Mathf.Max(0.05f, seconds);
    }

    private bool animationPlaying = false;
    private bool missing = false;     // 已进入漏击缩小状态
    private bool missComplete = false; // 缩小动画结束，可出 MISS

    // 普通点击沿用圆柱；连轨点击使用带圆角的扁平矩形网格。
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

    private static Mesh _roundedRectMesh;
    public static Mesh RoundedRectMesh
    {
        get
        {
            if (_roundedRectMesh == null)
                _roundedRectMesh = CreateRoundedRectMesh(0.18f, 2);
            return _roundedRectMesh;
        }
    }

    private static Mesh CreateRoundedRectMesh(float radius, int cornerSegments)
    {
        var outline = new System.Collections.Generic.List<Vector2>();
        Vector2[] centers =
        {
            new Vector2(0.5f - radius, 0.5f - radius),
            new Vector2(-0.5f + radius, 0.5f - radius),
            new Vector2(-0.5f + radius, -0.5f + radius),
            new Vector2(0.5f - radius, -0.5f + radius)
        };

        // 顺时针轮廓，便于从上方看到带圆角的正面。
        for (int corner = 0; corner < centers.Length; corner++)
        {
            float start = corner * 90f;
            for (int i = 0; i <= cornerSegments; i++)
            {
                float angle = (start + i * 90f / cornerSegments) * Mathf.Deg2Rad;
                outline.Add(centers[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }

        int count = outline.Count;
        var vertices = new Vector3[count * 2 + 2];
        for (int i = 0; i < count; i++)
        {
            vertices[i] = new Vector3(outline[i].x, 0.5f, outline[i].y);
            vertices[count + i] = new Vector3(outline[i].x, -0.5f, outline[i].y);
        }
        int topCenter = count * 2;
        int bottomCenter = topCenter + 1;
        vertices[topCenter] = new Vector3(0f, 0.5f, 0f);
        vertices[bottomCenter] = new Vector3(0f, -0.5f, 0f);

        var triangles = new System.Collections.Generic.List<int>(count * 12);
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            // 顶面、底面各自反向，确保双面都能被看到。
            triangles.Add(topCenter); triangles.Add(next); triangles.Add(i);
            triangles.Add(bottomCenter); triangles.Add(count + i); triangles.Add(count + next);
            // 侧面
            triangles.Add(i); triangles.Add(next); triangles.Add(count + i);
            triangles.Add(next); triangles.Add(count + next); triangles.Add(count + i);
        }

        var mesh = new Mesh { name = "RoundedFlatNote" };
        mesh.vertices = vertices;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
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
                     Conductor conductor, BattleCenterLine centerLine, float noteRadius = 0.45f,
                     bool isSmallTap = false, int laneSpan = 1, float laneSpacing = 1f,
                     bool isChainTap = false, int chainTapCount = 0, float chainTapHoldDuration = 0.4f)
    {
        this.spawnPos = spawnPos;
        this.hitPos = hitPos;
        this.hitTime = hitTime;
        this.leadTime = leadTime;
        this.conductor = conductor;
        this.centerLine = centerLine;
        this.isChainTap = isChainTap;
        this.chainTapHoldDuration = Mathf.Max(0.05f, chainTapHoldDuration);
        chainTapDeadline = -1f;

        // 小型点击音符：半径只有基础 Tap 的 65%
        if (isSmallTap) noteRadius *= 0.65f;
        this.noteRadius = noteRadius;

        note = GetComponent<Note>();
        note.hitTime = hitTime;
        note.isSmallTap = isSmallTap;
        note.isChainTap = isChainTap;
        note.chainTapRequired = isChainTap ? Mathf.Max(1, chainTapCount) : 0;
        note.chainTapRemaining = note.chainTapRequired;
        startTime = hitTime - leadTime;

        // 计算越过判定线的终点（再往前 1.5 个单位，保证能完全穿过）
        Vector3 moveDir = (hitPos - spawnPos).normalized;
        exitPos = hitPos + moveDir * 1.5f;

        // 音符应在 hitTime 时到达 hitPos，之后继续滑向 exitPos
        float hitDistance = Vector3.Distance(spawnPos, hitPos);
        float exitDistance = hitDistance + 1.5f;
        exitLeadTime = hitDistance > 0.001f ? leadTime * (exitDistance / hitDistance) : leadTime + 0.5f;

        // 运行时替换预制体网格：普通点击是圆柱，连轨点击是圆角扁平矩形。
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = laneSpan > 1 ? RoundedRectMesh : CylinderMesh;

        // 移除可能存在的碰撞体，音符不需要物理
        Collider col = GetComponent<Collider>();
        if (col != null) Destroy(col);

        // 圆柱基准直径为 1；圆角矩形网格基准宽深也为 1。
        float radiusScale = noteRadius / 0.5f;
        // 连轨音符沿 Z 横跨两轨，沿 X 的判定半径仍与普通点击一致。
        float zDiameter = laneSpan > 1
            ? laneSpacing * (laneSpan - 1) + noteRadius * 2f
            : radiusScale;
        float xDiameter = laneSpan > 1 ? noteRadius * 2f : radiusScale;
        transform.localScale = new Vector3(xDiameter, 0.12f, zDiameter);
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

        if (isChainTap)
            CreateChainCountText(Mathf.Max(1, chainTapCount));
    }

    void Update()
    {
        if (conductor == null || animationPlaying) return;

        // 连点命中后停在判定线处，直到下一次命中或超时 Miss。
        // 超时后仍保持停留，等待 NoteSpawner 触发统一的 Miss 缩小动画。
        if (isChainTap && chainTapDeadline >= 0f)
        {
            transform.position = new Vector3(hitPos.x, rideY, hitPos.z);
            return;
        }

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
            if (chainCountRenderer != null) chainCountRenderer.enabled = true;
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

    public void RegisterChainTapHit(int remaining, int required, float songTime)
    {
        if (!isChainTap || animationPlaying) return;

        chainTapDeadline = songTime + chainTapHoldDuration;
        float progress = 1f - Mathf.Clamp01(remaining / (float)Mathf.Max(1, required));
        SetChainBodyColor(Color.Lerp(Color.black, Color.white, progress));
        if (chainCountText != null) chainCountText.text = remaining.ToString();
        transform.position = new Vector3(hitPos.x, rideY, hitPos.z);
        // 每次命中叠加一次「白闪 + 放大缩小」反馈（不破坏 SetChainBodyColor 的进度色，淡出后回归原进度色）。
        // 用户 2026-08-26 反馈：连点音符原本没有变大变白的命中反馈，特此补上。
        StartCoroutine(ChainTapHitPopCo());
    }

    /// <summary>连点命中反馈：scale 1x→1.3x→1x + 颜色短暂提亮，回落到 SetChainBodyColor 设的进度色。</summary>
    private IEnumerator ChainTapHitPopCo()
    {
        const float dur = 0.18f;
        float t = 0f;
        Vector3 baseS = transform.localScale;
        Vector3 peakS = baseS * 1.3f;
        Color baseC = noteMaterial != null ? noteMaterial.color : Color.white;

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float s = Mathf.Sin(k * Mathf.PI);                                  // 0→1→0 弹跳
            transform.localScale = Vector3.Lerp(baseS, peakS, s);
            if (noteMaterial != null)
            {
                // 短暂提亮（向纯白靠拢 60%），衰减回原进度色
                noteMaterial.color = Color.Lerp(baseC, Color.white, s * 0.6f);
            }
            yield return null;
        }
        // 结束：缩放回到原始大小，颜色保留 SetChainBodyColor 的进度色
        transform.localScale = baseS;
        if (noteMaterial != null) noteMaterial.color = baseC;
    }

    public void CompleteChainTap()
    {
        if (!isChainTap || animationPlaying) return;
        chainTapDeadline = -1f;
        animationPlaying = true;
        StopAllCoroutines();
        StartCoroutine(ChainClearCoroutine());
    }

    private System.Collections.IEnumerator ChainClearCoroutine()
    {
        const float duration = 0.6f;
        float timer = 0f;
        Vector3 startScale = transform.localScale;
        Vector3 endScale = startScale * 1.15f;
        Color startColor = noteMaterial != null ? noteMaterial.color : Color.black;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            if (noteMaterial != null)
            {
                Color c = Color.Lerp(startColor, Color.white, t);
                c.a = 1f - t;
                noteMaterial.color = c;
            }
            if (chainCountText != null)
            {
                Color c = chainCountText.color;
                c.a = 1f - t;
                chainCountText.color = c;
            }
            transform.localScale = Vector3.Lerp(startScale, endScale, Mathf.Sin(t * Mathf.PI * 0.5f));
            yield return null;
        }

        Destroy(gameObject);
    }

    private void CreateChainCountText(int count)
    {
        GameObject textGo = new GameObject("ChainTapCount");
        textGo.transform.SetParent(transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        // TextMesh 默认朝 +Z。这里从音符底面方向朝上放置文字，
        // 让字符的上方指向画面上方，避免额外绕法线旋转造成左右镜像。
        textGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        // 只翻转文字面的法线，不改变字符的左右/上下方向，兼容带背面裁剪的字体材质。
        textGo.transform.localScale = new Vector3(1f, 1f, -1f);

        chainCountText = textGo.AddComponent<TextMesh>();
        chainCountText.text = count.ToString();
        chainCountText.anchor = TextAnchor.MiddleCenter;
        chainCountText.alignment = TextAlignment.Center;
        chainCountText.fontSize = 64;
        chainCountText.characterSize = 0.04f;
        chainCountText.color = Color.white;
        chainCountRenderer = textGo.GetComponent<MeshRenderer>();
        if (chainCountRenderer != null) chainCountRenderer.enabled = false;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        chainCountText.font = font;
    }

    private void SetChainBodyColor(Color color)
    {
        if (noteMaterial != null) noteMaterial.color = color;
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
            if (chainCountText != null)
            {
                Color c = chainCountText.color;
                c.a = Mathf.Lerp(1f, 0f, t);
                chainCountText.color = c;
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
        if (chainCountText != null)
        {
            Color textColor = chainCountText.color;
            textColor.a = alpha;
            chainCountText.color = textColor;
            if (chainCountRenderer != null && note.isVisible)
                chainCountRenderer.enabled = alpha > 0.01f;
        }
    }
}
