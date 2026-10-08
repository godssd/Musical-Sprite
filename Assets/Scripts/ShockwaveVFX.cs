using UnityEngine;

/// <summary>
/// P4 冲击波特效。【本次只落地 ① 碰撞火花】，②③（注入波 / 消散）见 Docs/计划/冲击波P4特效方案.md，逐个验收后再做。
///
/// ① 碰撞火花的设计（用户 2026-10-08 指定）：
///   - 【穿插在对峙循环里】：每次对峙呼吸合拢到最紧（撞击瞬间）在中缝迸射一次。
///     与得分 / 放大 / 衰退完全无关 —— 触发源是 ShockwavePreview.ConsumeBreathImpact()。
///   - 【位置】沿中缝 Z 轴分布，由 sparkDistribution 控制集中在中央还是两端。
///   - 【方向】从中缝向外射出：主方向 ±Z + 上抛 + X 微发散。初速度大小 [min,max]，方向固定。
///   - 【物理】速度指数衰减；持续受 -Y 重力；落到 sparkGroundY 后停止下降，自然滑动到 lifetime 结束。
///
/// ⛔ 已踩过的坑（详见方案文档 §4，改本文件前必读）：
///   1) velocityOverLifetime 三轴必须同模式（MinMaxCurve(min,max)），否则 Console 报
///      "Particle Velocity curves must all be in the same mode"。
///   2) randomDirectionAmount 必须为 0，方向全交给 velocityOverLifetime，否则粒子四散飞看不见。
///   3) 本脚本必须 LateUpdate（排在 Preview.Update 之后），否则状态被 Preview 覆盖。
///   4) 一切 GameObject / Material / Texture 都是运行时创建且 HideFlags.DontSave，
///      ⛔ 绝不写任何场景对象的 sharedMesh / sharedMaterial（P1 外扩方案的污染事故）。
/// </summary>
public class ShockwaveVFX : MonoBehaviour
{
    public ShockwavePreview preview;

    [Header("① 碰撞火花（对峙循环：合拢撞击瞬间在中缝迸射）")]
    [Tooltip("总开关。关闭后不发任何火花，已发射的粒子自然消散")]
    public bool enableSpark = true;

    [Header("火花：发射")]
    [Tooltip("迸射点的高度（世界 Y）")]
    public float sparkY = 0.7f;
    [Tooltip("地面高度。粒子 Y 不会低于此值")]
    public float sparkGroundY = 0f;
    [Tooltip("中缝线上『有多长一段』会迸射火花（世界单位，沿 Z 铺开）")]
    public float sparkZLength = 2.5f;
    [Tooltip("粒子发射位置沿 Z 的分布：0=集中在 Z=0 中央，0.5=均匀，1=偏向 Z=±Half 两端")]
    [Range(0f, 1f)] public float sparkDistribution = 0.5f;
    [Tooltip("每次撞击发射的粒子总量")]
    public int sparkBurstCount = 16;

    [Header("火花：速度")]
    [Tooltip("粒子射出时的最大速度")]
    public float sparkMaxSpeed = 3.5f;
    [Tooltip("粒子射出时的最小速度")]
    public float sparkMinSpeed = 1.2f;
    [Tooltip("速度指数衰减强度（越大减速越快）")]
    [Range(0f, 20f)] public float sparkDecel = 3f;

    [Header("火花：方向")]
    [Tooltip("向上的抛射分量（>=0，避免朝下射入地面）")]
    public float sparkUp = 1.6f;
    [Tooltip("X 方向发散。中缝两侧微微散开；符号随机，不会偏向某一侧")]
    public float sparkSideX = 0.3f;

    [Header("火花：外观")]
    [Tooltip("粒子最大尺寸")]
    public float sparkMaxSize = 0.28f;
    [Tooltip("粒子最小尺寸")]
    public float sparkMinSize = 0.12f;
    [Tooltip("粒子寿命（秒）")]
    public float sparkLifetime = 0.55f;

    [Header("火花：颜色")]
    [Tooltip("红方火花颜色（HDR：分量 >1 才会被 Bloom 泛出光晕）")]
    public Color sparkColorRed = new Color(2.0f, 0.95f, 0.70f, 1f);
    [Tooltip("蓝方火花颜色（HDR）")]
    public Color sparkColorBlue = new Color(0.70f, 1.15f, 2.0f, 1f);

    [Header("火花：触发")]
    [Tooltip("撞击强度不足时不发火花（0=只要撞就发，1=只有呼吸完全稳定才发）。\n用来避免『刚从移动中停下、呼吸还在淡入』就先炸一下")]
    [Range(0f, 1f)] public float sparkMinImpact = 0.6f;

    [Header("火花：环境")]
    [Tooltip("向下重力加速度（世界 Y）。粒子被持续拉低；建议保持较低值，让弧线更明显")]
    [Range(0f, 20f)] public float sparkGravity = 2f;

    [Tooltip("渲染排序。火花盖在墙(约20)之上")]
    public int sparkSortingOrder = 21;

    private ParticleSystem _sparkRed, _sparkBlue;
    private Material _matRed, _matBlue;
    private Texture2D _dot;

    // 每帧 GetParticles/SetParticles 的缓冲区，复用避免 GC。
    private ParticleSystem.Particle[] _redParticles, _blueParticles;

    void OnEnable()
    {
        // ⛔ 编辑态绝不创建：会往场景里塞 DontSave 物体造成污染
        if (Application.isPlaying) Ensure();
    }

    void OnDestroy()
    {
        DestroyRuntime(ref _matRed);
        DestroyRuntime(ref _matBlue);
        DestroyRuntime(ref _dot);
        _redParticles = null;
        _blueParticles = null;
    }

    private static void DestroyRuntime<T>(ref T o) where T : Object
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        o = null;
    }

    private void Ensure()
    {
        if (preview == null) preview = GetComponentInParent<ShockwavePreview>();
        if (preview == null) preview = FindFirstObjectByType<ShockwavePreview>();
        if (_dot == null) _dot = MakeDotTexture();
        if (_sparkRed != null) return;

        _matRed = MakeMaterial(sparkColorRed);
        _matBlue = MakeMaterial(sparkColorBlue);
        _sparkRed = CreateSpark("Spark_Red", _matRed);
        _sparkBlue = CreateSpark("Spark_Blue", _matBlue);
    }

    /// <summary>程序化圆点贴图：项目暂无粒子贴图资产，运行时生成一张径向渐隐的白点。
    /// 白色即可 —— 颜色由材质的 HDR _BaseColor 决定。⛔ 不落盘、不入场景。</summary>
    private static Texture2D MakeDotTexture()
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
        t.Apply(false, true);
        return t;
    }

    private Material MakeMaterial(Color hdrColor)
    {
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        var m = new Material(sh);
        m.hideFlags = HideFlags.DontSave;
        m.SetFloat("_Surface", 1f);      // Transparent
        m.SetFloat("_Blend", 2f);        // Additive
        m.SetFloat("_SrcBlend", 5f);     // SrcAlpha
        m.SetFloat("_DstBlend", 1f);     // One
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        if (_dot != null) m.SetTexture("_BaseMap", _dot);
        m.SetColor("_BaseColor", hdrColor);   // >1 的 HDR 颜色，配合 Bloom 出泛光
        return m;
    }

    private ParticleSystem CreateSpark(string name, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.hideFlags = HideFlags.DontSave;
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        // loop + playOnAwake 必须为 true：系统必须处于 Playing 状态才会模拟与渲染粒子。
        // 发射率恒为 0，粒子只由 Emit 手动产生。
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // 世界空间：不随父物体拉伸
        main.startSpeed = 0f;                                         // 速度完全由我们手动写入
        main.startSize = 1f;
        main.startLifetime = 1f;
        main.maxParticles = 512;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var em = ps.emission;
        em.rateOverTime = 0f;            // 只靠 Emit 手动爆发，不自动持续发射

        // shape 保留一个极小 Box，实际发射位置由 EmitParams.position 指定
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = Vector3.one * 0.01f;
        sh.randomDirectionAmount = 0f;

        // 关闭内置速度/受力模块：我们每帧手动写 velocity，避免和内置模拟打架
        ps.velocityOverLifetime.enabled = false;
        ps.forceOverLifetime.enabled = false;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sortingOrder = sparkSortingOrder;

        ps.Play();   // 保证进入 Playing 状态，Emit 出来的粒子才会被模拟与绘制
        return ps;
    }

    /// <summary>⛔ 必须 LateUpdate：排在 Preview.Update（它推进呼吸相位）之后，
    /// 否则会漏掉/重复消费撞击事件（坑 #6 的同款时序问题）。</summary>
    void LateUpdate()
    {
        if (!Application.isPlaying) return;
        if (!enableSpark) return;
        if (preview == null) { preview = GetComponentInParent<ShockwavePreview>(); if (preview == null) return; }
        Ensure();

        SyncMaterials();
        ApplyPhysics(_sparkRed, ref _redParticles);
        ApplyPhysics(_sparkBlue, ref _blueParticles);

        if (!preview.ConsumeBreathImpact()) return;

        float strength = preview.BreathImpactStrength;
        if (strength < sparkMinImpact) return;

        // 撞击强度缩放发射数量：呼吸淡入未满时火花更少，稳定后满量
        int n = Mathf.Max(1, Mathf.RoundToInt(sparkBurstCount * strength));
        // 红系统在中缝左侧，蓝系统在右侧，保证两侧颜色不混在一起
        EmitBurst(_sparkRed, -1f, n);
        EmitBurst(_sparkBlue, 1f, n);
    }

    /// <summary>同步材质颜色 / 排序 —— Play 中改参数立刻可见。</summary>
    private void SyncMaterials()
    {
        if (_matRed != null) _matRed.SetColor("_BaseColor", sparkColorRed);
        if (_matBlue != null) _matBlue.SetColor("_BaseColor", sparkColorBlue);

        if (_sparkRed != null)
        {
            var r = _sparkRed.GetComponent<ParticleSystemRenderer>();
            if (r != null && r.sortingOrder != sparkSortingOrder) r.sortingOrder = sparkSortingOrder;
        }
        if (_sparkBlue != null)
        {
            var r = _sparkBlue.GetComponent<ParticleSystemRenderer>();
            if (r != null && r.sortingOrder != sparkSortingOrder) r.sortingOrder = sparkSortingOrder;
        }
    }

    /// <summary>爆发一次：count 个粒子从中缝附近按 distribution 沿 Z 分布射出。</summary>
    /// <param name="sideSign">-1=红侧（中缝左侧），+1=蓝侧（中缝右侧）</param>
    private void EmitBurst(ParticleSystem ps, float sideSign, int count)
    {
        if (ps == null) return;
        float seamX = preview.SeamWorldX;
        // 让红蓝系统分别位于中缝两侧，避免颜色混在一起
        float x = seamX + sideSign * Mathf.Max(0.01f, preview.centerGap * 0.25f);
        float halfLen = Mathf.Max(0.01f, sparkZLength * 0.5f);

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        ep.startColor = Color.white;  // 颜色由两侧各自材质 _BaseColor 决定
        ep.startLifetime = Mathf.Max(0.05f, sparkLifetime);

        for (int i = 0; i < count; i++)
        {
            float z = SampleSparkZ(halfLen, sparkDistribution);
            float speed = Random.Range(sparkMinSpeed, sparkMaxSpeed);
            float up = Mathf.Max(0f, sparkUp);  // 保证不朝下
            float sideX = Random.Range(-sparkSideX, sparkSideX);
            float zDir = Random.value < 0.5f ? -1f : 1f;  // 沿中缝 ±Z 主方向

            // 合成方向并归一化：固定方向，速度大小由 speed 决定，之后按指数衰减
            Vector3 dir = new Vector3(sideX, up, zDir).normalized;
            Vector3 velocity = dir * speed;

            ep.position = new Vector3(x, sparkY, z);
            ep.velocity = velocity;
            ep.startSize = Random.Range(sparkMinSize, sparkMaxSize);

            ps.Emit(ep, 1);
        }
    }

    /// <summary>沿 Z 轴采样发射位置。
    /// distribution=0.5 时均匀；<0.5 时通过 power>1 推向中央；>0.5 时通过 power<1 推向两端。</summary>
    private static float SampleSparkZ(float halfLen, float distribution)
    {
        float u = Random.value;
        float t = (u - 0.5f) * 2f; // -1..1
        float p;
        if (distribution <= 0.5f)
        {
            // 集中中央：p 从 3 (distribution=0) 到 1 (distribution=0.5)
            p = Mathf.Lerp(3f, 1f, distribution * 2f);
        }
        else
        {
            // 偏向两端：p 从 1 (distribution=0.5) 到 0.25 (distribution=1)
            p = Mathf.Lerp(1f, 0.25f, (distribution - 0.5f) * 2f);
        }
        float zNorm = Mathf.Sign(t) * Mathf.Pow(Mathf.Abs(t), p);
        return zNorm * halfLen;
    }

    /// <summary>手动模拟：重力 + 指数衰减 + 地面碰撞。
    /// 只改 velocity，不改 position——position 仍由粒子系统内部积分，避免双重积分。</summary>
    private void ApplyPhysics(ParticleSystem ps, ref ParticleSystem.Particle[] buffer)
    {
        if (ps == null) return;
        int count = ps.particleCount;
        if (count == 0) return;
        if (buffer == null || buffer.Length < count) buffer = new ParticleSystem.Particle[count];

        ps.GetParticles(buffer, count);
        float dt = Time.deltaTime;
        float drag = Mathf.Exp(-sparkDecel * dt);
        bool changed = false;

        for (int i = 0; i < count; i++)
        {
            var p = buffer[i];
            // 重力：只改 velocity，让粒子系统内部去积分 position
            p.velocity = new Vector3(p.velocity.x, p.velocity.y - sparkGravity * dt, p.velocity.z);
            // 速度指数衰减（标量 drag，方向不变）
            p.velocity *= drag;
            // 地面碰撞：Y 停在地面，vy 清零，x/z 继续滑动
            if (p.position.y <= sparkGroundY)
            {
                p.position = new Vector3(p.position.x, sparkGroundY, p.position.z);
                p.velocity = new Vector3(p.velocity.x, 0f, p.velocity.z);
            }
            buffer[i] = p;
            changed = true;
        }

        if (changed) ps.SetParticles(buffer, count);
    }
}
