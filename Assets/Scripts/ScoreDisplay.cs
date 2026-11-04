using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 单个玩家的分数显示。
/// 挂载在 World Space Canvas 上，用 UI Text 显示分数。
/// </summary>
public class ScoreDisplay : MonoBehaviour
{
    [Header("UI 引用")]
    public Text scoreText;

    [Header("显示格式")]
    public string prefix = "";
    public string suffix = "";

    [Header("空间模式")]
    [Tooltip("true = ScreenSpaceOverlay（如顶部血条旁）；false = WorldSpace（场景中 3D 分数板）")]
    public bool isScreenSpace = false;

    [Header("动画")]
    public float popScale = 1.2f;
    public float popDuration = 0.15f;

    private Vector3 baseScale;
    private Vector2 baseSize;
    private float popTimer = 0f;
    private int currentScore = 0;
    private SpriteNumberGraphic paintedNumber;

    void Start()
    {
        if (scoreText == null)
        {
            scoreText = GetComponentInChildren<Text>();
            if (scoreText == null)
                Debug.LogWarning($"[ScoreDisplay] {name} 找不到 Text 组件，分数不会显示。");
        }

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null && !canvas.overrideSorting)
        {
            // 只有未设置 overrideSorting 的 Canvas 才由本脚本控制 renderMode，
            // 避免覆盖 SceneSetupWindow/ScoreManager 为子 Canvas 特意设置的层级。
            if (isScreenSpace)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            else
            {
                canvas.renderMode = RenderMode.WorldSpace;
                if (Camera.main != null)
                    transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
            }
        }

        baseScale = transform.localScale;
        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            baseSize = rt.sizeDelta;
            Debug.Log($"[ScoreDisplay] {name} 初始化完成： anchoredPosition={rt.anchoredPosition} sizeDelta={rt.sizeDelta} scoreText={(scoreText != null ? scoreText.text : "NULL")}");
        }
        else
        {
            Debug.LogWarning($"[ScoreDisplay] {name} 找不到 RectTransform");
        }

        // ScoreManager may already have synchronized the real value before Start.
        RefreshScoreText();
    }

    void Update()
    {
        if (paintedNumber != null)
        {
            popTimer = Mathf.Max(0f, popTimer - Time.deltaTime);
            float t = popTimer / .14f;
            paintedNumber.rectTransform.localScale = Vector3.one * (1f + .04f * Mathf.Sin(t * Mathf.PI));
            return;
        }
        if (popTimer > 0f)
        {
            popTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(popTimer / popDuration);
            transform.localScale = Vector3.Lerp(baseScale, baseScale * popScale, t);
        }
        else
        {
            transform.localScale = baseScale;
        }
    }

    public void SetScore(int score)
    {
        if (score == currentScore) return;
        bool increased = score > currentScore;
        currentScore = score;
        RefreshScoreText();
        if (gameObject.activeInHierarchy && increased)
            popTimer = paintedNumber != null ? .14f : popDuration;
        else popTimer = 0f;
    }

    private void RefreshScoreText()
    {
        if (scoreText != null)
        {
            scoreText.text = $"{prefix}{currentScore}{suffix}";
        }
        if (paintedNumber != null) paintedNumber.SetNumber(currentScore, true);
    }

    public void UseHandpaintedHUD(float height)
    {
        if (!HandpaintedHUD.EnsureLoaded()) return;
        if (paintedNumber == null)
        {
            var go = new GameObject("HandpaintedScore_V11", typeof(RectTransform), typeof(CanvasRenderer), typeof(SpriteNumberGraphic));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);
            go.transform.SetParent(transform, false);
            paintedNumber = go.GetComponent<SpriteNumberGraphic>();
            var rect = paintedNumber.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            paintedNumber.color = Color.white; paintedNumber.raycastTarget = false;
            popTimer = 0f;
            transform.localScale = Vector3.one;
        }
        paintedNumber.digitHeight = height;
        paintedNumber.SetVerticesDirty();
        if (scoreText == null) scoreText = GetComponentInChildren<Text>();
        if (scoreText != null) scoreText.enabled = false;
        var nestedCanvas = GetComponent<Canvas>();
        if (nestedCanvas != null) nestedCanvas.overrideSorting = false;
        RefreshScoreText();
    }

    public int GetScore() => currentScore;

    public bool HasHandpaintedDisplay => paintedNumber != null && paintedNumber.isActiveAndEnabled &&
        paintedNumber.canvas != null && paintedNumber.canvas.isActiveAndEnabled;

    public float HandpaintedWidth(float height) => SpriteNumberGraphic.MeasureWidth(currentScore, height);

    public void SetHandpaintedAlignment(bool rightAlign)
    {
        if (paintedNumber == null) return;
        float alignment = rightAlign ? 1f : 0f;
        if (paintedNumber.horizontalAlignment != alignment)
        {
            paintedNumber.horizontalAlignment = alignment;
            paintedNumber.SetVerticesDirty();
        }
        // The small score punch grows away from its fixed HP-side edge.
        paintedNumber.rectTransform.pivot = new Vector2(alignment, .5f);
    }
}
