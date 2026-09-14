using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 对手（AI）输入模拟器（新框架，替换旧 aimOffset/missChance 模型）。
///
/// 设计（来自用户方案表）：
/// 1) 音符打击：按 profile 的 noteHitRate 决定是否按下按键；按下时偏移 offsetFrac∈[offsetMinMul,offsetMaxMul]，
///    符号 50% 提前 / 50% 延后；|时间线偏移|≤有效窗口才命中，否则「点出但未命中(MISS)」。
///    小音符(小型点击)有效窗口 = 0.6×goodWindow，故 offsetFrac>0.6 必 MISS（AI 侧按 0.6 缩放时间线偏移实现）。
/// 2) 主动技能：大前提(能量满[无能量=视为满] + 不在 CD) → 专属条件 → 每个候选独立掷 releaseProbability
///    → 从发动里随机抽 1 个 = 主技能 → 主技能=大狗叫/炸弹雨 时按概率追加全体进攻（额外判定，不要求领先 1300），
///    重排为先放全体进攻、再放主技能（一次只输入一个技能，但先后捆绑）。
/// 3) 模拟玩家：AI 不直接调 BeginCast，而是把输入序列喂给 SkillInputUI.FeedInput(1, step)，走与玩家键盘/触摸完全相同的管线。
///
/// 挂载：场景任意 GO；GameManager 会自动接线 spawner/conductor/beatmap 并调用 ResetInput()。
/// </summary>
public class OpponentInput : MonoBehaviour
{
    [Header("引用（GameManager 会自动接线；缺失时自动查找）")]
    public NoteSpawner spawner;
    public Conductor conductor;
    public BeatmapSO beatmap;

    [Header("AI 难度参数")]
    [Tooltip("难度参数表（音符命中率/偏移/技能判定/概率等）。留空则使用内置默认（≈中等）。可在 Assets/Data/AI/ 选择 4 份难度资产")]
    public OpponentAIProfile profile;

    [Header("视觉表现")]
    [Tooltip("AI 按下轨道时是否显示轨道高亮")]
    public bool showVisualFeedback = true;

    /// <summary>AI 按下轨道时触发（参数：lane）。供 OpponentVisualFeedback 订阅做指示灯反馈。</summary>
    public event System.Action<int> OnPressLane;

    // ===== 音符打击调度 =====
    private int pressIndex = 0;
    private List<(float time, int lane, bool isSmall)> pressEvents = new List<(float, int, bool)>();
    private int releaseIndex = 0;
    private List<(float time, int lane)> releaseEvents = new List<(float, int)>();

    // ===== 主动技能 =====
    private float nextEvaluateTime = 0f;
    private bool aiCasting = false;
    private bool aiDiagnosedOnce = false;   // EvaluateSkills 一次性诊断标志（打印 side=1 运行时概览）

    // 自动查找的引用
    private ScoreManager scoreManager;
    private CharacterBattleSystem battleSystem;
    private SkillInputUI skillInputUI;

    void Start()
    {
        if (spawner == null) spawner = FindRightSpawner();
        if (conductor == null) conductor = FindFirstObjectByType<Conductor>();
        if (beatmap == null && spawner != null) beatmap = spawner.beatmap;
        scoreManager = FindFirstObjectByType<ScoreManager>();
        battleSystem = FindFirstObjectByType<CharacterBattleSystem>();
        skillInputUI = FindFirstObjectByType<SkillInputUI>();
        // 无 profile 时给一份内置默认（≈中等），保证不配资产也能跑
        if (profile == null) profile = ScriptableObject.CreateInstance<OpponentAIProfile>();
        BuildSchedule();
    }

    private T FindFirstObjectByType<T>() where T : Object => FindObjectsByType<T>(FindObjectsSortMode.None).Length > 0 ? FindObjectsByType<T>(FindObjectsSortMode.None)[0] : null;

    /// <summary>延迟解析 SkillInputUI：Start 时可能还没被 ScoreManager 创建（时序竞态），
    /// 这里在真正要喂输入时再 Find 一次；仍为空则告警（不直调 BeginCast，保证与玩家同管线）。</summary>
    private SkillInputUI ResolveSkillInputUI()
    {
        if (skillInputUI != null) return skillInputUI;
        skillInputUI = FindFirstObjectByType<SkillInputUI>();
        return skillInputUI;
    }
    private NoteSpawner FindRightSpawner()
    {
        var all = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var s in all) if (s.side == 1) return s;
        return null;
    }
    private NoteSpawner FindSpawner(int side)
    {
        var all = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var s in all) if (s.side == side) return s;
        return null;
    }

    /// <summary>预排序所有按下/松开事件（与旧版同构，附加 isSmall 标记供偏移缩放）。</summary>
    private void BuildSchedule()
    {
        pressEvents.Clear();
        releaseEvents.Clear();
        pressIndex = 0;
        releaseIndex = 0;

        if (beatmap == null || beatmap.notes == null) return;

        foreach (var n in beatmap.notes)
        {
            if (n.side != spawner.side) continue;
            if (n.type == NoteData.NoteType.ChainTap)
            {
                float interval = Mathf.Max(0.05f, spawner.chainTapHoldDuration * 0.35f);
                int count = Mathf.Max(1, n.chainTapCount);
                for (int i = 1; i < count; i++)
                    pressEvents.Add((n.time + i * interval, n.lane, false));
            }
            else if (n.type == NoteData.NoteType.Hold)
            {
                float[] times = n.GetHoldTimes();
                int[] lanes = n.GetHoldLanes();
                for (int i = 1; i < times.Length && i < lanes.Length; i++)
                    releaseEvents.Add((times[i], lanes[i]));
            }
            else if (n.IsLinkedHold())
            {
                float[] times = n.GetHoldTimes();
                int[] lanes = n.GetHoldLanes();
                int startLane = Mathf.Clamp(lanes[0], 0, Mathf.Max(0, spawner.laneCount - 2));
                float tailTime = times[Mathf.Min(times.Length, lanes.Length) - 1];
                releaseEvents.Add((tailTime, startLane));
                releaseEvents.Add((tailTime, startLane + 1));
            }
            else
            {
                // 普通点击 / 小型点击：head 按下事件
                bool isSmall = n.type == NoteData.NoteType.SmallTap;
                if (n.IsLinkedHold()) { } // 非 linked，忽略
                pressEvents.Add((n.time, n.lane, isSmall));
            }
        }
        // 长按/连轨的 head 也作为按下事件（与普通点击同处理；linked 两轨都按）
        foreach (var n in beatmap.notes)
        {
            if (n.side != spawner.side) continue;
            if (n.type == NoteData.NoteType.Hold)
            {
                float[] times = n.GetHoldTimes();
                int[] lanes = n.GetHoldLanes();
                if (times.Length > 0 && lanes.Length > 0)
                    pressEvents.Add((times[0], lanes[0], false));
            }
            else if (n.IsLinkedHold())
            {
                float[] times = n.GetHoldTimes();
                int[] lanes = n.GetHoldLanes();
                int startLane = Mathf.Clamp(lanes[0], 0, Mathf.Max(0, spawner.laneCount - 2));
                if (times.Length > 0)
                {
                    pressEvents.Add((times[0], startLane, false));
                    pressEvents.Add((times[0], startLane + 1, false));
                }
            }
        }

        pressEvents.Sort((a, b) => a.time.CompareTo(b.time));
        releaseEvents.Sort((a, b) => a.time.CompareTo(b.time));
    }

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return;
        if (spawner == null || conductor == null || beatmap == null || beatmap.notes == null) return;
        // 沉睡期间禁操作（控制免疫由 SleepController 内部判断）
        if (SleepController.Instance != null && SleepController.Instance.IsSideSleeping(1)) return;

        float songTime = conductor.songPosition;

        // 1) 按下事件（普通点击 / 小型点击 / Hold head / Linked head）
        while (pressIndex < pressEvents.Count)
        {
            var ev = pressEvents[pressIndex];
            // 用 profile 计算本次偏移目标时刻
            float targetTime = ComputePressTargetTime(ev.time, ev.isSmall);
            if (songTime < targetTime - 0.01f) break;   // 还没到窗口

            if (songTime <= targetTime + 0.25f)
            {
                // 命中概率：只决定「是否按下按键」；不按 = 无输入，音符自然 MISS
                if (UnityEngine.Random.value <= profile.noteHitRate)
                {
                    bool linked = false;
                    int startLane = ev.lane;
                    spawner.TriggerLaneDown(startLane, true);
                    if (!linked)
                    {
                        spawner.TriggerLaneUp(startLane);
                    }
                    if (showVisualFeedback) ShowOpponentPress(startLane);
                }
            }
            pressIndex++;
        }

        // 2) 松开事件（仅 Hold 的 tail；AI 自动完成长按）
        while (releaseIndex < releaseEvents.Count)
        {
            var ev = releaseEvents[releaseIndex];
            if (songTime < ev.time - 0.01f) break;
            if (songTime <= ev.time + 0.25f)
            {
                spawner.TriggerLaneUp(ev.lane);
            }
            releaseIndex++;
        }

        // 3) 主动技能评估（按间隔）
        if (songTime >= nextEvaluateTime)
        {
            nextEvaluateTime = songTime + Mathf.Max(0.1f, profile.evaluateInterval);
            EvaluateSkills();
        }
    }

    /// <summary>计算 AI 本次按下相对音符时刻的时间线偏移：
    /// 普通音符偏移 = offsetFrac × goodWindow；小型点击偏移 = (offsetFrac/0.6) × goodWindow
    /// → |偏移Frac|≤0.6 命中、&gt;0.6 必 MISS（与「小音符有效窗口=0.6×goodWindow」一致）。
    /// offsetFrac 符号 50% 提前 / 50% 延后。</summary>
    private float ComputePressTargetTime(float noteTime, bool isSmall)
    {
        float good = (spawner != null) ? spawner.goodWindow : 0.07f;
        float frac = UnityEngine.Random.Range(profile.offsetMinMul, profile.offsetMaxMul);
        float sign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        float dt = (isSmall ? frac / 0.6f : frac) * good * sign;
        return noteTime + dt;
    }

    private void ShowOpponentPress(int lane)
    {
        OnPressLane?.Invoke(lane);
    }

    // ===================== 主动技能 AI =====================

    /// <summary>大前提：能量满（无能量技能=视为满）+ 不在 CD（phase==Standby）。</summary>
    private bool BigPremiseMet(ActiveSkillRuntime rt)
    {
        if (rt == null || rt.phase != ActiveSkillRuntime.Phase.Standby) return false;
        if (rt.NeedsEnergyGate && !rt.IsSlotFull()) return false;   // 需要能量但未满
        return true;                                                // 无能量技能(needsEnergy=false) → 视为满
    }

    /// <summary>各技能专属条件（side=1）。</summary>
    private bool SkillConditionMet(ActiveSkillRuntime rt)
    {
        if (rt.SkillRef == null) return false;
        string eff = rt.SkillRef.effectType;
        switch (eff)
        {
            case "DogHowl":   // 对方(side0)连击超过阈值 → 削我连击=控制
                return rt.opponentCombo != null && rt.opponentCombo.CurrentCombo > profile.dogHowlOppComboThreshold;
            case "Heal":      // 自身血量比例低于阈值
                {
                    float hp = (scoreManager != null) ? (float)scoreManager.GetHP(1) / Mathf.Max(1, scoreManager.GetMaxHP(1)) : 1f;
                    return hp < profile.healHpRatioThreshold;
                }
            case "ClearScreen":  // 自身侧清屏带内可清音符数超过阈值
                return CountClearableNotes(rt) > profile.clearScreenNoteThreshold;
            case "Buff":
                {
                    bool defense = rt.SkillRef.buffSlot == BuffSlot.A && rt.SkillRef.buffSubType == BuffSubType.Defense;
                    bool offense = rt.SkillRef.buffSlot == BuffSlot.A && rt.SkillRef.buffSubType == BuffSubType.Offense;
                    if (defense) return IsIncomingControl(1);
                    if (offense) return (scoreManager != null) && (scoreManager.GetRightScore() - scoreManager.GetLeftScore()) > profile.offenseScoreLeadThreshold;
                    return false;
                }
            case "Bomb":      // 无条件
                return true;
        }
        return false;
    }

    /// <summary>全体防御触发条件：检测到「即将受到控制」——
    /// (1) 对方(side0)正在释放大狗叫（削我连击=控制）；(2) 自己(side1)正在释放清屏（自沉睡=自控制）。</summary>
    private bool IsIncomingControl(int self)
    {
        var all = FindObjectsByType<ActiveSkillRuntime>(FindObjectsSortMode.None);
        foreach (var rt in all)
        {
            if (rt.phase != ActiveSkillRuntime.Phase.Grow && rt.phase != ActiveSkillRuntime.Phase.Charming && rt.phase != ActiveSkillRuntime.Phase.Releasing) continue;
            if (rt.SkillRef == null) continue;
            string e = rt.SkillRef.effectType;
            if (e == "DogHowl" && rt.ownerSide != self) return true;       // 对方发动大狗叫
            if (e == "ClearScreen" && rt.ownerSide == self) return true;   // 自己发动清屏（自沉睡）
        }
        return false;
    }

    /// <summary>小黑清屏：统计自身侧（side1）清屏带内可清音符数（与 ActiveSkillRuntime.ClearScreenSequence 的带几何一致）。</summary>
    private int CountClearableNotes(ActiveSkillRuntime rt)
    {
        if (spawner == null || spawner.hitPoint == null) return 0;
        float ownX = spawner.hitPoint.position.x;
        var enemy = FindSpawner(1 - 1);
        float enemyX = (enemy != null && enemy.hitPoint != null) ? enemy.hitPoint.position.x : (ownX - 10f);
        float rangeMult = (rt.SkillRef != null) ? rt.SkillRef.clearBandRangeMult : 1f;
        float bandEndX = ownX + 0.25f * rangeMult * (enemyX - ownX);
        float xMin = Mathf.Min(ownX, bandEndX);
        float xMax = Mathf.Max(ownX, bandEndX);
        return spawner.CountNotesInRange(xMin, xMax);
    }

    /// <summary>在给定运行时集合中找 side=1 的全体进攻运行时（Buff/A/Offense）。</summary>
    private ActiveSkillRuntime FindOffenseRuntime(IList<ActiveSkillRuntime> all)
    {
        foreach (var rt in all)
        {
            if (rt.ownerSide != 1 || rt.SkillRef == null) continue;
            if (rt.SkillRef.effectType == "Buff" && rt.SkillRef.buffSlot == BuffSlot.A && rt.SkillRef.buffSubType == BuffSubType.Offense)
                return rt;
        }
        return null;
    }

    /// <summary>每轮评估：收集候选 → 独立概率 → 随机抽主技能 → 可能追加全体进攻（链式）。</summary>
    private void EvaluateSkills()
    {
        if (aiCasting) return;
        var all = FindObjectsByType<ActiveSkillRuntime>(FindObjectsSortMode.None);
        // 诊断：打印 side=1 全部运行时概览（一次足够定位「候选永远为空」问题），避免静默 return 后无日志。
        if (!aiDiagnosedOnce)
        {
            aiDiagnosedOnce = true;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[OpponentAI][诊断] side=1 全部 ActiveSkillRuntime 概览：");
            int n = 0;
            foreach (var rt in all)
            {
                if (rt.ownerSide != 1) continue;
                n++;
                bool big = BigPremiseMet(rt);
                bool cond = SkillConditionMet(rt);
                sb.AppendLine($"  #{n} eff={rt.SkillRef?.effectType ?? "?"} phase={rt.phase} needsEnergy={rt.NeedsEnergyGate} slotFull={rt.IsSlotFull()} bigPremise={big} cond={cond} seqLen={(rt.inputSequence != null ? rt.inputSequence.Length : -1)} ownerNull={(rt.owner == null)} hasActive={(rt.owner != null ? rt.owner.HasActiveSkill : false)}");
            }
            if (n == 0) sb.AppendLine("  (side=1 没有任何 ActiveSkillRuntime —— AttachSkillRuntimes 未给 AI 挂载！)");
            Debug.Log(sb.ToString());
        }
        var candidates = new List<ActiveSkillRuntime>();
        foreach (var rt in all)
        {
            if (rt.ownerSide != 1) continue;
            if (!BigPremiseMet(rt)) continue;
            if (!SkillConditionMet(rt)) continue;
            candidates.Add(rt);
        }
        if (candidates.Count == 0) return;

        // 每个候选独立掷一次发动概率
        var fired = candidates.FindAll(rt => UnityEngine.Random.value <= profile.releaseProbability);
        if (fired.Count == 0)
        {
            Debug.Log($"[OpponentAI] 评估：{candidates.Count} 个候选满足大前提+条件，但本次掷骰均未通过发动概率 {profile.releaseProbability}（本轮未发动）。");
            return;
        }

        // 从发动里随机抽 1 个 = 主技能
        var main = fired[UnityEngine.Random.Range(0, fired.Count)];
        string mainEff = main.SkillRef != null ? main.SkillRef.effectType : "?";
        string mainName = main.SkillRef != null ? (main.SkillRef.displayName ?? "") : "";
        Debug.Log($"[OpponentAI] 评估：候选 {candidates.Count}，发动 {fired.Count}，选中主技能 = {mainEff}（{mainName}）");

        // 主技能=大狗叫/炸弹雨 → 按概率追加全体进攻（额外判定，不要求领先 1300），重排为先进攻后主技能
        if (mainEff == "DogHowl" || mainEff == "Bomb")
        {
            var offense = FindOffenseRuntime(all);
            if (offense != null && BigPremiseMet(offense))
            {
                float chance = (mainEff == "DogHowl") ? profile.offenseAfterDogHowlChance : profile.offenseAfterBombChance;
                if (UnityEngine.Random.value <= chance)
                {
                    Debug.Log($"[OpponentAI] 追加全体进攻（主={mainEff}，追加概率 {chance} 命中）→ 先进攻后主技能");
                    StartCoroutine(SimulateChain(offense, main));
                    return;
                }
            }
        }

        StartCoroutine(SimulateSkillInput(main));
    }

    /// <summary>模拟玩家输入释放单个技能：把 inputSequence 逐帧喂给 SkillInputUI.FeedInput(1, step)（与玩家同管线，不直接 BeginCast）。
    /// 任意一步时技能已不在 Standby（被抢占/打断/进 CD）→ 中止。</summary>
    private System.Collections.IEnumerator SimulateSkillInput(ActiveSkillRuntime rt)
    {
        if (rt == null || rt.inputSequence == null || rt.inputSequence.Length == 0)
        {
            Debug.LogWarning($"[OpponentAI][诊断] SimulateSkillInput 中止：rt空或inputSequence空（length={(rt != null && rt.inputSequence != null ? rt.inputSequence.Length : -1)}）。已恢复 aiCasting=false。");
            aiCasting = false; yield break;
        }
        var sui = ResolveSkillInputUI();
        if (sui == null)
        {
            Debug.LogWarning("[OpponentAI] 仍未找到 SkillInputUI，无法模拟玩家输入释放技能（已跳过，不直调 BeginCast）。请确认 ScoreManager 已正常创建 SkillInputUI。已恢复 aiCasting=false。");
            aiCasting = false; yield break;
        }
        aiCasting = true;
        float stepInterval = Mathf.Max(0.05f, profile.inputSpeed / rt.inputSequence.Length);
        Debug.Log($"[OpponentAI] 开始模拟释放 side=1 技能 {rt.SkillRef?.effectType ?? "?"}（{rt.SkillRef?.displayName ?? ""}，序列长 {rt.inputSequence.Length}，步间隔 {stepInterval:F2}s）");
        for (int i = 0; i < rt.inputSequence.Length; i++)
        {
            if (rt.phase != ActiveSkillRuntime.Phase.Standby) { aiCasting = false; yield break; }
            var res = sui.FeedInput(1, rt.inputSequence[i]);
            if (res == FeedResult.Rejected)
            {
                Debug.LogWarning($"[OpponentAI][诊断] 第{i}步被 FeedInput 拒绝(缓冲已清空)。目标rt: ownerSide={rt.ownerSide} ownerNull={(rt.owner == null)} hasActive={(rt.owner != null ? rt.owner.HasActiveSkill : false)} phase={rt.phase} needsEnergy={rt.NeedsEnergyGate} slotFull={rt.IsSlotFull()} seqLen={(rt.inputSequence != null ? rt.inputSequence.Length : -1)} step={(rt.inputSequence != null && i < rt.inputSequence.Length ? rt.inputSequence[i].ToString() : "?")}");
            }
            else if (res == FeedResult.Triggered)
            {
                Debug.Log($"[OpponentAI] 第{i}步触发释放成功（{rt.SkillRef?.effectType}），剩余步骤不再输入。");
                aiCasting = false;
                yield break;
            }
            yield return new WaitForSeconds(stepInterval);
        }
        aiCasting = false;
    }

    /// <summary>链式：先模拟输入全体进攻，等其起手后，再模拟输入主技能（大狗叫/炸弹雨）。一次只输入一个技能，但先后捆绑。</summary>
    private System.Collections.IEnumerator SimulateChain(ActiveSkillRuntime offense, ActiveSkillRuntime main)
    {
        yield return StartCoroutine(SimulateSkillInput(offense));
        yield return new WaitForSeconds(0.25f);
        yield return StartCoroutine(SimulateSkillInput(main));
    }

    public void ResetInput()
    {
        pressIndex = 0;
        releaseIndex = 0;
        nextEvaluateTime = 0f;
        aiCasting = false;
        BuildSchedule();
    }

    /// <summary>调试用：强制 AI 立即释放一个满足大前提(Standby + 能量满)的 side=1 技能，
    /// 绕过概率/条件，用于验证「SkillInputUI.FeedInput → BeginCast」输入管线是否通畅。</summary>
    public void ForceCastSkill()
    {
        if (aiCasting) { Debug.Log("[OpponentAI] 已在进行技能输入，跳过 ForceCast。"); return; }
        var all = FindObjectsByType<ActiveSkillRuntime>(FindObjectsSortMode.None);
        ActiveSkillRuntime pick = null;
        foreach (var rt in all)
        {
            if (rt.ownerSide != 1) continue;
            if (!BigPremiseMet(rt)) continue;
            pick = rt; break;
        }
        if (pick == null)
        {
            // 找不到任何可释放技能：dump 全部 side=1 运行时状态，帮助定位（能量/phase/owner/序列）
            var dump = FindObjectsByType<ActiveSkillRuntime>(FindObjectsSortMode.None);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("[OpponentAI][诊断] ForceCast 未找到可释放 skill。当前全部 side=1 运行时：");
            foreach (var rt in dump)
            {
                if (rt.ownerSide != 1) continue;
                sb.AppendLine($"  - effect={rt.SkillRef?.effectType ?? "?"} ownerSide={rt.ownerSide} ownerNull={(rt.owner == null)} hasActive={(rt.owner != null ? rt.owner.HasActiveSkill : false)} phase={rt.phase} needsEnergy={rt.NeedsEnergyGate} slotFull={rt.IsSlotFull()} seqLen={(rt.inputSequence != null ? rt.inputSequence.Length : -1)}");
            }
            Debug.LogWarning(sb.ToString());
            return;
        }
        string seqStr = (pick.inputSequence != null) ? string.Join(",", System.Array.ConvertAll(pick.inputSequence, s => s.ToString())) : "null";
        Debug.Log($"[OpponentAI] ForceCast 强制释放：{pick.SkillRef?.effectType ?? "?"}（{pick.SkillRef?.displayName ?? ""}）| ownerSide={pick.ownerSide} ownerNull={(pick.owner==null)} hasActive={(pick.owner!=null?pick.owner.HasActiveSkill:false)} phase={pick.phase} needsEnergy={pick.NeedsEnergyGate} slotFull={pick.IsSlotFull()} seqLen={(pick.inputSequence!=null?pick.inputSequence.Length:-1)} seq=[{seqStr}]");
        StartCoroutine(SimulateSkillInput(pick));
    }
}
