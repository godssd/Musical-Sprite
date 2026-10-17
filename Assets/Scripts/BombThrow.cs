using UnityEngine;

/// <summary>
/// 布姆炸弹投掷：一个发光炸弹沿抛物线从 launchPoint 飞到 targetPoint，
/// 飞行中拖出发光尾迹（发射粒子），落地瞬间触发 BombExplosion 爆炸。
///
/// 【占位约定】当前用长方体（Cube）做炸弹外观占位；等你的 spine / 图片素材就绪后，
/// 只需把预制体拖到 bombVisualPrefab 槽即可替换，无需改代码（后续若要 spine 骨骼动画，
/// 在 Ensure() 里用 SkeletonAnimation 取代 Cube 即可，逻辑层不动）。
///
/// 运行流程：Start（Play 中）→ Ensure 构建外观/拖尾/爆炸 → 若 autoThrowOnStart 则 Throw()；
/// Update 推进抛物线；t≥1 时关炸弹、停拖尾、在落点 ExplodeAt。
/// ⛔ 所有运行时创建的 GameObject / Material / Texture 均 HideFlags.DontSave，不污染场景存档。
/// </summary>
public class BombThrow : MonoBehaviour
{
    [Header("弹道")]
    [Tooltip("发射起点（世界坐标）")]
    public Vector3 launchPoint = new Vector3(-6f, 0.5f, 0f);
    [Tooltip("落地终点（世界坐标，爆炸在此发生）")]
    public Vector3 targetPoint = new Vector3(0f, 0f, 0f);
    [Tooltip("抛物线拱高（顶点高出直线的高度）")]
    public float arcHeight = 3f;
    [Tooltip("飞行总时长（秒）")]
    public float duration = 1.2f;
    [Tooltip("进 Play 自动投掷一次（方便验收）")]
    public bool autoThrowOnStart = true;

    [Header("炸弹外观")]
    [Tooltip("炸弹视觉预制体；留空则用长方体占位。素材就绪后把 spine/图片预制体拖进来替换")]
    public GameObject bombVisualPrefab;
    [Tooltip("占位炸弹发光颜色（HDR：分量 >1 被 Bloom 泛光）")]
    public Color bombColor = new Color(3.0f, 1.2f, 0.4f, 1f);
    [Tooltip("占位长方体自转速度（度/秒），仅占位用")]
    public float bombSpin = 360f;

    [Header("拖尾 / 发射粒子")]
    [Tooltip("拖尾发光颜色（HDR）")]
    public Color trailColor = new Color(2.5f, 1.3f, 0.5f, 1f);
    [Tooltip("发射口闪光强度（落地无关，纯发射瞬间点缀）")]
    public Color muzzleColor = new Color(3.0f, 2.0f, 1.0f, 1f);

    [Header("爆炸")]
    [Tooltip("爆炸组件；留空则自动创建子物体 BombExplosion（用默认参数）。也可手拖一个自定义参数的进来")]
    public BombExplosion explosion;

    private GameObject _bomb;
    private ParticleSystem _trail;
    private ParticleSystem _muzzle;
    private Material _bombMat, _trailMat, _muzzleMat;
    private Texture2D _dot;
    private bool _flying;
    private float _t;

    void Start()
    {
        if (Application.isPlaying)
        {
            Ensure();
            if (autoThrowOnStart) Throw();
        }
    }

    void OnDisable()
    {
        DestroyRuntime(ref _bombMat);
        DestroyRuntime(ref _trailMat);
        DestroyRuntime(ref _muzzleMat);
        DestroyRuntime(ref _dot);
    }

    void OnDestroy()
    {
        DestroyRuntime(ref _bombMat);
        DestroyRuntime(ref _trailMat);
        DestroyRuntime(ref _muzzleMat);
        DestroyRuntime(ref _dot);
    }

    private static void DestroyRuntime<T>(ref T o) where T : Object
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        o = null;
    }

    /// <summary>构建外观 / 拖尾 / 发射闪光 / 爆炸（幂等）。</summary>
    private void Ensure()
    {
        if (_dot == null) _dot = MakeSoft();

        // 炸弹外观：优先预制体，否则长方体占位
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
                _bombMat = MakeLitEmissive(bombColor);
                var mr = _bomb.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = _bombMat;
            }
            _bomb.hideFlags = HideFlags.DontSave;
            _bomb.SetActive(false);
        }

        // 拖尾（发光尾迹，作为炸弹子物体跟随）
        if (_trail == null)
        {
            var trailGO = new GameObject("BombTrail");
            trailGO.transform.SetParent(_bomb.transform, false);
            trailGO.hideFlags = HideFlags.DontSave;
            _trail = trailGO.AddComponent<ParticleSystem>();
            // 新建 ParticleSystem 默认 playOnAwake=true，会立即开始播放；
            // 必须先彻底 Stop，否则后续设置 duration/startLifetime 等会报 "Setting the duration while system is still playing"。
            _trail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _trail.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // 世界空间：粒子留在身后
            main.startLifetime = 0.3f;
            main.startSpeed = 0f;
            main.startSize = 0.4f;
            main.startColor = Color.white;
            main.maxParticles = 128;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = _trail.emission;
            em.rateOverTime = 40f;
            var sh = _trail.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.15f;
            sh.randomDirectionAmount = 0f;
            _trailMat = MakeAdditive(trailColor, _dot);
            var r = _trail.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _trailMat;
            r.sortingOrder = 24;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            _trail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // 发射口闪光（一次性小爆发）
        if (_muzzle == null)
        {
            var mGO = new GameObject("BombMuzzle");
            mGO.transform.SetParent(transform, false);
            mGO.hideFlags = HideFlags.DontSave;
            _muzzle = mGO.AddComponent<ParticleSystem>();
            // 新建 ParticleSystem 默认 playOnAwake=true，会立即开始播放；
            // 必须先彻底 Stop，否则后续设置 duration/startLifetime 等会报 "Setting the duration while system is still playing"。
            _muzzle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _muzzle.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.duration = 0.25f;
            main.startLifetime = 0.25f;
            main.startSpeed = 3f;
            main.startSize = 0.5f;
            main.startColor = Color.white;
            main.maxParticles = 64;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = _muzzle.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 12, 12) });
            var sh = _muzzle.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.15f;
            sh.randomDirectionAmount = 1f;
            var sz = _muzzle.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));
            var col = _muzzle.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) };
            g.colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) };
            col.color = new ParticleSystem.MinMaxGradient(g);
            _muzzleMat = MakeAdditive(muzzleColor, _dot);
            var r = _muzzle.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _muzzleMat;
            r.sortingOrder = 26;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            _muzzle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // 爆炸：未指定则自动创建子物体
        if (explosion == null)
        {
            var exGO = new GameObject("BombExplosion");
            exGO.transform.SetParent(transform, false);
            exGO.hideFlags = HideFlags.DontSave;
            explosion = exGO.AddComponent<BombExplosion>();
        }
    }

    /// <summary>投掷一次（从 launchPoint 飞向 targetPoint）。Play 中有效。</summary>
    [ContextMenu("投掷一次（Play 中可用）")]
    public void Throw()
    {
        if (!Application.isPlaying) return;
        Ensure();
        _t = 0f;
        _flying = true;
        _bomb.SetActive(true);
        _bomb.transform.position = launchPoint;
        _bomb.transform.rotation = Quaternion.identity;
        if (_trail != null) _trail.Play(true);
        if (_muzzle != null)
        {
            _muzzle.transform.position = launchPoint;
            _muzzle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _muzzle.Play(true);
        }
    }

    void Update()
    {
        if (!_flying) return;

        _t += Time.deltaTime / Mathf.Max(0.01f, duration);
        if (_t >= 1f)
        {
            _t = 1f;
            _flying = false;
            _bomb.SetActive(false);
            if (_trail != null) _trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (explosion != null) explosion.ExplodeAt(targetPoint);
            return;
        }

        // 抛物线：直线插值 + 拱形高度（顶点在 t=0.5）
        Vector3 basePos = Vector3.Lerp(launchPoint, targetPoint, _t);
        float arc = arcHeight * 4f * _t * (1f - _t);
        basePos.y += arc;
        _bomb.transform.position = basePos;

        // 占位长方体自转（素材替换后可删）
        _bomb.transform.Rotate(0f, 0f, bombSpin * Time.deltaTime, Space.Self);
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
            Debug.LogWarning("[BombThrow] 找不到 MusicalSprite/BombVFXAdditive，拖尾将显示为 magenta。");
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

    /// <summary>占位炸弹本体材质：直接用 URP/Unlit 不透明 + HDR 底色，靠 Bloom 提亮。</summary>
    private static Material MakeLitEmissive(Color hdrColor)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null)
        {
            Debug.LogWarning("[BombThrow] 找不到 URP/Unlit/Lit shader，占位炸弹将显示为 magenta。");
            sh = Shader.Find("Hidden/InternalErrorShader");
        }
        var m = new Material(sh);
        m.hideFlags = HideFlags.DontSave;
        m.SetColor("_BaseColor", hdrColor);
        m.SetColor("_Color", hdrColor);
        m.color = hdrColor;
        return m;
    }
}
