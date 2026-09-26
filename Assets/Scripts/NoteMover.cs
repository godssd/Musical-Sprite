using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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
    [Tooltip("小型点击音符贴图（Note_Tap_Small.png）；留空则尝试从 NoteSpriteLibrary.smallTap 读取。仅作用于小型点击。")]
    public Texture2D smallTapTexture;
    [Tooltip("可选成品材质（需 URP Unlit/Transparent + _BaseMap）。留空则运行时用贴图自动生成。")]
    public Material tapNoteMaterial;
    [Tooltip("小型点击可选成品材质（需 URP Unlit/Transparent + _BaseMap）。留空则运行时用 smallTapTexture 自动生成。")]
    public Material smallTapNoteMaterial;

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
    [Tooltip("普通点击贴图整体不透明度（0~1）；需配合透明背景 PNG 才有半透效果，否则只是把黄底卡片调淡。")]
    public float tapAlpha = 1f;
    [Tooltip("普通点击圆角半径（0~0.5，0.5=半圆/胶囊）。仅影响普通点击使用的圆角网格，不影响其他音符类型。")]
    [Range(0.05f, 0.5f)]
    public float tapCornerRadius = 0.35f;

    private bool textured = false;                 // 当前音符是否启用卡通换皮
    private Texture2D _selectTex;                  // 命中时切换到的"完成"贴图（Select），按类型取自 NoteSpriteLibrary
    private Color visibleBaseColor = Color.black; // 可见态基础色：换皮=白（显贴图），否则=黑
    private float visibleAlpha = 1f;               // 可见态不透明度（换皮时 = tapAlpha）
    // P0：记录本音符类型，供附魔换皮时选定对应皮肤 kind（非连点）
    private bool _isPlainTapType = false;
    private bool _isSmallTapType = false;
    private bool _isWideTapType = false;
    [Header("连点音符运动")]
    [Tooltip("后退减速时长（秒）：命中后以极快初速后退，期间匀减速，到该时长速度降为 0、退到 chainRetreatDist 处。")]
    public float chainTapHoldDuration = 1f;
    [Tooltip("后退距离（世界单位）：命中后从判定线后退多远，再以前进速度回到判定线。Inspector 可调。回退时间不变（仍为 chainTapHoldDuration），距离越小越接近原地停留。")]
    public float chainRetreatDist = 0.1f;

    // P8：连点音符附魔换皮（本体=圆角板+Note_Repeat_N 贴图，附魔直接换整张贴图，无覆盖层）
    private bool _chainEnchanted = false;
    private string _enchantSkillId = null;

    // ---- 连点数字精灵系统（09-17 块B，P9 恢复：果冻底座卡面 + 独立数字精灵叠上面）----
    [Header("连点数字精灵(09-17)")]
    [Tooltip("数字相对卡面局部高度（Inspector 所见局部值，默认 1.63，非世界坐标）。")]
    public float digitYOffset = 1.63f;
    [Tooltip("数字整体缩放（抵消父级非均匀缩放后生效）。")]
    public float digitScaleMul = 1f;
    [Tooltip("双数字两精灵沿 X 间距（世界单位）。")]
    public float digitSpacing = 0.06f;
    private GameObject digitRoot = null;
    private SpriteRenderer[] digitRenders = new SpriteRenderer[2];
    private int digitFigures = 0;
    private Dictionary<string, Sprite> _enchantDigitSpriteCache = new Dictionary<string, Sprite>();
    private bool chainFarthestDone = false;   // D+E：本次命中是否已到撤退最远点（触发递减+回退Select）
    private bool _isChainTextured = false;   // 连点是否为圆角板贴图形态（通用 repeatDigits 可用时 true，否则回退旧圆柱+程序数字）

    private float chainTapDeadline = -1f;        // 可点击截止时间 = nextContactTime + goodWindow
    private float chainHitTime = -999f;           // 本次命中时刻
    private float chainNextContactTime = -999f;   // 重新接触判定线时刻（public 可读）
    private float normalSpeed = 1f;               // 正常前进速度（Init 时按曲速推算）
    private float goodWindow = 0.07f;             // 判定窗口（由 NoteSpawner 传入）

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

    /// <summary>
    /// 换皮音符统一画布：1×1 纯 quad（XZ 平面、双面、UV 铺满）。
    /// 圆角外形完全交给贴图 alpha 通道成形——与链接音节（HoldNote.MakeQuadMesh）同一渲染路径、同一精度，
    /// 修复此前"圆角网格 × 非均匀缩放"造成的边缘坑洼/椭圆畸变（2026-09-25）。
    /// </summary>
    private static Mesh _flatQuadMesh;
    public static Mesh FlatQuadMesh
    {
        get
        {
            if (_flatQuadMesh == null)
            {
                var m = new Mesh { name = "NoteFlatQuad" };
                m.vertices = new Vector3[]
                {
                    new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, 0.5f),   new Vector3(-0.5f, 0f, 0.5f)
                };
                // UV 约定与圆角平板网格一致：uv = (x+0.5, z+0.5)，贴图完整铺满画布
                m.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                // 双面：正反两套绕序，任意视角可见
                m.triangles = new int[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
                m.RecalculateNormals();
                m.RecalculateBounds();
                _flatQuadMesh = m;
            }
            return _flatQuadMesh;
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

    /// <summary>
    /// 静默期预热（2026-09-25 卡顿优化）：构建全部静态音符网格，
    /// 避免音乐起播帧首批音符集中生成时一次性建网格。由 NotePrewarmer 调用。
    /// </summary>
    public static void PrewarmMeshes()
    {
        _ = FlatQuadMesh;
        _ = RoundedRectMesh;
        _ = TapRoundedRectMesh(0.18f);
        _ = CylinderMesh;
    }

    /// <summary>
    /// 预热用采样材质：与 MakeTapMaterial 同一路径，但 alpha=0（不可见）。
    /// 供 NotePrewarmer 用临时物体真渲染两帧，触发 shader 变体编译与纹理 GPU 上传。
    /// </summary>
    public static Material BuildPrewarmMaterial(Texture2D tex)
    {
        Material mat = MakeTapMaterial(tex);
        mat.color = new Color(1f, 1f, 1f, 0f);
        return mat;
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

    /// <summary>P8：取通用连点数字贴图 Note_Repeat{N}（圆角板+数字成品，n 夹取 0..9；取不到返回 null）。</summary>
    private static Texture2D GetChainRepeatTex(int n)
    {
        if (NoteSpriteLibrary.Instance == null) return null;
        var arr = NoteSpriteLibrary.Instance.repeatDigits;
        int i = Mathf.Clamp(n, 0, 9);
        return (arr != null && i < arr.Length && arr[i] != null) ? arr[i].texture : null;
    }

    /// <summary>把主体材质贴图换成 t（兼容 URP _BaseMap 与旧 _MainTex）。</summary>
    private void SetNoteTexture(Texture2D t)
    {
        if (noteMaterial == null || t == null) return;
        string p = noteMaterial.HasProperty("_BaseMap") ? "_BaseMap" : (noteMaterial.HasProperty("_MainTex") ? "_MainTex" : "_BaseMap");
        noteMaterial.SetTexture(p, t);
    }

    /// <summary>P9(恢复09-17) 连点逐数字更新（附魔态）：只换数字精灵为技能数字剩余值，本体保持技能底座。</summary>
    public void UpdateChainEnchantDigit(int remaining)
    {
        if (!_chainEnchanted || string.IsNullOrEmpty(_enchantSkillId)) return;
        SetDigitSprite(remaining, false, true);
    }

    /// <summary>P9(恢复09-17) 附魔次数耗尽：本体与数字复原为通用底座 + 通用数字，继续普通结算。</summary>
    public void RestoreChainBase()
    {
        _chainEnchanted = false;
        if (_isChainTextured && digitRenders[0] != null)
        {
            int rem = (note != null) ? Mathf.Max(1, note.chainTapRemaining) : 1;
            RefreshChainVisual(rem, false, false);
        }
    }

    // ---------------------------------------------------------------------------
    // P9(恢复09-17 块A/B/C)：连点音符 = 果冻底座卡面(Repeat) + 独立数字精灵(repeatDigits[N])
    // ---------------------------------------------------------------------------

    /// <summary>按模式刷新连点本体贴图 + 数字精灵。enchant=附魔态(技能皮肤)；select=命中完成态。</summary>
    private void RefreshChainVisual(int n, bool select, bool enchant)
    {
        if (!_isChainTextured) return;
        // 本体：附魔→技能底座(Note_Repeat_skill)；命中→通用 Select(repeatSelect)；否则通用底座(repeat)
        if (enchant && !string.IsNullOrEmpty(_enchantSkillId))
            SetNoteTexture(NoteSpriteLibrary.GetEnchantSkin(_enchantSkillId, "Note_Repeat"));
        else if (select && NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.repeatSelect != null)
            SetNoteTexture(NoteSpriteLibrary.Instance.repeatSelect.texture);
        else if (NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.repeat != null)
            SetNoteTexture(NoteSpriteLibrary.Instance.repeat.texture);
        SetDigitSprite(n, select, enchant);
    }

    /// <summary>设置数字精灵。单数字放 slot1；双数字(≥10) slot0=十位左 / slot1=个位右。</summary>
    private void SetDigitSprite(int n, bool select, bool enchant)
    {
        if (digitRenders[0] == null) return;
        Sprite dig = null;
        if (enchant && !string.IsNullOrEmpty(_enchantSkillId))
            dig = GetEnchantDigitSprite(_enchantSkillId, n, select);
        else if (NoteSpriteLibrary.Instance != null)
        {
            Sprite[] src = (select && NoteSpriteLibrary.Instance.repeatDigitsSelect != null) ? NoteSpriteLibrary.Instance.repeatDigitsSelect
                        : (NoteSpriteLibrary.Instance.repeatDigits);
            if (src != null && n >= 0 && n < src.Length) dig = src[n];
        }
        if (n >= 10)
        {
            // 双数字：十位 / 个位 沿 X 并排
            int tens = n / 10, ones = n % 10;
            Sprite digT = null, digO = null;
            if (enchant && !string.IsNullOrEmpty(_enchantSkillId))
            {
                digT = GetEnchantDigitSprite(_enchantSkillId, tens, select);
                digO = GetEnchantDigitSprite(_enchantSkillId, ones, select);
            }
            else if (NoteSpriteLibrary.Instance != null)
            {
                Sprite[] src = (select && NoteSpriteLibrary.Instance.repeatDigitsSelect != null) ? NoteSpriteLibrary.Instance.repeatDigitsSelect
                            : (NoteSpriteLibrary.Instance.repeatDigits);
                if (src != null)
                {
                    if (tens >= 0 && tens < src.Length) digT = src[tens];
                    if (ones >= 0 && ones < src.Length) digO = src[ones];
                }
            }
            digitRenders[0].sprite = digT;
            digitRenders[1].sprite = digO;
            float off = (digitSpacing > 0f) ? digitSpacing / Mathf.Max(0.001f, digitScaleMul) : 0f;
            digitRenders[0].transform.localPosition = new Vector3(-off, 0f, 0f);
            digitRenders[1].transform.localPosition = new Vector3(off, 0f, 0f);
            digitFigures = 2;
        }
        else
        {
            digitRenders[0].sprite = null;
            digitRenders[1].sprite = dig;
            digitRenders[1].transform.localPosition = Vector3.zero;
            digitFigures = 1;
        }
    }

    /// <summary>附魔数字贴图(Texture2D)转 Sprite 并缓存（PPU 取对应通用数字精灵，保持一致尺寸）。select=取 _Select 变体。</summary>
    private Sprite GetEnchantDigitSprite(string skillId, int n, bool select = false)
    {
        string key = skillId + "|" + n + (select ? "|S" : "");
        if (_enchantDigitSpriteCache.TryGetValue(key, out var sp) && sp != null) return sp;
        string kind = "Note_Repeat_" + n + (select ? "_Select" : "");
        Texture2D tex = NoteSpriteLibrary.GetEnchantSkin(skillId, kind);
        if (tex == null) return null;
        float ppu = 100f;
        if (NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.repeatDigits != null
            && n >= 0 && n < NoteSpriteLibrary.Instance.repeatDigits.Length
            && NoteSpriteLibrary.Instance.repeatDigits[n] != null)
            ppu = NoteSpriteLibrary.Instance.repeatDigits[n].pixelsPerUnit;
        Sprite s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
        s.name = "EnchantDigit_" + key;
        _enchantDigitSpriteCache[key] = s;
        return s;
    }

    /// <summary>创建连点数字精灵（独立 SpriteRenderer，躺平印在卡面中央）。回退态不调用。</summary>
    private void CreateDigitSprites(int n)
    {
        if (digitRoot != null) return;
        digitRoot = new GameObject("ChainDigits");
        digitRoot.transform.SetParent(transform, false);
        digitRoot.transform.localPosition = new Vector3(0f, digitYOffset, 0f);   // 局部高度（Inspector 所见）
        digitRoot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);       // 躺平朝上（面朝相机下方世界 +Y）
        Vector3 ps = transform.localScale;
        float sx = ps.x > 0.0001f ? ps.x : 1f;
        float sy = ps.y > 0.0001f ? ps.y : 1f;
        float sz = ps.z > 0.0001f ? ps.z : 1f;
        // 抵消父级非均匀缩放。注意：digitRoot 已绕 X 转 90°（躺平），
        // 其 local Y 轴映射到父级 Z（轨道方向）、local Z 映射到父级 Y（厚度，对平躺精灵无视觉影响）。
        // 因此 X 补偿 1/sx、Y（→轨道向）补偿 1/sz；绝不能除以 sy（厚度 0.12），否则数字沿轨道被拉长 ~8 倍成"火苗"。
        digitRoot.transform.localScale = new Vector3(1f / sx, 1f / sz, 1f) * digitScaleMul;

        for (int i = 0; i < 2; i++)
        {
            var go = new GameObject("Digit" + i);
            go.transform.SetParent(digitRoot.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = null;
            sr.sortingOrder = 10;   // 压在卡面之上
            sr.enabled = false;      // 初始隐藏，过粉杠后由可见性逻辑打开
            digitRenders[i] = sr;
        }
        SetDigitSprite(n, false, false);
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
        _isPlainTapType = isPlainTap;
        _isSmallTapType = isSmallTap;
        _isWideTapType = isWideTap;

        // P9(恢复09-17)：连点音符主体 = 果冻底座卡面（NoteSpriteLibrary.repeat，无数字）；
        // 数字由独立数字精灵(repeatDigits[N])叠在卡面上。取不到 repeat 则保持旧圆柱+程序数字形态，改动可回退。
        Texture2D chainBaseTex = (NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.repeat != null)
            ? NoteSpriteLibrary.Instance.repeat.texture : null;
        bool isChainTextured = isChainTap && chainBaseTex != null;
        _isChainTextured = isChainTextured;

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
        textured = (isPlainTap && tapTex != null) || (isWideTap && wideTex != null) || isSmallTextured || isChainTextured;
        Texture2D activeTex = isChainTap ? chainBaseTex : (isWideTap ? wideTex : (isSmallTap ? smallTex : tapTex));   // 实际用于渲染的贴图

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

        // 运行时替换预制体网格（2026-09-25 重构）：换皮音符统一用 1×1 纯 quad 当画布，
        // 圆角外形完全交给贴图 alpha 成形（与链接音节同一精度，修复边缘坑洼/圆弧畸变）；
        // 无贴图兜底：跨轨沿用圆角平板，普通点击沿用圆柱。
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = textured ? FlatQuadMesh : (laneSpan > 1 ? RoundedRectMesh : CylinderMesh);

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
            if (isChainTap && chainBaseTex != null)
            {
                // P9(恢复09-17)：连点底座卡面按贴图原生尺寸 1:1（repeat 精灵 PPU）
                Sprite baseSpr = (NoteSpriteLibrary.Instance != null) ? NoteSpriteLibrary.Instance.repeat : null;
                float chainPPU = (baseSpr != null) ? baseSpr.pixelsPerUnit : 100f;
                xDiameter = chainBaseTex.width / chainPPU;
                zDiameter = chainBaseTex.height / chainPPU;
                transform.localScale = new Vector3(xDiameter * tapVisualScaleMul, 0.12f, zDiameter * tapVisualScaleMul);
            }
            else if (isWideTap && wideTex != null)
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
        // 判定半宽跟随视觉（2026-09-25）：换皮后为 1×1 quad，世界 X 半宽 = localScale.x/2
        // （已含贴图原生尺寸与 tapVisualScaleMul）；无贴图兜底（圆柱/圆角平板）沿用 noteRadius。
        noteHalfSize = textured ? transform.localScale.x * 0.5f : noteRadius;
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
                    noteMaterial = MakeTapMaterial(activeTex);   // P8 连点：强制用通用 Repeat 贴图建材质，不复用 tap 模板（其自带 Note_Tap 贴图）
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

        if (isChainTap && isChainTextured)
            CreateDigitSprites(Mathf.Max(1, chainTapCount));   // P9(恢复09-17)：底座卡面 + 独立数字精灵
        else if (isChainTap && !isChainTextured)
            CreateChainCountText(Mathf.Max(1, chainTapCount));  // 回退态：旧圆柱+程序数字（repeat 未配置时）
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
            // D+E(恢复09-17)：首次越过撤退最远点 → 本体/数字从 Select 态切回普通态。
            // （递减已在命中时刻完成——2026-09-25 用户决策，此处只做显示切换）
            if (!chainFarthestDone && te >= chainTapHoldDuration && note != null)
            {
                chainFarthestDone = true;
                int rem = note.OnChainRetreatFarthest();
                RefreshChainVisual(rem, false, _chainEnchanted);
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
            if (chainCountRenderer != null) chainCountRenderer.enabled = true;
            if (digitRenders[0] != null) { digitRenders[0].enabled = true; digitRenders[1].enabled = true; }
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

        // 记录本次命中时刻，并算出「重新接触判定线」时刻与可点击截止时间
        chainHitTime = songTime;
        float v = Mathf.Max(0.001f, normalSpeed);
        chainNextContactTime = songTime + chainTapHoldDuration + chainRetreatDist / v;
        chainTapDeadline = chainNextContactTime + goodWindow;

        // 本次命中重置最远点标记（贴图/回退两形态都需要；原先只重置贴图形态是隐患）
        chainFarthestDone = false;

        if (_isChainTextured)
        {
            // P9(恢复09-17 块C)：命中 → 本体切 Select（完成）态、数字切 Select 数字；保留 pop 弹跳反馈。
            // 附魔态：本体/数字都走技能 Select 皮肤；非附魔态：走通用 repeatSelect + repeatDigitsSelect。
            RefreshChainVisual(remaining, true, _chainEnchanted);
        }
        else
        {
            // 旧圆柱回退形态：进度染色 + 程序数字
            float progress = 1f - Mathf.Clamp01(remaining / (float)Mathf.Max(1, required));
            SetChainBodyColor(Color.Lerp(Color.black, Color.white, progress));
            if (chainCountText != null) chainCountText.text = remaining.ToString();
        }
        // 不再钉死位置：下一帧 Update 按 te 平滑接管（te≈0 即在判定线，无跳变）。
        // 每次命中叠加一次「白闪 + 放大缩小」反馈（不破坏进度色，淡出后回归原进度色）。
        StartCoroutine(ChainTapHitPopCo());
    }

    /// <summary>
    /// 回退中断窗口（2026-09-25）：命中后的减速后退期内（chainTapHoldDuration 内）再次点击可立即重新命中。
    /// 命中表现/行为与正常连点命中完全一致（递减在命中时刻发生，中断无需任何补偿），
    /// 打点够快即可连续触发，缩短连点完成时间。
    /// </summary>
    public bool IsRetreatInterruptible(float songTime)
    {
        return chainTapDeadline >= 0f && (songTime - chainHitTime) < chainTapHoldDuration;
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

    /// <summary>命中时把材质贴图切换到"完成"版（Select）。贴图属性名兼容 URP Unlit(_BaseMap) 与旧(_MainTex)。
    /// 连点音符同时把数字精灵切到 Select 版（附魔则技能 Select 数字）。</summary>
    private void ApplySelectTexture()
    {
        if (noteMaterial == null || _selectTex == null) return;
        string texProp = noteMaterial.HasProperty("_BaseMap") ? "_BaseMap" : (noteMaterial.HasProperty("_MainTex") ? "_MainTex" : "_BaseMap");
        noteMaterial.SetTexture(texProp, _selectTex);
        if (isChainTap && _isChainTextured && digitRenders[0] != null)
        {
            int rem = (note != null) ? Mathf.Max(0, note.chainTapRemaining) : 0;
            SetDigitSprite(rem, true, _chainEnchanted);
        }
    }

    /// <summary>
    /// P0 附魔换皮：把本音符整体替换成 skillId 对应皮肤集里的贴图（普通=Note_Tap / 小型=Note_Tap_Small / 跨轨=Note_Wide，
    /// 含命中 Select 版）。成功返回 true；该技能没有皮肤集（如炸弹雨）返回 false，调用方回退到原发光染色。
    /// 连点音符（isChainTap）：本体=圆角板+Note_Repeat{N} 通用贴图；附魔直接换 Note_Repeat{N}_skill 整图，逐数字更新（P8）。
    /// </summary>
    public bool ApplyEnchantSkin(string skillId)
    {
        if (noteMaterial == null) return false;
        // P9(恢复09-17)：连点音符 = 底座卡面 + 数字精灵。附魔：本体换技能底座(Note_Repeat_skill)、数字换技能数字(req)。
        // 回退态（无通用贴图的旧圆柱）不支持贴图换皮，返回 false 走发光染色。
        if (isChainTap)
        {
            if (!_isChainTextured) return false;
            int req = (note != null) ? note.chainTapRequired : 0;
            if (req <= 0) return false;
            Texture2D skinBase = NoteSpriteLibrary.GetEnchantSkin(skillId, "Note_Repeat");   // 通用底座(技能色，无数字)
            if (skinBase == null) return false; // 无皮肤集：回退发光染色
            _enchantSkillId = skillId;
            _chainEnchanted = true;
            RefreshChainVisual(req, false, true);   // 本体=技能底座、数字=技能数字 req
            return true;
        }
        string kind = _isWideTapType ? "Note_Wide" : (_isSmallTapType ? "Note_Tap_Small" : "Note_Tap");
        Texture2D skin2 = NoteSpriteLibrary.GetEnchantSkin(skillId, kind);
        if (skin2 == null) return false; // 无皮肤集：回退发光染色
        Texture2D skinSelect = NoteSpriteLibrary.GetEnchantSkin(skillId, kind + "_Select");
        string texProp = noteMaterial.HasProperty("_BaseMap") ? "_BaseMap" : (noteMaterial.HasProperty("_MainTex") ? "_MainTex" : "_BaseMap");
        noteMaterial.SetTexture(texProp, skin2);
        if (skinSelect != null) _selectTex = skinSelect;   // 命中后切到皮肤 Select 版
        textured = true;                                   // 命中走 Select（完成）贴图路径
        SetNoteTint(Color.white);                          // 以贴图原色显示
        return true;
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
        // 连点数字精灵（09-17）：与卡面同步淡入淡出
        if (digitRenders[0] != null && note.isVisible)
        {
            for (int i = 0; i < 2; i++)
            {
                if (digitRenders[i] != null && digitRenders[i].sprite != null)
                    digitRenders[i].enabled = alpha > 0.01f;
            }
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
    private static Material MakeTapMaterial(Texture2D tex)
    {
        // 2026-09-25 修复：改用 Sprites/Default——与链接音节（HoldNote）完全同一渲染路径。
        // 根因：URP/Unlit 是不透明 shader，其无 _Surface/_Blend 属性（那是 URP/Lit 的），
        // 旧的"强制透明"设置对它无效；换纯 quad 画布后，贴图 alpha=0 的区域被渲染成
        // 不透明 RGB 底色（"本该透明的地方不透明"）。Sprites/Default 自带 alpha 混合，
        // 且 material.color 可直接调 tint/alpha（SetNoteTint/SetAlpha 依赖此路径）。
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null)
        {
            Debug.LogWarning("[NoteMover] Sprites/Default shader not found, falling back to Unlit/Transparent");
            sh = Shader.Find("Unlit/Transparent");
        }
        var mat = new Material(sh);
        mat.mainTexture = tex;
        mat.color = Color.white;
        mat.SetInt("_Cull", 0); // 双面，任意视角可见
        return mat;
    }
}
