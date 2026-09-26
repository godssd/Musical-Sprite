using UnityEngine;
using Spine;
using Spine.Unity;
using System.Collections.Generic;

/// <summary>
/// 角色 Spine 动画驱动器（统一优先级模型）。
///
/// 核心规则：只有一套优先级比较——新动画 priority >= 当前动画 priority 才打断当前动画；否则本次不播/不切换。
/// 两类动画只是"触发方式"不同：
///  - loop（循环）：由"状态检测"持续驱动。每帧检查"当前状态应播哪个 loop"，
///    若当前动画优先级 &lt;= 该 loop 优先级才切过去；否则等高优先级 oneshot 播完，
///    由持续检测自动接回对应 loop（oneshot 无需显式回退逻辑）。
///  - oneshot（单次）：由"触发机制"驱动。达到触发条件即打断当前动画单次播放；
///    若当前动画优先级 &gt; 自己则本次触发作废（不播放）。
///
/// 动画名解析：运行时按 "animationPrefix + '_' + 标准槽位后缀" 在 SkeletonData 中自动查找，
/// 找到才注册（缺失动画自动跳过），因此每个角色只需在 CharacterDataSO.animationPrefix 填前缀，
/// 无需手动维护整张表。标准槽位编号约定见 SlotTable。
///
/// 重构说明（2026-09-13）：优先级不再用独立 int 记录，改为记录"当前实际在播状态 currentState"，
/// 比较时从 available[currentState] 实查优先级，并发/扩展安全（用户确认：currentPriority→currentState）。
/// </summary>
public class CharacterAnimator : MonoBehaviour
{
    [Header("命名")]
    [Tooltip("Spine 动画名前缀，如 Aibo_2_Shit / Player_01_Bear。完整动画名 = 前缀 + '_' + 槽位后缀(如 02_Play_Normal)。由 CharacterDataSO.animationPrefix 经 CharacterCubeMarker 注入")]
    public string animationPrefix = "Aibo_2_Shit";

    [Header("初始")]
    [Tooltip("角色生成时进入的基础 loop 状态（游戏逻辑可调用 PlayOpening 覆盖为开场）")]
    public CharacterAnimationState initialState = CharacterAnimationState.PlayNormal;

    /// <summary>角色动画状态枚举（对应屎屎 19 个动画 + 通用状态）。</summary>
    public enum CharacterAnimationState
    {
        // —— loop 状态（由状态检测持续驱动）——
        PlayNormal,        // 正常演奏
        PlayFever,         // 过热演奏
        PlaySuperFever,    // 超级过热演奏（暂复用过热动画资源）
        SkillLoop,         // 技能准备攻击
        Victory,           // 胜利（结果后常循环）
        Fail,              // 失败（结果后常循环）

        // —— oneshot（由触发机制驱动）——
        Opening,           // 开场（强制播完 → 回 initialState）
        Special,           // 爆发（进入过热/超级过热）
        TargetNormal,      // 正常演奏命中
        TargetFever,       // 过热演奏命中
        Hit,               // 受击
        Dizziness,         // 晕眩（暂不用）
        Decadent,          // 颓废（过热退出时）
        SkillSelect,       // 呼号选中
        SkillStart,        // 释放技能启动
        SkillAttak,        // 释放技能攻击
        SkillEnd,          // 释放技能结束
        Select,            // 选中（暂不用）
        Idle,              // 正常呼吸（暂不用）
    }

    private struct Slot
    {
        public string suffix;
        public int priority;
        public bool loop;
        public Slot(string suffix, int priority, bool loop) { this.suffix = suffix; this.priority = priority; this.loop = loop; }
    }

    /// <summary>标准槽位表（所有角色共用编号约定）。priority 数字越高越优先；loop=true 表示持续状态动画。</summary>
    private static readonly Dictionary<CharacterAnimationState, Slot> SlotTable = new Dictionary<CharacterAnimationState, Slot>
    {
        { CharacterAnimationState.Opening,        new Slot("01_Opening",        20, false) },
        { CharacterAnimationState.PlayNormal,     new Slot("02_Play_Normal",     1, true)  },
        { CharacterAnimationState.PlayFever,      new Slot("03_Play_Fever",      3, true)  },
        { CharacterAnimationState.PlaySuperFever, new Slot("03_Play_Fever",      3, true)  }, // 暂复用过热资源
        { CharacterAnimationState.Special,        new Slot("05_Special",         4, false) },
        { CharacterAnimationState.TargetNormal,   new Slot("06_Target_Normal",   2, false) },
        { CharacterAnimationState.TargetFever,    new Slot("07_Target_Fever",    5, false) },
        { CharacterAnimationState.Hit,            new Slot("08_Hit",             8, false) },
        { CharacterAnimationState.Dizziness,      new Slot("09_Dizziness",       8, false) },
        { CharacterAnimationState.Decadent,       new Slot("10_Decadent",        2, false) },
        { CharacterAnimationState.SkillSelect,    new Slot("11_Skill_Select",    9, false) },
        { CharacterAnimationState.SkillStart,     new Slot("12_Skill_Start",    10, false) },
        { CharacterAnimationState.SkillLoop,      new Slot("13_Skill_Loop",     10, true)  },
        { CharacterAnimationState.SkillAttak,     new Slot("14_Skill_Attak",    12, false) },
        { CharacterAnimationState.SkillEnd,       new Slot("15_Skill_End",      11, false) },
        { CharacterAnimationState.Victory,        new Slot("16_Victory01",      20, true)  }, // 仅 Victory01；Victory02 暂不用
        { CharacterAnimationState.Fail,           new Slot("17_Fail",           20, true)  },
        { CharacterAnimationState.Select,         new Slot("04_Select",          0, false) }, // 暂不用
        { CharacterAnimationState.Idle,           new Slot("00_Idle",            0, false) }, // 暂不用
    };

    private SkeletonAnimation skel;
    private Dictionary<CharacterAnimationState, Slot> available;
    private CharacterAnimationState currentLoopState;
    // 当前实际在播的状态（loop 或 oneshot）。优先级一律从 available[currentState] 实查，
    // 不再用独立 int（用户确认：currentPriority→currentState 重构，并发/扩展安全）。
    private CharacterAnimationState currentState;
    private bool skillLoopLocked = false;   // 技能期间锁定 loop（见 SetSkillLoopLock）

    private bool IsReady => skel != null && skel.AnimationState != null;

    void Awake()
    {
        skel = GetComponentInChildren<SkeletonAnimation>(true);
        if (skel == null)
        {
            Debug.LogError($"[CharacterAnimator] 找不到 SkeletonAnimation 子组件。gameObject={gameObject.name}", this);
            enabled = false;
        }
        else if (skel.AnimationState == null)
        {
            Debug.LogError($"[CharacterAnimator] SkeletonAnimation.AnimationState 为 null（SkeletonDataAsset 可能缺失或导入失败）。gameObject={gameObject.name} prefix={animationPrefix}", this);
            enabled = false;
        }
    }

    void Start()
    {
        if (!IsReady) return;
        Rebuild();
        currentLoopState = initialState;
        PlayOpening();   // 开场：装配即播一次 Opening，结束后 Update 自动接回 initialState（默认 PlayNormal）
    }

    /// <summary>供外部（CharacterCubeMarker）快速判断该模型是否已有可正常驱动的 Spine 运行时。</summary>
    public bool HasValidSkeleton() => skel != null && skel.SkeletonDataAsset != null && skel.AnimationState != null;

    /// <summary>开场动画是否已播完（供音乐起播门控用）。
    /// - 无 Skeleton / 未注册 Opening（方块占位角色、缺失开场资源）→ 视为 0 秒开场，直接 true；
    /// - 正在播 Opening 且未完成 → false（Spine 走主线程 Update，卡顿时动画同样冻结，因此该事件天然抗卡顿）；
    /// - Opening 已完成并接回 loop（或被其他动画接替）→ true。
    /// </summary>
    public bool IsOpeningDone
    {
        get
        {
            if (!IsReady || available == null || !available.ContainsKey(CharacterAnimationState.Opening)) return true;
            var cur = skel.AnimationState.GetTrack(0);
            if (cur == null) return false; // Opening 尚未起播（Start/PlayOpening 还没跑到）
            string openingName = ResolveName(CharacterAnimationState.Opening);
            if (!string.IsNullOrEmpty(openingName) && cur.Animation != null && cur.Animation.Name == openingName)
                return cur.IsComplete;     // 正在播 Opening：以是否播完为准
            return true;                   // 已接回 loop 或被其他动画接替 = 开场已结束
        }
    }

    /// <summary>运行时自动发现：按 prefix + 槽位后缀在 SkeletonData 中查找，存在才注册（缺失动画自动跳过）。
    /// 可由 CharacterCubeMarker 在注入 animationPrefix 后再次调用以重建（换角/框架健壮性）。</summary>
    public void Rebuild()
    {
        if (skel == null) skel = GetComponentInChildren<SkeletonAnimation>(true);
        if (skel == null) { enabled = false; return; }
        var skeletonData = (skel.SkeletonDataAsset != null) ? skel.SkeletonDataAsset.GetSkeletonData(true) : null;
        available = new Dictionary<CharacterAnimationState, Slot>();
        if (skeletonData != null && !string.IsNullOrEmpty(animationPrefix))
        {
            var missing = new System.Collections.Generic.List<string>();
            foreach (var kv in SlotTable)
            {
                string full = animationPrefix + "_" + kv.Value.suffix;
                if (skeletonData.FindAnimation(full) != null)
                    available[kv.Key] = kv.Value;
                else
                    missing.Add(full);
            }
            // 诊断：把"在 SkeletonData 里找不到"的动画名直接打出来，方便核对 SlotTable 后缀是否跟 Spine 导出名一致。
            // 这正是普通命中/过热命中/呼号/技能攻击"完全没反应"的根因——对应动画名没匹配上，available 里没注册。
            if (missing.Count > 0)
                Debug.LogWarning($"[CharacterAnimator] prefix={animationPrefix} 在 SkeletonData 中未找到以下动画（对应状态不会播放）:\n  " + string.Join("\n  ", missing));
        }
    }

    void Update()
    {
        if (!IsReady || available == null) return;
        var st = skel.AnimationState;
        var cur = st.GetTrack(0);
        string desired = ResolveName(currentLoopState);
        if (string.IsNullOrEmpty(desired)) return;

        if (cur == null)
        {
            PlayLoop(currentLoopState);
            return;
        }
        if (cur.Loop)
        {
            if (cur.Animation.Name != desired)
                PlayLoop(currentLoopState); // 状态已切到另一个 loop
        }
        else
        {
            if (cur.IsComplete)
                PlayLoop(currentLoopState); // 高优先级 oneshot 播完，自动接回当前状态 loop
        }
    }

    /// <summary>切换"当前 loop 状态"。
    /// - 当前是 loop（或无动画）：状态切换立即生效（loop 之间本就可互切，这是状态检测的本意），
    ///   且立刻把 currentState 置为新 loop，使同帧内后续的低优先级 oneshot（如 Decadent 打断刚切回的 Normal loop）能正确生效。
    /// - 当前是 oneshot：不强行打断高优先级 oneshot，等 Update 在 oneshot 结束后自动接回新 loop。
    /// 重复设置相同状态为 no-op（由 Update 维持连续性）。</summary>
    public void SetLoopState(CharacterAnimationState loopState)
    {
        // 技能期间锁定：正在播技能准备攻击 loop（SkillLoop）时，外部状态切换（过热/普通 loop）不生效（不吃控制/状态切换）
        if (skillLoopLocked && loopState != CharacterAnimationState.SkillLoop) return;
        bool changed = (currentLoopState != loopState);
        currentLoopState = loopState;
        if (!changed) return;
        if (IsReady && available != null)
        {
            var cur = skel.AnimationState.GetTrack(0);
            if (cur == null || cur.Loop)
                PlayLoop(loopState); // 当前是 loop：状态变化立即切过去
        }
    }

    /// <summary>技能期间锁定 loop 状态（防止过热/普通 loop 切走技能准备攻击循环）。技能结束调用 SetSkillLoopLock(false) 解除。</summary>
    public void SetSkillLoopLock(bool on) { skillLoopLocked = on; }

    /// <summary>触发一次单次动画（命中/受击/技能段等）。按优先级打断；当前更高优先级则本次作废。
    /// 返回 true 表示实际播放了（调用方可据此决定是否回退 cube 兜底表现）。</summary>
    public bool PlayOnce(CharacterAnimationState onceState)
    {
        if (!IsReady || available == null) return false;
        if (!available.TryGetValue(onceState, out var e)) return false;
        if (e.loop) { SetLoopState(onceState); return true; }
        string name = ResolveName(onceState);
        if (string.IsNullOrEmpty(name)) return false;

        var st = skel.AnimationState;
        var cur = st.GetTrack(0);
        if (cur != null && CurrentPriority() > e.priority) return false; // 当前更高优先级，打不断，本次作废
        if (cur != null && cur.Animation.Name == name) return false;   // 已在播同一动画，避免重启动

        st.SetAnimation(0, name, false);
        currentState = onceState;
        return true;
    }

    /// <summary>开场：播放 Opening 一次，结束后由 Update 自动接回 initialState（默认 PlayNormal）。</summary>
    [ContextMenu("Test/Play Opening")]
    public void PlayOpening()
    {
        currentLoopState = initialState;
        PlayOnce(CharacterAnimationState.Opening);
    }

    // ---- Inspector 右键测试入口（手动验证打断/回环用，不影响运行时逻辑）----
    [ContextMenu("Test/Play Hit")]
    private void TestPlayHit() => PlayOnce(CharacterAnimationState.Hit);
    [ContextMenu("Test/Play Target(Fever)")]
    private void TestPlayTargetFever() => PlayOnce(CharacterAnimationState.TargetFever);
    [ContextMenu("Test/Play Skill Start")]
    private void TestPlaySkillStart() => PlayOnce(CharacterAnimationState.SkillStart);
    [ContextMenu("Test/Play Skill Attak")]
    private void TestPlaySkillAttak() => PlayOnce(CharacterAnimationState.SkillAttak);
    [ContextMenu("Test/Play Skill End")]
    private void TestPlaySkillEnd() => PlayOnce(CharacterAnimationState.SkillEnd);
    [ContextMenu("Test/Play Decadent")]
    private void TestPlayDecadent() => PlayOnce(CharacterAnimationState.Decadent);
    [ContextMenu("Test/Set Normal Loop")]
    private void TestSetNormal() => SetLoopState(CharacterAnimationState.PlayNormal);
    [ContextMenu("Test/Set Fever Loop")]
    private void TestSetFever() => SetLoopState(CharacterAnimationState.PlayFever);
    [ContextMenu("Test/Set Skill Loop")]
    private void TestSetSkillLoop() => SetLoopState(CharacterAnimationState.SkillLoop);
    [ContextMenu("Test/Set Victory Loop")]
    private void TestSetVictory() => SetLoopState(CharacterAnimationState.Victory);
    [ContextMenu("Test/Set Fail Loop")]
    private void TestSetFail() => SetLoopState(CharacterAnimationState.Fail);

    /// <summary>诊断：把该 Spine 预制体 SkeletonData 里的全部动画名打印到 Console（含前缀）。
    /// 不进 Play、选中带 CharacterAnimator 的 Spine 子物体右键即可调用，用来核对 SlotTable 后缀是否跟导出名一致。</summary>
    [ContextMenu("Diagnostic/Dump Spine Animations")]
    private void DumpAnimations()
    {
        if (skel == null) skel = GetComponentInChildren<SkeletonAnimation>(true);
        if (skel == null || skel.SkeletonDataAsset == null) { Debug.LogWarning("[CharacterAnimator] 无 SkeletonAnimation / SkeletonDataAsset"); return; }
        var sd = skel.SkeletonDataAsset.GetSkeletonData(true);
        if (sd == null) { Debug.LogWarning("[CharacterAnimator] GetSkeletonData 返回 null"); return; }
        var names = new System.Collections.Generic.List<string>();
        foreach (var a in sd.Animations) names.Add(a.Name);
        Debug.Log($"[CharacterAnimator] prefix={animationPrefix} SkeletonData 全部动画名 ({names.Count}):\n  " + string.Join("\n  ", names));
    }

    private void PlayLoop(CharacterAnimationState state)
    {
        if (!IsReady || !available.TryGetValue(state, out var e)) return;
        string name = ResolveName(state);
        if (string.IsNullOrEmpty(name)) return;
        skel.AnimationState.SetAnimation(0, name, true);
        currentState = state;
    }

    private string ResolveName(CharacterAnimationState state)
    {
        if (!available.TryGetValue(state, out var e)) return null;
        if (string.IsNullOrEmpty(e.suffix)) return null;
        return animationPrefix + "_" + e.suffix;
    }

    /// <summary>当前播放动画的优先级：从 SlotTable 实查当前在播状态（currentState）的优先级，
    /// 不再依赖独立 int（并发/扩展安全，用户确认重构）。</summary>
    private int CurrentPriority()
    {
        if (available != null && available.TryGetValue(currentState, out var s)) return s.priority;
        return 0;
    }
}
