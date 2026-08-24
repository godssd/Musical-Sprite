using UnityEngine;
using UnityEditor;

/// <summary>
/// Musical Sprite 运行时调试工具。
/// 可调整：AI 实力、音符分数、音符速度和数量、音符半径。
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

    // 音符
    private float leadTime = 2f;
    private float noteRadius = 0.45f;
    private int totalBeats = 120;

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
        leadTime = EditorPrefs.GetFloat(PREFS + "leadTime", leadTime);
        noteRadius = EditorPrefs.GetFloat(PREFS + "noteRadius", noteRadius);
        totalBeats = EditorPrefs.GetInt(PREFS + "totalBeats", totalBeats);
    }

    private void SavePrefs()
    {
        EditorPrefs.SetFloat(PREFS + "aiStrength", aiStrength);
        EditorPrefs.SetFloat(PREFS + "aiAimOffset", aiAimOffset);
        EditorPrefs.SetFloat(PREFS + "aiMissChance", aiMissChance);
        EditorPrefs.SetInt(PREFS + "perfectScore", perfectScore);
        EditorPrefs.SetInt(PREFS + "goodScore", goodScore);
        EditorPrefs.SetInt(PREFS + "missScore", missScore);
        EditorPrefs.SetFloat(PREFS + "leadTime", leadTime);
        EditorPrefs.SetFloat(PREFS + "noteRadius", noteRadius);
        EditorPrefs.SetInt(PREFS + "totalBeats", totalBeats);
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
        }

        NoteSpawner spawner = FindFirstObjectByType<NoteSpawner>();
        if (spawner != null)
        {
            leadTime = spawner.leadTime;
            noteRadius = spawner.noteRadius;
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

        totalBeats = EditorGUILayout.IntField("谱面音符拍数", totalBeats);
        EditorGUILayout.HelpBox("重新生成 DemoBeatmap.asset 并重启对战。", MessageType.None);

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
            EditorUtility.SetDirty(scoreManager);
        }

        // 3. 音符速度和半径（两边发射器同步）
        NoteSpawner[] spawners = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var s in spawners)
        {
            s.leadTime = leadTime;
            s.noteRadius = noteRadius;
            s.RecomputeWindows();
            EditorUtility.SetDirty(s);
        }

        SavePrefs();

        if (!silent)
            Debug.Log("[MS Debug] 参数已应用：aimOffset=" + aiAimOffset + " miss=" + aiMissChance +
                      " perfect=" + perfectScore + " good=" + goodScore + " leadTime=" + leadTime + " radius=" + noteRadius);
    }

    private void ApplyAll()
    {
        ApplyValues(true);

        // 4. 重新生成谱面并重启
        BeatmapSO newBeatmap = DemoBeatmapGenerator.CreateDemoBeatmapWithBeats(totalBeats);

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
