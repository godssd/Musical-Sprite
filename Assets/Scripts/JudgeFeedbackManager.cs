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
    public Color clearColor = new Color(0.3f, 0.9f, 1f, 1f); // 长按完成（每段 CLEAR）
    public Color passColor = new Color(0.6f, 1f, 0.4f, 1f);  // 小型点击音符命中（PASS）

    [Header("飘字偏移")]
    public Vector3 spawnOffset = new Vector3(0f, 0.25f, 0f);

    public void ShowFeedback(int side, int lane, string rank, Vector3 position, UnityEngine.Object source)
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
            case "CLEAR":
                prefab = null;
                color = clearColor;
                break;
            case "PASS":
                prefab = null;
                color = passColor;
                break;
            case "MISS":
                prefab = missPrefab;
                color = missColor;
                break;
            case "BREAK":   // 连轨中途断连：飘字显示为 MISS（语义同失败，但不清零连击）
                prefab = missPrefab;
                color = missColor;
                break;
        }

        bool isRuntimeTemplate = prefab == null;
        if (isRuntimeTemplate)
        {
            // 如果没有预制体，动态生成一个默认 UI Text 模板。
            prefab = CreateDefaultFeedbackPrefab(rank, color);
            // 模板本身不能显示，否则会在世界原点额外出现一个评价文字。
            prefab.SetActive(false);
        }

        Vector3 pos = position + spawnOffset;
        GameObject go = Instantiate(prefab, pos, Quaternion.identity);
        go.SetActive(true);

        if (isRuntimeTemplate)
        {
            Destroy(prefab);
        }

        // 让 Canvas 正面朝向相机（绕相机 up 轴翻转 180°，避免文字以背面示人导致左右镜像）。
        if (Camera.main != null)
        {
            Vector3 toCamera = Camera.main.transform.position - pos;
            if (toCamera.sqrMagnitude > 0.0001f)
                go.transform.rotation = Quaternion.LookRotation(-toCamera, Camera.main.transform.up);
        }

        // 按等级缩放：PERFECT 0.5，GOOD 0.5*0.8=0.4，MISS 0.5*0.6=0.3
        float rankScale = 1f;
        switch (rank)
        {
            case "PERFECT": rankScale = 0.5f; break;
            case "GOOD":    rankScale = 0.4f; break;
            case "CLEAR":   rankScale = 0.42f; break;
            case "PASS":    rankScale = 0.4f; break;
            case "MISS":    rankScale = 0.3f; break;
            case "BREAK":   rankScale = 0.3f; break;
        }
        go.transform.localScale *= rankScale;

        // 确保有 JudgeFeedbackItem 自动动画
        JudgeFeedbackItem item = go.GetComponent<JudgeFeedbackItem>();
        if (item == null) item = go.AddComponent<JudgeFeedbackItem>();

        // 设置文字内容（BREAK 断连在飘字上显示为 MISS）
        Text uiText = go.GetComponentInChildren<Text>();
        if (uiText != null)
        {
            uiText.text = (rank == "BREAK") ? "MISS" : rank;
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
        root.transform.localScale = Vector3.one * 0.01f;

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(root.transform);
        textGo.transform.localPosition = Vector3.zero;
        textGo.transform.localScale = Vector3.one;

        RectTransform rect = textGo.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(200f, 60f);

        Text uiText = textGo.AddComponent<Text>();
        uiText.text = text;
        uiText.color = color;
        uiText.fontSize = 40;
        uiText.alignment = TextAnchor.MiddleCenter;
        uiText.font = GetUIFont();

        Outline outline = textGo.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2f, 2f);

        return root;
    }
}
