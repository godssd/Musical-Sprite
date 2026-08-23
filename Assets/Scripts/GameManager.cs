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

    [Header("快捷键")]
    [Tooltip("按 R 重新开始")]
    public KeyCode restartKey = KeyCode.R;

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
    }

    public void RestartGame()
    {
        if (leftSpawner != null) leftSpawner.ResetSpawner();
        if (rightSpawner != null) rightSpawner.ResetSpawner();
        if (centerLine != null) centerLine.ResetBattle();
        if (opponentInput != null) opponentInput.ResetInput();
        if (scoreManager != null) scoreManager.ResetScores();
        if (conductor != null) conductor.Play();

        Debug.Log("游戏已重置");
    }
}
