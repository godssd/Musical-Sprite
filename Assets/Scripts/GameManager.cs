using UnityEngine;

/// <summary>
/// 游戏总控。负责一键开始/重置对战。
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("核心组件")]
    public Conductor conductor;
    public BattleCenterLine centerLine;

    [Header("双方发射器")]
    public NoteSpawner leftSpawner;  // side = 0
    public NoteSpawner rightSpawner; // side = 1

    [Header("反馈与输入")]
    public JudgeFeedbackManager judgeFeedback;
    public TouchInputManager touchInput;
    public OpponentInput opponentInput;
    public ScoreManager scoreManager;

    [Header("获胜提示")]
    [Tooltip("谱面结束后在中央显示获胜者的 UI 根对象（运行时动态创建）")]
    public GameObject winnerDisplayRoot;

    [Header("快捷键")]
    [Tooltip("按 R 重新开始")]
    public KeyCode restartKey = KeyCode.R;

    private bool winnerShown = false;

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

        // 把发射器传给触摸输入
        if (touchInput != null)
        {
            touchInput.leftSpawner = leftSpawner;
            touchInput.rightSpawner = rightSpawner;
        }
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
    }

    private void ShowWinner(string text, Color color)
    {
        // 删除旧的获胜提示
        if (winnerDisplayRoot != null) Destroy(winnerDisplayRoot);

        winnerDisplayRoot = new GameObject("WinnerDisplay");
        winnerDisplayRoot.transform.position = new Vector3(0f, 4f, 0f);

        Canvas canvas = winnerDisplayRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        if (Camera.main != null)
            winnerDisplayRoot.transform.rotation = Quaternion.LookRotation(winnerDisplayRoot.transform.position - Camera.main.transform.position);
        winnerDisplayRoot.transform.localScale = Vector3.one * 0.04f;

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(winnerDisplayRoot.transform);
        textGo.transform.localPosition = Vector3.zero;
        textGo.transform.localScale = Vector3.one;

        RectTransform rect = textGo.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(600f, 200f);

        UnityEngine.UI.Text uiText = textGo.AddComponent<UnityEngine.UI.Text>();
        uiText.text = text;
        uiText.fontSize = 120;
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

        Debug.Log("游戏已重置");
    }
}
