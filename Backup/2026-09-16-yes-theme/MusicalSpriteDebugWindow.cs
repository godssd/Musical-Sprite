using UnityEngine;
using UnityEditor;
using MusicalSprite.Editor;

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
    // AI（新框架：直接编辑 OpponentAIProfile 难度资产，旧 aimOffset/missChance 模型已淘汰）
    private OpponentAIProfile aiProfileRef;            // 直接编辑的难度资产引用（默认指向场景中 OpponentInput.profile）
    private float aiNoteHitRate = 0.6f;
    private float aiOffsetMin = 0f;
    private float aiOffsetMax = 1f;
    private float aiEvaluateInterval = 5f;
    private float aiReleaseProb = 0.4f;
    private float aiInputSpeed = 1f;
    private int aiDogHowlCombo = 30;
    private float aiHealHpRatio = 0.35f;
    private int aiClearScreenNotes = 2;
    private int aiOffenseLead = 1300;
    private float aiOffenseAfterDog = 0.05f;
    private float aiOffenseAfterBomb = 0.1f;

    // 新建难度资产时的名称（默认 Custom，可改）
    private string aiNewProfileName = "OpponentAIProfile_Custom";

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
    private BeatmapDensity density = BeatmapDensity.Medium;

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
        // 2026-09-14：不再在关窗口时自动存草稿。
        // 「应用所有修改」才会把当前面板值写入 EditorPrefs，确保下次打开是上次应用值；
        // 没点应用就关闭 → 正确回退到上次应用值。
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
        aiNoteHitRate = EditorPrefs.GetFloat(PREFS + "aiNoteHitRate", aiNoteHitRate);
        aiOffsetMin = EditorPrefs.GetFloat(PREFS + "aiOffsetMin", aiOffsetMin);
        aiOffsetMax = EditorPrefs.GetFloat(PREFS + "aiOffsetMax", aiOffsetMax);
        aiEvaluateInterval = EditorPrefs.GetFloat(PREFS + "aiEvaluateInterval", aiEvaluateInterval);
        aiReleaseProb = EditorPrefs.GetFloat(PREFS + "aiReleaseProb", aiReleaseProb);
        aiInputSpeed = EditorPrefs.GetFloat(PREFS + "aiInputSpeed", aiInputSpeed);
        aiDogHowlCombo = EditorPrefs.GetInt(PREFS + "aiDogHowlCombo", aiDogHowlCombo);
        aiHealHpRatio = EditorPrefs.GetFloat(PREFS + "aiHealHpRatio", aiHealHpRatio);
        aiClearScreenNotes = EditorPrefs.GetInt(PREFS + "aiClearScreenNotes", aiClearScreenNotes);
        aiOffenseLead = EditorPrefs.GetInt(PREFS + "aiOffenseLead", aiOffenseLead);
        aiOffenseAfterDog = EditorPrefs.GetFloat(PREFS + "aiOffenseAfterDog", aiOffenseAfterDog);
        aiOffenseAfterBomb = EditorPrefs.GetFloat(PREFS + "aiOffenseAfterBomb", aiOffenseAfterBomb);
        perfectScore = EditorPrefs.GetInt(PREFS + "perfectScore", perfectScore);
        goodScore = EditorPrefs.GetInt(PREFS + "goodScore", goodScore);
        missScore = EditorPrefs.GetInt(PREFS + "missScore", missScore);
        clearScore = EditorPrefs.GetInt(PREFS + "clearScore", clearScore);
        passScore = EditorPrefs.GetInt(PREFS + "passScore", passScore);
        leadTime = EditorPrefs.GetFloat(PREFS + "leadTime", leadTime);
        noteRadius = EditorPrefs.GetFloat(PREFS + "noteRadius", noteRadius);
        totalBeats = EditorPrefs.GetInt(PREFS + "totalBeats", totalBeats);
        density = (BeatmapDensity)EditorPrefs.GetInt(PREFS + "density", (int)density);
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
        EditorPrefs.SetFloat(PREFS + "aiNoteHitRate", aiNoteHitRate);
        EditorPrefs.SetFloat(PREFS + "aiOffsetMin", aiOffsetMin);
        EditorPrefs.SetFloat(PREFS + "aiOffsetMax", aiOffsetMax);
        EditorPrefs.SetFloat(PREFS + "aiEvaluateInterval", aiEvaluateInterval);
        EditorPrefs.SetFloat(PREFS + "aiReleaseProb", aiReleaseProb);
        EditorPrefs.SetFloat(PREFS + "aiInputSpeed", aiInputSpeed);
        EditorPrefs.SetInt(PREFS + "aiDogHowlCombo", aiDogHowlCombo);
        EditorPrefs.SetFloat(PREFS + "aiHealHpRatio", aiHealHpRatio);
        EditorPrefs.SetInt(PREFS + "aiClearScreenNotes", aiClearScreenNotes);
        EditorPrefs.SetInt(PREFS + "aiOffenseLead", aiOffenseLead);
        EditorPrefs.SetFloat(PREFS + "aiOffenseAfterDog", aiOffenseAfterDog);
        EditorPrefs.SetFloat(PREFS + "aiOffenseAfterBomb", aiOffenseAfterBomb);
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
        if (opponent != null && opponent.profile != null)
        {
            aiProfileRef = opponent.profile;
            SyncFromProfile(opponent.profile);
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
        EditorGUILayout.HelpBox("修改参数后点击「应用所有修改」写入工程（EditorPrefs）并注入运行时；只改不应用，关闭窗口后会回退到上次应用值。「保存难度资产」仅把当前 AI 参数另存/覆盖 .asset，不应用。", MessageType.Info);
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

    private void SyncFromProfile(OpponentAIProfile p)
    {
        if (p == null) return;
        aiNoteHitRate = p.noteHitRate;
        aiOffsetMin = p.offsetMinMul;
        aiOffsetMax = p.offsetMaxMul;
        aiEvaluateInterval = p.evaluateInterval;
        aiReleaseProb = p.releaseProbability;
        aiInputSpeed = p.inputSpeed;
        aiDogHowlCombo = p.dogHowlOppComboThreshold;
        aiHealHpRatio = p.healHpRatioThreshold;
        aiClearScreenNotes = p.clearScreenNoteThreshold;
        aiOffenseLead = p.offenseScoreLeadThreshold;
        aiOffenseAfterDog = p.offenseAfterDogHowlChance;
        aiOffenseAfterBomb = p.offenseAfterBombChance;
    }

    private void ApplyToProfile(OpponentAIProfile p)
    {
        if (p == null) return;
        p.noteHitRate = aiNoteHitRate;
        p.offsetMinMul = aiOffsetMin;
        p.offsetMaxMul = aiOffsetMax;
        p.evaluateInterval = aiEvaluateInterval;
        p.releaseProbability = aiReleaseProb;
        p.inputSpeed = aiInputSpeed;
        p.dogHowlOppComboThreshold = aiDogHowlCombo;
        p.healHpRatioThreshold = aiHealHpRatio;
        p.clearScreenNoteThreshold = aiClearScreenNotes;
        p.offenseScoreLeadThreshold = aiOffenseLead;
        p.offenseAfterDogHowlChance = aiOffenseAfterDog;
        p.offenseAfterBombChance = aiOffenseAfterBomb;
        EditorUtility.SetDirty(p);
        // 2026-09-14：这里不再自动 SaveAssets，避免「应用所有修改」顺手写回 .asset。
        // 只有「保存难度资产」按钮会显式调 SaveAssets。
    }

    /// <summary>保存难度资产：把当前窗口 12 个 AI 参数写入 Assets/Data/AI/{name}.asset。
    /// 同名资产会询问是否覆盖；不同名则新建。本操作只写磁盘 .asset，不注入运行时。</summary>
    private void SaveProfileAsset(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "OpponentAIProfile_Custom";
        if (!AssetDatabase.IsValidFolder("Assets/Data/AI"))
            AssetDatabase.CreateFolder("Assets/Data", "AI");
        string path = "Assets/Data/AI/" + name + ".asset";

        bool overwrite = false;
        OpponentAIProfile existing = null;
        if (System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), path)))
        {
            existing = AssetDatabase.LoadAssetAtPath<OpponentAIProfile>(path);
            if (existing != null)
            {
                overwrite = EditorUtility.DisplayDialog("覆盖难度资产",
                    $"已存在难度资产「{name}」，是否覆盖？\n路径：{path}",
                    "覆盖", "取消");
                if (!overwrite) return;
            }
        }

        OpponentAIProfile p;
        if (overwrite && existing != null)
        {
            p = existing;
        }
        else
        {
            p = ScriptableObject.CreateInstance<OpponentAIProfile>();
        }
        ApplyToProfile(p);
        if (!overwrite)
        {
            AssetDatabase.CreateAsset(p, path);
        }
        AssetDatabase.SaveAssets();
        aiProfileRef = p;
        SyncFromProfile(p);
        Debug.Log("[MS Debug] 已保存难度资产：" + path);
    }

    /// <summary>删除当前选中的难度资产（含 .meta）；基础 4 档会额外警告。同时清空场景引用。</summary>
    private void DeleteCurrentProfile()
    {
        if (aiProfileRef == null)
        {
            Debug.LogWarning("[MS Debug] 当前没有选中任何难度资产，无法删除（可先用「从场景/资产读取」或「新建难度资产」）。");
            return;
        }
        string path = AssetDatabase.GetAssetPath(aiProfileRef);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("[MS Debug] 选中的 Profile 不在磁盘上（运行时临时实例），无法删除。请先「新建」或「从场景/资产读取」一个磁盘上的资产。");
            return;
        }
        bool isBase = path.Contains("OpponentAIProfile_Easy") || path.Contains("OpponentAIProfile_Medium")
                   || path.Contains("OpponentAIProfile_Hard") || path.Contains("OpponentAIProfile_Nightmare");
        string msg = isBase
            ? "确定要删除基础难度资产「" + aiProfileRef.name + "」吗？\n路径：" + path + "\n（这是 4 个基础档之一，删除后需重新生成。）"
            : "确定要删除难度资产「" + aiProfileRef.name + "」吗？\n路径：" + path;
        if (EditorUtility.DisplayDialog("删除难度资产", msg, "删除", "取消"))
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
            // 若场景 OpponentInput 引用的就是它，一并清空，避免悬空引用
            var opp = FindFirstObjectByType<OpponentInput>();
            if (opp != null && opp.profile == aiProfileRef) { opp.profile = null; EditorUtility.SetDirty(opp); }
            aiProfileRef = null;
            Debug.Log("[MS Debug] 已删除难度资产：" + path);
        }
    }

    private void DrawAI()
    {
        GUILayout.Label("AI 对手（新框架：难度参数表 OpponentAIProfile）", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(GUI.skin.box);

        aiProfileRef = (OpponentAIProfile)EditorGUILayout.ObjectField("难度资产 Profile", aiProfileRef, typeof(OpponentAIProfile), false);
        EditorGUILayout.HelpBox("直接编辑该难度资产（Assets/Data/AI/ 下 Easy/Medium/Hard/Nightmare）。留空则自动指向场景中 OpponentInput.profile。修改会写回资产并保存。", MessageType.None);

        if (GUILayout.Button("从场景/资产读取当前值"))
        {
            OpponentInput opponent = FindFirstObjectByType<OpponentInput>();
            OpponentAIProfile src = (aiProfileRef != null) ? aiProfileRef : (opponent != null ? opponent.profile : null);
            if (src != null) { aiProfileRef = src; SyncFromProfile(src); }
            else Debug.LogWarning("[MS Debug] 未找到 OpponentAIProfile（场景中 OpponentInput.profile 为空）。");
        }

        aiNoteHitRate = EditorGUILayout.Slider("命中概率 noteHitRate", aiNoteHitRate, 0f, 1f);
        EditorGUILayout.HelpBox("只决定 AI 是否按正确时机按下按键（不按=无输入，音符自然 MISS）。", MessageType.None);

        aiOffsetMin = EditorGUILayout.FloatField("偏移下限 offsetMinMul (×goodWindow)", aiOffsetMin);
        aiOffsetMax = EditorGUILayout.FloatField("偏移上限 offsetMaxMul (×goodWindow)", aiOffsetMax);
        EditorGUILayout.HelpBox("偏移上限>1 表示有概率「点出但未命中」(超出 GOOD 窗口即 MISS)；小音符(小型点击)有效窗口=0.6×goodWindow，故 offsetFrac>0.6 必 MISS。", MessageType.None);

        aiEvaluateInterval = EditorGUILayout.FloatField("技能评估间隔 evaluateInterval (秒)", aiEvaluateInterval);
        aiReleaseProb = EditorGUILayout.Slider("发动概率 releaseProbability", aiReleaseProb, 0f, 1f);
        aiInputSpeed = EditorGUILayout.FloatField("输入手速 inputSpeed (秒/整段)", aiInputSpeed);

        aiDogHowlCombo = EditorGUILayout.IntField("大狗叫连击阈值 dogHowlOppComboThreshold", aiDogHowlCombo);
        aiHealHpRatio = EditorGUILayout.Slider("牛角包血量阈值 healHpRatioThreshold", aiHealHpRatio, 0f, 1f);
        aiClearScreenNotes = EditorGUILayout.IntField("清屏音符阈值 clearScreenNoteThreshold", aiClearScreenNotes);
        aiOffenseLead = EditorGUILayout.IntField("全体进攻领先阈值 offenseScoreLeadThreshold", aiOffenseLead);

        aiOffenseAfterDog = EditorGUILayout.Slider("追加进攻(主=大狗叫) offenseAfterDogHowlChance", aiOffenseAfterDog, 0f, 1f);
        aiOffenseAfterBomb = EditorGUILayout.Slider("追加进攻(主=炸弹雨) offenseAfterBombChance", aiOffenseAfterBomb, 0f, 1f);
        EditorGUILayout.HelpBox("主技能=大狗叫/炸弹雨 时，按对应概率额外追加全体进攻（先进攻后主技能，不要求领先 1300）。", MessageType.None);

        // —— 难度资产管理（替代原「套用中等预设」）——
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("难度资产管理", EditorStyles.boldLabel);

        GUILayout.BeginHorizontal();
        aiNewProfileName = EditorGUILayout.TextField("资产名称", aiNewProfileName);
        if (GUILayout.Button("保存难度资产", GUILayout.Width(110)))
        {
            SaveProfileAsset(aiNewProfileName);
        }
        GUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("把当前窗口里这 12 个 AI 参数保存为 Assets/Data/AI/ 下的难度资产。同名覆盖、不同名新建；只写 .asset，不应用到运行时。", MessageType.None);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("删除当前资产"))
        {
            DeleteCurrentProfile();
        }
        if (GUILayout.Button("（调试）强制 AI 立即放一个技能"))
        {
            var opp = FindFirstObjectByType<OpponentInput>();
            if (opp != null) opp.ForceCastSkill();
            else Debug.LogWarning("[MS Debug] 未找到 OpponentInput。");
        }
        GUILayout.EndHorizontal();

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

        density = (BeatmapDensity)EditorGUILayout.EnumPopup("谱面密度", density);
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
        if (GUILayout.Button("按角色身份色重新上色所有方块（小熊灰/大狗橙/屎屎绿/布姆白/小黑黑）"))
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
        // 1. AI（新框架：写回 OpponentAIProfile 难度资产，旧 aimOffset/missChance 模型已淘汰）
        OpponentInput opponent = FindFirstObjectByType<OpponentInput>();
        OpponentAIProfile targetProfile = (aiProfileRef != null) ? aiProfileRef : (opponent != null ? opponent.profile : null);
        if (targetProfile != null)
        {
            ApplyToProfile(targetProfile);
            // 若场景 opponent.profile 与手动指定的资产不同，也同步写回场景引用的那份
            if (opponent != null && opponent.profile != null && opponent.profile != targetProfile)
                ApplyToProfile(opponent.profile);
        }
        else
        {
            Debug.LogWarning("[MS Debug] 未找到 OpponentAIProfile，AI 参数未应用（请在场景中给 OpponentInput.profile 拖入难度资产）。");
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
        // 2026-09-14：「应用所有修改」不再写任何 .asset（包括 Fever 配置）。
        // 只把当前面板值保存到 EditorPrefs 并注入运行时实例。

        SavePrefs();

        if (!silent)
            Debug.Log("[MS Debug] 参数已应用：AI profile=" + (targetProfile != null ? targetProfile.name : "null") +
                      " hitRate=" + aiNoteHitRate + " offset=[" + aiOffsetMin + "," + aiOffsetMax + "]" +
                      " eval=" + aiEvaluateInterval + " releaseProb=" + aiReleaseProb + " inputSpeed=" + aiInputSpeed +
                      " dogHowlCombo=" + aiDogHowlCombo + " healHp=" + aiHealHpRatio + " clearNotes=" + aiClearScreenNotes +
                      " offenseLead=" + aiOffenseLead + " offAfterDog=" + aiOffenseAfterDog + " offAfterBomb=" + aiOffenseAfterBomb +
                      " | perfect=" + perfectScore + " good=" + goodScore + " clear=" + clearScore +
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
