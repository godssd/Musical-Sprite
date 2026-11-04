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

        SetScore(0);
    }

    void Update()
    {
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
        currentScore = score;
        if (scoreText != null)
        {
            scoreText.text = $"{prefix}{score}{suffix}";
        }

        if (gameObject.activeInHierarchy)
        {
            popTimer = popDuration;
        }
    }

    public int GetScore() => currentScore;
}
