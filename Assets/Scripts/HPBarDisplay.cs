using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 顶部血条 UI。side=0 为左玩家（红、从左往右填充），side=1 为右玩家（蓝、从右往左填充）。
/// 通过修改 Fill 子对象的 RectTransform 锚点来实现血量变化，避免依赖 Image.Type.Filled。
/// </summary>
public class HPBarDisplay : MonoBehaviour
{
    [Header("归属")]
    [Tooltip("0 = 左玩家，1 = 右玩家")]
    public int side = 0;

    [Header("引用")]
    public ScoreManager scoreManager;
    public Image fillImage;
    public Text hpText;

    [Header("颜色")]
    public Color leftColor = new Color(0.9f, 0.2f, 0.2f, 1f);
    public Color rightColor = new Color(0.2f, 0.3f, 0.9f, 1f);

    [Header("动画")]
    [Tooltip("血条宽度每秒变化速度，0 = 瞬间变化")]
    public float fillSpeed = 4f;

    private float currentFillAmount = 1f;
    private float targetFillAmount = 1f;
    private RectTransform fillRect;

    void Start()
    {
        if (scoreManager == null)
            scoreManager = FindFirstObjectByType<ScoreManager>();

        ResolveFillImage();

        if (fillImage != null)
        {
            fillImage.type = Image.Type.Simple;
            fillImage.color = side == 0 ? leftColor : rightColor;
            fillRect = fillImage.GetComponent<RectTransform>();
            EnsureFillAboveBackground();
        }
        else
        {
            Debug.LogWarning($"[HPBarDisplay] 找不到 Fill Image（side={side}），血条不会变短。请确保子对象有叫 Fill 的 Image。");
        }

        if (scoreManager != null)
        {
            scoreManager.OnHPChanged += OnHPChanged;
            // 初始化一次
            int hp = side == 0 ? scoreManager.GetLeftHP() : scoreManager.GetRightHP();
            int max = scoreManager.maxHP;
            UpdateBar(hp, max);
        }
    }

    void Update()
    {
        if (fillImage == null)
        {
            ResolveFillImage();
            if (fillImage == null) return;
        }

        if (fillRect == null)
            fillRect = fillImage.GetComponent<RectTransform>();

        EnsureFillAboveBackground();

        if (fillSpeed <= 0f)
        {
            currentFillAmount = targetFillAmount;
        }
        else
        {
            currentFillAmount = Mathf.MoveTowards(currentFillAmount, targetFillAmount, fillSpeed * Time.deltaTime);
        }

        ApplyFillAnchors(currentFillAmount);
    }

    /// <summary>
    /// 如果 fillImage 未分配，尝试按子对象名 "Fill" 自动查找。
    /// </summary>
    private void ResolveFillImage()
    {
        if (fillImage != null) return;

        Transform fillTrans = transform.Find("Fill");
        if (fillTrans != null)
        {
            fillImage = fillTrans.GetComponent<Image>();
            fillRect = fillTrans.GetComponent<RectTransform>();
        }

        if (fillImage == null)
        {
            fillImage = GetComponentInChildren<Image>();
            if (fillImage != null)
                fillRect = fillImage.GetComponent<RectTransform>();
        }
    }

    /// <summary>
    /// 确保 Fill 子对象渲染层级高于 Background，否则血量减少后露出的
    /// 空槽会被 Fill 遮挡，看起来永远满血。
    /// </summary>
    private void EnsureFillAboveBackground()
    {
        if (fillImage == null) return;

        Transform bg = transform.Find("Background");
        if (bg != null && fillImage.transform.GetSiblingIndex() <= bg.GetSiblingIndex())
        {
            fillImage.transform.SetSiblingIndex(bg.GetSiblingIndex() + 1);
        }
    }

    /// <summary>
    /// 根据血量比例设置 Fill 的锚点，从而控制显示宽度。
    /// 左玩家：从左侧开始填充；右玩家：从右侧开始填充。
    /// </summary>
    private void ApplyFillAnchors(float ratio)
    {
        if (fillRect == null) return;

        ratio = Mathf.Clamp01(ratio);

        if (side == 0)
        {
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(ratio, 1f);
        }
        else
        {
            fillRect.anchorMin = new Vector2(1f - ratio, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
        }

        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fillRect.pivot = new Vector2(0.5f, 0.5f);
    }

    void OnDestroy()
    {
        if (scoreManager != null)
            scoreManager.OnHPChanged -= OnHPChanged;
    }

    private void OnHPChanged(int leftHP, int rightHP)
    {
        int hp = side == 0 ? leftHP : rightHP;
        int max = scoreManager != null ? scoreManager.maxHP : 1;
        UpdateBar(hp, max);
    }

    private void UpdateBar(int hp, int max)
    {
        targetFillAmount = Mathf.Clamp01((float)hp / Mathf.Max(1, max));
        currentFillAmount = targetFillAmount;

        ApplyFillAnchors(currentFillAmount);
        EnsureFillAboveBackground();

        if (hpText != null)
            hpText.text = $"{hp}/{max}";

        Debug.Log($"[HPBarDisplay] side={side} hp={hp}/{max} fillAmount={targetFillAmount}");
    }
}
