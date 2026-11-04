using System.Collections.Generic;
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
    [Range(1f, 1.2f)] public float growthPeak = 1.13f;
    [Min(.12f), Tooltip("旧场景的 0.16 秒自动升级为 0.42 秒，保留完整的收缩、回弹、阻尼阶段。")] public float growthDuration = .42f;
    [Min(.05f)] public float tearDuration = .75f;
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

    // Upgrade old serialized 0.30s scenes without rewriting the user scene.
    private float EffectiveTearDuration => useHandpaintedArtwork && tearDuration <= .601f ? .75f : Mathf.Max(.05f,tearDuration);
    private int currentCombo = 0;

    /// <summary>当前连击数（只读）。供技能效果计算"实际降低的连击数" = 削减前 - 削减后（已封顶到 0）。</summary>
    public int CurrentCombo => currentCombo;
    private readonly DampedNumberPulse numberFeedback=new DampedNumberPulse();
    private Transform textTransform;
    private Vector3 textBaseScale;
    private bool initialized;
    private ComboTearGraphic tear;
    private SpriteNumberGraphic paintedNumber;
    private CanvasGroup ornaments;
    private sealed class BreakSlot {public ComboTearGraphic digits,accents;public bool Playing=>digits.IsPlaying||accents.IsPlaying;}
    private readonly List<BreakSlot> breakSlots=new List<BreakSlot>();
    public int ActiveBreakSnapshots{get{int n=0;foreach(var s in breakSlots)if(s.Playing)n++;return n;}}
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
            var slot=CreateBreakSlot();tear=slot.digits;
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
        foreach(var slot in breakSlots){slot.digits.Advance(deltaTime);slot.accents.Advance(deltaTime);}
        numberFeedback.Advance(deltaTime);
        if(textTransform!=null)textTransform.localScale=textBaseScale*numberFeedback.Scale;
    }

    private void OnDisable()
    {
        CancelBreaks(); numberFeedback.Reset();
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
        foreach(var slot in breakSlots){slot.digits.YieldToNewNumber();slot.accents.YieldToNewNumber();}
        currentCombo = (int)System.Math.Min(int.MaxValue, (long)currentCombo + amount);
        UpdateText();
        Pop(false);
    }

    public void ResetCombo()
    {
        Initialize();
        if (currentCombo > 0)
        {
            numberFeedback.Reset();
            if (textTransform != null) textTransform.localScale = textBaseScale;
            BreakSlot slot=null;foreach(var candidate in breakSlots)if(!candidate.Playing){slot=candidate;break;}
            if(slot==null&&breakSlots.Count<4)slot=CreateBreakSlot();
            // Never abruptly clear an older visible departure for a rapid new break.
            if(slot!=null){
                if(paintedNumber!=null)slot.digits.Capture(paintedNumber,EffectiveTearDuration,tearSpread);else slot.digits.Capture(text,EffectiveTearDuration,tearSpread);
                if(ornaments!=null)slot.accents.CaptureImages(ornaments.GetComponentsInChildren<Image>(),EffectiveTearDuration,tearSpread);
            }
            currentCombo = 0;
            UpdateText();
        }
    }

    /// <summary>Initialization / restart / synchronization; never invent a hit or a failure.</summary>
    public void SetComboImmediate(int value)
    {
        Initialize();
        CancelBreaks(); numberFeedback.Reset();
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
        if (ornaments != null)
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
        paintedNumber.lowerShade=.24f;paintedNumber.color = comboColor; paintedNumber.raycastTarget = false;
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

    private BreakSlot CreateBreakSlot(){
        ComboTearGraphic Make(string name){
            var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(ComboTearGraphic));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,gameObject.scene);go.layer=text.gameObject.layer;go.transform.SetParent(text.transform.parent,false);
            var r=go.GetComponent<RectTransform>();var source=text.rectTransform;
            r.anchorMin=source.anchorMin;r.anchorMax=source.anchorMax;r.pivot=source.pivot;r.sizeDelta=source.sizeDelta;
            r.anchoredPosition3D=source.anchoredPosition3D;r.localRotation=source.localRotation;r.localScale=textBaseScale;r.SetSiblingIndex(text.transform.GetSiblingIndex());
            return go.GetComponent<ComboTearGraphic>();
        }
        var slot=new BreakSlot{digits=Make(breakSlots.Count==0?"ComboTear_V11":"ComboTear_V7_"+breakSlots.Count),accents=Make("ComboAccentsTear_V7_"+breakSlots.Count)};breakSlots.Add(slot);return slot;
    }
    private void CancelBreaks(){foreach(var slot in breakSlots){slot.digits.Cancel();slot.accents.Cancel();}}

    private void Pop(bool isReduction)
    {
        float peak=useHandpaintedArtwork&&Mathf.Abs(growthPeak-1.07f)<.0001f?1.13f:growthPeak;
        float seconds=useHandpaintedArtwork&&growthDuration<=.161f?.42f:growthDuration;
        numberFeedback.Trigger(isReduction?1.08f:peak,isReduction?.91f:.88f,seconds);
    }
}