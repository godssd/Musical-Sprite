using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 控制点击音符从生成点沿轨道移动到判定线并穿过判定线。
/// 现在使用 SpriteRenderer 直接显示美术资源，命中反馈为「切 Select + 朝上光束 + 光碎」。
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
    private float exitLeadTime;
    private float noteHalfSize;
    private float rideY;
    private float noteRadius = 0.45f;

    [Header("果冻 / 发光")]
    [Range(0.01f, 1f)] public float noteAlpha = 0.85f;            // 主体半透明（果冻感）
    public float glowScaleMul = 1.2f;                             // 光晕相对主体的放大倍率
    public Color glowColor = new Color(1f, 0.96f, 0.84f, 1f);    // 光晕颜色（暖白，接近效果图）
    [Range(0f, 1f)] public float glowAlpha = 0.35f;               // 光晕强度（克制，避免过曝糊）
    [Range(0.05f, 1f)] public float digitWorldSize = 0.4f;        // Repeat 数字直径（世界单位，Init 内按 visualScaleMul 重算）

    [Header("尺寸 / 显形 / 动画")]
    [Range(0.2f, 1.5f)] public float visualScaleMul = 0.55f;     // 整体缩小到约一半（单轨/跨轨通用基准）
    [Range(0.4f, 1f)] public float laneFit = 0.9f;               // 单轨音符跨轨方向(Z)占轨道比例上限，保证不超轨
    public float revealMargin = 0.05f;                            // 越过粉杠后再延迟一点显形
    public float approachDistance = 1.1f;                         // 距判定线多近开始"微微变长变亮"
    public float approachStretchY = 1.08f;                        // 靠近判定线时沿跨轨方向(Z)微微变长
    public float approachBright = 1.18f;                          // 靠近判定线时亮度
    public float hitScaleMul = 1.15f;                             // 命中整体放大（削弱到约一半，原 1.3）
    public float hitOvershoot = 0.35f;                            // 命中后沿前进方向减速滑行的距离
    public float hitDuration = 0.28f;                             // 命中动画时长
    public float missOvershoot = 0.25f;                           // 漏击时轻微前进距离
    public float missDuration = 0.22f;                            // 漏击消失时长
    public bool flipSmallTapY = true;                             // 小点击音符 flipY（图片反向修正）

    private SpriteRenderer glowRend;                              // 音符光晕（加法）
    private SpriteRenderer digitGlowRend;                         // Repeat 数字光晕（加法）
    private Vector3 digitBaseScale = Vector3.one;

    private SpriteRenderer rend;
    private Sprite normalSprite;
    private Sprite selectSprite;
    private Color baseColor = Color.white;
    private bool isChainTap = false;
    private SpriteRenderer chainCountRenderer;
    private float chainTapDeadline = -1f;
    private float chainTapHoldDuration = 0.4f;

    public float ChainTapDeadline => chainTapDeadline;

    public void SetChainTapHoldDuration(float seconds)
    {
        chainTapHoldDuration = Mathf.Max(0.05f, seconds);
    }

    private bool animationPlaying = false;
    private bool missing = false;
    private bool missComplete = false;

    private Vector3 baseScale;
    private int laneSpan = 1;
    private float laneSpacing = 1f;

    public bool hasFullyPassed { get; private set; }
    public bool IsMissing => missing;
    public bool MissShrinkComplete => missComplete;
    public Vector3 HitPosition => hitPos;
    public float NoteEdgeLength => transform.localScale.x;

    [Tooltip("判定线（hit line cube）在移动方向上的半厚度")]
    public float judgeLineHalfThickness = 0.05f;

    public bool IsOverlappingHitLine()
    {
        return Mathf.Abs(transform.position.x - hitPos.x) <= noteHalfSize + judgeLineHalfThickness;
    }

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
        this.laneSpan = laneSpan;
        this.laneSpacing = laneSpacing;
        chainTapDeadline = -1f;

        if (isSmallTap) noteRadius *= 0.65f;
        this.noteRadius = noteRadius;

        note = GetComponent<Note>();
        note.hitTime = hitTime;
        note.isSmallTap = isSmallTap;
        note.isChainTap = isChainTap;
        note.chainTapRequired = isChainTap ? Mathf.Max(1, chainTapCount) : 0;
        note.chainTapRemaining = note.chainTapRequired;
        startTime = hitTime - leadTime;

        Vector3 moveDir = (hitPos - spawnPos).normalized;
        exitPos = hitPos + moveDir * 1.5f;

        float hitDistance = Vector3.Distance(spawnPos, hitPos);
        float exitDistance = hitDistance + 1.5f;
        exitLeadTime = hitDistance > 0.001f ? leadTime * (exitDistance / hitDistance) : leadTime + 0.5f;

        // 初始化 SpriteRenderer
        rend = GetComponent<SpriteRenderer>();
        if (rend == null)
        {
            // 移除旧 MeshFilter/MeshRenderer（如果存在）
            var mf = GetComponent<MeshFilter>();
            if (mf != null) DestroyImmediate(mf);
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) DestroyImmediate(mr);
            var mc = GetComponent<Collider>();
            if (mc != null) DestroyImmediate(mc);
            rend = gameObject.AddComponent<SpriteRenderer>();
        }
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.sortingOrder = 10;

        // 选择对应 Sprite
        var lib = NoteSpriteLibrary.Instance;
        if (lib != null)
        {
            if (laneSpan > 1) { normalSprite = lib.wide; selectSprite = lib.wideSelect; }
            else if (isSmallTap) { normalSprite = lib.smallTap; selectSprite = lib.smallTapSelect; }
            else if (isChainTap) { normalSprite = lib.repeat; selectSprite = lib.repeatSelect; }
            else { normalSprite = lib.tap; selectSprite = lib.tapSelect; }
        }
        rend.sprite = normalSprite;
        if (isSmallTap && flipSmallTapY) rend.flipY = true;     // 小点击音符图片反向修正
        rend.color = new Color(baseColor.r, baseColor.g, baseColor.b, noteAlpha);

        // 平躺到 XZ 平面，面朝下方（俯视相机可见）
        transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        // 计算基础缩放：单轨音符跨轨方向(Z)不超出当前轨道；跨轨音符仅跨 2 轨；整体按 visualScaleMul 缩小
        float spriteAspect = (rend.sprite != null && rend.sprite.bounds.size.x > 0.0001f && rend.sprite.bounds.size.y > 0.0001f)
            ? rend.sprite.bounds.size.y / rend.sprite.bounds.size.x : 1f;   // 原图 高/宽
        float d = noteRadius * 2f * visualScaleMul;                         // 单轨基准直径（约一半）
        float desiredWidth, desiredHeight;
        if (laneSpan > 1)
        {
            // 跨轨：Z 方向跨越 laneSpan 条轨道（最多 2 轨），X 方向保持单音符长度
            desiredHeight = laneSpacing * (laneSpan - 1) + noteRadius * 2f * visualScaleMul;  // 跨轨方向(Z)
            desiredWidth  = d;                                                                 // 沿轨方向(X)
        }
        else
        {
            // 单轨：Z 不超出 laneSpacing，X 随比例；整体更小，不超当前音轨
            float zMax = laneSpacing * laneFit;
            desiredHeight = Mathf.Min(d, zMax);          // Z（跨轨方向）
            desiredWidth  = desiredHeight / spriteAspect;  // X（沿轨方向），保持原图比例
        }

        baseScale = SpriteScaleForSize(rend.sprite, desiredWidth, desiredHeight);
        transform.localScale = baseScale;
        CreateGlowChild();
        noteHalfSize = desiredWidth * 0.5f;              // 沿轨方向(X)世界半宽，用于显形/判定
        rideY = hitPos.y;

        rend.enabled = false;

        // Repeat 数字跟随缩小后的音符尺寸
        this.digitWorldSize = Mathf.Max(0.2f, noteRadius * visualScaleMul * 1.8f);

        if (isChainTap)
            CreateChainCountSprite(Mathf.Max(1, chainTapCount));
    }

    private Vector3 SpriteScaleForSize(Sprite s, float worldWidth, float worldHeight)
    {
        if (s == null) return new Vector3(worldWidth, worldHeight, 1f);
        Vector2 sz = s.bounds.size;
        float scX = sz.x > 0.0001f ? worldWidth / sz.x : 1f;
        float scY = sz.y > 0.0001f ? worldHeight / sz.y : 1f;
        return new Vector3(scX, scY, 1f);
    }

    /// <summary>在音符根下挂一个加法混合光晕子物体（同图放大、半透明），营造发光/果冻感。</summary>
    private void CreateGlowChild()
    {
        var ag = RuntimeSpriteUtility.AdditiveGlow;
        if (ag == null) return;                       // shader 缺失时跳过光晕，主体仍可见
        var go = new GameObject("NoteGlow");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        glowRend = go.AddComponent<SpriteRenderer>();
        glowRend.sprite = normalSprite;
        glowRend.material = ag;
        glowRend.color = new Color(glowColor.r, glowColor.g, glowColor.b, glowAlpha);
        glowRend.sortingOrder = 9;                    // 在主体(10)之下
        glowRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // 子物体相对父(父已带 baseScale)，故只乘 glowScaleMul 即可
        go.transform.localScale = Vector3.one * glowScaleMul;
    }

    void Update()
    {
        if (conductor == null || animationPlaying) return;

        if (isChainTap && chainTapDeadline >= 0f)
        {
            transform.position = new Vector3(hitPos.x, rideY, hitPos.z);
            return;
        }

        float t = (conductor.songPosition - startTime) / exitLeadTime;
        t = Mathf.Clamp01(t);
        Vector3 pos = Vector3.Lerp(spawnPos, exitPos, t);
        pos.y = rideY;
        transform.position = pos;

        float centerX = centerLine != null ? centerLine.currentX : 0f;
        bool fullyCrossed = note.side == 0
            ? (transform.position.x + noteHalfSize) < (centerX - revealMargin)
            : (transform.position.x - noteHalfSize) > (centerX + revealMargin);

        if (fullyCrossed && !note.isVisible)
        {
            note.isVisible = true;
            if (rend != null) rend.enabled = true;
            if (chainCountRenderer != null) chainCountRenderer.enabled = true;
            if (digitGlowRend != null) digitGlowRend.enabled = true;
            SetAlpha(1f);
        }

        // 靠近判定线：沿跨轨方向(Z)微微变长 + 变亮（仅普通音符；连点音符自行管理缩放）
        if (!isChainTap && note.isVisible)
        {
            float dToJudge = Mathf.Abs(transform.position.x - hitPos.x);
            if (dToJudge < approachDistance)
            {
                float f = 1f - dToJudge / approachDistance;                 // 越近越大
                float stretch = 1f + (approachStretchY - 1f) * f;          // 沿跨轨方向(Z)微微变长
                transform.localScale = new Vector3(baseScale.x, baseScale.y * stretch, baseScale.z);
                if (rend != null) { Color c = rend.color; c.a = Mathf.Min(1f, noteAlpha * (1f + (approachBright - 1f) * f)); rend.color = c; }
            }
            else
            {
                transform.localScale = baseScale;
                if (rend != null) { Color c = rend.color; c.a = noteAlpha; rend.color = c; }
            }
        }

        if (!hasFullyPassed)
        {
            float noteBack = note.side == 0
                ? transform.position.x + noteHalfSize
                : transform.position.x - noteHalfSize;
            hasFullyPassed = note.side == 0 ? noteBack < hitPos.x : noteBack > hitPos.x;
        }
    }

    public void PlayHitAnimation(string rank)
    {
        if (animationPlaying) return;
        animationPlaying = true;
        StopAllCoroutines();
        StartCoroutine(HitCoroutine());
    }

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
        transform.position = new Vector3(hitPos.x, rideY, hitPos.z);
        StartCoroutine(ChainTapHitPopCo(remaining, remaining == 0));
    }

    public void CompleteChainTap()
    {
        if (!isChainTap || animationPlaying) return;
        chainTapDeadline = -1f;
        animationPlaying = true;
        StopAllCoroutines();
        StartCoroutine(ChainClearCoroutine());
    }

    public void PlaySkillClear()
    {
        if (animationPlaying) { StopAllCoroutines(); }
        animationPlaying = true;
        StartCoroutine(SkillClearCoroutine());
    }

    private void CreateChainCountSprite(int count)
    {
        GameObject go = new GameObject("ChainTapCount");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.18f, 0f); // 在音符上方（local Y = world Z）
        go.transform.localRotation = Quaternion.identity;
        chainCountRenderer = go.AddComponent<SpriteRenderer>();
        chainCountRenderer.sortingOrder = 11;
        chainCountRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        digitBaseScale = DigitScale();
        chainCountRenderer.transform.localScale = digitBaseScale;

        // 数字光晕（加法）
        var ag = RuntimeSpriteUtility.AdditiveGlow;
        if (ag != null)
        {
            var glowGo = new GameObject("DigitGlow");
            glowGo.transform.SetParent(go.transform, false);
            glowGo.transform.localPosition = Vector3.zero;
            glowGo.transform.localRotation = Quaternion.identity;
            digitGlowRend = glowGo.AddComponent<SpriteRenderer>();
            digitGlowRend.material = ag;
            digitGlowRend.sortingOrder = 10;
            digitGlowRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            digitGlowRend.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0.3f);
            digitGlowRend.transform.localScale = Vector3.one;   // 相对父(父已带 digitBaseScale)
        }

        SetChainCount(count);
        chainCountRenderer.enabled = false;
        if (digitGlowRend != null) digitGlowRend.enabled = false;
    }

    private Vector3 DigitScale()
    {
        var lib = NoteSpriteLibrary.Instance;
        Sprite s = null;
        if (lib != null && lib.repeatDigits != null && lib.repeatDigits.Length > 0)
            s = lib.repeatDigits[Mathf.Min(1, lib.repeatDigits.Length - 1)];
        if (s == null) return Vector3.one * digitWorldSize;
        return SpriteScaleForSize(s, digitWorldSize, digitWorldSize);
    }

    private void SetChainCount(int remaining)
    {
        if (chainCountRenderer == null) return;
        int idx = Mathf.Clamp(remaining, 0, 9);
        var lib = NoteSpriteLibrary.Instance;
        if (lib != null && lib.repeatDigits != null && idx < lib.repeatDigits.Length)
        {
            chainCountRenderer.sprite = lib.repeatDigits[idx];
            if (digitGlowRend != null) digitGlowRend.sprite = lib.repeatDigits[idx];
        }
    }

    private IEnumerator ChainTapHitPopCo(int remaining, bool isFinal)
    {
        const float dur = 0.18f;
        float t = 0f;
        Vector3 startS = baseScale;
        Vector3 peakS = new Vector3(baseScale.x, baseScale.y * 1.3f, baseScale.z);

        // 主体切 Select 图（短帧）
        if (rend != null && selectSprite != null) rend.sprite = selectSprite;

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float s = Mathf.Sin(k * Mathf.PI);
            transform.localScale = Vector3.Lerp(startS, peakS, s);
            yield return null;
        }
        transform.localScale = baseScale;

        if (!isFinal)
        {
            // 旧数字快速缩小消失 -> 换图 -> 新数字从 0 放大接替
            if (chainCountRenderer != null)
            {
                float dt = 0f;
                while (dt < 0.09f)
                {
                    dt += Time.deltaTime;
                    float k = Mathf.Clamp01(dt / 0.09f);
                    chainCountRenderer.transform.localScale = digitBaseScale * (1f - k);   // 缩放容器(数字+光晕同缩放)
                    yield return null;
                }
                SetChainCount(remaining);
                float gt = 0f;
                while (gt < 0.12f)
                {
                    gt += Time.deltaTime;
                    float k = Mathf.Clamp01(gt / 0.12f);
                    chainCountRenderer.transform.localScale = digitBaseScale * k;
                    yield return null;
                }
                chainCountRenderer.transform.localScale = digitBaseScale;
            }
            else
            {
                SetChainCount(remaining);
            }
            // 主体回到普通图
            if (rend != null && normalSprite != null) rend.sprite = normalSprite;
        }
        else
        {
            // 最后一击：数字显示 + 闪烁，并保持 Select（不回退）
            SetChainCount(remaining);
            if (chainCountRenderer != null) chainCountRenderer.transform.localScale = digitBaseScale;
            float bt = 0f;
            while (bt < 0.5f)
            {
                bt += Time.deltaTime;
                float k = Mathf.Clamp01(bt / 0.5f);
                float blink = 0.5f + 0.5f * Mathf.Sin(k * Mathf.PI * 6f);
                if (chainCountRenderer != null)
                {
                    Color c = chainCountRenderer.color; c.a = blink; chainCountRenderer.color = c;
                }
                if (digitGlowRend != null)
                {
                    Color c = digitGlowRend.color; c.a = glowAlpha * 0.8f * blink; digitGlowRend.color = c;
                }
                yield return null;
            }
            if (chainCountRenderer != null) { Color c = chainCountRenderer.color; c.a = 1f; chainCountRenderer.color = c; }
            if (digitGlowRend != null) { Color c = digitGlowRend.color; c.a = glowAlpha * 0.8f; digitGlowRend.color = c; }
        }
    }

    private IEnumerator ChainClearCoroutine()
    {
        if (rend != null && selectSprite != null) rend.sprite = selectSprite;
        yield return StartCoroutine(HitCoroutine());
    }

    private IEnumerator HitCoroutine()
    {
        if (rend != null)
        {
            if (selectSprite != null) rend.sprite = selectSprite;
            rend.color = new Color(baseColor.r, baseColor.g, baseColor.b, noteAlpha);
        }
        if (glowRend != null)
        {
            glowRend.enabled = true;
            glowRend.color = new Color(glowColor.r, glowColor.g, glowColor.b, glowAlpha);
        }

        SpawnBeam();
        SpawnSparkles();

        // 命中后沿前进方向减速滑行（不再停住）
        Vector3 travelDir = (hitPos - spawnPos);
        if (travelDir.sqrMagnitude < 1e-6f) travelDir = Vector3.right; else travelDir.Normalize();
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + travelDir * hitOvershoot;
        float dur = hitDuration;
        float timer = 0f;

        while (timer < dur)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / dur);
            float ease = 1f - (1f - t) * (1f - t);            // ease-out：快速起步、减速前进
            transform.position = Vector3.Lerp(startPos, endPos, ease);
            float s = 1f + (hitScaleMul - 1f) * Mathf.Sin(t * Mathf.PI);   // 整体放大 → 回弹到 1
            transform.localScale = baseScale * s;
            float a = 1f - t;
            if (rend != null) { Color c = rend.color; c.a = noteAlpha * a; rend.color = c; }
            if (glowRend != null) { Color c = glowRend.color; c.a = glowAlpha * a; glowRend.color = c; }
            yield return null;
        }

        Destroy(gameObject);
    }

    private IEnumerator MissCoroutine()
    {
        // 命中失败：整体缩小消失，节奏先慢后快（加速消失），同时沿前进方向轻微减速滑行，不再停在判定线
        Vector3 travelDir = (hitPos - spawnPos);
        if (travelDir.sqrMagnitude < 1e-6f) travelDir = Vector3.right; else travelDir.Normalize();
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + travelDir * missOvershoot;
        float dur = missDuration;
        float timer = 0f;

        while (timer < dur)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / dur);
            float ease = t * t;                                 // ease-in：先慢后快加速消失
            transform.position = Vector3.Lerp(startPos, endPos, ease);
            transform.localScale = baseScale * (1f - ease);     // 整体缩小（非仅 Y）
            float a = 1f - ease;
            if (rend != null) { Color c = rend.color; c.a = noteAlpha * a; rend.color = c; }
            if (chainCountRenderer != null) { Color c = chainCountRenderer.color; c.a = a; chainCountRenderer.color = c; }
            if (glowRend != null) { Color c = glowRend.color; c.a = glowAlpha * a; glowRend.color = c; }
            yield return null;
        }

        missComplete = true;
    }

    private IEnumerator SkillClearCoroutine()
    {
        if (rend != null)
        {
            rend.enabled = true;
            if (selectSprite != null) rend.sprite = selectSprite;
            rend.color = new Color(baseColor.r, baseColor.g, baseColor.b, noteAlpha);
        }
        if (glowRend != null)
        {
            glowRend.enabled = true;
            glowRend.color = new Color(glowColor.r, glowColor.g, glowColor.b, glowAlpha);
        }
        SpawnBeam();
        yield return StartCoroutine(HitCoroutine());
    }

    private void SpawnBeam()
    {
        var lib = NoteSpriteLibrary.Instance;
        Sprite beamSpr = (lib != null && lib.beam != null) ? lib.beam : RuntimeSpriteUtility.Beam;
        if (beamSpr == null) return;

        GameObject go = new GameObject("HitBeam");
        go.transform.SetParent(transform.parent, false);   // 脱离音符 -90°翻转/非均匀缩放，避免被压平
        go.transform.position = transform.position;        // 立于音符世界位置
        go.transform.rotation = Quaternion.identity;       // 世界直立（光束朝上 +Y）
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = beamSpr;
        var ag = RuntimeSpriteUtility.AdditiveGlow;
        if (ag != null) sr.material = ag;                       // 加法发光
        sr.color = new Color(0.92f, 0.97f, 1f, 0.7f);           // 冷白，克制
        sr.sortingOrder = 13;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // 光束竖直向上（世界 +Y），宽度收窄；Wide 命中时光束更宽
        float beamW = baseScale.x * 0.35f * (laneSpan > 1 ? 1.6f : 1f);
        Vector3 beamScale = SpriteScaleForSize(beamSpr, beamW, baseScale.x * 2.5f);
        go.transform.localScale = beamScale;
        StartCoroutine(BeamFadeCo(sr, go));
    }

    private IEnumerator BeamFadeCo(SpriteRenderer sr, GameObject go)
    {
        const float dur = 0.35f;
        float t = 0f;
        Vector3 startS = go.transform.localScale;
        Vector3 endS = new Vector3(startS.x * 0.5f, startS.y * 1.4f, startS.z);
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            if (sr != null)
            {
                Color c = sr.color;
                c.a = (1f - k) * 0.7f;
                sr.color = c;
            }
            go.transform.localScale = Vector3.Lerp(startS, endS, k);
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private void SpawnSparkles()
    {
        var lib = NoteSpriteLibrary.Instance;
        Sprite sparkSpr = (lib != null && lib.sparkle != null) ? lib.sparkle : RuntimeSpriteUtility.Sparkle;
        if (sparkSpr == null) return;

        var ag = RuntimeSpriteUtility.AdditiveGlow;
        float lateral = laneSpan > 1 ? 1f : 0f;          // Wide：碎光从左右迸发
        int count = Random.Range(5, 9);
        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject($"Sparkle_{i}");
            go.transform.SetParent(transform.parent, false);   // 同光束：世界空间，方向用世界坐标
            go.transform.position = transform.position;
            go.transform.rotation = Quaternion.identity;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sparkSpr;
            if (ag != null) sr.material = ag;             // 加法发光
            sr.color = new Color(0.92f, 0.97f, 1f, 0.8f);
            sr.sortingOrder = 13;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            StartCoroutine(SparkleCo(go, sr, lateral));
        }
    }

    private IEnumerator SparkleCo(GameObject go, SpriteRenderer sr, float lateral)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float speed = Random.Range(1f, 2.5f);
        float offset = Random.Range(0f, 0.15f);
        float life = 1f;
        Vector3 dir = new Vector3(Mathf.Cos(angle) * (0.3f + lateral * 0.8f), 1f, Mathf.Sin(angle) * 0.3f);
        Vector3 origin = go.transform.position;               // 世界坐标起点（音符位置）
        Vector3 startPos = new Vector3(Random.Range(-0.1f, 0.1f), 0f, Random.Range(-0.1f, 0.1f));
        go.transform.position = origin + startPos;
        float startScale = Random.Range(0.3f, 0.7f);
        go.transform.localScale = Vector3.one * startScale;

        float t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            go.transform.position = origin + startPos + dir * (offset + speed * k);
            go.transform.localScale = Vector3.one * startScale * (1f - k);
            if (sr != null)
            {
                Color c = sr.color;
                c.a = 1f - k;
                sr.color = c;
            }
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private void SetAlpha(float alpha)
    {
        if (rend != null)
        {
            Color c = rend.color;
            c.a = noteAlpha * alpha;
            rend.color = c;
            rend.enabled = alpha > 0.01f && note.isVisible;
        }
        if (glowRend != null)
        {
            Color c = glowRend.color;
            c.a = glowAlpha * alpha;
            glowRend.color = c;
            glowRend.enabled = alpha > 0.01f && note.isVisible;
        }
        if (chainCountRenderer != null)
        {
            Color c = chainCountRenderer.color;
            c.a = alpha;
            chainCountRenderer.color = c;
            chainCountRenderer.enabled = alpha > 0.01f && note.isVisible;
        }
        if (digitGlowRend != null)
        {
            Color c = digitGlowRend.color;
            c.a = glowAlpha * alpha;
            digitGlowRend.color = c;
            digitGlowRend.enabled = alpha > 0.01f && note.isVisible;
        }
    }
}
