using UnityEngine;

/// <summary>
/// 布姆炸弹「飞行光效」的**独立循环版**（已从投掷 / 爆炸中剥离，成为两个特效之一）。
///
/// 【用途】沙盒中循环预览炸弹本体的发光观感（发光占位体 + 环绕光晕），
///   无需真实抛物线弹道 —— 弹道进游戏后再按玩法做，这里只做「原地循环」的发光体。
///
/// 组成：
///   - 炸弹外观：立方体占位（bombVisualPrefab 槽可换 spine/图片），原地轻微上下浮动 + 自转 + 自发光脉动；
///   - 环绕光晕：一圈持续发射的加法发光粒子（即「飞行拖尾」的静止版），循环不灭。
///
/// 触发：Play 中 Start 自动开始循环（autoLoopOnStart）；也可手动 ContextMenu 开关。
/// ⛔ 运行时创建的 GameObject / Material / Texture 均 HideFlags.DontSave，不污染场景存档。
/// </summary>
public class BombGlow : MonoBehaviour
{
    [Header("炸弹外观")]
    [Tooltip("炸弹视觉预制体；留空则用长方体占位。素材就绪后把 spine/图片预制体拖进来替换")]
    public GameObject bombVisualPrefab;
    [Tooltip("占位炸弹发光颜色（HDR：分量 >1 被 Bloom 泛光）")]
    public Color bombColor = new Color(3.0f, 1.2f, 0.4f, 1f);
    [Tooltip("占位长方体自转速度（度/秒），仅占位用")]
    public float bombSpin = 120f;
    [Tooltip("上下浮动幅度（世界单位）")]
    public float bobHeight = 0.25f;
    [Tooltip("浮动频率")]
    public float bobSpeed = 2f;
    [Tooltip("自发光脉动频率")]
    public float pulseSpeed = 3f;
    [Tooltip("自发光脉动幅度（相对 bombColor 的比例）")]
    public float pulseAmount = 0.4f;
    [Tooltip("占位立方体缩放")]
    public float bombScale = 0.7f;

    [Header("环绕光晕")]
    [Tooltip("光晕颜色（HDR）")]
    public Color auraColor = new Color(2.5f, 1.3f, 0.5f, 1f);
    [Tooltip("每秒发射粒子数")]
    public float auraRate = 60f;
    [Tooltip("光晕粒子尺寸")]
    public float auraSize = 0.5f;
    [Tooltip("光晕向外飘散速度")]
    public float auraSpeed = 0.6f;
    [Tooltip("光晕粒子寿命（秒）")]
    public float auraLifetime = 0.5f;

    [Header("运行")]
    [Tooltip("进 Play 自动开始循环发光")]
    public bool autoLoopOnStart = true;
    [Tooltip("渲染排序")]
    public int sortingOrder = 24;

    private GameObject _bomb;
    private ParticleSystem _aura;
    private Material _bombMat, _auraMat;
    private Texture2D _dot;
    private bool _looping;

    void Start()
    {
        if (Application.isPlaying && autoLoopOnStart) StartLoop();
    }

    void OnDisable()
    {
        DestroyRuntime(ref _bombMat);
        DestroyRuntime(ref _auraMat);
        DestroyRuntime(ref _dot);
    }

    void OnDestroy()
    {
        DestroyRuntime(ref _bombMat);
        DestroyRuntime(ref _auraMat);
        DestroyRuntime(ref _dot);
    }

    private static void DestroyRuntime<T>(ref T o) where T : Object
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        o = null;
    }

    /// <summary>构建外观 / 光晕（幂等）。</summary>
    private void Ensure()
    {
        if (_dot == null) _dot = MakeSoft();

        if (_bomb == null)
        {
            if (bombVisualPrefab != null)
            {
                _bomb = Instantiate(bombVisualPrefab, transform);
            }
            else
            {
                _bomb = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _bomb.transform.SetParent(transform, false);
                _bomb.transform.localScale = Vector3.one * bombScale;
                _bombMat = MakeLitEmissive(bombColor);
                var mr = _bomb.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = _bombMat;
            }
            _bomb.hideFlags = HideFlags.DontSave;
            _bomb.SetActive(false);
        }

        if (_aura == null)
        {
            var auraGO = new GameObject("BombAura");
            auraGO.transform.SetParent(_bomb.transform, false);   // 跟随炸弹浮动 / 自转
            auraGO.hideFlags = HideFlags.DontSave;
            _aura = auraGO.AddComponent<ParticleSystem>();
            // 新建 ParticleSystem 默认 playOnAwake=true，会立即开始播放；
            // 必须先彻底 Stop，否则后续设置 duration/startLifetime 等会报 "Setting the duration while system is still playing"。
            _aura.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _aura.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = auraLifetime;
            main.startSpeed = auraSpeed;
            main.startSize = auraSize;
            main.startColor = Color.white;
            main.maxParticles = 256;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = _aura.emission;
            em.rateOverTime = auraRate;
            var sh = _aura.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = bombScale * 0.6f;
            sh.randomDirectionAmount = 1f;                         // 各向同性向外飘
            var sz = _aura.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(1f, 1.0f)));
            _auraMat = MakeAdditive(auraColor, _dot);
            var r = _aura.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _auraMat;
            r.sortingOrder = sortingOrder;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            _aura.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    /// <summary>开始原地循环发光（Play 中有效）。</summary>
    [ContextMenu("开始循环发光（Play 中可用）")]
    public void StartLoop()
    {
        if (!Application.isPlaying) return;
        Ensure();
        _looping = true;
        _bomb.SetActive(true);
        if (_aura != null) { _aura.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); _aura.Play(true); }
    }

    /// <summary>停止循环发光。</summary>
    [ContextMenu("停止循环发光")]
    public void StopLoop()
    {
        _looping = false;
        if (_bomb != null) _bomb.SetActive(false);
        if (_aura != null) _aura.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void Update()
    {
        if (!_looping || _bomb == null) return;

        float t = Time.time;
        // 上下浮动（原地，不移动 x/z）
        _bomb.transform.localPosition = new Vector3(0f, Mathf.Sin(t * bobSpeed) * bobHeight, 0f);
        // 自转
        _bomb.transform.Rotate(0f, 0f, bombSpin * Time.deltaTime, Space.Self);
        // 自发光脉动（让发光体有「呼吸」生命力）
        if (_bombMat != null)
        {
            var c = bombColor;
            float k = 1f + Mathf.Sin(t * pulseSpeed) * pulseAmount;
            _bombMat.SetColor("_EmissiveColor", c * k);
        }
    }

    // ---------------- 构建辅助 ----------------

    private static Texture2D MakeSoft()
    {
        const int n = 64;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        t.hideFlags = HideFlags.DontSave;
        float c = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / c;
                float a = Mathf.Clamp01(1f - d);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        }
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        t.anisoLevel = 1;
        t.Apply();
        return t;
    }

    /// <summary>URP 加法材质（HDR 注入色 + 灰度贴图 + Bloom）。</summary>
    private static Material MakeAdditive(Color hdrColor, Texture2D tex)
    {
        var sh = Shader.Find("MusicalSprite/BombVFXAdditive");
        if (sh == null)
        {
            Debug.LogWarning("[BombGlow] 找不到 MusicalSprite/BombVFXAdditive，光晕将显示为 magenta。");
            sh = Shader.Find("Hidden/InternalErrorShader");
        }
        var m = new Material(sh);
        m.hideFlags = HideFlags.DontSave;
        if (tex != null)
        {
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            m.SetTexture("_BaseMap", tex);
        }
        m.SetColor("_BaseColor", hdrColor);
        m.SetColor("_Color", hdrColor);
        m.color = hdrColor;
        return m;
    }

    /// <summary>占位炸弹本体材质：直接用 URP/Unlit 不透明 + HDR 底色，靠 Bloom 提亮。不依赖 URP/Lit 的 _EMISSION keyword（不同版本名字/启用方式容易变）。</summary>
    private static Material MakeLitEmissive(Color hdrColor)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null)
        {
            Debug.LogWarning("[BombGlow] 找不到 URP/Unlit/Lit shader，占位炸弹将显示为 magenta。");
            sh = Shader.Find("Hidden/InternalErrorShader");
        }
        var m = new Material(sh);
        m.hideFlags = HideFlags.DontSave;
        m.SetColor("_BaseColor", hdrColor);
        m.SetColor("_Color", hdrColor);
        m.color = hdrColor;
        // URP/Unlit Opaque 默认就是 surface=0，不需要 transparent keyword；保留不透明避免穿帮。
        return m;
    }
}
