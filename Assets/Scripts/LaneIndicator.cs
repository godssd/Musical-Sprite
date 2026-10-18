using UnityEngine;

/// <summary>每条音轨的判定灯。输入为低亮白光，命中时短暂切换阵营色并沿轨道推出一条细光。</summary>
public class LaneIndicator : MonoBehaviour
{
    [Header("现有引用")]
    public Renderer targetRenderer;
    public Material activeMaterial;
    public Material idleMaterial;

    [Header("节奏")]
    public float flashDuration = 0.12f;
    public float popScale = 1.6f;
    [Min(0.01f)] public float hitDuration = 0.22f;
    [Min(0f)] public float guideLength = 1.55f;
    [Min(0f)] public float guideWidth = 0.045f;

    [Header("颜色预览")]
    public Color idleColor = new Color(0.11f, 0.12f, 0.14f, 1f);
    public Color pressColor = new Color(0.72f, 0.79f, 0.84f, 1f);
    public Color redHitColor = new Color(1f, 0.27f, 0.10f, 1f);
    public Color blueHitColor = new Color(0.16f, 0.58f, 1f, 1f);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private Material bodyMaterial;
    private Material guideMaterial;
    private GameObject guide;
    private Renderer guideRenderer;
    private Vector3 baseScale;
    private float pressTimer;
    private float hitTimer;
    private bool held;
    private bool sleeping;
    private int side;

    private void Awake()
    {
        baseScale = transform.localScale;
        Material source = activeMaterial != null ? activeMaterial : idleMaterial;
        if (targetRenderer != null && source != null)
        {
            bodyMaterial = new Material(source) { renderQueue = 3050 };
            bodyMaterial.EnableKeyword("_EMISSION");
            targetRenderer.sharedMaterial = bodyMaterial;
            Paint(bodyMaterial, idleColor, 0f);
        }
    }

    /// <summary>由战斗视觉控制器指定所属阵营，决定命中颜色和辅助线方向。</summary>
    public void ConfigureSide(int ownerSide)
    {
        side = ownerSide == 1 ? 1 : 0;
        // 旧场景仍序列化了 3 倍弹起；预览阶段收敛到较轻的卡通反馈。
        popScale = Mathf.Min(popScale, 1.6f);
        if (guide != null || bodyMaterial == null) return;

        guide = GameObject.CreatePrimitive(PrimitiveType.Cube);
        guide.name = name + "_HitGuidePreview";
        guide.transform.SetParent(transform.parent, true);
        Destroy(guide.GetComponent<Collider>());
        // 辅助线先于音符绘制，避免盖住判定目标。
        guideMaterial = new Material(bodyMaterial) { renderQueue = 2995 };
        guideRenderer = guide.GetComponent<Renderer>();
        guideRenderer.sharedMaterial = guideMaterial;
        guideRenderer.enabled = false;
    }

    private void Update()
    {
        if (targetRenderer == null || bodyMaterial == null) return;
        if (pressTimer > 0f) pressTimer = Mathf.Max(0f, pressTimer - Time.deltaTime);
        if (hitTimer > 0f) hitTimer = Mathf.Max(0f, hitTimer - Time.deltaTime);

        float press = flashDuration > 0f ? Mathf.Clamp01(pressTimer / flashDuration) : 0f;
        float hit = hitDuration > 0f ? Mathf.Clamp01(hitTimer / hitDuration) : 0f;
        bool pressed = held || press > 0f;
        Color color = sleeping ? Color.black : hit > 0f
            ? Color.Lerp(pressColor, side == 0 ? redHitColor : blueHitColor, hit)
            : pressed ? pressColor : idleColor;
        Paint(bodyMaterial, color, sleeping ? 0f : hit > 0f ? 1.35f * hit : pressed ? 0.28f : 0f);

        Vector3 scale = baseScale;
        scale.y *= 1f + (Mathf.Max(press, hit) * (popScale - 1f));
        transform.localScale = scale;

        if (guideRenderer == null) return;
        guideRenderer.enabled = !sleeping && hit > 0f && guideLength > 0f;
        if (!guideRenderer.enabled) return;
        float length = guideLength * (1f - Mathf.Pow(1f - hit, 2f));
        float direction = side == 0 ? 1f : -1f;
        guide.transform.position = transform.position + new Vector3(direction * length * 0.5f, 0.055f, 0f);
        guide.transform.localScale = new Vector3(length, 0.012f, guideWidth);
        Paint(guideMaterial, Color.Lerp(idleColor, side == 0 ? redHitColor : blueHitColor, hit), 1.2f * hit);
    }

    public void Flash() { Hold(true); held = false; }

    public void Hold(bool on)
    {
        if (sleeping) return;
        held = on;
        if (on) pressTimer = flashDuration;
    }

    public void Hit()
    {
        if (!sleeping) hitTimer = hitDuration;
    }

    public void SetSleep(bool on)
    {
        sleeping = on;
        if (on)
        {
            held = false;
            pressTimer = 0f;
            hitTimer = 0f;
            if (guideRenderer != null) guideRenderer.enabled = false;
        }
    }

    private static void Paint(Material material, Color color, float emission)
    {
        if (material == null) return;
        material.SetColor(BaseColorId, color);
        material.SetColor(ColorId, color);
        material.SetColor(EmissionColorId, color * emission);
    }

    private void OnDestroy()
    {
        if (guide != null) Destroy(guide);
        if (guideMaterial != null) Destroy(guideMaterial);
        if (bodyMaterial != null) Destroy(bodyMaterial);
    }
}
