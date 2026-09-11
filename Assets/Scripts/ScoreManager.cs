using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// 计分板管理器。
/// 订阅 NoteSpawner.OnJudge，根据 PERFECT/GOOD/MISS 更新左右玩家分数。
/// </summary>
public class ScoreManager : MonoBehaviour
{
    [Header("分数权重")]
    public int perfectScore = 100;
    public int goodScore = 60;
    public int missScore = 0;
    public int clearScore = 50;     // 长按音符每完成一段链接（节点→节点）的加分
    public int passScore = 80;      // 小型点击音符（SmallTap）命中统一加分

    [Header("玩家分数显示（可选，会自动查找）")]
    public ScoreDisplay leftScoreDisplay;
    public ScoreDisplay rightScoreDisplay;

    [Header("发射器（可选，会自动查找）")]
    public NoteSpawner leftSpawner;
    public NoteSpawner rightSpawner;

    [Header("过热系统（可选，会自动查找/补建）")]
    public FeverManager feverManager;

    [Header("战斗系统（可选，会自动查找/补建）")]
    public CharacterBattleSystem battleSystem;
    public SkillInputUI skillInputUI;

    [Header("血量与追分机制")]
    [Tooltip("每侧玩家血量上限兜底值（仅当 battleSystem.GetMaxHP 无效时使用）")]
    public int maxHP = 300;
    [Tooltip("每侧血量上限（运行时由 CharacterBattleSystem.GetMaxHP 注入）。P2 起左侧/右侧可不同。")]
    public int[] maxHPBySide = new int[2] { 300, 300 };
    [Tooltip("分差超过多少时触发保底（给劣势方补救加分）。粉杠在分差=此值时已移动到 3 单位处（BattleCenterLine.pushPerHit=0.001），此处不另动粉杠。")]
    public int catchUpDiffThreshold = 3000;
    [Tooltip("保底判定间隔（秒），每秒检查一次")]
    public float catchUpInterval = 1f;
    [Tooltip("保底扣血系数：damage = extra × combatSum × drainRate。drainRate=0.001 即“该侧(全队角色+玩家自身)战斗力总和 × 0.1%”。仅作用于劣势方。")]
    public float catchUpDrainRate = 0.001f;

    private int leftScore = 0;
    private int rightScore = 0;
    private int leftHP;
    private int rightHP;
    private float catchUpTimer = 0f;

    public event Action<int, int> OnScoreChanged;
    public event Action<int, int> OnHPChanged;
    public event Action<int> OnPlayerDefeated; // 参数：战败方 side

    /// <summary>外部读取某侧血量上限（HPBarDisplay 用）。</summary>
    public int GetMaxHP(int side) => maxHPBySide[Mathf.Clamp(side, 0, 1)];
    /// <summary>外部读取某侧当前血量。</summary>
    public int GetHP(int side) => side == 0 ? leftHP : rightHP;

    void Start()
    {
        if (leftSpawner == null) leftSpawner = FindLeftSpawner();
        if (rightSpawner == null) rightSpawner = FindRightSpawner();

        if (leftSpawner != null)
            leftSpawner.OnJudge += HandleJudge;
        else
            Debug.LogWarning("[ScoreManager] 未找到左发射器");

        if (rightSpawner != null)
            rightSpawner.OnJudge += HandleJudge;
        else
            Debug.LogWarning("[ScoreManager] 未找到右发射器");

        // 过热系统：自动查找，缺失则补建（保证倍率/连击逻辑存在）
        if (feverManager == null)
        {
            feverManager = FindFirstObjectByType<FeverManager>();
            if (feverManager == null)
            {
                var go = new GameObject("FeverManager");
                feverManager = go.AddComponent<FeverManager>();
                Debug.Log("[ScoreManager] 自动创建 FeverManager");
            }
        }

        // 战斗系统：自动查找，缺失则补建
        if (battleSystem == null)
        {
            battleSystem = FindFirstObjectByType<CharacterBattleSystem>();
            if (battleSystem == null)
            {
                var go = new GameObject("CharacterBattleSystem");
                battleSystem = go.AddComponent<CharacterBattleSystem>();
                Debug.Log("[ScoreManager] 自动创建 CharacterBattleSystem");
            }
        }
        if (skillInputUI == null)
        {
            skillInputUI = FindFirstObjectByType<SkillInputUI>();
            if (skillInputUI == null)
            {
                var go = new GameObject("SkillInputUI");
                skillInputUI = go.AddComponent<SkillInputUI>();
                Debug.Log("[ScoreManager] 自动创建 SkillInputUI");
            }
        }

        if (leftScoreDisplay == null)
            leftScoreDisplay = FindScoreDisplay("ScoreLeft");

        if (rightScoreDisplay == null)
            rightScoreDisplay = FindScoreDisplay("ScoreRight");

        // 运行时兜底：如果场景里没有分数显示对象，自动创建，避免分数 UI 缺失
        EnsureScoreDisplays();

        if (leftScoreDisplay == null)
            Debug.LogWarning("[ScoreManager] 未找到左分数显示");

        if (rightScoreDisplay == null)
            Debug.LogWarning("[ScoreManager] 未找到右分数显示");

        if (battleSystem != null)
        {
            int l = battleSystem.GetMaxHP(0);
            int r = battleSystem.GetMaxHP(1);
            if (l > 0) maxHPBySide[0] = l;
            else maxHPBySide[0] = maxHP;
            if (r > 0) maxHPBySide[1] = r;
            else maxHPBySide[1] = maxHP;
        }
        else
        {
            maxHPBySide[0] = maxHP;
            maxHPBySide[1] = maxHP;
        }
        leftHP = maxHPBySide[0];
        rightHP = maxHPBySide[1];
        this.maxHP = maxHPBySide[0]; // 兼容旧的"全游戏单一 max"读取者（HPBarDisplay 仍可读到非零值）
        UpdateDisplays();
        // 刷新血条初始显示（避免 HPBarDisplay 在 Start 中读到字段默认 0）
        OnHPChanged?.Invoke(leftHP, rightHP);
    }

    private ScoreDisplay FindScoreDisplay(string goName)
    {
        GameObject go = GameObject.Find(goName);
        if (go != null)
            return go.GetComponent<ScoreDisplay>();
        return null;
    }

    /// <summary>
    /// 如果分数显示对象不存在，则创建独立的 Screen Space Overlay Canvas 并在其下生成左右分数显示，
    /// 不依赖 HPBarCanvas，避免嵌套或层级问题导致分数不可见。
    /// </summary>
    private void EnsureScoreDisplays()
    {
        try
        {
            GameObject canvasGo = GameObject.Find("ScoreCanvas");
            if (canvasGo == null)
            {
                canvasGo = new GameObject("ScoreCanvas");
                Canvas canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100; // 确保在最上层

                CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                GraphicRaycaster raycaster = canvasGo.AddComponent<GraphicRaycaster>();
            }
            Transform canvasTrans = canvasGo.transform;

            if (leftScoreDisplay == null)
            {
                // 左分数：在 1.1 倍基础上再放大 1.2 倍（总 1.32 倍）
                leftScoreDisplay = CreateRuntimeScoreDisplay("ScoreLeft", canvasTrans,
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(456f, -18f), new Color(0.2f, 1f, 0.2f, 1f),
                    TextAnchor.MiddleLeft);
                Debug.Log("[ScoreManager] 自动创建左分数显示 ScoreLeft");
            }

            if (rightScoreDisplay == null)
            {
                // 右分数：在 1.1 倍基础上再放大 1.2 倍（总 1.32 倍）
                rightScoreDisplay = CreateRuntimeScoreDisplay("ScoreRight", canvasTrans,
                    new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-456f, -18f), new Color(1f, 0.85f, 0.1f, 1f),
                    TextAnchor.MiddleRight);
                Debug.Log("[ScoreManager] 自动创建右分数显示 ScoreRight");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScoreManager] EnsureScoreDisplays 创建分数显示失败：{e}");
        }
    }

    private ScoreDisplay CreateRuntimeScoreDisplay(string name, Transform canvas,
        Vector2 aMin, Vector2 aMax, Vector2 pos, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(canvas, false);

        RectTransform rt = root.AddComponent<RectTransform>();
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = aMin;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(396f, 73f); // 在 1.1 倍基础上再放大 1.2 倍

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(root.transform, false);

        RectTransform tr = textGo.AddComponent<RectTransform>();
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;

        Text uiText = textGo.AddComponent<Text>();
        uiText.text = "0";
        uiText.fontSize = 68; // 在 1.1 倍基础上再放大 1.2 倍
        uiText.alignment = alignment;
        uiText.color = color;
        uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (uiText.font == null) uiText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (uiText.font == null) uiText.font = Font.CreateDynamicFontFromOSFont("Arial", 80);

        Outline outline = textGo.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(5f, 5f);

        ScoreDisplay display = root.AddComponent<ScoreDisplay>();
        display.isScreenSpace = true;
        display.scoreText = uiText;
        return display;
    }

    /// <summary>
    /// IMGUI 兜底绘制：只显示分数（不显示血量），放在血条旁边。
    /// 始终绘制，不再因 uGUI 对象存在就跳过——避免 uGUI 层级/缩放异常时玩家看不到分数。
    /// </summary>
    void OnGUI()
    {
        // 实时同步 uGUI 分数文字（如果存在），保持两边一致
        SyncUGUIScoresToIMGUI();

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = Mathf.RoundToInt(48f * 1.32f); // 在 1.1 倍基础上再放大 1.2 倍
        style.alignment = TextAnchor.MiddleLeft;
        style.fontStyle = FontStyle.Bold;

        // 根据 HPBarCanvas 的实际参数与用户微调计算分数位置。
        // 左血条 reference： anchoredPosition=(30,-30), sizeDelta=(432,43), pivot=左上角
        // 右血条 reference： anchoredPosition=(-30,-30), sizeDelta=(432,43), pivot=右上角
        // 左血条右缘 x=462；右血条左缘 x=1458；血条中心 y=-51.5（从顶部向下 reference 像素）。
        // 在 1.1 倍基础上再放大 1.2 倍，右移/左移偏移从 40 放大到 48。
        float scaleX = Screen.width / 1920f;
        float scaleY = Screen.height / 1080f;
        float canvasScale = Mathf.Sqrt(scaleX * scaleY); // 对应 match=0.5 的 CanvasScaler

        float zoom = 1.32f;
        float labelHeight = 46f * canvasScale * zoom;
        float labelWidth = 360f * canvasScale * zoom;  // 加宽以容纳 >10000 的高分（原 180 会裁切）

        int shiftX = 48; // 在 40 基础上放大 1.2 倍
        float leftEdgeRef = 462f + 10f + shiftX; // 左分数左缘 reference x = 520
        float rightEdgeRef = 1458f - 10f - shiftX; // 右分数右缘 reference x = 1400
        float centerYFromTop = 51.5f;              // 回到血条中心（从顶部向下 reference 像素）

        float posY = centerYFromTop * scaleY - labelHeight * 0.5f;
        float leftPosX = leftEdgeRef * scaleX;
        float rightPosX = rightEdgeRef * scaleX - labelWidth; // 右分数右缘 - 宽度 = 左上角

        // 左分数（绿）放在左血条右侧
        style.normal.textColor = Color.green;
        GUI.Label(new Rect(leftPosX, posY, labelWidth, labelHeight), $"{leftScore}", style);

        // 右分数（黄）放在右血条左侧
        style.alignment = TextAnchor.MiddleRight;
        style.normal.textColor = Color.yellow;
        GUI.Label(new Rect(rightPosX, posY, labelWidth, labelHeight), $"{rightScore}", style);
    }

    private void SyncUGUIScoresToIMGUI()
    {
        if (leftScoreDisplay != null && leftScoreDisplay.scoreText != null &&
            leftScoreDisplay.scoreText.text != leftScore.ToString())
        {
            leftScoreDisplay.SetScore(leftScore);
        }

        if (rightScoreDisplay != null && rightScoreDisplay.scoreText != null &&
            rightScoreDisplay.scoreText.text != rightScore.ToString())
        {
            rightScoreDisplay.SetScore(rightScore);
        }
    }

    void Update()
    {
        catchUpTimer += Time.deltaTime;
        if (catchUpTimer >= catchUpInterval)
        {
            catchUpTimer -= catchUpInterval;
            ApplyCatchUp();
        }

        // 保险：如果分数显示对象在运行时被意外销毁或丢失，尝试重新创建
        if (leftScoreDisplay == null || rightScoreDisplay == null ||
            (leftScoreDisplay != null && leftScoreDisplay.scoreText == null) ||
            (rightScoreDisplay != null && rightScoreDisplay.scoreText == null))
        {
            EnsureScoreDisplays();
        }
    }

    /// <summary>
    /// 保底（追分）机制（P2 修正版，依据最新设计描述）：
    /// - 分差 > catchUpDiffThreshold（默认 3000）时启动。优势方每 catchUpInterval 秒继续拉开分差后，
    ///   给劣势方额外加分 100×M（M 为使分差≤阈值的最小整数倍），把分差一次性压回 ≤3000。
    /// - 优势方分数与血量均不受影响（不转移、不扣血）。
    /// - 劣势方因被保底补救而付出的代价：按“额外加分 × (优势方全队角色+玩家自身战斗力总和) × 0.1%”扣血。
    ///   即 damage = extra × combatSum(优势方) × catchUpDrainRate（drainRate=0.001 → 优势方战斗力总和×0.1%）。
    /// 注：粉杠随真实分差移动（BattleCenterLine），分差 3000 时粉杠已在 3 单位处；此处只改分数/血量，不改粉杠。
    /// </summary>
    private void ApplyCatchUp()
    {
        int diff = Mathf.Abs(leftScore - rightScore);
        if (diff <= catchUpDiffThreshold) return;

        int higherSide = leftScore > rightScore ? 0 : 1;
        int lowerSide = 1 - higherSide;

        // 超过阈值的部分压回 ≤ 阈值：给劣势方额外加分（优势方分数/血量均不动）
        int overflow = diff - catchUpDiffThreshold;
        int multiple = Mathf.CeilToInt(overflow / 100f); // 让分差≤阈值的最小整数倍
        int extra = 100 * multiple;
        AddScore(lowerSide, +extra);

        // 劣势方代价：damage = extra × (优势方全队+玩家自身战斗力总和) × 0.1%
        // 注意：combatSum 取【优势方】的战斗力总和，不是劣势方
        float combatSum = (battleSystem != null) ? battleSystem.GetCombatSum(higherSide) : 100f;
        int damage = Mathf.RoundToInt(extra * combatSum * catchUpDrainRate);
        TakeDamage(lowerSide, damage);

        Debug.Log($"[ScoreManager] 保底：diff={diff} 优势方side{higherSide}不动，劣势方side{lowerSide} +{extra}分，扣血 {damage}（优势方combatSum={combatSum}, drain={catchUpDrainRate}）");
    }

    /// <summary>
    /// 外部或内部用来加减分。
    /// </summary>
    public void AddScore(int side, int amount)
    {
        if (side == 0) leftScore = Mathf.Max(0, leftScore + amount);
        else rightScore = Mathf.Max(0, rightScore + amount);

        UpdateDisplays();
        OnScoreChanged?.Invoke(leftScore, rightScore);
    }

    /// <summary>
    /// 对指定 side 造成伤害，血量 ≤ 0 时触发战败。
    /// 先按 a 防御 buff 减伤，再施加以触发"受扣血解除沉睡"。
    /// </summary>
    public void TakeDamage(int side, int damage)
    {
        // 减伤（a 防御 buff）：受到的伤害按比例减少
        if (damage > 0)
        {
            var bc = BuffController.Instance ?? FindFirstObjectByType<BuffController>();
            if (bc != null)
            {
                float reduce = bc.GetDamageReduction(side);
                if (reduce > 0f) damage = Mathf.RoundToInt(damage * (1f - reduce));
            }
        }

        // 受击解除沉睡（控制免疫由 SleepController 内部判断）
        var sc = SleepController.Instance ?? FindFirstObjectByType<SleepController>();
        if (sc != null) sc.BreakSleepOnDamage(side);

        if (side == 0)
            leftHP = Mathf.Max(0, leftHP - damage);
        else
            rightHP = Mathf.Max(0, rightHP - damage);

        OnHPChanged?.Invoke(leftHP, rightHP);

        if (side == 0 && leftHP <= 0)
            OnPlayerDefeated?.Invoke(0);
        else if (side == 1 && rightHP <= 0)
            OnPlayerDefeated?.Invoke(1);
    }

    /// <summary>
    /// 对指定 side 进行治疗，不超过该侧血量上限（不触发战败）。
    /// amount 应已向上取整为整数（全局整数规则：伤害/回血/生命上限一律 CeilToInt）。
    /// </summary>
    public void Heal(int side, int amount)
    {
        if (amount <= 0) return;
        int max = GetMaxHP(side);
        if (side == 0)
            leftHP = Mathf.Min(max, leftHP + amount);
        else
            rightHP = Mathf.Min(max, rightHP + amount);

        OnHPChanged?.Invoke(leftHP, rightHP);
    }

    void OnDestroy()
    {
        if (leftSpawner != null)
            leftSpawner.OnJudge -= HandleJudge;

        if (rightSpawner != null)
            rightSpawner.OnJudge -= HandleJudge;
    }

    private void HandleJudge(int side, int lane, string rank, Vector3 position, UnityEngine.Object source)
    {
        int delta = 0;
        switch (rank)
        {
            case "PERFECT": delta = perfectScore; break;
            case "GOOD": delta = goodScore; break;
            case "CLEAR": delta = clearScore; break;
            case "PASS": delta = passScore; break;
            case "MISS": delta = missScore; break;
            case "BREAK": delta = 0; break;   // 连轨中途断连：不计分、不充能（与 MISS 同效，仅不清零连击）
        }

        // MISS：不加分、不充能（仅由 FeverManager 的 OnNoteMiss 断连）
        if (delta <= 0) return;

        // 过热倍率：最终得分 = 基础分 × 倍率（分数不损失，只在得分上乘倍率）
        float mul = (feverManager != null) ? feverManager.GetScoreMultiplier(side) : 1f;
        int actual = Mathf.RoundToInt(delta * mul);

        if (side == 0)
        {
            leftScore += actual;
        }
        else
        {
            rightScore += actual;
        }

        // 能量：仅给命中音轨对应的队伍角色充能（10 分 = 1 能量），其他角色不动
        CharacterClass tc = CharacterRoster.GetTeam(side, lane);
        if (tc != null)
            tc.AddEnergy(actual / 10f);

        UpdateDisplays();
        OnScoreChanged?.Invoke(leftScore, rightScore);
        Debug.Log($"[ScoreManager] Side {side} {rank} +{actual} (raw {delta}, x{mul}) | Left={leftScore} Right={rightScore}");
    }

    private void UpdateDisplays()
    {
        if (leftScoreDisplay == null) leftScoreDisplay = FindScoreDisplay("ScoreLeft");
        if (rightScoreDisplay == null) rightScoreDisplay = FindScoreDisplay("ScoreRight");

        if (leftScoreDisplay == null)
            Debug.LogWarning($"[ScoreManager] 找不到左分数显示对象 ScoreLeft（leftScore={leftScore}）");
        if (rightScoreDisplay == null)
            Debug.LogWarning($"[ScoreManager] 找不到右分数显示对象 ScoreRight（rightScore={rightScore}）");

        if (leftScoreDisplay != null) leftScoreDisplay.SetScore(leftScore);
        if (rightScoreDisplay != null) rightScoreDisplay.SetScore(rightScore);
    }

    public void ResetScores()
    {
        leftScore = 0;
        rightScore = 0;
        int l = (battleSystem != null) ? battleSystem.GetMaxHP(0) : 0;
        int r = (battleSystem != null) ? battleSystem.GetMaxHP(1) : 0;
        maxHPBySide[0] = l > 0 ? l : maxHP;
        maxHPBySide[1] = r > 0 ? r : maxHP;
        leftHP = maxHPBySide[0];
        rightHP = maxHPBySide[1];
        this.maxHP = maxHPBySide[0];
        catchUpTimer = 0f;
        UpdateDisplays();
        OnScoreChanged?.Invoke(leftScore, rightScore);
        OnHPChanged?.Invoke(leftHP, rightHP);
    }

    public int GetLeftScore() => leftScore;
    public int GetRightScore() => rightScore;
    public int GetLeftHP() => leftHP;
    public int GetRightHP() => rightHP;

    private NoteSpawner FindLeftSpawner()
    {
        var all = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var s in all)
            if (s.side == 0) return s;
        return null;
    }

    private NoteSpawner FindRightSpawner()
    {
        var all = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var s in all)
            if (s.side == 1) return s;
        return null;
    }
}
