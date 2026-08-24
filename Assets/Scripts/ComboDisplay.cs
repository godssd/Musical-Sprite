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

    [Header("动画")]
    public float popScale = 1.3f;
    public float popDuration = 0.1f;

    [Header("动态定位")]
    [Tooltip("是否根据世界坐标动态更新屏幕位置")]
    public bool isDynamic = true;
    [Tooltip("所属 side：0=左，1=右")]
    public int side = 0;
    [Tooltip("对应侧的发射器，用于读取判定线位置与音轨间距")]
    public NoteSpawner spawner;
    [Tooltip("中间粉杠，用于计算判定线到粉杠中央")]
    public BattleCenterLine centerLine;
    [Tooltip("高度偏移（地面以上）")]
    public float heightOffset = 0.5f;

    private int currentCombo = 0;
    private Vector3 baseScale;
    private float popTimer = 0f;
    private Transform textTransform;
    private RectTransform rectTransform;
    private Canvas parentCanvas;

    void Start()
    {
        if (text == null) text = GetComponentInChildren<Text>();
        if (text != null) textTransform = text.transform;
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();
        baseScale = transform.localScale;
        UpdateText();
    }

    void Update()
    {
        if (isDynamic)
            UpdateDynamicPosition();

        if (popTimer > 0f && textTransform != null)
        {
            popTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(popTimer / popDuration);
            textTransform.localScale = Vector3.Lerp(Vector3.one, Vector3.one * popScale, t);
        }
        else if (textTransform != null)
        {
            textTransform.localScale = Vector3.one;
        }
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

        // 先用主相机得到屏幕像素坐标，再按 Canvas 缩放系数换算成 anchoredPosition。
        // ScreenSpaceOverlay 的 Canvas 本地单位 = 屏幕像素 / scaleFactor。
        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
        float scaleFactor = parentCanvas.scaleFactor;
        if (scaleFactor <= 0.0001f) scaleFactor = 1f;

        rectTransform.anchoredPosition = new Vector2(
            (screenPos.x - Screen.width * 0.5f) / scaleFactor,
            (screenPos.y - Screen.height * 0.5f) / scaleFactor);
    }

    public void AddCombo(int amount = 1)
    {
        currentCombo += amount;
        UpdateText();
        Pop();
    }

    public void ResetCombo()
    {
        if (currentCombo > 0)
        {
            currentCombo = 0;
            UpdateText();
        }
    }

    private void UpdateText()
    {
        if (text == null) return;
        text.text = currentCombo > 0 ? currentCombo.ToString() : "";
        text.color = comboColor;
    }

    private void ShowMiss()
    {
        if (text == null) return;
        text.text = "MISS";
        text.color = missColor;
    }

    private void Pop()
    {
        popTimer = popDuration;
    }
}
