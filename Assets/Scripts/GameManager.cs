using UnityEngine;

/// <summary>
/// 游戏总控。负责一键开始/重置对战。
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("核心组件")]
    public Conductor conductor;
    public BattleCenterLine centerLine;

    [Header("双方发射器")]
    public NoteSpawner leftSpawner;  // side = 0
    public NoteSpawner rightSpawner; // side = 1

    [Header("反馈与输入")]
    public JudgeFeedbackManager judgeFeedback;
    public OpponentInput opponentInput;
    public ScoreManager scoreManager;

    [Header("获胜提示")]
    [Tooltip("谱面结束后在中央显示获胜者的 UI 根对象（运行时动态创建）")]
    public GameObject winnerDisplayRoot;

    [Header("快捷键")]
    [Tooltip("按 R 重新开始")]
    public KeyCode restartKey = KeyCode.R;

    private bool winnerShown = false;

    /// <summary>当前对战是否已结束（血量归零或谱面结束已出胜者）。</summary>
    public bool isGameOver { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // 把判定事件接到 UI
        if (leftSpawner != null && judgeFeedback != null)
            leftSpawner.OnJudge += judgeFeedback.ShowFeedback;

        if (rightSpawner != null && judgeFeedback != null)
            rightSpawner.OnJudge += judgeFeedback.ShowFeedback;

        // 把对手输入接到右玩家
        if (opponentInput != null && rightSpawner != null)
        {
            opponentInput.spawner = rightSpawner;
            opponentInput.conductor = conductor;
            opponentInput.beatmap = rightSpawner.beatmap;
        }

        // 监听血量归零，直接判胜负
        if (scoreManager != null)
            scoreManager.OnPlayerDefeated += OnPlayerDefeated;
    }

    void OnDestroy()
    {
        if (scoreManager != null)
            scoreManager.OnPlayerDefeated -= OnPlayerDefeated;
    }

    void Update()
    {
        if (Input.GetKeyDown(restartKey))
        {
            RestartGame();
        }

        CheckAndShowWinner();
    }

    private void CheckAndShowWinner()
    {
        if (winnerShown) return;
        if (leftSpawner == null || rightSpawner == null) return;
        if (!leftSpawner.IsFinished || !rightSpawner.IsFinished) return;

        int leftScore = scoreManager != null ? scoreManager.GetLeftScore() : 0;
        int rightScore = scoreManager != null ? scoreManager.GetRightScore() : 0;

        string result;
        Color color;
        if (leftScore > rightScore)
        {
            result = "红方胜";
            color = new Color(0.9f, 0.2f, 0.2f, 1f);
        }
        else if (rightScore > leftScore)
        {
            result = "蓝方胜";
            color = new Color(0.2f, 0.3f, 0.9f, 1f);
        }
        else
        {
            result = "平局";
            color = Color.white;
        }

        ShowWinner(result, color);
        winnerShown = true;
        isGameOver = true;
    }

    private void OnPlayerDefeated(int defeatedSide)
    {
        if (winnerShown) return;

        string result;
        Color color;
        if (defeatedSide == 0)
        {
            result = "蓝方胜";
            color = new Color(0.2f, 0.3f, 0.9f, 1f);
        }
        else
        {
            result = "红方胜";
            color = new Color(0.9f, 0.2f, 0.2f, 1f);
        }

        ShowWinner(result, color);
        winnerShown = true;
        isGameOver = true;
    }

    private void ShowWinner(string text, Color color)
    {
        // 先清理场上残留音符，避免游戏结束后黑音符堆积不消失
        if (leftSpawner != null) leftSpawner.ClearActiveNotes();
        if (rightSpawner != null) rightSpawner.ClearActiveNotes();

        // 删除旧的获胜提示
        if (winnerDisplayRoot != null) Destroy(winnerDisplayRoot);

        winnerDisplayRoot = new GameObject("WinnerDisplay");

        // 屏幕空间画布，无透视，盖在最上层
        Canvas canvas = winnerDisplayRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        // 锚定到屏幕正上方中央，不再使用 3D 世界坐标（避免透视变形）
        RectTransform rootRect = winnerDisplayRoot.GetComponent<RectTransform>();
        if (rootRect == null) rootRect = winnerDisplayRoot.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 1f);
        rootRect.anchorMax = new Vector2(0.5f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.anchoredPosition = new Vector2(0f, 0f); // 贴顶中央
        rootRect.sizeDelta = new Vector2(1200f, 300f); // 整体放大一倍

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(winnerDisplayRoot.transform, false);

        RectTransform rect = textGo.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        UnityEngine.UI.Text uiText = textGo.AddComponent<UnityEngine.UI.Text>();
        uiText.text = text;
        uiText.fontSize = 144; // 字号同步放大一倍
        uiText.alignment = TextAnchor.MiddleCenter;
        uiText.color = color;
        uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (uiText.font == null) uiText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        UnityEngine.UI.Outline outline = textGo.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(4f, 4f);
    }

    public void RestartGame()
    {
        if (leftSpawner != null) leftSpawner.ResetSpawner();
        if (rightSpawner != null) rightSpawner.ResetSpawner();
        if (centerLine != null) centerLine.ResetBattle();
        if (opponentInput != null) opponentInput.ResetInput();
        if (scoreManager != null) scoreManager.ResetScores();
        if (conductor != null) conductor.Play();

        if (winnerDisplayRoot != null) Destroy(winnerDisplayRoot);
        winnerShown = false;
        isGameOver = false;

        Debug.Log("游戏已重置");
    }
}
