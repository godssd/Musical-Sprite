using UnityEngine;
using UnityEditor;

/// <summary>
/// Musical Sprite 运行时调试工具。
/// 可调整：AI 实力、音符分数、音符速度和数量、音符半径、连轨长按判定（滑动手感：滞留窗口/断连阈值/单轨容差）。
/// 菜单：Tools > Musical Sprite > Debug Window
///
/// 关键修复：之前在编辑模式修改参数后，进入 Play 模式会被重置。
/// 现在所有参数都存入 EditorPrefs，并在进入 Play 模式时自动重新应用，
/// 确保修改一定生效。
/// </summary>
public class MusicalSpriteDebugWindow : EditorWindow
{
    // AI
    private float aiStrength = 0.8f;
    private float aiAimOffset = 0f;
    private float aiMissChance = 0.05f;

    // 分数
    private int perfectScore = 100;
    private int goodScore = 60;
    private int missScore = 0;
    private int clearScore = 50;
    private int passScore = 80;

    // 音符
    private float leadTime = 2f;
    private float noteRadius = 0.45f;
    private int totalBeats = 120;
    private DemoBeatmapGenerator.Density density = DemoBeatmapGenerator.Density.Medium;

    // 连轨长按判定（滑动手感）
    private float holdSlideSettleWindow = 0.15f;
    private float holdBreakThreshold = 0.2f;
    private float holdLaneTolerance = 1.0f;

    private Vector2 scroll;

    private const string PREFS = "MusicalSprite.Debug.";

    [MenuItem("Tools/Musical Sprite/Debug Window")]
    public static void ShowWindow()
    {
        GetWindow<MusicalSpriteDebugWindow>("MS Debug");
    }

    private void OnEnable()
    {
        LoadPrefs();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        SavePrefs();
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            // 进入运行时重新应用，确保编辑模式下修改的参数不被重置
            ApplyValues(true);
        }
    }

    private void LoadPrefs()
    {
        aiStrength = EditorPrefs.GetFloat(PREFS + "aiStrength", aiStrength);
        aiAimOffset = EditorPrefs.GetFloat(PREFS + "aiAimOffset", aiAimOffset);
        aiMissChance = EditorPrefs.GetFloat(PREFS + "aiMissChance", aiMissChance);
        perfectScore = EditorPrefs.GetInt(PREFS + "perfectScore", perfectScore);
        goodScore = EditorPrefs.GetInt(PREFS + "goodScore", goodScore);
        missScore = EditorPrefs.GetInt(PREFS + "missScore", missScore);
        clearScore = EditorPrefs.GetInt(PREFS + "clearScore", clearScore);
        passScore = EditorPrefs.GetInt(PREFS + "passScore", passScore);
        leadTime = EditorPrefs.GetFloat(PREFS + "leadTime", leadTime);
        noteRadius = EditorPrefs.GetFloat(PREFS + "noteRadius", noteRadius);
        totalBeats = EditorPrefs.GetInt(PREFS + "totalBeats", totalBeats);
        density = (DemoBeatmapGenerator.Density)EditorPrefs.GetInt(PREFS + "density", (int)density);
        holdSlideSettleWindow = EditorPrefs.GetFloat(PREFS + "holdSlideSettleWindow", holdSlideSettleWindow);
        holdBreakThreshold = EditorPrefs.GetFloat(PREFS + "holdBreakThreshold", holdBreakThreshold);
        holdLaneTolerance = EditorPrefs.GetFloat(PREFS + "holdLaneTolerance", holdLaneTolerance);
    }

    private void SavePrefs()
    {
        EditorPrefs.SetFloat(PREFS + "aiStrength", aiStrength);
        EditorPrefs.SetFloat(PREFS + "aiAimOffset", aiAimOffset);
        EditorPrefs.SetFloat(PREFS + "aiMissChance", aiMissChance);
        EditorPrefs.SetInt(PREFS + "perfectScore", perfectScore);
        EditorPrefs.SetInt(PREFS + "goodScore", goodScore);
        EditorPrefs.SetInt(PREFS + "missScore", missScore);
        EditorPrefs.SetInt(PREFS + "clearScore", clearScore);
        EditorPrefs.SetInt(PREFS + "passScore", passScore);
        EditorPrefs.SetFloat(PREFS + "leadTime", leadTime);
        EditorPrefs.SetFloat(PREFS + "noteRadius", noteRadius);
        EditorPrefs.SetInt(PREFS + "totalBeats", totalBeats);
        EditorPrefs.SetInt(PREFS + "density", (int)density);
        EditorPrefs.SetFloat(PREFS + "holdSlideSettleWindow", holdSlideSettleWindow);
        EditorPrefs.SetFloat(PREFS + "holdBreakThreshold", holdBreakThreshold);
        EditorPrefs.SetFloat(PREFS + "holdLaneTolerance", holdLaneTolerance);
    }

    private void SyncFromScene()
    {
        OpponentInput opponent = FindFirstObjectByType<OpponentInput>();
        if (opponent != null)
        {
            aiAimOffset = opponent.aimOffset;
            aiMissChance = opponent.missChance;
            aiStrength = Mathf.Clamp01(1f - aiAimOffset / 0.05f);
        }

        ScoreManager scoreManager = FindFirstObjectByType<ScoreManager>();
        if (scoreManager != null)
        {
            perfectScore = scoreManager.perfectScore;
            goodScore = scoreManager.goodScore;
            missScore = scoreManager.missScore;
            clearScore = scoreManager.clearScore;
            passScore = scoreManager.passScore;
        }

        NoteSpawner spawner = FindFirstObjectByType<NoteSpawner>();
        if (spawner != null)
        {
            leadTime = spawner.leadTime;
            noteRadius = spawner.noteRadius;
            holdSlideSettleWindow = spawner.holdSlideSettleWindow;
            holdBreakThreshold = spawner.holdBreakThreshold;
            holdLaneTolerance = spawner.holdLaneTolerance;
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("Musical Sprite 调试工具", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("运行时修改以下参数，点击「应用」生效。参数会自动保存，进入 Play 模式时也会重新应用，不会被重置。", MessageType.Info);
        GUILayout.Space(10);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawAI();
        GUILayout.Space(15);
        DrawScores();
        GUILayout.Space(15);
        DrawNotes();
        GUILayout.Space(15);
        DrawHoldJudgment();
        GUILayout.Space(20);

        EditorGUILayout.EndScrollView();

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("应用所有修改", GUILayout.Height(40)))
        {
            ApplyAll();
        }
    }

    private void DrawAI()
    {
        GUILayout.Label("AI 对手", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(GUI.skin.box);

        aiStrength = EditorGUILayout.Slider("AI 综合实力", aiStrength, 0f, 1f);
        EditorGUILayout.HelpBox("0 = 很弱（大幅偏移、高 Miss 率），1 = 很强（接近 Perfect）。", MessageType.None);

        aiAimOffset = EditorGUILayout.FloatField("判定偏移 aimOffset", aiAimOffset);
        aiMissChance = EditorGUILayout.Slider("Miss 概率", aiMissChance, 0f, 1f);

        if (GUILayout.Button("按综合实力自动设置"))
        {
            aiAimOffset = (1f - aiStrength) * 0.05f;
            aiMissChance = (1f - aiStrength) * 0.25f;
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawScores()
    {
        GUILayout.Label("音符分数", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(GUI.skin.box);

        perfectScore = EditorGUILayout.IntField("PERFECT 分数", perfectScore);
        goodScore = EditorGUILayout.IntField("GOOD 分数", goodScore);
        missScore = EditorGUILayout.IntField("MISS 分数", missScore);
        clearScore = EditorGUILayout.IntField("CLEAR 分数(长按每段)", clearScore);
        passScore = EditorGUILayout.IntField("PASS 分数(小型点击)", passScore);
        EditorGUILayout.HelpBox("CLEAR = 长按音符每完成一段链接（节点→节点）的加分；PASS = 小型点击音符命中统一加分。", MessageType.None);

        EditorGUILayout.EndVertical();
    }

    private void DrawNotes()
    {
        GUILayout.Label("音符参数", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(GUI.skin.box);

        leadTime = EditorGUILayout.FloatField("音符飞行时间 leadTime", leadTime);
        EditorGUILayout.HelpBox("值越小音符飞得越快，GOOD 判定窗口也会按比例变窄。", MessageType.None);

        noteRadius = EditorGUILayout.FloatField("音符圆柱半径", noteRadius);
        EditorGUILayout.HelpBox("半径越大，视觉越大，GOOD 判定窗口也越宽。", MessageType.None);

        totalBeats = EditorGUILayout.IntField("谱面总拍数", totalBeats);
        EditorGUILayout.HelpBox("谱面时间窗口长度（决定音乐/谱面总时长）。", MessageType.None);

        density = (DemoBeatmapGenerator.Density)EditorGUILayout.EnumPopup("谱面密度", density);
        EditorGUILayout.HelpBox("稀疏=约 35% 拍点有音符，中等=约 60%，密集=约 90%。生成时还会随机混合整拍/半拍/四分音符网格。", MessageType.None);

        EditorGUILayout.EndVertical();
    }

    private void DrawHoldJudgment()
    {
        GUILayout.Label("连轨长按判定（滑动手感）", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(GUI.skin.box);

        holdSlideSettleWindow = EditorGUILayout.Slider("滞留窗口 slideSettleWindow", holdSlideSettleWindow, 0f, 0.5f);
        EditorGUILayout.HelpBox("连轨滑动 Hold（如 第2轨→第3轨）在节点时间中点切换\"应被按住\"的轨道；切换前后各该秒数内，起手轨与目标轨都允许（容滑动手感）。调大=更宽松，调小=更接近正中切点切换。", MessageType.None);

        holdBreakThreshold = EditorGUILayout.Slider("断连阈值 breakThreshold", holdBreakThreshold, 0.05f, 0.5f);
        EditorGUILayout.HelpBox("所需轨道超过该秒数未被按住即断连 MISS。调小=更快断（更严格）。", MessageType.None);

        holdLaneTolerance = EditorGUILayout.Slider("单轨容差 laneTolerance", holdLaneTolerance, 0f, 2f);
        EditorGUILayout.HelpBox("仅普通单轨 Hold 的跟随容差：按住轨道与当前插值轨道相差多少条轨道内算命中。", MessageType.None);

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// 仅应用参数值（AI/分数/发射器），不重生成谱面、不重启。
    /// 用于进入 Play 模式时确保修改生效。
    /// </summary>
    private void ApplyValues(bool silent)
    {
        // 1. AI
        OpponentInput opponent = FindFirstObjectByType<OpponentInput>();
        if (opponent != null)
        {
            opponent.aimOffset = aiAimOffset;
            opponent.missChance = aiMissChance;
            EditorUtility.SetDirty(opponent);
        }

        // 2. 分数
        ScoreManager scoreManager = FindFirstObjectByType<ScoreManager>();
        if (scoreManager != null)
        {
            scoreManager.perfectScore = perfectScore;
            scoreManager.goodScore = goodScore;
            scoreManager.missScore = missScore;
            scoreManager.clearScore = clearScore;
            scoreManager.passScore = passScore;
            EditorUtility.SetDirty(scoreManager);
        }

        // 3. 音符速度、半径、连轨判定（两边发射器同步）
        NoteSpawner[] spawners = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var s in spawners)
        {
            s.leadTime = leadTime;
            s.noteRadius = noteRadius;
            s.holdSlideSettleWindow = holdSlideSettleWindow;
            s.holdBreakThreshold = holdBreakThreshold;
            s.holdLaneTolerance = holdLaneTolerance;
            s.RecomputeWindows();
            EditorUtility.SetDirty(s);
        }

        // 3b. 立即应用到场上已存在的长按音符（调试时无需重启即可看到效果）
        HoldNote[] liveHolds = FindObjectsByType<HoldNote>(FindObjectsSortMode.None);
        foreach (var h in liveHolds)
        {
            h.slideSettleWindow = holdSlideSettleWindow;
            h.breakThreshold = holdBreakThreshold;
            h.laneTolerance = holdLaneTolerance;
            EditorUtility.SetDirty(h);
        }

        SavePrefs();

        if (!silent)
            Debug.Log("[MS Debug] 参数已应用：aimOffset=" + aiAimOffset + " miss=" + aiMissChance +
                      " perfect=" + perfectScore + " good=" + goodScore + " clear=" + clearScore +
                      " pass=" + passScore + " miss=" + missScore + " leadTime=" + leadTime + " radius=" + noteRadius +
                      " slideSettle=" + holdSlideSettleWindow + " breakTh=" + holdBreakThreshold + " laneTol=" + holdLaneTolerance);
    }

    private void ApplyAll()
    {
        ApplyValues(true);

        // 4. 重新生成谱面并重启
        BeatmapSO newBeatmap = DemoBeatmapGenerator.CreateDemoBeatmapWithBeats(totalBeats, density);

        GameManager gm = FindFirstObjectByType<GameManager>();
        if (gm != null)
        {
            NoteSpawner[] spawners = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
            foreach (var s in spawners)
            {
                s.beatmap = newBeatmap;
                s.ResetSpawner();
                EditorUtility.SetDirty(s);
            }
            if (gm.leftSpawner != null) gm.leftSpawner.beatmap = newBeatmap;
            if (gm.rightSpawner != null) gm.rightSpawner.beatmap = newBeatmap;

            if (gm.opponentInput != null)
            {
                gm.opponentInput.beatmap = newBeatmap;
                EditorUtility.SetDirty(gm.opponentInput);
            }

            gm.RestartGame();
        }

        EditorUtility.DisplayDialog("完成", "调试参数已应用，谱面已重新生成并重启。", "确定");
    }
}
