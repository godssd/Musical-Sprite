using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 判定反馈管理器。
/// 订阅 NoteSpawner.OnJudge，在判定位置生成 Perfect/Good/Miss 飘字。
/// </summary>
public class JudgeFeedbackManager : MonoBehaviour
{
    [Header("飘字预制体（世界空间 Canvas + Text）")]
    public GameObject perfectPrefab;
    public GameObject goodPrefab;
    public GameObject missPrefab;

    [Header("默认颜色（当 Prefab 没有 Text 时兜底）")]
    public Color perfectColor = new Color(1f, 0.85f, 0.2f, 1f);
    public Color goodColor = new Color(0.2f, 0.8f, 0.2f, 1f);
    public Color missColor = new Color(0.8f, 0.2f, 0.2f, 1f);

    [Header("飘字偏移")]
    public Vector3 spawnOffset = new Vector3(0f, 0.5f, 0f);

    public void ShowFeedback(int side, int lane, string rank, Vector3 position)
    {
        GameObject prefab = null;
        Color color = Color.white;

        switch (rank)
        {
            case "PERFECT":
                prefab = perfectPrefab;
                color = perfectColor;
                break;
            case "GOOD":
                prefab = goodPrefab;
                color = goodColor;
                break;
            case "MISS":
                prefab = missPrefab;
                color = missColor;
                break;
        }

        if (prefab == null)
        {
            // 如果没有预制体，动态生成一个默认 UI Text 飘字
            prefab = CreateDefaultFeedbackPrefab(rank, color);
        }

        Vector3 pos = position + spawnOffset;
        GameObject go = Instantiate(prefab, pos, Quaternion.identity);

        // 让 Canvas 面向相机
        if (Camera.main != null)
            go.transform.rotation = Quaternion.LookRotation(Camera.main.transform.position - pos);

        // 确保有 JudgeFeedbackItem 自动动画
        JudgeFeedbackItem item = go.GetComponent<JudgeFeedbackItem>();
        if (item == null) item = go.AddComponent<JudgeFeedbackItem>();

        // 设置文字内容
        Text uiText = go.GetComponentInChildren<Text>();
        if (uiText != null)
        {
            uiText.text = rank;
            uiText.color = color;
        }
    }

    private Font GetUIFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 16);
        return font;
    }

    private GameObject CreateDefaultFeedbackPrefab(string text, Color color)
    {
        GameObject root = new GameObject("JudgeFeedbackDefault");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(root.transform);
        textGo.transform.localPosition = Vector3.zero;
        textGo.transform.localScale = Vector3.one * 0.05f;

        RectTransform rect = textGo.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(4f, 1f);

        Text uiText = textGo.AddComponent<Text>();
        uiText.text = text;
        uiText.color = color;
        uiText.fontSize = 60;
        uiText.alignment = TextAnchor.MiddleCenter;
        uiText.font = GetUIFont();

        root.AddComponent<JudgeFeedbackItem>();
        return root;
    }
}
