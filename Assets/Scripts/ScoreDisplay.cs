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

    [Header("动画")]
    public float popScale = 1.2f;
    public float popDuration = 0.15f;

    private Vector3 baseScale;
    private float popTimer = 0f;
    private int currentScore = 0;

    void Start()
    {
        if (scoreText == null) scoreText = GetComponentInChildren<Text>();
        baseScale = transform.localScale;

        // 确保 Canvas 在世界空间可见
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            if (Camera.main != null)
                transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
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
