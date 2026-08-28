using UnityEngine;

/// <summary>
/// 队伍 buff 视觉总集（清屏电流 + a/b 槽 aura）。
/// 公用 helper：构造 URP/Lit 半透明发光材质，封装给所有 VFX 类复用，避免分散重复配置漏掉 keyword。
/// </summary>
public static class AuraMat
{
    private static Shader _lit;
    private static Shader LitShader
    {
        get
        {
            if (_lit == null)
            {
                _lit = Shader.Find("Universal Render Pipeline/Lit");
                if (_lit == null) _lit = Shader.Find("Standard");
            }
            return _lit;
        }
    }

    /// <summary>创建 URP/Lit 半透明发光材质（alpha+emission 全开）。baseColor 同时写 _BaseColor / .color；emission 为发光色。</summary>
    public static Material Create(Color baseColor, Color emission, float alpha = 1f)
    {
        var c = baseColor; c.a = alpha;
        Material mat = new Material(LitShader);
        mat.SetColor("_BaseColor", c);
        mat.color = c;
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0f);
        mat.SetFloat("_Surface", 1f); // 1 = Transparent
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", emission);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return mat;
    }
}

/// <summary>
/// 清屏电流 VFX：从小黑射向范围内的一条细长发光长方体，快速闪烁后淡出销毁（占位特效）。
/// </summary>
public class ElectricityFx : MonoBehaviour
{
    public Vector3 from;
    public Vector3 to;
    public float duration = 0.3f;
    private float t;

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / duration);
        Vector3 dir = to - from;
        float len = dir.magnitude;
        transform.position = (from + to) * 0.5f;
        if (len > 0.0001f)
            transform.rotation = Quaternion.FromToRotation(Vector3.right, dir.normalized);
        transform.localScale = new Vector3(Mathf.Max(0.01f, len), 0.08f, 0.08f);
        var r = GetComponent<Renderer>();
        if (r != null && r.material != null)
        {
            Color c = new Color(0.4f, 0.9f, 1f);
            c.a = 1f - k;
            r.material.color = c;
        }
        if (k >= 1f) Destroy(gameObject);
    }

    /// <summary>在 from→to 之间生成一条电流光带。</summary>
    public static void Spawn(Vector3 from, Vector3 to)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Electricity";
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.material = AuraMat.Create(new Color(0.4f, 0.9f, 1f), new Color(0.4f, 0.9f, 1f) * 1.6f);
        }
        var fx = go.AddComponent<ElectricityFx>();
        fx.from = from;
        fx.to = to;
        fx.duration = 0.3f;
    }
}

/// <summary>
/// 防御 buff 的视觉：队伍前一道淡蓝色薄膜（6 块半透明蓝板在低半球壳里呼吸）。
/// 由 BuffController 在施放 a 防御时 spawn，到 ClearBuff 时 Destroy(parent) 整体收尾。
/// [PLACEHOLDER] 尺寸/位置/呼吸频率可调。
/// </summary>
public class DefenseAuraFx : MonoBehaviour
{
    public Vector3 centerPos;
    public Vector3 facing;

    private Transform[] panels;
    private Renderer[] rends;
    private Material[] mats;
    private float[] phase;
    private float spinSpeed = 0.6f;
    private float breatheHz = 1.6f;

    void Start()
    {
        const int N = 6;
        panels = new Transform[N];
        rends = new Renderer[N];
        mats = new Material[N];
        phase = new float[N];
        for (int i = 0; i < N; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"DefensePanel_{i}";
            go.transform.SetParent(transform, false);
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            var r = go.GetComponent<Renderer>();
            r.material = AuraMat.Create(new Color(0.1f, 0.4f, 1f, 0.55f), new Color(0.2f, 0.5f, 1f) * 1.6f, 0.55f);
            panels[i] = go.transform;
            rends[i] = r;
            mats[i] = r.material;
            float theta = (i / (float)N) * Mathf.PI * 2f;
            float phi = Mathf.PI * 0.35f;
            Vector3 dir = new Vector3(Mathf.Cos(theta) * Mathf.Sin(phi), Mathf.Cos(phi), Mathf.Sin(theta) * Mathf.Sin(phi));
            panels[i].localPosition = dir * 2.2f;
            panels[i].localScale = new Vector3(1.2f, 1.6f, 0.04f);
            panels[i].rotation = Quaternion.LookRotation(dir);
            phase[i] = (i / (float)N) * Mathf.PI * 2f;
        }
    }

    void Update()
    {
        if (panels == null) return;
        for (int i = 0; i < panels.Length; i++)
        {
            phase[i] += Time.deltaTime * spinSpeed;
            float theta = phase[i];
            float phi = Mathf.PI * 0.35f;
            Vector3 dir = new Vector3(Mathf.Cos(theta) * Mathf.Sin(phi), Mathf.Cos(phi), Mathf.Sin(theta) * Mathf.Sin(phi));
            panels[i].localPosition = dir * 2.2f;
            panels[i].rotation = Quaternion.LookRotation(dir);
            float a = Mathf.Lerp(0.28f, 0.5f, 0.5f + 0.5f * Mathf.Sin(Time.time * breatheHz + i));
            Color c = mats[i].GetColor("_BaseColor"); c.a = a; mats[i].SetColor("_BaseColor", c);
        }
    }

    public static GameObject Spawn(Vector3 center, Vector3 facing)
    {
        var go = new GameObject("DefenseAura");
        go.transform.position = center;
        var fx = go.AddComponent<DefenseAuraFx>();
        fx.centerPos = center;
        fx.facing = facing;
        return go;
    }
}

/// <summary>
/// 进攻 buff 的视觉：队伍周围 8 个淡红色气流方块绕中心旋转（公转）+ 自身小幅翻转。
/// 由 BuffController 在施放 a 进攻时 spawn。
/// </summary>
public class OffenseAuraFx : MonoBehaviour
{
    public Vector3 centerPos;
    private Transform[] nodes;
    private Material[] mats;
    private float orbitSpeed = 2.4f;

    void Start()
    {
        const int N = 8;
        nodes = new Transform[N];
        mats = new Material[N];
        for (int i = 0; i < N; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"OffenseAura_{i}";
            go.transform.SetParent(transform, false);
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            var r = go.GetComponent<Renderer>();
            r.material = AuraMat.Create(new Color(0.85f, 0.05f, 0.05f, 0.85f), new Color(1f, 0.1f, 0.1f) * 2.0f, 0.85f);
            nodes[i] = go.transform;
            mats[i] = r.material;
            nodes[i].localScale = new Vector3(0.4f, 0.4f, 0.4f);
        }
    }

    void Update()
    {
        if (nodes == null) return;
        float t = Time.time * orbitSpeed;
        for (int i = 0; i < nodes.Length; i++)
        {
            float theta = t + (i / (float)nodes.Length) * Mathf.PI * 2f;
            float radius = 1.6f + 0.15f * Mathf.Sin(t * 1.2f + i);
            float yOff = Mathf.Sin(t * 1.7f + i * 0.7f) * 0.5f;
            nodes[i].localPosition = new Vector3(Mathf.Cos(theta) * radius, yOff, Mathf.Sin(theta) * radius);
            nodes[i].rotation = Quaternion.Euler(0f, theta * Mathf.Rad2Deg, 0f);
            float pulse = 0.6f + 0.2f * Mathf.Sin(t * 2.5f + i);
            Color c = mats[i].GetColor("_BaseColor"); c.a = pulse; mats[i].SetColor("_BaseColor", c);
        }
    }

    public static GameObject Spawn(Vector3 center)
    {
        var go = new GameObject("OffenseAura");
        go.transform.position = center;
        var fx = go.AddComponent<OffenseAuraFx>();
        fx.centerPos = center;
        return go;
    }
}

/// <summary>
/// b 类 buff 的视觉（仅小黑个人战力）：自身周围 1 个红色小方块绕其旋转。
/// 由 BuffController 在施放 b 时 spawn（仅在该侧的小黑存在时 spawn）。
/// </summary>
public class SelfPowerAuraFx : MonoBehaviour
{
    public Transform orbitTarget;
    private Transform cube;
    private Material mat;
    private float orbitSpeed = 3.2f;

    void Start()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "SelfPowerCube";
        go.transform.SetParent(transform, false);
        var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
        var r = go.GetComponent<Renderer>();
        r.material = AuraMat.Create(new Color(1f, 0.7f, 0.1f, 0.9f), new Color(1f, 0.6f, 0.1f) * 2.0f, 0.9f);
        mat = r.material;
        cube = go.transform;
        cube.localScale = new Vector3(0.45f, 0.45f, 0.45f);
    }

    void Update()
    {
        if (cube == null) return;
        float t = Time.time * orbitSpeed;
        float radius = 1.0f;
        float yOff = Mathf.Sin(t * 1.4f) * 0.25f;
        Vector3 offset = new Vector3(Mathf.Cos(t), yOff, Mathf.Sin(t)) * radius;
        if (orbitTarget != null)
        {
            transform.position = orbitTarget.position;
            cube.localPosition = offset;
        }
        cube.rotation = Quaternion.Euler(t * 60f, t * 80f, 0f);
    }

    public static GameObject Spawn(Transform target)
    {
        var go = new GameObject("SelfPowerAura");
        var fx = go.AddComponent<SelfPowerAuraFx>();
        fx.orbitTarget = target;
        return go;
    }
}
