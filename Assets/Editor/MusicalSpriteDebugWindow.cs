using UnityEngine;
using UnityEditor;

/// <summary>
/// Musical Sprite 运行时调试工具。
/// 可调整：AI 实力、音符分数、音符速度和数量、音符半径、连点停留时间、连轨长按判定。
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
    private float holdEarlySlideGrace = 0.18f;  // 连轨滑动「过早」容错（与 slideSettleWindow 对称）[PLACEHOLDER 可微调]
    private float chainTapHoldDuration = 0.4f;

    // 过热加成（Fever / Super Fever）
    private float feverMult = 1.05f;
    private float superMult = 1.10f;
    private int feverThresh = 20;
    private int superThresh = 50;

    // 追分 / 扣血（P2）
    private int catchUpDiffThreshold = 3000;
    private float catchUpInterval = 1f;
    private float catchUpDrainRate = 0.001f;

    // 主动技能输入窗口（P2）
    private float skillInputWindow = 0.5f;

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
        holdEarlySlideGrace = EditorPrefs.GetFloat(PREFS + "holdEarlySlideGrace", holdEarlySlideGrace);
        holdLaneTolerance = EditorPrefs.GetFloat(PREFS + "holdLaneTolerance", holdLaneTolerance);
        chainTapHoldDuration = EditorPrefs.GetFloat(PREFS + "chainTapHoldDuration", chainTapHoldDuration);
        feverMult = EditorPrefs.GetFloat(PREFS + "feverMult", feverMult);
        superMult = EditorPrefs.GetFloat(PREFS + "superMult", superMult);
        feverThresh = EditorPrefs.GetInt(PREFS + "feverThresh", feverThresh);
        superThresh = EditorPrefs.GetInt(PREFS + "superThresh", superThresh);
        catchUpDiffThreshold = EditorPrefs.GetInt(PREFS + "catchUpDiffThreshold", catchUpDiffThreshold);
        catchUpInterval = EditorPrefs.GetFloat(PREFS + "catchUpInterval", catchUpInterval);
        catchUpDrainRate = EditorPrefs.GetFloat(PREFS + "catchUpDrainRate", catchUpDrainRate);
        skillInputWindow = EditorPrefs.GetFloat(PREFS + "skillInputWindow", skillInputWindow);
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
        EditorPrefs.SetFloat(PREFS + "holdEarlySlideGrace", holdEarlySlideGrace);
        EditorPrefs.SetFloat(PREFS + "holdLaneTolerance", holdLaneTolerance);
        EditorPrefs.SetFloat(PREFS + "chainTapHoldDuration", chainTapHoldDuration);
        EditorPrefs.SetFloat(PREFS + "feverMult", feverMult);
        EditorPrefs.SetFloat(PREFS + "superMult", superMult);
        EditorPrefs.SetInt(PREFS + "feverThresh", feverThresh);
        EditorPrefs.SetInt(PREFS + "superThresh", superThresh);
        EditorPrefs.SetInt(PREFS + "catchUpDiffThreshold", catchUpDiffThreshold);
        EditorPrefs.SetFloat(PREFS + "catchUpInterval", catchUpInterval);
        EditorPrefs.SetFloat(PREFS + "catchUpDrainRate", catchUpDrainRate);
        EditorPrefs.SetFloat(PREFS + "skillInputWindow", skillInputWindow);
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
            holdEarlySlideGrace = spawner.holdEarlySlideGrace;
            holdLaneTolerance = spawner.holdLaneTolerance;
            chainTapHoldDuration = spawner.chainTapHoldDuration;
        }

        // 过热加成：优先读运行时 FeverManager.config，否则读 FeverConfigSO 资产
        FeverManager fever = FindFirstObjectByType<FeverManager>();
        if (fever != null && fever.config != null)
        {
            feverMult = fever.config.feverScoreMultiplier;
            superMult = fever.config.superFeverScoreMultiplier;
            feverThresh = fever.config.feverComboThreshold;
            superThresh = fever.config.superFeverComboThreshold;
        }
        else
        {
            var guids = AssetDatabase.FindAssets("t:FeverConfigSO");
            if (guids.Length > 0)
            {
                var cfg = AssetDatabase.LoadAssetAtPath<FeverConfigSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (cfg != null)
                {
                    feverMult = cfg.feverScoreMultiplier;
                    superMult = cfg.superFeverScoreMultiplier;
                    feverThresh = cfg.feverComboThreshold;
                    superThresh = cfg.superFeverComboThreshold;
                }
            }
        }

        // P2: 同步 ScoreManager / SkillInputUI 字段
        if (scoreManager != null)
        {
            catchUpDiffThreshold = scoreManager.catchUpDiffThreshold;
            catchUpInterval = scoreManager.catchUpInterval;
            catchUpDrainRate = scoreManager.catchUpDrainRate;
        }
        var skillUI = FindFirstObjectByType<SkillInputUI>();
        if (skillUI != null)
        {
            skillInputWindow = skillUI.inputInterval;
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
        GUILayout.Space(15);
        DrawFever();
        GUILayout.Space(15);
        DrawP2Battle();
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

        holdEarlySlideGrace = EditorGUILayout.Slider("过早容错 earlySlideGrace", holdEarlySlideGrace, 0f, 0.5f);
        EditorGUILayout.HelpBox("连轨滑动「提前完成滑动」的容错：在中点之前该秒数内提前滑到下一轨（toLane），视为已完成滑动、不报警（容「过早」手感）。与 slideSettleWindow（过晚/停滞）对称。", MessageType.None);

        holdLaneTolerance = EditorGUILayout.Slider("单轨容差 laneTolerance", holdLaneTolerance, 0f, 2f);
        EditorGUILayout.HelpBox("仅普通单轨 Hold 的跟随容差：按住轨道与当前插值轨道相差多少条轨道内算命中。", MessageType.None);

        chainTapHoldDuration = EditorGUILayout.Slider("连点停留时间 (秒)", chainTapHoldDuration, 0.05f, 2f);
        EditorGUILayout.HelpBox("连点音符每次命中后等待下一次点击的时间。默认 0.4 秒，超时后判定 MISS。", MessageType.None);

        EditorGUILayout.EndVertical();
    }

    private void DrawFever()
    {
        GUILayout.Label("过热加成（Fever / Super Fever）", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(GUI.skin.box);

        feverMult = EditorGUILayout.Slider("Fever 加成系数", feverMult, 1f, 2f);
        EditorGUILayout.HelpBox("Fever 状态下该 side 命中得分乘数。1.05 = 基础分 +5%（例：PERFECT 100 → 105）。", MessageType.None);

        superMult = EditorGUILayout.Slider("SuperFever 加成系数", superMult, 1f, 2f);
        EditorGUILayout.HelpBox("SuperFever 状态下该 side 命中得分乘数。1.10 = 基础分 +10%（例：PERFECT 100 → 110）。", MessageType.None);

        feverThresh = EditorGUILayout.IntSlider("Fever 阈值（连击）", feverThresh, 1, 100);
        EditorGUILayout.HelpBox("玩家侧连击达到该值进入 Fever。", MessageType.None);

        superThresh = EditorGUILayout.IntSlider("SuperFever 阈值（连击）", superThresh, 1, 200);
        EditorGUILayout.HelpBox("连击达到该值进入 SuperFever（独立阈值，非累加）。务必 > Fever 阈值。", MessageType.None);

        EditorGUILayout.EndVertical();
    }

    private void DrawP2Battle()
    {
        GUILayout.Label("P2 战斗：追分 / 主动技能", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(GUI.skin.box);

        catchUpDiffThreshold = EditorGUILayout.IntSlider("保底分差阈值", catchUpDiffThreshold, 1000, 8000);
        EditorGUILayout.HelpBox("双方分差超过此值时启动保底：每 N 秒给劣势方额外加分(100×整数倍)把分差压回≤阈值；劣势方按 (该侧全队+玩家战斗力总和)×0.1% 扣血；优势方不受影响。", MessageType.None);

        catchUpInterval = EditorGUILayout.Slider("扣血节流间隔 (秒)", catchUpInterval, 0.25f, 5f);
        catchUpDrainRate = EditorGUILayout.Slider("扣血系数 drainRate", catchUpDrainRate, 0.0001f, 0.005f);
        EditorGUILayout.HelpBox("damage = extra × combat_total × drainRate。drainRate=0.001 即(全队+玩家战斗力总和)×0.1%。extra 为本次保底给劣势方的额外加分。仅扣劣势方。", MessageType.None);

        skillInputWindow = EditorGUILayout.Slider("技能按键时间窗 (秒)", skillInputWindow, 0.1f, 1.5f);
        EditorGUILayout.HelpBox("←/↓/→ 顺序触发对应车道队伍角色的主动技能；相邻两次按键超过该秒数即视为超时、立刻重置输入缓冲（不会等到完整序列未完成才清）。", MessageType.None);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("角色系统辅助", EditorStyles.boldLabel);
        if (GUILayout.Button("为场景所有 Renderer 自动补 CharacterCubeMarker"))
        {
            var b = FindFirstObjectByType<CharacterBattleSystem>();
            if (b != null) b.AutoFillMarkers();
            else Debug.LogWarning("[MS Debug] 未找到 CharacterBattleSystem；先进入 Play 模式或重启 GameManager。");
        }
        if (GUILayout.Button("按角色身份色重新上色所有方块（宝宝灰/大狗橙/嘟嘟绿/爱格白/小黑黑）"))
        {
            var b = FindFirstObjectByType<CharacterBattleSystem>();
            if (b != null) b.ColorAllMarkers();
            else Debug.LogWarning("[MS Debug] 未找到 CharacterBattleSystem；先进入 Play 模式或重启 GameManager。");
        }

        EditorGUILayout.EndVertical();
    }

    /// <summary>
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
            // P2: 追分 / 扣血参数
            scoreManager.catchUpDiffThreshold = catchUpDiffThreshold;
            scoreManager.catchUpInterval = catchUpInterval;
            scoreManager.catchUpDrainRate = catchUpDrainRate;
            EditorUtility.SetDirty(scoreManager);
        }

        // 2b. P2: 主动技能输入窗口
        var skillUI = FindFirstObjectByType<SkillInputUI>();
        if (skillUI != null)
        {
            skillUI.inputInterval = skillInputWindow;
            EditorUtility.SetDirty(skillUI);
        }

        // 3. 音符速度、半径、连轨判定（两边发射器同步）
        NoteSpawner[] spawners = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var s in spawners)
        {
            s.leadTime = leadTime;
            s.noteRadius = noteRadius;
            s.holdSlideSettleWindow = holdSlideSettleWindow;
            s.holdBreakThreshold = holdBreakThreshold;
            s.holdEarlySlideGrace = holdEarlySlideGrace;
            s.holdLaneTolerance = holdLaneTolerance;
            s.chainTapHoldDuration = Mathf.Max(0.05f, chainTapHoldDuration);
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
            h.earlySlideGrace = holdEarlySlideGrace;
            EditorUtility.SetDirty(h);
        }

        NoteMover[] liveNotes = FindObjectsByType<NoteMover>(FindObjectsSortMode.None);
        foreach (var n in liveNotes)
            n.SetChainTapHoldDuration(chainTapHoldDuration);

        // 4. 过热加成：写入运行时 FeverManager.config（即时生效）+ 所有 FeverConfigSO 资产（持久化）
        FeverManager fever = FindFirstObjectByType<FeverManager>();
        if (fever != null && fever.config != null)
        {
            fever.config.feverScoreMultiplier = feverMult;
            fever.config.superFeverScoreMultiplier = superMult;
            fever.config.feverComboThreshold = feverThresh;
            fever.config.superFeverComboThreshold = superThresh;
            EditorUtility.SetDirty(fever.config);
        }
        var feverGuids = AssetDatabase.FindAssets("t:FeverConfigSO");
        foreach (var g in feverGuids)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<FeverConfigSO>(AssetDatabase.GUIDToAssetPath(g));
            if (cfg == null) continue;
            cfg.feverScoreMultiplier = feverMult;
            cfg.superFeverScoreMultiplier = superMult;
            cfg.feverComboThreshold = feverThresh;
            cfg.superFeverComboThreshold = superThresh;
            EditorUtility.SetDirty(cfg);
        }
        if (feverGuids.Length > 0) AssetDatabase.SaveAssets();

        SavePrefs();

        if (!silent)
            Debug.Log("[MS Debug] 参数已应用：aimOffset=" + aiAimOffset + " miss=" + aiMissChance +
                      " perfect=" + perfectScore + " good=" + goodScore + " clear=" + clearScore +
                      " pass=" + passScore + " miss=" + missScore + " leadTime=" + leadTime + " radius=" + noteRadius +
                      " chainTapHold=" + chainTapHoldDuration + " slideSettle=" + holdSlideSettleWindow + " breakTh=" + holdBreakThreshold + " earlyGrace=" + holdEarlySlideGrace + " laneTol=" + holdLaneTolerance +
                      " | Fever 系数=" + feverMult + " Super 系数=" + superMult + " Fever阈值=" + feverThresh + " Super阈值=" + superThresh);
    }

    private void ApplyAll()
    {
        // 仅把 MS Debug 参数推到运行实例（ApplyValues 已实时写入 ScoreManager / NoteSpawner / HoldNote / FeverManager.config 等），
        // 不碰谱面、不重启战斗。用户正在调用的谱面与场上音符流保持不变（2026-08-26 Issue 3 修复）。
        // 需要随机内容请用谱面编辑器的「生成随机测试谱面（替换当前编辑内容）」按钮。
        ApplyValues(true);

        EditorUtility.DisplayDialog("完成", "调试参数已应用，活跃谱面保持不变。", "确定");
    }
}
