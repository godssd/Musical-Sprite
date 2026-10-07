using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 冲击波附加特效（P4）：
///   ① 碰撞火花   —— 只在【中缝那条线】迸发，朝 ±Z（两侧）/ ±Y（上下）放射；前进单侧加强。
///   ② 后方能量注入 —— 起点固定在【判定线侧（身后）】且只在中缝附近一小段，朝中缝射出；
///                     追求"一道连贯的窄波"：极低发射率 + 中缝收窄发射口 + 长 Trail，
///                     不做铺满画面的粒子雨。
///   ③ 消散       —— shader 从【身后 + 两侧 z 边缘】侵蚀（头部受保护）；
///                     粒子飘散从【身后段】生成并朝【上方 + Z 两侧】飘散。
///
/// 用法：由 ShockwavePreview 自动查找 / 创建（挂在不随动的对象下，粒子用世界空间模拟）。
/// 全部参数 Inspector 可调；enableXXX 关掉即可完全回退。
/// </summary>
[ExecuteAlways]
public class ShockwaveVFX : MonoBehaviour
{
    public ShockwavePreview preview;

    [Header("公共资源（留空则自动创建 / 自动加载占位图）")]
    public Texture2D particleTexture;
    public bool autoCreateMaterials = true;

    [Header("① 碰撞火花（中缝线，朝两侧 ±Z / 上下 ±Y 放射）")]
    public bool enableSpark = true;
    [Tooltip("火花线离地高度")]
    public float sparkY = 0.35f;
    [Tooltip("基础每秒发射数（对峙时保持很低）")]
    public float sparkBaseRate = 18f;
    [Tooltip("前进加强：目标倍率每超出 1.0 之 0.1 增加的发射数")]
    public float sparkPerScale = 18f;
    [Tooltip("水平两侧（±Z）迸发速度")]
    public float sparkSideZ = 2.2f;
    [Tooltip("上下（±Y）迸发速度")]
    public float sparkUp = 1.4f;
    [Tooltip("X 方向微小发散速度")]
    public float sparkSideX = 0.25f;
    public float sparkLifetime = 0.65f;
    public float sparkSize = 0.22f;
    [Tooltip("前进侧的额外爆发间隔（秒）")]
    public float sparkBurstInterval = 0.2f;
    [Tooltip("每次爆发的粒子数（随倍率缩放）")]
    public int sparkBurstCount = 6;
    public Color sparkColorRed = new Color(1.6f, 0.85f, 0.75f);
    public Color sparkColorBlue = new Color(0.75f, 1.0f, 1.6f);

    [Header("② 后方能量注入（暂时搁置：默认关闭，等冲击波本体与火花调好再开）")]
    public bool enableInject = false;
    public float injectY = 0.45f;
    [Tooltip("每秒发射数。越低越像一道波；建议 0.8~3")]
    public float injectRate = 1.5f;
    public float injectSpeed = 3.0f;
    [Tooltip("距离加强系数：飞行距离每增加 1 单位，发射数 ×(1+该值)、亮度 ×(1+该值×0.5)。建议 ≤0.25，不然远处又变粒子雨")]
    public float injectFarBoost = 0.22f;
    public float injectSize = 0.18f;
    [Tooltip("发射口在 Z（场地纵深）上的半宽：0.5=只在中缝附近 1 单位宽，越小越集中")]
    public float injectHalfWidthZ = 0.5f;
    [Tooltip("发射口在 Y 上的高度范围")]
    public float injectSpreadY = 0.5f;
    [Header("②-b 注入拖尾（连贯感的关键：把粒子串成光带）")]
    public bool enableInjectTrail = true;
    [Tooltip("拖尾长度（占粒子生命周期的比例 0~1）。越长越连贯")]
    [Range(0f, 1f)] public float trailLifetime = 0.9f;
    [Tooltip("拖尾宽度（相对粒子大小的比例 0~1）")]
    [Range(0f, 1f)] public float trailWidth = 0.75f;
    [Tooltip("拖尾采样间距：越小拖尾越平滑（性能略降）")]
    public float trailMinVertex = 0.03f;

    [Header("③ 消散（shader 边缘侵蚀 + 粒子飘散）——粒子默认关闭，只保留 shader 侵蚀")]
    public bool enableDrift = false;
    [Tooltip("循环飘散每秒发射数（待机轻微）")]
    public float driftBaseRate = 8f;
    [Tooltip("前进加强：倍率每超出 1.0 之 0.1 增加的飘散发射数")]
    public float driftPerScale = 12f;
    public float driftSize = 0.09f;
    public float driftLifetime = 1.4f;
    [Tooltip("粒子上飘速度")]
    public float driftRise = 0.35f;
    [Tooltip("粒子朝 Z 两侧散开的速度")]
    public float driftSpreadZ = 0.70f;
    [Tooltip("生成区占墙身后段的比例（只从身后飘散，不从头部）")]
    [Range(0.1f, 1f)] public float driftBackPortion = 0.45f;
    [Tooltip("待机时的 shader 边缘侵蚀量（0=关）。默认 0：噪点/斑驳观感差，先关掉")]
    [Range(0f, 1f)] public float dissolveIdle = 0f;
    [Tooltip("前进时额外增加的侵蚀量")]
    [Range(0f, 1f)] public float dissolveAdvance = 0f;

    private ParticleSystem _sparkRed, _sparkBlue;
    private ParticleSystem _injectRed, _injectBlue;
    private ParticleSystem _driftRed, _driftBlue;
    private Material _matSparkR, _matSparkB, _matInjectR, _matInjectB, _matDriftR, _matDriftB;

    private float _sparkBurstTR, _sparkBurstTB;

    void OnEnable() { Ensure(); }

    void Ensure()
    {
        if (preview == null) preview = FindFirstObjectByType<ShockwavePreview>();
        if (particleTexture == null)
        {
#if UNITY_EDITOR
            particleTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/VFX/Spark_Dot_Placeholder.png");
#endif
        }

        if (_sparkRed == null)
        {
            _matSparkR = MakeMaterial(sparkColorRed);
            _matSparkB = MakeMaterial(sparkColorBlue);
            _matInjectR = MakeMaterial(sparkColorRed);
            _matInjectB = MakeMaterial(sparkColorBlue);
            _matDriftR = MakeMaterial(sparkColorRed * 0.6f);
            _matDriftB = MakeMaterial(sparkColorBlue * 0.6f);

            _sparkRed = CreateSystem("Spark_Red", _matSparkR);
            _sparkBlue = CreateSystem("Spark_Blue", _matSparkB);
            _injectRed = CreateSystem("Inject_Red", _matInjectR, true);
            _injectBlue = CreateSystem("Inject_Blue", _matInjectB, true);
            _driftRed = CreateSystem("Drift_Red", _matDriftR);
            _driftBlue = CreateSystem("Drift_Blue", _matDriftB);
        }
    }

    // ------------------------------------------------------------------ 构建

    private Material MakeMaterial(Color hdrColor)
    {
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var m = new Material(sh);
        m.SetFloat("_Surface", 1f);      // Transparent
        m.SetFloat("_Blend", 2f);        // Additive
        m.SetFloat("_SrcBlend", 5f);     // SrcAlpha
        m.SetFloat("_DstBlend", 1f);     // One
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        if (particleTexture != null) m.SetTexture("_BaseMap", particleTexture);
        m.SetColor("_BaseColor", hdrColor);   // >1 的 HDR 颜色，配合 Bloom 出泛光
        m.hideFlags = HideFlags.DontSave;
        return m;
    }

    private ParticleSystem CreateSystem(string name, Material mat, bool withTrail = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.hideFlags = HideFlags.DontSave;
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;  // 世界空间：不随父物体拉伸
        main.startSpeed = 0f;
        main.startSize = 0.1f;
        main.startLifetime = 1f;
        main.startColor = Color.white;
        main.maxParticles = 512;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var em = ps.emission;
        em.rateOverTime = 0f;

        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(0.05f, 0.4f, 7f);
        sh.randomDirectionAmount = 0f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(MakeFadeGradient());

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;   // 用 sharedMaterial，避免运行时实例化材质造成泄漏
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sortingOrder = 21;

        if (withTrail)
        {
            var tr = ps.trails;
            tr.enabled = enableInjectTrail;
            tr.mode = ParticleSystemTrailMode.PerParticle;
            tr.ratio = 1f;
            tr.lifetime = new ParticleSystem.MinMaxCurve(trailLifetime);
            tr.minVertexDistance = trailMinVertex;
            tr.widthOverTrail = new ParticleSystem.MinMaxCurve(trailWidth);
            tr.inheritParticleColor = true;
            tr.dieWithParticles = true;
            tr.worldSpace = true;
            r.trailMaterial = mat;
        }

        return ps;
    }

    private static Gradient MakeFadeGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.25f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    private static void SetRate(ParticleSystem ps, float rate)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.rateOverTime = new ParticleSystem.MinMaxCurve(Mathf.Max(0f, rate));
    }

    /// <summary>Velocity Over Lifetime 三条轴必须同模式，否则 Unity 报错。
    /// 这里统一用 RandomBetweenTwoConstants（min=max 即恒定），可同时支持恒定值与随机范围。</summary>
    private static ParticleSystem.MinMaxCurve V(float min, float max)
    {
        return new ParticleSystem.MinMaxCurve(min, max);
    }

    private static void SetVelXYZ(ParticleSystem ps, float vx, float vy, float vz)
    {
        if (ps == null) return;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = V(vx, vx);
        vel.y = V(vy, vy);
        vel.z = V(vz, vz);
    }

    // ------------------------------------------------------------------ 每帧

    // 用 LateUpdate：必须排在 ShockwavePreview.Update -> MeshGenerator.SyncMaterial 之后，
    // 否则本帧写的 _Dissolve 会被 SyncMaterial 用基础值覆盖掉。
    void LateUpdate()
    {
        if (preview == null) preview = FindFirstObjectByType<ShockwavePreview>();
        if (preview == null) return;
        Ensure();

        var rw = preview.redWall;
        var bw = preview.blueWall;
        if (rw == null || bw == null) return;

        var rr = rw.GetComponent<Renderer>();
        var br = bw.GetComponent<Renderer>();
        if (rr == null || br == null) return;

        float seamX = (rr.bounds.max.x + br.bounds.min.x) * 0.5f;
        float y = preview.transform.position.y;

        float redScale = preview.redScaleValue;
        float blueScale = preview.blueScaleValue;
        float redAdv = Mathf.Max(0f, redScale - 1f);
        float blueAdv = Mathf.Max(0f, blueScale - 1f);
        float dt = Mathf.Max(1e-4f, Time.deltaTime);

        // ① 火花：中缝线连续发射；前进侧额外 burst（对峙呼吸循环时 scale≈1，只有 base 的微弱量）
        SetRate(_sparkRed, enableSpark ? sparkBaseRate + redAdv * 10f * sparkPerScale : 0f);
        SetRate(_sparkBlue, enableSpark ? sparkBaseRate + blueAdv * 10f * sparkPerScale : 0f);
        PlaceSpark(_sparkRed, seamX, y + sparkY);
        PlaceSpark(_sparkBlue, seamX, y + sparkY);
        ApplySparkMain(_sparkRed, sparkLifetime, sparkSize);
        ApplySparkMain(_sparkBlue, sparkLifetime, sparkSize);
        SetSparkVel(_sparkRed, 1f);
        SetSparkVel(_sparkBlue, 1f);
        if (_matSparkR != null) _matSparkR.SetColor("_BaseColor", sparkColorRed * (1f + redAdv * 1.5f));
        if (_matSparkB != null) _matSparkB.SetColor("_BaseColor", sparkColorBlue * (1f + blueAdv * 1.5f));
        TickSparkBurst(_sparkRed, redAdv, ref _sparkBurstTR, dt);
        TickSparkBurst(_sparkBlue, blueAdv, ref _sparkBurstTB, dt);

        // ② 注入：起点固定在【判定线】且只在【中缝附近】，朝中缝飞；
        //    低发射率 + 收窄发射口 + 长 Trail = 一道连贯窄波
        float redJudgeX = preview.baseRootWorldX + preview.leftEdge;    // 红方判定线（固定）
        float blueJudgeX = preview.baseRootWorldX + preview.rightEdge;  // 蓝方判定线（固定）
        float redDist = Mathf.Abs(seamX - redJudgeX);
        float blueDist = Mathf.Abs(blueJudgeX - seamX);
        int redDir = seamX >= redJudgeX ? 1 : -1;
        int blueDir = seamX <= blueJudgeX ? -1 : 1;
        UpdateInject(_injectRed, _matInjectR, redJudgeX, y + injectY, redDir, redDist);
        UpdateInject(_injectBlue, _matInjectB, blueJudgeX, y + injectY, blueDir, blueDist);

        // ③ 消散：shader 侵蚀（身后 + 两侧，头部受保护）+ 从身后段生成的飘散粒子
        float dissR = Mathf.Clamp01(dissolveIdle + Mathf.Clamp01(redAdv / 0.5f) * dissolveAdvance);
        float dissB = Mathf.Clamp01(dissolveIdle + Mathf.Clamp01(blueAdv / 0.5f) * dissolveAdvance);
        UpdateDrift(_driftRed, rr.bounds, y, driftBaseRate + redAdv * 10f * driftPerScale, true);
        UpdateDrift(_driftBlue, br.bounds, y, driftBaseRate + blueAdv * 10f * driftPerScale, false);
        preview.SetWallFloatPublic(rw, "_Dissolve", dissR);
        preview.SetWallFloatPublic(bw, "_Dissolve", dissB);
    }

    // ------------------------------------------------------------------ ① 火花

    private void PlaceSpark(ParticleSystem ps, float x, float y)
    {
        if (ps == null) return;
        var t = ps.transform;
        t.position = new Vector3(x, y, 0f);   // Z 中心对齐场地中线
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        // 发射口：竖直细长条，沿中缝线；Z 很窄，只在碰撞点附近
        sh.scale = new Vector3(0.05f, 0.7f, 0.25f);
        sh.randomDirectionAmount = 0f;   // 方向完全由 velocityOverLifetime 控制
    }

    private void ApplySparkMain(ParticleSystem ps, float life, float size)
    {
        if (ps == null) return;
        var main = ps.main;
        main.startLifetime = life;
        main.startSize = size;
        main.startSpeed = 0f;   // 速度交给 velocityOverLifetime
    }

    private void SetSparkVel(ParticleSystem ps, float intensity)
    {
        if (ps == null) return;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        // 从中缝线向 ±Z（两侧）/ ±Y（上下）放射，X 几乎不动
        vel.x = V(-sparkSideX * intensity, sparkSideX * intensity);
        vel.y = V(-sparkUp * intensity, sparkUp * intensity);
        vel.z = V(-sparkSideZ * intensity, sparkSideZ * intensity);
    }

    private void TickSparkBurst(ParticleSystem ps, float adv, ref float timer, float dt)
    {
        if (ps == null || !enableSpark) return;
        // 只有真正在前进（放大）的一侧才迸发，对峙呼吸循环不触发
        if (adv <= 0.02f) { timer = 0f; return; }
        timer += dt;
        if (timer < sparkBurstInterval) return;
        timer = 0f;
        int n = Mathf.Max(1, Mathf.RoundToInt(sparkBurstCount * (1f + adv * 2f)));
        ps.Emit(n);
    }

    // ------------------------------------------------------------------ ② 注入

    private void UpdateInject(ParticleSystem ps, Material mat, float x, float y, int dir, float dist)
    {
        if (ps == null) return;
        float boost = 1f + dist * injectFarBoost;
        float speed = injectSpeed * (1f + dist * injectFarBoost * 0.5f);
        float life = Mathf.Max(0.15f, dist / Mathf.Max(0.01f, speed));

        var t = ps.transform;
        t.position = new Vector3(x, y, 0f);   // Z 中心对齐场地中线

        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        // 发射口：X 极薄（判定线处）、Y 按系数、Z 只在中缝附近一小段
        sh.scale = new Vector3(0.05f, Mathf.Max(0.1f, injectSpreadY), Mathf.Max(0.1f, injectHalfWidthZ * 2f));
        sh.randomDirectionAmount = 0f;   // 严格朝中缝直线飞

        var main = ps.main;
        main.startLifetime = life;
        main.startSize = injectSize * (1f + dist * injectFarBoost * 0.15f);
        main.startSpeed = 0f;

        // 拖尾参数每帧同步，Inspector 改了立刻生效
        var tr = ps.trails;
        tr.enabled = enableInjectTrail;
        tr.lifetime = new ParticleSystem.MinMaxCurve(trailLifetime);
        tr.widthOverTrail = new ParticleSystem.MinMaxCurve(trailWidth);
        tr.minVertexDistance = trailMinVertex;

        SetVelXYZ(ps, speed * dir, 0f, 0f);
        SetRate(ps, enableInject ? injectRate * boost : 0f);
        if (mat != null) mat.SetColor("_BaseColor", (dir > 0 ? sparkColorRed : sparkColorBlue) * (1f + dist * injectFarBoost * 0.5f));
    }

    // ------------------------------------------------------------------ ③ 消散飘散

    private void UpdateDrift(ParticleSystem ps, Bounds b, float baseY, float rate, bool isRed)
    {
        if (ps == null) return;
        // 生成区：只取墙的"身后段"（红墙=min.x 侧，蓝墙=max.x 侧），避免从头部飘散
        float backX = isRed ? b.min.x : b.max.x;
        float toward = isRed ? 1f : -1f;
        float segLen = Mathf.Max(0.2f, b.size.x * driftBackPortion);
        float cx = backX + toward * segLen * 0.5f;

        var t = ps.transform;
        t.position = new Vector3(cx, baseY + b.size.y * 0.30f, (b.min.z + b.max.z) * 0.5f);

        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(segLen, Mathf.Max(0.2f, b.size.y * 0.5f), Mathf.Max(0.5f, b.size.z));
        sh.randomDirectionAmount = 0f;

        var main = ps.main;
        main.startLifetime = driftLifetime;
        main.startSize = driftSize;
        main.startSpeed = 0f;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        // 统一 RandomBetweenTwoConstants 模式（三条轴都是 V(min,max)）
        vel.x = V(0f, 0f);
        vel.y = V(driftRise * 0.3f, driftRise * 1.2f);    // 向上飘，略带随机
        vel.z = V(-driftSpreadZ, driftSpreadZ);            // 朝 Z 两侧散开

        SetRate(ps, enableDrift ? rate : 0f);
    }

    /// <summary>P8 击杀演出预留接口：强制让某侧（0=红 1=蓝）进入消散。
    /// 现在只提供入口，若后续用不到可直接删掉本方法。</summary>
    public void TriggerDissolve(int side, float amount = 1f)
    {
        if (preview == null) preview = FindFirstObjectByType<ShockwavePreview>();
        if (preview == null) return;
        var wall = side == 0 ? preview.redWall : preview.blueWall;
        preview.SetWallFloatPublic(wall, "_Dissolve", Mathf.Clamp01(amount));
    }
}
