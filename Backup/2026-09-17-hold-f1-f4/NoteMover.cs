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
    // 连点数字精灵系统（替换原 TextMesh）：digitRoot 浮于音符之上、躺平朝上（同原 TextMesh 朝向）。
    // 单数字 n<10 用一个 quad；双数字（≥10）十位+个位沿 X 并排。
    private GameObject digitRoot;
    private MeshRenderer[] digitRenders = System.Array.Empty<MeshRenderer>();
    private Material[] digitMats = System.Array.Empty<Material>();
    private int[] digitFiguresCurrent = new int[0];   // 当前显示数字拆解出的各位（用于块 C 命中切 Select）
    private int digitDisplay = -1;                    // 当前显示数字（0..N；-1=未设）

    [Header("换皮")]
    [Tooltip("普通点击音符贴图；留空则尝试从 NoteSpriteLibrary.tap 读取。仅作用于普通点击。")]
    public Texture2D tapTexture;
    [Tooltip("跨轨点击音符贴图（Note_Wide.png）；留空则尝试从 NoteSpriteLibrary.wide 读取。仅作用于跨轨点击。")]
    public Texture2D wideTexture;
    [Tooltip("小型点击音符贴图（Note_Tap_Small.png）；留空则尝试从 NoteSpriteLibrary.smallTap 读取。仅作用于小型点击。")]
    public Texture2D smallTapTexture;
    [Tooltip("可选成品材质（需 URP Unlit/Transparent + _BaseMap）。留空则运行时用贴图自动生成。")]
    public Material tapNoteMaterial;
    [Tooltip("小型点击可选成品材质（需 URP Unlit/Transparent + _BaseMap）。留空则运行时用 smallTapTexture 自动生成。")]
    public Material smallTapNoteMaterial;
    [Tooltip("连点音符(Repeat)体贴图（Note_Repeat.png）；留空则尝试从 NoteSpriteLibrary.repeat 读取。仅作用于连点音符。")]
    public Texture2D repeatTexture;
    [Tooltip("连点音符可选成品材质（需 URP Unlit/Transparent + _BaseMap）。留空则运行时用 repeatTexture 自动生成。")]
    public Material repeatNoteMaterial;

    [Header("普通点击视觉（仅普通点击，不影响其他类型与判定窗口）")]
    [Tooltip("尺寸模式：true=按贴图原生尺寸 1:1 显示（美术改贴图像素即改大小，PPU 由贴图导入设置决定）；false=回退到手调绝对尺寸 tapTargetScale/wideTapTargetScale。")]
    public bool useTextureNativeSize = true;
    [Tooltip("普通点击额外整体缩放倍数（在贴图比例基础上再乘）；默认 1.0 = 纯按贴图比例，不改。只缩视觉，不改 noteRadius，判定窗口不变。")]
    public float tapVisualScaleMul = 1f;
    [Header("音符实测目标尺寸（按效果图实测烤定；任一分量设 0 = 回退比例逻辑）")]
    [Tooltip("普通点击（换皮）目标 Scale(X,Z)，绝对套用；X=0.8446591 Z=1.37376。设 0 回退到贴图比例逻辑。")]
    public Vector2 tapTargetScale = new Vector2(0.8446591f, 1.37376f);
    [Tooltip("跨轨音符（Linked，恒为 span=2）目标 Scale(X,Z)，绝对套用；X=0.7777194 Z=3.020018。设 0 回退到 laneSpacing 公式。")]
    public Vector2 wideTapTargetScale = new Vector2(0.7777194f, 3.020018f);
    [Tooltip("小型点击（换皮）目标 Scale(X,Z)，绝对套用；设 0 回退到贴图比例逻辑。默认 0 = 跟普通点击一样走 1:1（X=0.5 Z=0.82 @PPU100）。")]
    public Vector2 smallTapTargetScale = new Vector2(0f, 0f);
    [Tooltip("连点音符（换皮）目标 Scale(X,Z)，绝对套用；设 0 回退到贴图比例逻辑。默认 0 = 走 1:1（X=1.0 Z=1.5 @PPU100）。")]
    public Vector2 repeatTargetScale = new Vector2(0f, 0f);
    [Tooltip("普通点击贴图整体不透明度（0~1）；需配合透明背景 PNG 才有半透效果，否则只是把黄底卡片调淡。")]
    public float tapAlpha = 1f;
    [Tooltip("普通点击圆角半径（0~0.5，0.5=半圆/胶囊）。仅影响普通点击使用的圆角网格，不影响其他音符类型。")]
    [Range(0.05f, 0.5f)]
    public float tapCornerRadius = 0.35f;

    private bool textured = false;                 // 当前音符是否启用卡通换皮
    private Texture2D _selectTex;                  // 命中时切换到的"完成"贴图（Select），按类型取自 NoteSpriteLibrary
    private Texture2D _baseTex;                    // 非命中态基准贴图（用于 D 最远点切回），= Init 时的 activeTex（连点=repeatTex）
    private Color visibleBaseColor = Color.black; // 可见态基础色：换皮=白（显贴图），否则=黑
    private float visibleAlpha = 1f;               // 可见态不透明度（换皮时 = tapAlpha）
    [Header("连点音符运动")]
    [Tooltip("后退减速时长（秒）：命中后以极快初速后退，期间匀减速，到该时长速度降为 0、退到 chainRetreatDist 处。")]
    public float chainTapHoldDuration = 1f;
    [Tooltip("后退距离（世界单位）：命中后从判定线后退多远，再以前进速度回到判定线。Inspector 可调。")]
    public float chainRetreatDist = 1f;

    [Header("连点数字精灵（替换 TextMesh，仅连点音符）")]
    [Tooltip("数字局部 Y（Inspector 里 ChainTapDigits 的 Position Y 就是这个值，默认 1.63）。不换算、所见即所得。可在运行时拖。")]
    public float digitYOffset = 1.63f;
    [Tooltip("数字整体大小倍数（在贴图 1:1 基础上再乘；默认 1 = 按贴图像素真实大小）。可在运行时拖。")]
    public float digitScaleMul = 1f;
    [Tooltip("双数字横向间距（世界单位，已含 digitScaleMul）。可在运行时拖。")]
    public float digitSpacing = 0.06f;

        private float chainTapDeadline = -1f;        // 可点击截止时间 = nextContactTime + goodWindow
        private float chainHitTime = -999f;           // 本次命中时刻
        private float chainNextContactTime = -999f;   // 重新接触判定线时刻（public 可读）
        private float normalSpeed = 1f;               // 正常前进速度（Init 时按曲速推算）
        private float goodWindow = 0.07f;             // 判定窗口（由 NoteSpawner 传入）
        private bool chainFarthestDone = false;       // D：本次命中撤退是否已到最远点（触发一次递减+切回）

    public float ChainTapDeadline => chainTapDeadline;
    public float ChainTapNextContactTime => chainNextContactTime;
    public void SetChainTapHoldDuration(float seconds) { chainTapHoldDuration = Mathf.Max(0.05f, seconds); }
    public void SetChainTapGoodWindow(float w) { goodWindow = Mathf.Max(0.001f, w); }

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
                     bool isChainTap = false, int chainTapCount = 0, float chainTapHoldDuration = 0.4f, float chainTapGoodWindow = 0.07f)
    {
        this.spawnPos = spawnPos;
        this.hitPos = hitPos;
        this.hitTime = hitTime;
        this.leadTime = leadTime;
        this.conductor = conductor;
        this.centerLine = centerLine;
        this.isChainTap = isChainTap;
        this.chainTapHoldDuration = Mathf.Max(0.05f, chainTapHoldDuration);
        SetChainTapGoodWindow(chainTapGoodWindow);
        chainTapDeadline = -1f;

        // 小型点击音符：半径只有基础 Tap 的 65%
        if (isSmallTap) noteRadius *= 0.65f;
        this.noteRadius = noteRadius;

        // 普通点击换皮判定：仅「普通点击」(非小点击 / 非连点 / 单轨) 且能取到贴图时启用卡通贴图。
        bool isPlainTap = !isSmallTap && !isChainTap && laneSpan <= 1;
        bool isWideTap = !isSmallTap && !isChainTap && laneSpan > 1;   // 跨轨点击

        Texture2D tapTex = tapTexture;
        Sprite tapSpriteRef = null;
        if (tapTex == null && NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.tap != null)
        {
            tapSpriteRef = NoteSpriteLibrary.Instance.tap;
            tapTex = tapSpriteRef.texture;
        }
        float tapPPU = tapSpriteRef != null ? tapSpriteRef.pixelsPerUnit : 100f;
        Texture2D wideTex = wideTexture;
        Sprite wideSpriteRef = null;
        if (wideTex == null && NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.wide != null)
        {
            wideSpriteRef = NoteSpriteLibrary.Instance.wide;
            wideTex = wideSpriteRef.texture;
        }
        float widePPU = wideSpriteRef != null ? wideSpriteRef.pixelsPerUnit : 100f;
        Texture2D smallTex = smallTapTexture;
        Sprite smallSpriteRef = null;
        if (smallTex == null && NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.smallTap != null)
        {
            smallSpriteRef = NoteSpriteLibrary.Instance.smallTap;
            smallTex = smallSpriteRef.texture;
        }
        float smallPPU = smallSpriteRef != null ? smallSpriteRef.pixelsPerUnit : 100f;
        Texture2D repeatTex = repeatTexture;
        Sprite repeatSpriteRef = null;
        if (repeatTex == null && NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.repeat != null)
        {
            repeatSpriteRef = NoteSpriteLibrary.Instance.repeat;
            repeatTex = repeatSpriteRef.texture;
        }
        float repeatPPU = repeatSpriteRef != null ? repeatSpriteRef.pixelsPerUnit : 100f;
        // 命中时切换到的"完成"贴图（Select）：按音符类型从 NoteSpriteLibrary 取对应精灵的 Texture。
        _selectTex = null;
        if (NoteSpriteLibrary.Instance != null)
        {
            Sprite sel = isChainTap ? NoteSpriteLibrary.Instance.repeatSelect
                        : (isWideTap ? NoteSpriteLibrary.Instance.wideSelect
                        : (isSmallTap ? NoteSpriteLibrary.Instance.smallTapSelect
                                      : NoteSpriteLibrary.Instance.tapSelect));
            if (sel != null) _selectTex = sel.texture;
        }

        bool isSmallTextured = isSmallTap && smallTex != null;
        textured = (isPlainTap && tapTex != null) || (isWideTap && wideTex != null) || isSmallTextured || (isChainTap && repeatTex != null);
        Texture2D activeTex = isChainTap ? repeatTex : (isWideTap ? wideTex : (isSmallTap ? smallTex : tapTex));          // 实际用于渲染的贴图
        _baseTex = activeTex;                        // 基准（非命中态）贴图，D 最远点切回用

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
        // 正常前进速度（音符飞向判定线的速度），连点音符前进段用它回到判定线
        normalSpeed = hitDistance > 0.001f ? hitDistance / Mathf.Max(0.001f, leadTime) : 1f;

        // 运行时替换预制体网格：普通点击且换皮时用大圆角平板当画布，连轨点击也用圆角平板；其余用圆柱。
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = (laneSpan > 1 || textured) ? (isPlainTap || isSmallTap || isChainTap ? TapRoundedRectMesh(tapCornerRadius) : RoundedRectMesh) : CylinderMesh;

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
        // 音符视觉尺寸：默认「按贴图原生尺寸 1:1」(useTextureNativeSize=true)，
        // 美术改贴图像素即改大小，PPU 由贴图导入设置决定；关闭则回退手调绝对尺寸(可回退)。
        if (useTextureNativeSize)
        {
            if (isWideTap && wideTex != null)
            {
                float w = (wideSpriteRef != null) ? wideSpriteRef.rect.width : wideTex.width;
                float h = (wideSpriteRef != null) ? wideSpriteRef.rect.height : wideTex.height;
                xDiameter = w / widePPU;
                zDiameter = h / widePPU;
            }
            else if (isPlainTap && tapTex != null)
            {
                float w = (tapSpriteRef != null) ? tapSpriteRef.rect.width : tapTex.width;
                float h = (tapSpriteRef != null) ? tapSpriteRef.rect.height : tapTex.height;
                xDiameter = w / tapPPU;
                zDiameter = h / tapPPU;
            }
            else if (isSmallTap && smallTex != null)
            {
                float w = (smallSpriteRef != null) ? smallSpriteRef.rect.width : smallTex.width;
                float h = (smallSpriteRef != null) ? smallSpriteRef.rect.height : smallTex.height;
                xDiameter = w / smallPPU;
                zDiameter = h / smallPPU;
            }
            else if (isChainTap && repeatTex != null)
            {
                float w = (repeatSpriteRef != null) ? repeatSpriteRef.rect.width : repeatTex.width;
                float h = (repeatSpriteRef != null) ? repeatSpriteRef.rect.height : repeatTex.height;
                xDiameter = w / repeatPPU;
                zDiameter = h / repeatPPU;
            }
            // XZ 同乘 tapVisualScaleMul（默认 1），便于整体微调，不破坏比例
            transform.localScale = new Vector3(xDiameter * tapVisualScaleMul, 0.12f, zDiameter * tapVisualScaleMul);
        }
        else if (isWideTap && wideTapTargetScale.x > 0f && wideTapTargetScale.y > 0f)
        {
            // 跨轨（Linked，恒为 span=2）：绝对套用实测尺寸
            transform.localScale = new Vector3(wideTapTargetScale.x, 0.12f, wideTapTargetScale.y);
        }
        else if (isPlainTap && tapTex != null)
        {
            if (tapTargetScale.x > 0f && tapTargetScale.y > 0f)
            {
                // 普通点击（换皮）：绝对套用实测尺寸
                transform.localScale = new Vector3(tapTargetScale.x, 0.12f, tapTargetScale.y);
            }
            else
            {
                float texRatio = (float)tapTex.width / Mathf.Max(1f, tapTex.height); // 宽/高
                xDiameter = zDiameter * texRatio;          // X 跟随贴图比例
                float s = tapVisualScaleMul;               // 等比缩放 X/Z，避免再次压扁
                transform.localScale = new Vector3(xDiameter * s, 0.12f, zDiameter * s);
            }
        }
        else if (isSmallTap && smallTex != null)
        {
            if (smallTapTargetScale.x > 0f && smallTapTargetScale.y > 0f)
            {
                // 小型点击（换皮）：绝对套用实测尺寸
                transform.localScale = new Vector3(smallTapTargetScale.x, 0.12f, smallTapTargetScale.y);
            }
            else
            {
                float texRatio = (float)smallTex.width / Mathf.Max(1f, smallTex.height); // 宽/高
                xDiameter = zDiameter * texRatio;          // X 跟随贴图比例
                float s = tapVisualScaleMul;               // 等比缩放 X/Z，避免再次压扁
                transform.localScale = new Vector3(xDiameter * s, 0.12f, zDiameter * s);
            }
        }
        else if (isChainTap && repeatTargetScale.x > 0f && repeatTargetScale.y > 0f)
        {
            // 连点音符（换皮）：绝对套用实测尺寸（关闭 useTextureNativeSize 时生效）
            transform.localScale = new Vector3(repeatTargetScale.x, 0.12f, repeatTargetScale.y);
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
                if (isSmallTap)
                    noteMaterial = smallTapNoteMaterial != null ? new Material(smallTapNoteMaterial) : MakeTapMaterial(smallTex);
                else if (isChainTap)
                    noteMaterial = repeatNoteMaterial != null ? new Material(repeatNoteMaterial) : MakeTapMaterial(repeatTex);
                else
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
            CreateDigitSprites(Mathf.Max(1, chainTapCount));
    }

    void Update()
    {
        if (conductor == null || animationPlaying) return;

        // 连点命中后：先匀减速后退 chainRetreatDist（chainTapHoldDuration 内速度降到 0），
        // 再以正常前进速度 normalSpeed 朝判定线回来；重新接触判定线附近可点击，越过则 Miss。
        if (isChainTap && chainTapDeadline >= 0f)
        {
            float te = conductor.songPosition - chainHitTime;
            float dirX = (note.side == 0) ? -1f : 1f;   // 前进方向（左玩家 -X，右玩家 +X）
            float d;
            if (te < chainTapHoldDuration)
            {
                float k = te / Mathf.Max(0.0001f, chainTapHoldDuration);   // 0..1
                d = chainRetreatDist * (2f * k - k * k);                    // 匀减速 0→dist，末速 0
            }
            else
            {
                d = chainRetreatDist - normalSpeed * (te - chainTapHoldDuration);  // 正常速前进，d<0=已越过判定线
            }
            transform.position = new Vector3(hitPos.x - dirX * d, rideY, hitPos.z);

            // D：撤退到最远点（te 首次越过 chainTapHoldDuration，速度降为 0）触发一次：
            // 数字 -1 + 整组切回非命中态（体→repeatTex，数字→对应普通数字），再随前进回到判定线。
            if (!chainFarthestDone && te >= chainTapHoldDuration)
            {
                chainFarthestDone = true;
                int newRemaining = note != null ? note.OnChainRetreatFarthest() : Mathf.Max(0, digitDisplay - 1);
                SetDigit(newRemaining);          // 数字切回非命中态并显示新数（含双数字）
                RevertSelectTexture();           // 体切回非命中态（Note_Repeat）
            }
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
            SetAlpha(1f);   // 同时按 note.isVisible 打开数字精灵渲染器并置不透明
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

        // 重新武装「撤退最远点」触发（D 每次命中重新计时一次）
        chainFarthestDone = false;

        // 记录本次命中时刻，并算出「重新接触判定线」时刻与可点击截止时间
        chainHitTime = songTime;
        float v = Mathf.Max(0.001f, normalSpeed);
        chainNextContactTime = songTime + chainTapHoldDuration + chainRetreatDist / v;
        chainTapDeadline = chainNextContactTime + goodWindow;

        float progress = 1f - Mathf.Clamp01(remaining / (float)Mathf.Max(1, required));
        SetChainBodyColor(Color.Lerp(Color.black, Color.white, progress));
        SetDigit(remaining);   // 命中显示当前数（非命中态）
        // 块 C：每次命中切到"完成"贴图组（体→repeatSelect，数字→repeatDigitsSelect）
        ApplySelectTexture();
        // 不再钉死位置：下一帧 Update 按 te 平滑接管（te≈0 即在判定线，无跳变）。
        // 每次命中叠加一次「白闪 + 放大缩小」反馈（不破坏进度色，淡出后回归原进度色）。
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
            SetDigitAlpha(1f - t);
            transform.localScale = Vector3.Lerp(startScale, endScale, Mathf.Sin(t * Mathf.PI * 0.5f));
            yield return null;
        }

        Destroy(gameObject);
    }

    /// <summary>构建连点数字精灵系统：digitRoot 浮于音符之上、躺平朝上（复刻原 TextMesh 朝向 Euler(90,0,0)+scale.z=-1）。
    /// 预建两个数字 quad（slot0=左/十位，slot1=右/个位），由 SetDigit 按当前数决定用几个、贴哪张数字图。</summary>
    private void CreateDigitSprites(int count)
    {
        digitRoot = new GameObject("ChainTapDigits");
        digitRoot.transform.SetParent(transform, false);
        digitRoot.transform.localPosition = new Vector3(0f, digitYOffset, 0f); // 直接用局部 Y，Inspector 所见即所得
        digitRoot.transform.localRotation = Quaternion.identity;
        digitRoot.transform.localScale = Vector3.one;

        digitRenders = new MeshRenderer[2];
        digitMats = new Material[2];
        for (int i = 0; i < 2; i++)
        {
            GameObject dg = new GameObject("Digit" + i);
            dg.transform.SetParent(digitRoot.transform, false);
            dg.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 躺平朝上（同原 TextMesh）
            dg.transform.localScale = new Vector3(1f, 1f, -1f);          // 复用原 TextMesh 的 z=-1 翻转（避免法线/镜像问题）
            MeshFilter mf = dg.AddComponent<MeshFilter>();
            mf.sharedMesh = DigitQuadMesh;
            MeshRenderer mr = dg.AddComponent<MeshRenderer>();
            mr.material = MakeTapMaterial(null);  // 占位，SetDigit 时换真实数字贴图
            mr.enabled = false;                   // 过粉杠前隐藏
            digitRenders[i] = mr;
            digitMats[i] = mr.material;
        }
        SetDigit(count);
    }

    /// <summary>按数字 n 设置连点数字贴图：单数字 n&lt;10 用一个 quad；双数字（≥10）十位(左)+个位(右)沿 X 并排。
    /// 局部 scale 会抵消音符非均匀缩放（卡面 Z=1.5），使数字按真实世界尺寸呈现、不被拉长。</summary>
    private void SetDigit(int n)
    {
        if (NoteSpriteLibrary.Instance == null) return;
        n = Mathf.Max(0, n);
        int[] figs = (n < 10) ? new int[] { n } : new int[] { n / 10, n % 10 };
        digitFiguresCurrent = figs;

        float nsx = transform.localScale.x;
        float nsz = transform.localScale.z;

        float[] wW = new float[figs.Length];
        float[] wH = new float[figs.Length];
        for (int i = 0; i < figs.Length; i++)
        {
            Sprite sp = NoteSpriteLibrary.Instance.repeatDigits[figs[i]];
            if (sp == null) { wW[i] = 0.6f; wH[i] = 0.9f; }
            else { wW[i] = (sp.rect.width / sp.pixelsPerUnit) * digitScaleMul; wH[i] = (sp.rect.height / sp.pixelsPerUnit) * digitScaleMul; }
        }

        float gapWorld = digitSpacing * digitScaleMul;
        float totalW = (figs.Length == 2) ? (wW[0] + gapWorld + wW[1]) : wW[0];

        for (int slot = 0; slot < 2; slot++)
        {
            if (slot < figs.Length)
            {
                Sprite sp = NoteSpriteLibrary.Instance.repeatDigits[figs[slot]];
                string tp = TexturePropName(digitMats[slot]);
                digitMats[slot].SetTexture(tp, sp != null ? sp.texture : null);
                digitMats[slot].SetInt("_Cull", 0); // 双面：避免朝向/背面剔除导致数字看不见
                // 抵消音符非均匀缩放，数字按真实世界尺寸呈现（localScale.y 经 R_x(90) 映射到世界 Z）
                digitRenders[slot].transform.localScale = new Vector3(wW[slot] / nsx, wH[slot] / nsz, -1f);
                float worldX = (figs.Length == 2)
                    ? (slot == 0 ? -(gapWorld / 2f + wW[1] / 2f) : (gapWorld / 2f + wW[0] / 2f))
                    : 0f;
                digitRenders[slot].transform.localPosition = new Vector3(worldX / nsx, 0f, 0f);
                digitRenders[slot].enabled = note != null && note.isVisible;
            }
            else
            {
                digitRenders[slot].enabled = false;
            }
        }
        digitDisplay = n;
    }

    /// <summary>连点数字命中切"完成"贴图（RepeatDigitsSelect[各位]）。供块 C 命中路径调用。</summary>
    private void SwapDigitToSelect()
    {
        if (NoteSpriteLibrary.Instance == null) return;
        for (int slot = 0; slot < digitFiguresCurrent.Length; slot++)
        {
            if (slot >= digitMats.Length) break;
            Sprite sp = NoteSpriteLibrary.Instance.repeatDigitsSelect[digitFiguresCurrent[slot]];
            if (sp != null) digitMats[slot].SetTexture(TexturePropName(digitMats[slot]), sp.texture);
        }
    }

    /// <summary>统一设置连点数字精灵的不透明度（0~1）并联动渲染器显隐（仅当音符已可见）。</summary>
    private void SetDigitAlpha(float a)
    {
        for (int i = 0; i < digitMats.Length; i++)
        {
            if (digitMats[i] == null) continue;
            Color c = Color.white; c.a = a;
            if (digitMats[i].HasProperty("_BaseColor")) digitMats[i].SetColor("_BaseColor", c);
            else digitMats[i].color = c;
            if (digitRenders[i] != null && note != null && note.isVisible)
                digitRenders[i].enabled = a > 0.01f;
        }
    }

    /// <summary>贴图属性名：优先 URP Unlit 的 _BaseMap，兼容旧 _MainTex。</summary>
    private static string TexturePropName(Material m)
    {
        if (m == null) return "_BaseMap";
        return m.HasProperty("_BaseMap") ? "_BaseMap" : (m.HasProperty("_MainTex") ? "_MainTex" : "_BaseMap");
    }

    /// <summary>数字 quad：XY 平面单位正方形（UV 0..1），经 R_x(90) 旋转后躺平在 XZ 平面、法线朝上。</summary>
    private static Mesh _digitQuadMesh;
    private static Mesh DigitQuadMesh
    {
        get
        {
            if (_digitQuadMesh == null)
            {
                _digitQuadMesh = new Mesh { name = "DigitQuad" };
                _digitQuadMesh.vertices = new Vector3[]
                {
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3( 0.5f, -0.5f, 0f),
                    new Vector3( 0.5f,  0.5f, 0f),
                    new Vector3(-0.5f,  0.5f, 0f),
                };
                _digitQuadMesh.uv = new Vector2[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f),
                };
                _digitQuadMesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
                _digitQuadMesh.RecalculateNormals();
            }
            return _digitQuadMesh;
        }
    }

    private void SetChainBodyColor(Color color)
    {
        // 换皮(贴图)连点体：不叠加连续渐变，直接以白底显贴图（进度由数字表达）；
        // 原黑圆柱回退分支仍保留进度渐变着色。
        if (textured) SetNoteTint(Color.white);
        else SetNoteTint(color);
    }

    /// <summary>命中时把材质贴图切换到"完成"版（Select）。贴图属性名兼容 URP Unlit(_BaseMap) 与旧(_MainTex)。</summary>
    private void ApplySelectTexture()
    {
        if (noteMaterial == null) return;
        string texProp = noteMaterial.HasProperty("_BaseMap") ? "_BaseMap" : (noteMaterial.HasProperty("_MainTex") ? "_MainTex" : "_BaseMap");
        if (isChainTap)
        {
            // 连点：体切 repeatSelect（完成体），数字切 repeatDigitsSelect（完成数字，按当前各位）
            if (NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.repeatSelect != null)
                noteMaterial.SetTexture(texProp, NoteSpriteLibrary.Instance.repeatSelect.texture);
            SwapDigitToSelect();
        }
        else if (_selectTex != null)
        {
            noteMaterial.SetTexture(texProp, _selectTex);
        }
    }

    /// <summary>连点撤退到最远点后整组切回非命中态：体切回基准贴图（Note_Repeat）；数字由 SetDigit 同步切回普通数字。</summary>
    private void RevertSelectTexture()
    {
        if (noteMaterial == null) return;
        string texProp = noteMaterial.HasProperty("_BaseMap") ? "_BaseMap" : (noteMaterial.HasProperty("_MainTex") ? "_MainTex" : "_BaseMap");
        if (_baseTex != null)
            noteMaterial.SetTexture(texProp, _baseTex);
    }

    private IEnumerator HitCoroutine(string rank)
    {
        float duration = 0.25f;
        float timer = 0f;

        // 命中表现：换皮音符且有"完成"贴图时，直接切成 Select（完成）贴图以原色显示，再放大淡出；
        // 其余（非换皮 / 无 Select，如连点圆柱）沿用旧的"变黑 → 白 → 放大消失"。
        bool useSelect = textured && _selectTex != null;
        if (noteMaterial != null)
        {
            if (useSelect)
            {
                ApplySelectTexture();      // 换成"完成"贴图
                SetNoteTint(Color.white);  // 以贴图原色显示（不闪黑）
            }
            else
            {
                SetNoteTint(Color.black);  // 旧：变黑强调被击中
            }
        }

        if (!useSelect)
            yield return new WaitForSeconds(0.05f); // 旧表现：先黑一下

        // 放大并淡出（两种路径共用）
        Vector3 startScale = transform.localScale;
        Vector3 endScale = startScale * 1.4f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = timer / duration;

            if (noteMaterial != null)
            {
                if (useSelect)
                    SetNoteTint(new Color(1f, 1f, 1f, 1f - t)); // 完成贴图淡出：保原色，仅降透明度
                else
                    SetNoteTint(Color.Lerp(Color.white, new Color(1f, 1f, 1f, 0f), t)); // 黑 → 白 → 透
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
            SetDigitAlpha(Mathf.Lerp(1f, 0f, t));

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
        SetDigitAlpha(alpha);
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
        // 关键：URP Unlit 的混合状态由隐藏属性 _SrcBlend/_DstBlend/_ZWrite 驱动，
        // new Material 默认 One/Zero/ZWrite=1（不透明）。必须显式设为 SrcAlpha/OneMinusSrcAlpha/ZWrite=0，
        // 否则贴图透明区域会显示成实心色块（数字贴图透明面积大，问题最明显）。
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return mat;
    }
}
