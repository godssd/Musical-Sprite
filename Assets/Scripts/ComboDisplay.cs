using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 连击数显示。命中增加，Miss 重置。
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

    private int currentCombo = 0;
    private Vector3 baseScale;
    private float popTimer = 0f;
    private Transform textTransform;

    void Start()
    {
        if (text == null) text = GetComponentInChildren<Text>();
        if (text != null) textTransform = text.transform;
        baseScale = transform.localScale;
        UpdateText();
    }

    void Update()
    {
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
