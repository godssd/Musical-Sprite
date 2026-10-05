using UnityEngine;
using UnityEditor;

/// <summary>
/// Musical Sprite AI 对手调试工具（2026-10-05 从 MusicalSpriteDebugWindow 拆出）。
/// 可调整：AI 难度参数表 OpponentAIProfile 的 12 个字段 + 难度资产管理（保存/删除 .asset）。
/// 菜单：Tools > Musical Sprite > AI Debug Window
///
/// 设计（用户裁定）：AI 参数高频调整且有多套难度，不落盘——
/// 「应用」只注入运行时实例（进 Play 自动重灌，开销极小：仅 1 次场景查找 + 写 12 个字段）；
/// 「保存难度资产」才写磁盘 .asset（显式操作）。
/// </summary>
public class MusicalSpriteAIDebugWindow : EditorWindow
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

    private Vector2 scroll;

    private const string PREFS = "MusicalSprite.AIDebug.";

    [MenuItem("Tools/Musical Sprite/AI Debug Window")]
    public static void ShowWindow()
    {
        GetWindow<MusicalSpriteAIDebugWindow>("MS AI Debug");
    }

    private void OnEnable()
    {
        LoadPrefs();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            // AI 参数不落盘，进 Play 时重灌一次（仅 1 次查找 + 12 字段写入，开销可忽略）
            ApplyAI();
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
    }

    private void SyncFromScene()
    {
        OpponentInput opponent = FindFirstObjectByType<OpponentInput>();
        if (opponent != null && opponent.profile != null)
        {
            aiProfileRef = opponent.profile;
            SyncFromProfile(opponent.profile);
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("Musical Sprite AI 对手调试", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("AI 参数高频调整、多套难度，故【不落盘】：「应用」只注入运行时（进 Play 自动重灌）；「保存难度资产」才写磁盘 .asset（显式操作）。", MessageType.Info);
        GUILayout.Space(10);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawAI();
        GUILayout.Space(20);
        EditorGUILayout.EndScrollView();

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("应用 AI 参数（运行时注入）", GUILayout.Height(40)))
        {
            ApplyAI();
            SavePrefs();
            EditorUtility.DisplayDialog("完成", "AI 参数已注入运行时（未写磁盘）。\n下次进 Play 会自动重灌。要永久保存请用「保存难度资产」。", "确定");
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
        // 注意：这里不 SaveAssets——AI 参数保持「运行时注入」语义；「保存难度资产」按钮才显式写盘。
    }

    /// <summary>运行时注入：把当前面板 12 个 AI 参数写入场景 OpponentInput 所指的难度资产实例（不写磁盘）。</summary>
    private void ApplyAI()
    {
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
            Debug.LogWarning("[MS AI Debug] 未找到 OpponentAIProfile，AI 参数未应用（请在场景中给 OpponentInput.profile 拖入难度资产）。");
        }
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
        Debug.Log("[MS AI Debug] 已保存难度资产：" + path);
    }

    /// <summary>删除当前选中的难度资产（含 .meta）；基础 4 档会额外警告。同时清空场景引用。</summary>
    private void DeleteCurrentProfile()
    {
        if (aiProfileRef == null)
        {
            Debug.LogWarning("[MS AI Debug] 当前没有选中任何难度资产，无法删除（可先用 ObjectField 选择一个）。");
            return;
        }
        string path = AssetDatabase.GetAssetPath(aiProfileRef);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("[MS AI Debug] 选中的 Profile 不在磁盘上（运行时临时实例），无法删除。请先「新建」或选择一个磁盘上的资产。");
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
            Debug.Log("[MS AI Debug] 已删除难度资产：" + path);
        }
    }

    private void DrawAI()
    {
        GUILayout.Label("AI 对手（难度参数表 OpponentAIProfile）", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(GUI.skin.box);

        aiProfileRef = (OpponentAIProfile)EditorGUILayout.ObjectField("难度资产 Profile", aiProfileRef, typeof(OpponentAIProfile), false);
        EditorGUILayout.HelpBox("直接编辑该难度资产（Assets/Data/AI/ 下 Easy/Medium/Hard/Nightmare）。留空则自动指向场景中 OpponentInput.profile。", MessageType.None);

        if (GUILayout.Button("从场景/资产读取当前值"))
        {
            SyncFromScene();
            if (aiProfileRef == null) Debug.LogWarning("[MS AI Debug] 未找到 OpponentAIProfile（场景中 OpponentInput.profile 为空）。");
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

        // —— 难度资产管理 ——
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
            else Debug.LogWarning("[MS AI Debug] 未找到 OpponentInput。");
        }
        GUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }
}
