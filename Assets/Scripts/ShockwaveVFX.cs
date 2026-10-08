using UnityEngine;

/// <summary>
/// P4 冲击波特效。【本次只落地 ① 碰撞火花】，②③（注入波 / 消散）见 Docs/计划/冲击波P4特效方案.md，逐个验收后再做。
///
/// ① 碰撞火花的设计（用户 2026-10-08 指定）：
///   - 【穿插在对峙循环里】：每次对峙呼吸合拢到最紧（撞击瞬间）在中缝迸射一次。
///     与得分 / 放大 / 衰退完全无关 —— 触发源是 ShockwavePreview.ConsumeBreathImpact()。
///   - 【位置】只在中缝那条线上，不是整条拱顶也不是墙全身。
///   - 【方向】从中缝向周围迸开：±Z 为主、±Y 为辅、X 只有极微小发散（不是上飘的火星）。
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

    [Tooltip("迸射点的高度（世界 Y）。中缝线是竖直的一条，这里是它的中心高度")]
    public float sparkY = 0.7f;
    [Tooltip("中缝线上『有多长一段』会迸射火花（世界单位，沿 Z 铺开）。\n调大=整条中缝都在冒火花；调小=只在中间一小段炸开")]
    public float sparkLineLengthZ = 2.5f;

    [Tooltip("每次撞击发射的粒子数。对撞的『量感』主要靠它")]
    public int sparkBurstCount = 16;

    [Tooltip("±Z 迸射速度（主方向）：粒子会在这个速度的 ± 区间里随机取一个恒定值")]
    public float sparkSideZ = 2.6f;
    [Tooltip("±Y 上下分量：让火花不是一条平线，有上下翻飞感。⚠ 不能只往上 —— 那会被看成上飘的火星")]
    public float sparkUp = 1.3f;
    [Tooltip("X 方向发散。保持很小：火花是『沿中缝迸开』不是『朝前后飞』")]
    public float sparkSideX = 0.35f;

    [Tooltip("粒子寿命（秒）。短一点更像撞击碎片，长了会糊成一片")]
    public float sparkLifetime = 0.55f;
    [Tooltip("粒子尺寸")]
    public float sparkSize = 0.22f;
    [Tooltip("尺寸随机比例 0~1：0=每个一样大，1=大小差异拉满。有差异才有碎屑感")]
    [Range(0f, 1f)] public float sparkSizeRandom = 0.45f;

    [Tooltip("红方火花颜色（HDR：分量 >1 才会被 Bloom 泛出光晕）")]
    public Color sparkColorRed = new Color(2.0f, 0.95f, 0.70f, 1f);
    [Tooltip("蓝方火花颜色（HDR）")]
    public Color sparkColorBlue = new Color(0.70f, 1.15f, 2.0f, 1f);

    [Tooltip("撞击强度不足时不发火花（0=只要撞就发，1=只有呼吸完全稳定才发）。\n用来避免『刚从移动中停下、呼吸还在淡入』就先炸一下")]
    [Range(0f, 1f)] public float sparkMinImpact = 0.6f;

    [Tooltip("渲染排序。火花盖在墙(约20)之上")]
    public int sparkSortingOrder = 21;

    private ParticleSystem _sparkRed, _sparkBlue;
    private Material _matRed, _matBlue;
    private Texture2D _dot;              // 程序化圆点贴图（项目暂无粒子贴图资产）

    void OnEnable()
    {
        // ⛔ 编辑态绝不创建：会往场景里塞 DontSave 物体造成污染
        if (Application.isPlaying) Ensure();
    }

    void OnDestroy() { DestroyRuntime(ref _matRed); DestroyRuntime(ref _matBlue); DestroyRuntime(ref _dot); }

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
        // ⚠ loop + playOnAwake 必须为 true：系统必须处于 Playing 状态才会模拟与渲染粒子。
        //   发射率恒为 0，粒子只由 Emit(n) 手动产生 —— 两者配合才是"纯爆发式"发射。
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // 世界空间：不随父物体拉伸
        main.startSpeed = 0f;                                         // 方向完全交给 velocityOverLifetime
        main.startSize = new ParticleSystem.MinMaxCurve(
            sparkSize * (1f - sparkSizeRandom), sparkSize * (1f + sparkSizeRandom));
        main.startLifetime = sparkLifetime;
        main.startColor = Color.white;
        main.maxParticles = 512;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var em = ps.emission;
        em.rateOverTime = 0f;            // 只靠 Emit(n) 手动爆发，不自动持续发射

        // 发射口：沿中缝线的一段竖直细条
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(0.06f, 1.0f, Mathf.Max(0.05f, sparkLineLengthZ));
        sh.randomDirectionAmount = 0f;   // ⛔ 必须为 0，否则粒子四散飞（坑 #4）

        // 方向：±Z 主、±Y 辅、X 微发散。三轴统一 RandomBetweenTwoConstants（坑 #1）
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = V(-sparkSideX, sparkSideX);
        vel.y = V(-sparkUp, sparkUp);
        vel.z = V(-sparkSideZ, sparkSideZ);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(MakeFadeGradient());

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sortingOrder = sparkSortingOrder;

        ps.Play();   // 保证进入 Playing 状态，Emit 出来的粒子才会被模拟与绘制
        return ps;
    }

    private static ParticleSystem.MinMaxCurve V(float min, float max)
    {
        return new ParticleSystem.MinMaxCurve(min, max);
    }

    private static Gradient MakeFadeGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.3f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    /// <summary>⛔ 必须 LateUpdate：排在 Preview.Update（它推进呼吸相位）之后，
    /// 否则会漏掉/重复消费撞击事件（坑 #6 的同款时序问题）。</summary>
    void LateUpdate()
    {
        if (!Application.isPlaying) return;
        if (!enableSpark) return;
        if (preview == null) { preview = GetComponentInParent<ShockwavePreview>(); if (preview == null) return; }
        Ensure();

        UpdateSparkTransform();
        SyncSparkParams(_sparkRed, _matRed, sparkColorRed);
        SyncSparkParams(_sparkBlue, _matBlue, sparkColorBlue);

        if (!preview.ConsumeBreathImpact()) return;

        float strength = preview.BreathImpactStrength;
        if (strength < sparkMinImpact) return;

        // 撞击强度缩放发射数量：呼吸淡入未满时火花更少，稳定后满量
        int n = Mathf.Max(1, Mathf.RoundToInt(sparkBurstCount * strength));
        if (_sparkRed != null) _sparkRed.Emit(n);
        if (_sparkBlue != null) _sparkBlue.Emit(n);
    }

    /// <summary>每帧同步 Inspector 参数到粒子系统 —— 让 Play 中调 Size / Lifetime / 速度 / 颜色立刻可见，
    /// 不用重建粒子系统（重建会丢掉正在飞的粒子，观感会断）。</summary>
    private void SyncSparkParams(ParticleSystem ps, Material mat, Color hdr)
    {
        if (ps == null) return;
        var main = ps.main;
        main.startSize = new ParticleSystem.MinMaxCurve(
            Mathf.Max(0.01f, sparkSize) * (1f - sparkSizeRandom), Mathf.Max(0.01f, sparkSize) * (1f + sparkSizeRandom));
        main.startLifetime = Mathf.Max(0.05f, sparkLifetime);

        var sh = ps.shape;
        sh.scale = new Vector3(0.06f, 1.0f, Mathf.Max(0.05f, sparkLineLengthZ));

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = V(-sparkSideX, sparkSideX);
        vel.y = V(-sparkUp, sparkUp);
        vel.z = V(-sparkSideZ, sparkSideZ);

        if (mat != null) mat.SetColor("_BaseColor", hdr);
    }

    /// <summary>把两个火花系统摆到中缝两侧（各让开半个 centerGap），跟随中缝移动。</summary>
    private void UpdateSparkTransform()
    {
        if (preview == null) return;
        float seamX = preview.SeamWorldX;
        float half = preview.centerGap * 0.5f;
        if (_sparkRed != null) _sparkRed.transform.position = new Vector3(seamX - half, sparkY, 0f);
        if (_sparkBlue != null) _sparkBlue.transform.position = new Vector3(seamX + half, sparkY, 0f);
    }
}
