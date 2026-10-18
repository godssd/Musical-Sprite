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

    /// <summary>场景内全部角色动画驱动器缓存（懒刷新），用于开场动画就绪判定与重播。</summary>
    private CharacterAnimator[] introAnimators = new CharacterAnimator[0];

    /// <summary>战斗结果事件：(winnerSide, loserSide)；-1 表示平局无胜者。订阅方用于播放 Victory/Fail 终态动画。</summary>
    public event System.Action<int, int> OnBattleResult;

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

        // 调用谱面后运行游戏自动同步播放 BGM（编辑器已把音乐存进 BeatmapSO.audioClip）。
        // 必须在赋值 clip 后重新锚定音乐时钟并播放，否则若 Conductor.Start 先于本方法执行、彼时 clip 尚未赋值，
        // 会出现「时钟在跑但音频没播放」的静音问题。
        if (conductor != null)
        {
            if (conductor.musicSource == null)
                conductor.musicSource = conductor.GetComponent<AudioSource>();
            if (conductor.musicSource != null)
            {
                BeatmapSO bgm = (leftSpawner != null) ? leftSpawner.beatmap : null;
                if (bgm == null && rightSpawner != null) bgm = rightSpawner.beatmap;
                if (bgm != null && bgm.audioClip != null)
                {
                    conductor.musicSource.clip = bgm.audioClip;
                    // 开场动画兜底起播：待命直到全员开场动画播完（最长的角色成为静默期标准）+ leadIn 保底，
                    // 再 PlayScheduled 精确起播。卡顿会冻结开场动画从而自动推迟起播，结构上不可能吞掉歌曲开头。
                    conductor.introReadyProvider = AllIntroAnimationsDone;
                    conductor.ArmPlayback();

                    // 静默期预热：在开场动画+保底静默期间逐帧消化音符系统首开成本
                    // （纹理加载/网格构建/shader 变体编译/GPU 上传），避免音乐起播帧
                    // 第一批音符集中生成时卡顿。幂等，RestartGame 重复调用安全。
                    StartCoroutine(NotePrewarmer.Run());
                }
            }
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

        int winnerSide = (leftScore > rightScore) ? 0 : (rightScore > leftScore ? 1 : -1);
        OnBattleResult?.Invoke(winnerSide, 1 - winnerSide);   // 平局 winnerSide=-1（双方不播）
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

        int winnerSide = (defeatedSide == 0) ? 1 : 0;
        OnBattleResult?.Invoke(winnerSide, defeatedSide);
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

        // 重播全员开场动画，并重新走"待命-起播"流程（方块占位角色无 Spine，瞬时完成=0 秒开场）
        ReplayAllIntroAnimations();
        if (conductor != null)
        {
            conductor.introReadyProvider = AllIntroAnimationsDone;
            conductor.ArmPlayback();
        }

        if (winnerDisplayRoot != null) Destroy(winnerDisplayRoot);
        winnerShown = false;
        isGameOver = false;

        Debug.Log("游戏已重置");
    }

    /// <summary>刷新场景内全部 CharacterAnimator（含运行时生成的角色；方块占位角色的该组件自禁用、开场视为瞬时完成）。</summary>
    private void RefreshIntroAnimators()
    {
        introAnimators = FindObjectsOfType<CharacterAnimator>(true);
    }

    /// <summary>就绪条件：全员开场动画播完（无开场资源的角色视为 0 秒，立即就绪）。</summary>
    private bool AllIntroAnimationsDone()
    {
        if (introAnimators == null || introAnimators.Length == 0) RefreshIntroAnimators();
        for (int i = 0; i < introAnimators.Length; i++)
        {
            var a = introAnimators[i];
            if (a != null && !a.IsOpeningDone) return false;
        }
        return true;
    }

    /// <summary>重播场景内全部角色的开场动画（RestartGame 用；无 Spine 的占位角色为 no-op）。</summary>
    private void ReplayAllIntroAnimations()
    {
        RefreshIntroAnimators();
        for (int i = 0; i < introAnimators.Length; i++)
        {
            if (introAnimators[i] != null) introAnimators[i].PlayOpening();
        }
    }
}
