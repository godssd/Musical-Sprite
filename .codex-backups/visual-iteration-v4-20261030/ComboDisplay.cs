using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 连击数显示。命中增加，Miss 重置。
/// 支持两种模式：
/// 1. 固定屏幕位置（isDynamic=false）：直接读取 RectTransform.anchoredPosition。
/// 2. 动态世界位置（isDynamic=true）：每帧根据 side 的判定线和粉杠位置，
///    把连击数放在「判定线到粉杠中央」+「第 2-3 音轨水平线」上。
/// </summary>
public class ComboDisplay : MonoBehaviour
{
    [Header("显示")]
    public Text text;

    [Header("颜色")]
    public Color comboColor = new Color(0.2f, 1f, 0.2f, 1f);
    public Color missColor = new Color(1f, 0.2f, 0.2f, 1f);

    // Retain serialized legacy fields for scene / prefab compatibility; V11 uses the fields below.
    [HideInInspector]
    public float popScale = 1.3f;
    [HideInInspector]
    public float popDuration = 0.1f;

    [Header("V11 连击反馈（红蓝共用节奏）")]
    [Range(1f, 1.15f)] public float growthPeak = 1.07f;
    [Min(.01f)] public float growthDuration = .16f;
    [Min(.05f)] public float tearDuration = .30f;
    [Range(0f, .5f), Tooltip("碎片分离距离，相对实际数字高度。")]
    public float tearSpread = .16f;
    public bool useHandpaintedArtwork = true;

    [Header("动态定位")]
    [Tooltip("是否根据世界坐标动态更新屏幕位置")]
    public bool isDynamic = true;
    [Tooltip("所属 side：0=左，1=右")]
    public int side = 0;
    [Tooltip("对应侧的发射器，用于读取判定线位置与音轨间距")]
    public NoteSpawner spawner;
    [Tooltip("中间粉杠，用于计算判定线到粉杠中央")]
    public BattleCenterLine centerLine;
    [Tooltip("高度偏移（地面以上）。连击数在世界坐标里的抬高量，默认 1.2（原 0.5，按手感微调）。")]
    public float heightOffset = 1.2f;

    private int currentCombo = 0;

    /// <summary>当前连击数（只读）。供技能效果计算"实际降低的连击数" = 削减前 - 削减后（已封顶到 0）。</summary>
    public int CurrentCombo => currentCombo;
    private float popTimer = 0f;
    private Transform textTransform;
    private Vector3 textBaseScale;
    private bool reducing, initialized;
    private ComboTearGraphic tear;
    private SpriteNumberGraphic paintedNumber;
    private CanvasGroup ornaments;
    private float ornamentFade;
    private RectTransform comboTitle, comboLeaves, leftArc, rightArc;
    private RectTransform rectTransform;
    private Canvas parentCanvas;

    void Awake() => Initialize();

    private void Initialize()
    {
        if (initialized) return;
        if (text == null) text = GetComponentInChildren<Text>();
        if (text == null) return;
        if (text != null)
        {
            textTransform = text.transform;
            textBaseScale = textTransform.localScale;
            if (useHandpaintedArtwork && HandpaintedHUD.EnsureLoaded()) BuildPaintedArtwork();
            var go = new GameObject("ComboTear_V11", typeof(RectTransform), typeof(CanvasRenderer), typeof(ComboTearGraphic));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);
            go.layer = text.gameObject.layer;
            go.transform.SetParent(text.transform, false);
            var tearRect = go.GetComponent<RectTransform>();
            tearRect.anchorMin = Vector2.zero; tearRect.anchorMax = Vector2.one;
            tearRect.pivot = text.rectTransform.pivot;
            tearRect.offsetMin = tearRect.offsetMax = Vector2.zero;
            tear = go.GetComponent<ComboTearGraphic>();
        }
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();
        initialized = true;
        UpdateText();
    }

    void Update()
    {
        if (isDynamic)
            UpdateDynamicPosition();

        AdvanceVisuals(Time.deltaTime);
    }

    public void AdvanceVisuals(float deltaTime)
    {
        tear?.Advance(deltaTime);
        if (ornaments != null && currentCombo == 0 && ornamentFade > 0f)
        {
            ornamentFade = Mathf.Max(0f, ornamentFade - Mathf.Max(0f, deltaTime));
            ornaments.alpha = ornamentFade / Mathf.Max(.05f, tearDuration);
        }
        if (popTimer > 0f && textTransform != null)
        {
            popTimer = Mathf.Max(0f, popTimer - Mathf.Max(0f, deltaTime));
            float t = 1f - Mathf.Clamp01(popTimer / Mathf.Max(.01f, growthDuration));
            float scale;
            if (reducing) scale = Mathf.Lerp(.94f, 1f, Mathf.SmoothStep(0f, 1f, t));
            else if (t < .32f) scale = Mathf.Lerp(.97f, growthPeak, Mathf.SmoothStep(0f, 1f, t / .32f));
            else scale = Mathf.Lerp(growthPeak, 1f, Mathf.SmoothStep(0f, 1f, (t - .32f) / .68f));
            textTransform.localScale = textBaseScale * scale;
        }
        else if (textTransform != null)
        {
            textTransform.localScale = textBaseScale;
        }
    }

    private void OnDisable()
    {
        tear?.Cancel(); popTimer = 0f;
        ornamentFade = 0f;
        if (ornaments != null) ornaments.alpha = currentCombo > 0 ? 1f : 0f;
        if (textTransform != null) textTransform.localScale = textBaseScale;
    }

    /// <summary>
    /// 将连击数定位在「判定线到粉杠中央」且「第 2-3 音轨水平线」上。
    /// 4 条轨道时，lane 1 与 lane 2 的中心正好落在 hitPoint.z 上。
    /// </summary>
    private void UpdateDynamicPosition()
    {
        if (rectTransform == null || spawner == null || spawner.hitPoint == null) return;
        if (parentCanvas == null || Camera.main == null) return;

        float hitX = spawner.hitPoint.position.x;
        float centerX = centerLine != null ? centerLine.currentX : 0f;

        // 判定线到粉杠的中央
        float worldX = (hitX + centerX) * 0.5f;
        // 第 2-3 音轨之间的水平线（4 轨时就是 hitPoint.z）
        float worldZ = spawner.hitPoint.position.z;
        Vector3 worldPos = new Vector3(worldX, heightOffset, worldZ);

        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
        var parent = rectTransform.parent as RectTransform;
        Camera uiCamera = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera;
        if (parent != null && screenPos.z > 0f &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, uiCamera, out Vector2 local))
            rectTransform.localPosition = new Vector3(local.x, local.y, rectTransform.localPosition.z);
    }

    public void AddCombo(int amount = 1)
    {
        if (amount <= 0) return;
        Initialize();
        tear?.Cancel();
        currentCombo = (int)System.Math.Min(int.MaxValue, (long)currentCombo + amount);
        UpdateText();
        Pop(false);
    }

    public void ResetCombo()
    {
        Initialize();
        if (currentCombo > 0)
        {
            popTimer = 0f;
            if (textTransform != null) textTransform.localScale = textBaseScale;
            if (paintedNumber != null) tear?.Capture(paintedNumber, tearDuration, tearSpread);
            else tear?.Capture(text, tearDuration, tearSpread);
            ornamentFade = tearDuration;
            currentCombo = 0;
            UpdateText();
        }
    }

    /// <summary>Initialization / restart / synchronization; never invent a hit or a failure.</summary>
    public void SetComboImmediate(int value)
    {
        Initialize();
        tear?.Cancel(); popTimer = 0f;
        ornamentFade = 0f;
        if (textTransform != null) textTransform.localScale = textBaseScale;
        currentCombo = Mathf.Max(0, value);
        UpdateText();
    }

    /// <summary>被对手技能（如大狗叫音波）削减连击数。</summary>
    public void ReduceBy(int amount)
    {
        if (amount <= 0) return;
        Initialize();
        if (currentCombo <= 0) return;
        if (amount >= currentCombo) { ResetCombo(); return; }
        tear?.Cancel();
        currentCombo = Mathf.Max(0, currentCombo - amount);
        UpdateText();
        Pop(true);
    }

    private void UpdateText()
    {
        if (text == null) return;
        text.text = currentCombo > 0 ? currentCombo.ToString() : "";
        text.color = comboColor;
        if (paintedNumber != null)
        {
            paintedNumber.SetNumber(currentCombo);
            if (currentCombo > 0)
            {
                Rect bounds = paintedNumber.NumberBounds();
                comboTitle.anchoredPosition = new Vector2(bounds.center.x, bounds.yMax + 24f);
                comboLeaves.anchoredPosition = new Vector2(bounds.xMax - 8f, bounds.yMin - 15f);
                leftArc.anchoredPosition = new Vector2(bounds.xMin - 14f, bounds.center.y);
                rightArc.anchoredPosition = new Vector2(bounds.xMax + 14f, bounds.center.y);
                leftArc.sizeDelta = rightArc.sizeDelta = new Vector2(15f, Mathf.Clamp(bounds.height * .65f, 34f, 80f));
            }
        }
        if (ornaments != null && (currentCombo > 0 || ornamentFade <= 0f))
            ornaments.alpha = currentCombo > 0 ? 1f : 0f;
    }

    private void BuildPaintedArtwork()
    {
        comboColor = HandpaintedHUD.TeamColor(side);
        text.enabled = false;
        var go = new GameObject("PaintedComboDigits_V11", typeof(RectTransform), typeof(CanvasRenderer), typeof(SpriteNumberGraphic));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        go.transform.SetParent(text.transform, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.pivot = text.rectTransform.pivot; rect.offsetMin = rect.offsetMax = Vector2.zero;
        paintedNumber = go.GetComponent<SpriteNumberGraphic>();
        paintedNumber.color = comboColor; paintedNumber.raycastTarget = false;
        paintedNumber.digitHeight = text.rectTransform.rect.height * .85f;
        var decoration = new GameObject("ComboAccents_V11", typeof(RectTransform), typeof(CanvasGroup));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(decoration, gameObject.scene);
        decoration.transform.SetParent(text.transform, false);
        ornaments = decoration.GetComponent<CanvasGroup>();
        ornaments.blocksRaycasts = false; ornaments.interactable = false; ornaments.alpha = 0f;
        comboTitle = Add(HandpaintedHUD.Combo, "COMBO", new Vector2(0f, 71f), new Vector2(116f, 37f), Color.white);
        comboLeaves = Add(HandpaintedHUD.Leaves, "Leaves", new Vector2(66f, -57f), new Vector2(27f, 23f), Color.white);
        leftArc = Add(HandpaintedHUD.Accents, "LeftArc", new Vector2(-116f, 0f), new Vector2(15f, 62f), comboColor);
        leftArc.localScale = new Vector3(-1f, 1f, 1f);
        rightArc = Add(HandpaintedHUD.Accents, "RightArc", new Vector2(116f, 0f), new Vector2(15f, 62f), comboColor);
        RectTransform Add(Sprite sprite, string name, Vector2 position, Vector2 size, Color tint)
        {
            var image = HandpaintedHUD.NewImage(decoration.transform, name, sprite, tint);
            image.preserveAspect = true; image.rectTransform.sizeDelta = size;
            image.rectTransform.anchoredPosition = position;
            return image.rectTransform;
        }
    }

    private void Pop(bool isReduction)
    {
        reducing = isReduction;
        popTimer = Mathf.Max(.01f, growthDuration);
        if (textTransform != null) textTransform.localScale = textBaseScale * (reducing ? .94f : .97f);
    }
}
