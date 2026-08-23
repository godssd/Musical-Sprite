using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 判定反馈文字（Perfect/Good/Miss）的自动动画。
/// 创建后向上飘并淡出，一段时间后自毁。
/// </summary>
public class JudgeFeedbackItem : MonoBehaviour
{
    [Tooltip("向上飘移速度")]
    public float moveSpeed = 1.5f;

    [Tooltip("存在时间")]
    public float lifetime = 0.8f;

    [Tooltip("放大脉冲强度")]
    public float popScale = 1.3f;

    private Text uiText;
    private Color startColor;
    private float timer;
    private Vector3 baseScale;
    private Transform textTransform;

    void Awake()
    {
        uiText = GetComponentInChildren<Text>();
        if (uiText != null)
        {
            startColor = uiText.color;
            textTransform = uiText.transform;
            baseScale = textTransform.localScale;
        }
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (uiText != null)
        {
            float alpha = 1f - (timer / lifetime);
            uiText.color = new Color(startColor.r, startColor.g, startColor.b, Mathf.Clamp01(alpha));
        }

        transform.position += Vector3.up * moveSpeed * Time.deltaTime;

        // 0.1 秒内做放大脉冲
        if (timer < 0.1f && textTransform != null)
        {
            float s = Mathf.Lerp(popScale, 1f, timer / 0.1f);
            textTransform.localScale = baseScale * s;
        }

        if (timer >= lifetime)
        {
            Destroy(gameObject);
        }
    }
}
