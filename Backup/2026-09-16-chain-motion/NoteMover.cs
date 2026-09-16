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

    [Header("换皮")]
    [Tooltip("普通点击音符贴图；留空则尝试从 NoteSpriteLibrary.tap 读取。仅作用于普通点击。")]
    public Texture2D tapTexture;
    [Tooltip("跨轨点击音符贴图（Note_Wide.png）；留空则尝试从 NoteSpriteLibrary.wide 读取。仅作用于跨轨点击。")]
    public Texture2D wideTexture;
    [Tooltip("可选成品材质（需 URP Unlit/Transparent + _BaseMap）。留空则运行时用贴图自动生成。")]
    public Material tapNoteMaterial;

    [Header("普通点击视觉（仅普通点击，不影响其他类型与判定窗口）")]
    [Tooltip("普通点击额外整体缩放倍数（在贴图比例基础上再乘）；默认 1.0 = 纯按贴图比例，不改。只缩视觉，不改 noteRadius，判定窗口不变。")]
    public float tapVisualScaleMul = 1f;
    [Tooltip("普通点击贴图整体不透明度（0~1）；需配合透明背景 PNG 才有半透效果，否则只是把黄底卡片调淡。")]
    public float tapAlpha = 1f;
    [Tooltip("普通点击圆角半径（0~0.5，0.5=半圆/胶囊）。仅影响普通点击使用的圆角网格，不影响其他音符类型。")]
    [Range(0.05f, 0.5f)]
    public float tapCornerRadius = 0.35f;

    private bool textured = false;                 // 当前音符是否启用卡通换皮
    private Color visibleBaseColor = Color.black; // 可见态基础色：换皮=白（显贴图），否则=黑
    private float visibleAlpha = 1f;               // 可见态不透明度（换皮时 = tapAlpha）
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

    /// <summary>普通点击专用圆角网格（圆角更大、更圆润，与通用 RoundedRectMesh 独立）。</summary>
    private static Mesh _tapRoundedRectMesh;
    private static float _tapCornerRadiusCache = -1f;
    public static Mesh TapRoundedRectMesh(float cornerRadius)
    {
        if (_tapRoundedRectMesh == null || Mathf.Abs(cornerRadius - _tapCornerRadiusCache) > 0.001f)
        {
            _tapRoundedRectMesh = CreateRoundedRectMesh(cornerRadius, 3); // 更多分段让大圆角更平滑
            _tapCornerRadiusCache = cornerRadius;
        }
        return _tapRoundedRectMesh;
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
        // UV：把 2D 轮廓 (x,y∈[-0.5,0.5]) 映射到 (0..1)，使贴图完整铺在平板上（换皮用；不影响原黑底表现）。
        var uvs = new Vector2[vertices.Length];
        for (int i = 0; i < count; i++)
        {
            uvs[i] = new Vector2(outline[i].x + 0.5f, outline[i].y + 0.5f);
            uvs[count + i] = uvs[i];
        }
        uvs[topCenter] = new Vector2(0.5f, 0.5f);
        uvs[bottomCenter] = new Vector2(0.5f, 0.5f);
        mesh.uv = uvs;
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

        // 普通点击换皮判定：仅「普通点击」(非小点击 / 非连点 / 单轨) 且能取到贴图时启用卡通贴图。
        bool isPlainTap = !isSmallTap && !isChainTap && laneSpan <= 1;
        bool isWideTap = !isSmallTap && !isChainTap && laneSpan > 1;   // 跨轨点击

        Texture2D tapTex = tapTexture;
        if (tapTex == null && NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.tap != null)
            tapTex = NoteSpriteLibrary.Instance.tap.texture;
        Texture2D wideTex = wideTexture;
        if (wideTex == null && NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.wide != null)
            wideTex = NoteSpriteLibrary.Instance.wide.texture;

        textured = (isPlainTap && tapTex != null) || (isWideTap && wideTex != null);
        Texture2D activeTex = isWideTap ? wideTex : tapTex;          // 实际用于渲染的贴图

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

        // 运行时替换预制体网格：普通点击且换皮时用大圆角平板当画布，连轨点击也用圆角平板；其余用圆柱。
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = (laneSpan > 1 || textured) ? (isPlainTap ? TapRoundedRectMesh(tapCornerRadius) : RoundedRectMesh) : CylinderMesh;

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
        // 普通点击方案 A：按贴图宽高比定 X/Z 几何（消除拉伸黄边），再乘 tapVisualScaleMul 等比微调（XZ 同乘，不破坏比例）。
        // 贴图竖向（高>宽）时 texRatio<1 → X 自然收窄成胶囊；Z 保持原长。
        if (isPlainTap && tapTex != null)
        {
            float texRatio = (float)tapTex.width / Mathf.Max(1f, tapTex.height); // 宽/高
            xDiameter = zDiameter * texRatio;          // X 跟随贴图比例
            float s = tapVisualScaleMul;               // 等比缩放 X/Z，避免再次压扁
            transform.localScale = new Vector3(xDiameter * s, 0.12f, zDiameter * s);
        }
        noteHalfSize = noteRadius; // 圆柱 X 方向半长即半径
        // hitPoint 表示音符中心高度；音符底面贴地时中心应为自身高度的一半。
        rideY = hitPos.y;

        rend = GetComponentInChildren<MeshRenderer>();
        if (rend != null)
        {
            if (textured)
            {
                // 卡通换皮：用成品材质或运行时生成的 URP Unlit 透明材质贴爪印；副本互不影响。
                noteMaterial = tapNoteMaterial != null ? new Material(tapNoteMaterial) : MakeTapMaterial(activeTex);
                visibleBaseColor = Color.white;
                visibleAlpha = tapAlpha;
            }
            else
            {
                // 复制一份材质实例，避免影响其他音符（保持原黑底表现，其他音符类型不受影响）
                noteMaterial = new Material(rend.material);
                visibleBaseColor = Color.black;
                visibleAlpha = 1f;
            }
            rend.material = noteMaterial;
            originalColor = visibleBaseColor;
            SetNoteTint(visibleBaseColor);   // 初始色（rend 仍 disabled，过粉杠后再显形）
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
        Color baseC = GetNoteTint();

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float s = Mathf.Sin(k * Mathf.PI);                                  // 0→1→0 弹跳
            transform.localScale = Vector3.Lerp(baseS, peakS, s);
            if (noteMaterial != null)
            {
                // 短暂提亮（向纯白靠拢 60%），衰减回原进度色
                SetNoteTint(Color.Lerp(baseC, Color.white, s * 0.6f));
            }
            yield return null;
        }
        // 结束：缩放回到原始大小，颜色保留 SetChainBodyColor 的进度色
        transform.localScale = baseS;
        SetNoteTint(baseC);
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
        Color startColor = GetNoteTint();

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            if (noteMaterial != null)
            {
                Color c = Color.Lerp(startColor, Color.white, t);
                c.a = 1f - t;
                SetNoteTint(c);
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
        SetNoteTint(color);
    }

    private IEnumerator HitCoroutine(string rank)
    {
        float duration = 0.25f;
        float timer = 0f;

        // 第一阶段：变黑（强调被击中）
        if (noteMaterial != null)
        {
            SetNoteTint(Color.black);
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
                SetNoteTint(Color.Lerp(Color.white, new Color(1f, 1f, 1f, 0f), t));
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
                Color c = GetNoteTint();
                c.a = Mathf.Lerp(1f, 0f, t);
                SetNoteTint(c);
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

    /// <summary>
    /// 被主动技能清除时的表现：变大 → 发白 → 消失（与命中"变黑→白→放大"区分，直接视为最佳击中）。
    /// 关键：若音符原本 isVisible=false（未过粉杠），rend.enabled 被 Init 置 false，这里必须重新打开，
    /// 否则变大/发白/淡出全程在不可见状态下进行——用户看不到清除表现。
    /// </summary>
    public void PlaySkillClear()
    {
        if (animationPlaying) { StopAllCoroutines(); }
        animationPlaying = true;
        StartCoroutine(SkillClearCoroutine());
    }

    private IEnumerator SkillClearCoroutine()
    {
        const float duration = 0.3f;
        float timer = 0f;
        // 清屏前确保可见（音符原本可能尚未过粉杠，rend.enabled=false）
        if (rend != null) rend.enabled = true;
        Vector3 startScale = transform.localScale;
        Vector3 endScale = startScale * 1.5f;   // 变大
        Color startColor = GetNoteTint();

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            if (noteMaterial != null)
            {
                // 发白并淡出
                Color c = Color.Lerp(startColor, Color.white, t);
                c.a = 1f - t;
                SetNoteTint(c);
            }
            transform.localScale = Vector3.Lerp(startScale, endScale, Mathf.Sin(t * Mathf.PI * 0.5f));
            yield return null;
        }

        Destroy(gameObject);
    }

    private void SetAlpha(float alpha)
    {
        if (noteMaterial == null) return;

        Color c = visibleBaseColor;
        c.a = visibleAlpha * alpha;
        SetNoteTint(c);

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

    /// <summary>统一着色：URP Unlit 用 _BaseColor（Material.color 不生效），旧材质用 _Color，两者兼容。</summary>
    private void SetNoteTint(Color c)
    {
        if (noteMaterial == null) return;
        if (noteMaterial.HasProperty("_BaseColor")) noteMaterial.SetColor("_BaseColor", c);
        else noteMaterial.color = c;
    }

    private Color GetNoteTint()
    {
        if (noteMaterial == null) return Color.white;
        return noteMaterial.HasProperty("_BaseColor") ? noteMaterial.GetColor("_BaseColor") : noteMaterial.color;
    }

    /// <summary>运行时生成普通点击的卡通材质：URP Unlit + Transparent + 贴图（绕开自定义 shader 在 URP 下加载失败的根因）。</summary>
    private Material MakeTapMaterial(Texture2D tex)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null)
        {
            Debug.LogWarning("[NoteMover] URP Unlit shader not found, falling back to Unlit/Transparent");
            sh = Shader.Find("Unlit/Transparent");
        }
        var mat = new Material(sh);
        string texProp = mat.HasProperty("_BaseMap") ? "_BaseMap" : (mat.HasProperty("_MainTex") ? "_MainTex" : "_BaseMap");
        mat.SetTexture(texProp, tex);
        string colProp = mat.HasProperty("_BaseColor") ? "_BaseColor" : (mat.HasProperty("_Color") ? "_Color" : "_BaseColor");
        mat.SetColor(colProp, Color.white);
        // 强制透明渲染：贴图已有 Alpha 时，材质必须声明为 Transparent 才会正确混合。
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f); // 1 = Alpha
        if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return mat;
    }
}
