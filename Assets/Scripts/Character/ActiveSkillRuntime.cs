using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 队伍角色的主动技能运行时状态机（大狗叫等）。
/// 由 CharacterBattleSystem 在 Start 时为每个队伍角色（非玩家）的 marker 挂载并 Setup。
///
/// 生命周期：
///   Standby  ->（玩家按对 inputSequence 且能量满）-> Grow
///   Grow     -> 立刻清空能量（停冒气）；角色变大+持续发光；同时向 ownerSpawner 请求附魔接下来 charmedNoteCount 个附魔单位
///   Charming -> 每个被附魔单位结算：普通 tap 命中/MISS、Hold/Linked 逐节点 CLEAR(成功)/断连漏击(失败) 回调；
///              当所有被附魔单位全部结算 -> 立刻 Settle
///   Releasing-> 根据 ownerSide 过热状态决定发 1/2/3 次音浪；每发按 成功数*reduceComboPerCharmedNote 削减对方连击；
///              全部发完后角色缩回熄灭 + 进入冷却
///   Cooldown -> 冷却倒计时后回到 Standby
///
/// 设计要点：
/// - 释放时机 = 等所有被附魔单位（含整条 Hold/Linked 逐节点）结算完毕（命中=成功，断连/漏击=失败），立刻释放狗叫。
/// - 效果强度（连击削减量）= 成功数 * reduceComboPerCharmedNote；效果强度取决于玩家的命中而非时间。
/// - 附魔单位记账：tap 按对象跟踪，Hold/Linked 按节点计数；全部六个单位越过判定线并结算后才放狗叫。
/// - 多角色技能同时释放：NoteSpawner 用 FIFO（最旧请求优先）+ 不可二次附魔守卫，后放的技能自动排到后面的音符。
/// - 附魔单位只有命中时才让变大的大狗闪烁；漏掉只消费六个单位中的名额，不增加声波削减量。
/// </summary>
public class ActiveSkillRuntime : MonoBehaviour
{
    public enum Phase { Standby, Grow, Charming, Releasing, Cooldown }

    [HideInInspector] public CharacterClass owner;
    [HideInInspector] public CharacterCubeMarker marker;
    [HideInInspector] public NoteSpawner ownerSpawner;
    [HideInInspector] public ComboDisplay opponentCombo;
    [HideInInspector] public int ownerSide = 0;
    [HideInInspector] public FeverManager feverManager;
    public Phase phase = Phase.Standby;

    private SkillSO skill;
    public SkillSO SkillRef => skill;      // 供 SkillInputUI 等按运行时唯一定位
    /// <summary>供 SkillInputUI 判断：本运行时对应主动槽的能量是否已充满。</summary>
    public bool IsSlotFull() => owner != null && owner.IsSlotFull(slotIndex);
    private float slotCooldown;           // 本槽冷却（秒），来自角色文档「技能冷却」列
    private bool needsEnergy = true;      // 是否需要能量门槛（无能量技能=false：跳过充能/能量满/清空）
    public SkillInputStep[] inputSequence; // 本槽输入序列（供 SkillInputUI 匹配）
    private int slotIndex = 0;             // 本运行时对应的主动槽索引（用于按槽清空/查询能量）
    public Color charmColor = Color.yellow; // 本次附魔颜色（取释放方自身颜色）

    /// <summary>供 SkillInputUI 判断：该技能是否需要"能量满"才能开始输入。无能量技能=false（随时可输入）。</summary>
    public bool NeedsEnergyGate => needsEnergy;
    private int charmQuotaTotal;          // 本次释放可附魔的总单位数（= skill.charmedNoteCount，仅参考/调试）
    private HashSet<Note> charmedNotes = new HashSet<Note>();          // 当前已被附魔、尚未结算消失的普通音符对象
    private Dictionary<HoldNote, int> charmedHoldNodes = new Dictionary<HoldNote, int>(); // 每条链尚未结算的附魔节点数
    private int completedCount;           // 成功数（非 MISS，用于削减对方连击的强度）
    private bool requestClosed;           // spawner 端配额是否已全部分配完
    private float cooldownLeft;
    private FeverState releaseFever = FeverState.None;  // 技能释放（能量清空）瞬间捕获的过热档；整段连叫锁定，后续不重判（2026-08-27）
    private ScoreManager _scoreMgr;                      // 懒加载缓存：用于造成对方 HP 伤害 / 治疗
    private CharacterBattleSystem _battleSys;            // 懒加载缓存：用于读取释放方队伍战斗力总和

    /// <summary>当前正在释放主动技能（变大+附魔期）的 side 集合，供 BattleVisualsController 屏蔽普通命中跳跃。</summary>
    private static HashSet<int> CastingSides = new HashSet<int>();
    /// <summary>该 side 是否正处于主动技能释放期（屏蔽普通命中跳跃表现）。</summary>
    public static bool IsSideCasting(int side) => CastingSides.Contains(side);

    public void Setup(CharacterClass owner, CharacterCubeMarker marker, NoteSpawner spawner, ComboDisplay oppCombo, int side, FeverManager fever, SkillSO skill, float cooldown, bool needsEnergy, SkillInputStep[] inputSequence, int slotIndex)
    {
        this.owner = owner;
        this.marker = marker;
        this.ownerSpawner = spawner;
        this.opponentCombo = oppCombo;
        this.ownerSide = side;
        this.feverManager = fever;
        this.skill = skill;
        this.slotCooldown = cooldown;
        this.needsEnergy = needsEnergy;
        this.inputSequence = inputSequence;
        this.slotIndex = slotIndex;
    }

    void Update()
    {
        if (phase == Phase.Cooldown)
        {
            cooldownLeft -= Time.deltaTime;
            if (cooldownLeft <= 0f)
            {
                phase = Phase.Standby;
                if (owner != null) owner.SetSlotBusy(slotIndex, false);  // 技能彻底结束：若能量已再次充满则恢复冒烟
            }
            return;
        }

        // 安全兜底：仅在「谱面已彻底结束（所有音符生成并消失，IsFinished）但仍有被附魔音符对象因异常未回调结算」时，
        // 才强制结算放狗叫。绝不在音符仍在进行中时强制释放——避免「场上还有附魔音符就提前叫」（用户重点关切）。
        // 正常流程由 TrySettleFromCharm 在「所有被附魔音符对象都已结算消失 + 配额关闭」时驱动 Settle。
        if ((phase == Phase.Grow || phase == Phase.Charming)
            && (charmedNotes.Count > 0 || charmedHoldNodes.Count > 0)
            && ownerSpawner != null && ownerSpawner.IsFinished)
        {
            if (phase != Phase.Releasing && phase != Phase.Cooldown) Settle();
        }
    }

    /// <summary>主动释放入口：只有能量满 + Standby 才可开始。由 SkillInputUI 在序列按对后调用。</summary>
    public void BeginCast()
    {
        if (phase != Phase.Standby) return;
        if (owner == null || !owner.HasActiveSkill) return;
        // 沉睡期间无法释放技能（控制免疫可抵抗）
        if (SleepController.Instance != null && SleepController.Instance.IsSideSleeping(ownerSide))
        {
            Debug.Log($"[Skill] side{ownerSide} 沉睡中，无法释放 {skill?.displayName}");
            return;
        }
        if (needsEnergy && !owner.IsSlotFull(slotIndex)) return;   // 仅需要能量的技能才卡对应槽能量门槛
        if (skill == null) return;

        phase = Phase.Grow;
        completedCount = 0;
        charmedNotes.Clear();
        charmedHoldNodes.Clear();
        requestClosed = false;
        CastingSides.Add(ownerSide);   // 进入释放期：屏蔽普通命中跳跃，直到缩回（EndCastSequence / FireSequence 末尾移除）

        // 释放成功：需要能量的技能立刻清空能量（冒气停止）；无能量技能跳过充能/清空流程与表现
        if (owner != null)
        {
            if (needsEnergy) owner.ConsumeSlot(slotIndex);
            owner.SetSlotBusy(slotIndex, true);   // 整个技能进行期（含过热连叫）抑制"能量充满冒烟"，回到 Standby 时才可能恢复
        }

        // 技能释放瞬间（能量清空此刻）捕获过热档，整段连叫锁定；后续附魔期即便断连/掉出过热也不再重判（2026-08-27 要求）。
        releaseFever = (feverManager != null) ? feverManager.GetState(ownerSide) : FeverState.None;

        if (marker != null) marker.GrowGlow();
        // 附魔颜色：优先用技能专属覆盖色（charmColorOverride），回退到释放方角色自身颜色。
        charmColor = (skill != null && skill.charmColorOverride != Color.black)
            ? skill.charmColorOverride
            : (owner != null ? owner.blockColor : Color.yellow);

        // a/b 双槽 buff 分流：
        //   - 清屏（ClearScreen）：直接清除范围内音符 + 敌方沉睡 + 激活自身 b 类 buff（小黑个人战力）。
        //   - 纯 buff（无附魔的 a/b 类技能，如小熊双技能）：直接施加 buff 后收尾。
        //   - 其余（Bomb / Heal / DogHowl 等带附魔）：走常规附魔路径。
        bool isClear = skill != null && skill.effectType == "ClearScreen";
        bool isPureBuff = skill != null && skill.buffSlot != BuffSlot.None && skill.charmedNoteCount <= 0 && !isClear;
        if (isClear) { StartCoroutine(ClearScreenSequence()); return; }
        if (isPureBuff) { StartCoroutine(PureBuffSequence()); return; }

        if (ownerSpawner != null)
        {
            int targetSide = (skill != null && skill.enchantTarget == EnchantTarget.Enemy) ? (1 - ownerSide) : ownerSide;
            // 过热附魔加成：过热/超级过热在基础 charmedNoteCount 上额外附魔（字段由策划资产配置）
            int count = (skill != null) ? skill.charmedNoteCount : 1;
            if (releaseFever == FeverState.SuperFever && skill != null) count += skill.charmBonusSuperFever;
            else if (releaseFever == FeverState.Fever && skill != null) count += skill.charmBonusFever;
            ownerSpawner.RequestCharm(this, Mathf.Max(1, count), charmColor, targetSide);
        }

        Debug.Log(string.Format("[ActiveSkill {0}(side{1}, id={2})] BeginCast -> 等待 {3} 个附魔单位结算完毕即释放（能量已清空）",
            skill != null ? skill.displayName : "?",
            ownerSide, owner != null ? owner.characterId : -1,
            skill.charmedNoteCount));
    }

    /// <summary>NoteSpawner 给本技能成功附魔了一个 tap 音符时调用。</summary>
    public void OnNoteCharmed(Note n)
    {
        if (n == null) return;
        charmedNotes.Add(n);
    }

    /// <summary>spawner 端把本技能的 activeCharms 条目移除时回调（配额用完，或该请求被作废清理）。</summary>
    public void OnCharmRequestClosed()
    {
        requestClosed = true;
        TrySettleFromCharm();
    }

    /// <summary>Hold/Linked 节点被附魔时由 NoteSpawner 调用；同一条链可只附魔接下来的部分节点。</summary>
    public void OnHoldCharmed(HoldNote hn, int nodeCount)
    {
        if (hn == null || nodeCount <= 0) return;
        charmedHoldNodes.TryGetValue(hn, out int pending);
        charmedHoldNodes[hn] = pending + nodeCount;
    }

    /// <summary>被附魔的普通 tap 音符结算（命中/MISS/消失）时由 Note 调用。success=true 表示非 MISS。</summary>
    public void OnCharmedNoteResolved(Note n, bool success)
    {
        charmedNotes.Remove(n);
        if (success)
        {
            completedCount++;
            OnPerCharmSuccess();   // 逐音符触发：投弹 / 回血（Bomb / Heal 技能）
        }
        if (phase == Phase.Grow) phase = Phase.Charming;
        if (success && (phase == Phase.Grow || phase == Phase.Charming) && marker != null) marker.PulseGlow();
        TrySettleFromCharm();
    }

    /// <summary>被附魔的 Hold/Linked 某节点成功结算（CLEAR/头节点命中）时由 HoldNote 逐节点调用。</summary>
    // 长按逐节点成功：更新成功数、闪烁，并立即消费该节点的等待计数。
    public void OnCharmNodeSuccess(HoldNote hn)
    {
        completedCount++;
        OnPerCharmSuccess();   // 逐音符触发：投弹 / 回血（Bomb / Heal 技能）
        if (phase == Phase.Grow) phase = Phase.Charming;
        if ((phase == Phase.Grow || phase == Phase.Charming) && marker != null) marker.PulseGlow();
        ResolveCharmNode(hn);
    }

    /// <summary>被附魔的 Hold/Linked 某节点失败结算（断连/漏击）时由 HoldNote 逐节点调用。</summary>
    public void OnCharmNodeFail(HoldNote hn)
    {
        if (phase == Phase.Grow) phase = Phase.Charming;
        ResolveCharmNode(hn);
    }

    private void ResolveCharmNode(HoldNote hn)
    {
        if (hn != null && charmedHoldNodes.TryGetValue(hn, out int pending))
        {
            if (pending <= 1) charmedHoldNodes.Remove(hn);
            else charmedHoldNodes[hn] = pending - 1;
        }
        TrySettleFromCharm();
    }

    /// <summary>所有附魔音符对象都已结算消失（集合空），且 spawner 配额已分配完 -> 立刻 Settle；否则等待剩下的。</summary>
    private void TrySettleFromCharm()
    {
        if (phase != Phase.Grow && phase != Phase.Charming) return;
        if (requestClosed && charmedNotes.Count == 0 && charmedHoldNodes.Count == 0)
            Settle();
    }

    /// <summary>由 NoteSpawner.RequestCharm 调用，记录本技能可附魔的总名额（参考用）。</summary>
    public void SetCharmQuota(int q) { charmQuotaTotal = q; }

    private void Settle()
    {
        if (phase == Phase.Releasing || phase == Phase.Cooldown) return;
        phase = Phase.Releasing;

        // 过热判定：使用技能释放瞬间（能量清空时）已锁定的档位，整段连叫不再重判。
        // 即便附魔期断连/掉出过热，也不会影响已确定的连叫次数（用户 2026-08-27 要求）。
        int shots = 1;
        FeverState st = releaseFever;
        if (st == FeverState.SuperFever) shots = 3;
        else if (st == FeverState.Fever) shots = 2;

        Debug.Log(string.Format("[ActiveSkill {0}(side{1}, id={2})] Settle -> 命中 {3} 个附魔单位，过热={4} 发 {5} 次，effectType={6}",
            skill != null ? skill.displayName : "?",
            ownerSide, owner != null ? owner.characterId : -1,
            completedCount,
            st.ToString(),
            shots,
            skill != null ? skill.effectType : "?"));

        // 大狗叫：等所有附魔音符结算完才连叫释放（削减连击 + 额外 HP 伤害）。
        // 其他技能（Bomb 炸弹雨 / Heal 美味牛角包）：已在逐音符时投弹 / 回血，结算完仅缩回 + 冷却。
        if (skill != null && skill.effectType == "DogHowl")
        {
            if (marker != null) marker.PulseGlow();
            StartCoroutine(FireSequence(shots));
        }
        else
        {
            if (marker != null) marker.PulseGlow();
            StartCoroutine(EndCastSequence());
        }
    }

    /// <summary>按过热次数连发音浪；最后一次发射完后才缩小并进入冷却。</summary>
    private System.Collections.IEnumerator FireSequence(int shots)
    {
        for (int i = 0; i < shots; i++)
        {
            if (i > 0)
            {
                // 过热/超级过热：等待 feverExtraDelay 秒后再次发光并射出音浪（效果与第一次相同）。
                // 间隔由技能「过热/超级过热时追加的狗叫时间」系数决定（2026-08-27 新增，可在技能库调参）。
                float delay = (skill != null) ? skill.feverExtraDelay : 3f;
                yield return new WaitForSeconds(delay);
                if (marker != null) marker.PulseGlow();
                yield return new WaitForSeconds(0.12f);
            }
            FireOneShot();
        }

        // 彻底结算完毕：大狗缩回正常状态 + 冷却
        if (marker != null) marker.ShrinkUnglow();
        phase = Phase.Cooldown;
        CastingSides.Remove(ownerSide);   // 释放期结束：恢复普通命中角色跳跃
        // 冷却取本槽在角色文档配置的「技能冷却」；0 / 未填 = 无冷却（立刻回到 Standby 可再次释放）。
        cooldownLeft = slotCooldown;
    }

    /// <summary>非大狗叫技能（炸弹雨/牛角包）结算完所有附魔音符后的收尾：缩回 + 冷却；牛角包过热启动缓慢恢复。</summary>
    private System.Collections.IEnumerator EndCastSequence()
    {
        if (marker != null) marker.ShrinkUnglow();
        phase = Phase.Cooldown;
        CastingSides.Remove(ownerSide);   // 释放期结束：恢复普通命中跳跃

        // 牛角包：过热 / 超级过热 -> 技能结束后启动 9 秒缓慢恢复（每 3 秒一跳，共 3 跳）
        if (skill != null && skill.effectType == "Heal" && releaseFever != FeverState.None)
        {
            StartCoroutine(Regen());
        }

        // 冷却取本槽在角色文档配置的「技能冷却」；0 / 未填 = 无冷却（立刻回到 Standby 可再次释放）。
        cooldownLeft = slotCooldown;
        yield break;
    }

    /// <summary>清屏（小黑·断弦高压）：起手闪烁变大已完成，稍顿后射出电流，清除范围内全部音符（视为最佳命中），
    /// 敌方陷入沉睡，并激活自身 b 类 buff（小黑个人战力）。无附魔流程。</summary>
    private System.Collections.IEnumerator ClearScreenSequence()
    {
        yield return new WaitForSeconds(0.15f);   // 起手闪烁变大的停顿
        if (marker != null) SpawnElectricity(marker.transform.position);

        // 清屏带：自身判定线 → 自身判定线 + 0.25×rangeMult×(对方判定线-自身判定线)，覆盖全 4 轨
        if (ownerSpawner != null && ownerSpawner.hitPoint != null)
        {
            float ownX = ownerSpawner.hitPoint.position.x;
            var enemy = FindEnemySpawner(ownerSpawner.side);
            float enemyX = (enemy != null && enemy.hitPoint != null)
                ? enemy.hitPoint.position.x
                : (ownX + (ownerSide == 0 ? 10f : -10f));
            float rangeMult = (skill != null) ? skill.clearBandRangeMult : 1f;
            if (releaseFever == FeverState.SuperFever && skill != null) rangeMult = skill.clearSuperRangeMult;
            else if (releaseFever == FeverState.Fever && skill != null) rangeMult = skill.clearOverheatRangeMult;
            float bandEndX = ownX + 0.25f * rangeMult * (enemyX - ownX);
            float xMin = Mathf.Min(ownX, bandEndX);
            float xMax = Mathf.Max(ownX, bandEndX);
            ownerSpawner.SkillClearBand(xMin, xMax, this);
        }

        // 释放者（小黑）自身沉睡：作为清屏的代价（控制免疫可抵抗）。
        // 沉睡期间该侧禁命中/禁主动技能/被动失效；解除方式为：驱散 / 时长到 / 队伍扣血。
        float sleepSec = (skill != null && skill.clearSleepSeconds > 0f) ? skill.clearSleepSeconds : 3f;
        var sleepCtrl = SleepController.EnsureInstance();
        if (sleepCtrl != null) sleepCtrl.Sleep(ownerSide, sleepSec);
        else Debug.LogWarning("[ClearScreen] SleepController 未找到，沉睡未生效");

        // 激活自身 b 类 buff（小黑个人战力，与 a 类相乘叠加）
        // 仅过热 / 超级过热释放才有（普通释放不放 b）。颜色由 TeamAuraVfx.SelfPowerAuraFx 决定（橙黄）。
        bool feverActive = releaseFever == FeverState.Fever || releaseFever == FeverState.SuperFever;
        if (skill != null && skill.clearBuffAsB && feverActive && BuffController.Instance != null)
        {
            // 按过热档选对应字段：普通释放不放 b；过热/超级过热各自独立的 攻击提升·持续时间
            float mult = skill.buffCombatMult;
            float dur = skill.buffDuration;
            if (releaseFever == FeverState.SuperFever) { mult = skill.clearSuperCombatMult; dur = skill.clearSuperBuffDuration; }
            else if (releaseFever == FeverState.Fever) { mult = skill.clearOverheatCombatMult; dur = skill.clearOverheatBuffDuration; }
            BuffController.Instance.SetBBuff(ownerSide, mult > 0f ? mult : 1.5f);
            BuffController.Instance.SetBuffDuration(ownerSide, BuffSlot.B, dur > 0f ? dur : 10f);
        }

        yield return new WaitForSeconds(0.1f);
        if (marker != null) marker.ShrinkUnglow();
        phase = Phase.Cooldown;
        CastingSides.Remove(ownerSide);
        cooldownLeft = slotCooldown;
    }

    /// <summary>纯 buff 技能（无附魔）：施加 a/b 类 buff（互斥顶替）后直接收尾。</summary>
    private System.Collections.IEnumerator PureBuffSequence()
    {
        yield return new WaitForSeconds(0.15f);
        if (skill != null && skill.buffSlot != BuffSlot.None && BuffController.Instance != null)
        {
            if (skill.buffSlot == BuffSlot.A)
                BuffController.Instance.SetABuff(ownerSide, skill.buffSubType, skill.buffCombatMult, skill.buffDamageReduce);
            else
                BuffController.Instance.SetBBuff(ownerSide, skill.buffCombatMult);
            float dur = skill.buffDuration > 0f ? skill.buffDuration : 0f;
            BuffController.Instance.SetBuffDuration(ownerSide, skill.buffSlot, dur);
            Debug.Log($"[Buff] side{ownerSide} 施加 {skill.buffSlot}（{(skill.buffSlot == BuffSlot.A ? skill.buffSubType.ToString() : "b战力")}），持续 {dur}s");
        }
        if (marker != null) marker.PulseGlow();
        yield return new WaitForSeconds(0.1f);
        if (marker != null) marker.ShrinkUnglow();
        phase = Phase.Cooldown;
        CastingSides.Remove(ownerSide);
        cooldownLeft = slotCooldown;
    }

    /// <summary>清屏时射出电流：从释放者位置向范围内（覆盖 4 轨）发射数条电流光带。</summary>
    private void SpawnElectricity(Vector3 from)
    {
        float dir = (ownerSide == 0) ? 1f : -1f;
        for (int i = 0; i < 4; i++)
        {
            Vector3 to = from + new Vector3(dir * 4f, 0f, -1.5f + i * 1f);
            ElectricityFx.Spawn(from, to);
        }
    }

    private NoteSpawner FindEnemySpawner(int selfSide)
    {
        var all = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var s in all) if (s.side != selfSide) return s;
        return null;
    }

    /// <summary>清屏清除一个普通音符：播放大白消失 + 视为命中（按音符自身音轨计分/连击/充能）+ 弹 PERFECT 评价。</summary>
    public void OnSkillClearedNote(Note note, NoteSpawner spawner)
    {
        if (note == null) return;
        note.MarkClearedBySkill();
        // 视为命中：走统一 OnJudge 管线 -> ScoreManager 加分（按 ownerSide）+ BattleVisualsController 加连击；
        // 充能按音符自身音轨 note.lane 落到对应轨角色（HandleJudge 内 GetTeam(ownerSide, lane).AddEnergy）。
        // 评价取该音符真实最高判定：普通/连轨点击=PERFECT，小型点击=PASS（与玩家手动完成完全一致）。
        string noteRank = note.isSmallTap ? "PASS" : "PERFECT";
        if (spawner != null) spawner.ReportJudge(ownerSide, note.lane, noteRank, note.transform.position, note);
        var jfm = FindFirstObjectByType<JudgeFeedbackManager>();
        if (jfm != null) jfm.ShowFeedback(note.side, note.lane, noteRank, note.transform.position, note);
    }

    /// <summary>清屏清除一个长按节点：按节点音轨 lane 视为命中（计分/连击/充能）。
    /// 评价由调用方传入：头节点 PERFECT（最佳命中）、其余段 CLEAR（与玩家手动完成长按每段一致）。</summary>
    public void OnSkillClearedNode(HoldNote hn, int nodeIndex, string rank = "CLEAR")
    {
        if (hn == null) return;
        int lane = (hn.NodeCount > 0) ? hn.GetNodeLane(nodeIndex) : hn.side;
        // 用节点实时世界坐标作为反馈/结算位置：每个节点弹在各自所在位置（清屏时节点散布在轨道上，
        // 不再统一堆到判定线头部）。ReportJudge 与 ShowFeedback 共用此 pos。
        Vector3 pos = hn.GetNodePosition(nodeIndex);
        // 视为命中：按节点音轨 lane 走统一管线计分/连击/充能（对应轨角色）。
        if (hn.spawner != null) hn.spawner.ReportJudge(ownerSide, lane, rank, pos, hn);
        var jfm = FindFirstObjectByType<JudgeFeedbackManager>();
        if (jfm != null) jfm.ShowFeedback(hn.side, lane, rank, pos, hn);
    }

    /// <summary>每个被附魔音符「命中成功」时触发一次：按 effectType 分发投弹 / 回血。</summary>
    private void OnPerCharmSuccess()
    {
        if (skill == null) return;
        switch (skill.effectType)
        {
            case "Bomb": ThrowBomb(); break;
            case "Heal": HealSelf(); break;
            // 驱散：类附魔模块——每成功结算一个附魔音符，即清除本侧持续性控制效果（当前=沉睡）。
            case "Dispel": DispelControl(); break;
        }
    }

    /// <summary>驱散（类附魔模块效果）：每成功结算一个附魔音符，清除本侧持续性控制效果（当前实现=解除沉睡）。</summary>
    private void DispelControl()
    {
        var sc = SleepController.EnsureInstance();
        if (sc != null) sc.Dispel(ownerSide);
    }

    /// <summary>炸弹雨：从施法角色位置投出一颗白色方块炸弹，飞向对方阵地范围内随机落点，落地即时结算伤害。</summary>
    private void ThrowBomb()
    {
        if (marker == null) return;
        Vector3 from = marker.transform.position;
        Vector3 enemyCenter = from + new Vector3(ownerSide == 0 ? 8f : -8f, 0f, 0f);
        Vector3 to = enemyCenter + new Vector3(Random.Range(-3f, 3f), 0.6f, Random.Range(-3f, 3f));

        if (skill != null && skill.releaseSfxClip != null)
            AudioSource.PlayClipAtPoint(skill.releaseSfxClip, from);

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "BombProjectile";
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            r.material.EnableKeyword("_EMISSION");
            r.material.SetColor("_EmissionColor", Color.white);
            r.material.SetColor("_BaseColor", Color.white);
        }
        go.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        var fx = go.AddComponent<BombFx>();
        fx.from = from;
        fx.to = to;
        fx.duration = 0.5f;
        fx.onImpact = () =>
        {
            if (_scoreMgr == null) _scoreMgr = FindFirstObjectByType<ScoreManager>();
            if (_battleSys == null) _battleSys = FindFirstObjectByType<CharacterBattleSystem>();
            if (_scoreMgr != null && _battleSys != null)
            {
                // 伤害 = ceil(释放方全队战斗力总和 × bombDamageRate)，全局整数规则向上取整
                float combatSum = _battleSys.GetCombatSum(ownerSide);
                int dmg = Mathf.CeilToInt(combatSum * (skill != null ? skill.bombDamageRate : 0.1f));
                _scoreMgr.TakeDamage(1 - ownerSide, dmg);
            }
            SpawnImpactFlash(to, Color.white);
        };
    }

    /// <summary>美味牛角包：每个命中的附魔音符即时回血 = ceil(生命值总和×hpRate + 战斗力总和×combatRate)，并散布淡绿治疗特效。</summary>
    private void HealSelf()
    {
        if (_scoreMgr == null) _scoreMgr = FindFirstObjectByType<ScoreManager>();
        if (_battleSys == null) _battleSys = FindFirstObjectByType<CharacterBattleSystem>();
        if (_scoreMgr == null || _battleSys == null) return;

        int hpSum = _battleSys.GetMaxHP(ownerSide);
        float combatSum = _battleSys.GetCombatSum(ownerSide);
        float hpRate = (skill != null) ? skill.healHpRate : 0.005f;
        float combatRate = (skill != null) ? skill.healCombatRate : 0.015f;
        int heal = Mathf.CeilToInt(hpSum * hpRate + combatSum * combatRate);
        _scoreMgr.Heal(ownerSide, heal);
        if (marker != null) SpawnHealVfx(marker.transform.position);
    }

    /// <summary>牛角包过热/超级过热：技能结束后 9 秒缓慢恢复，每 3 秒一跳共 3 跳，每跳回血 = ceil(本次命中附魔音符数 × 生命值总和 × regenPerTickHpRate)。</summary>
    private System.Collections.IEnumerator Regen()
    {
        float interval = (skill != null) ? skill.regenInterval : 3f;
        int ticks = 0;
        while (ticks < 3)
        {
            yield return new WaitForSeconds(interval);
            ticks++;
            if (_scoreMgr == null) _scoreMgr = FindFirstObjectByType<ScoreManager>();
            if (_battleSys == null) _battleSys = FindFirstObjectByType<CharacterBattleSystem>();
            if (_scoreMgr != null && _battleSys != null)
            {
                int hpSum = _battleSys.GetMaxHP(ownerSide);
                float rate = (skill != null) ? skill.regenPerTickHpRate : 0.002f;
                int heal = Mathf.CeilToInt(completedCount * hpSum * rate);
                _scoreMgr.Heal(ownerSide, heal);
                if (marker != null) SpawnHealVfx(marker.transform.position);
            }
        }
    }

    /// <summary>白色方块炸弹落地爆炸闪效（占位）。</summary>
    private void SpawnImpactFlash(Vector3 at, Color c)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "BombImpact";
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            r.material.EnableKeyword("_EMISSION");
            r.material.SetColor("_EmissionColor", c);
            r.material.SetColor("_BaseColor", c);
        }
        go.transform.position = at;
        go.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        var fx = go.AddComponent<ImpactFx>();
        fx.duration = 0.35f;
    }

    /// <summary>淡绿色治疗特效（占位，低透明度）：在角色位置升起并放大淡出。</summary>
    private void SpawnHealVfx(Vector3 at)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "HealFx";
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            r.material.EnableKeyword("_EMISSION");
            Color g = new Color(0.4f, 1f, 0.4f);
            r.material.SetColor("_EmissionColor", g);
            r.material.SetColor("_BaseColor", g);
        }
        go.transform.position = at + Vector3.up * 0.5f;
        go.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        var fx = go.AddComponent<HealFx>();
        fx.duration = 0.8f;
    }

    private void FireOneShot()
    {
        int reduce = completedCount * (skill.reduceComboPerCharmedNote > 0 ? skill.reduceComboPerCharmedNote : 3);
        int before = (opponentCombo != null) ? opponentCombo.CurrentCombo : 0;
        if (opponentCombo != null) opponentCombo.ReduceBy(reduce);
        int actualReduced = (opponentCombo != null) ? (before - opponentCombo.CurrentCombo) : 0;

        // 大狗叫额外效果（2026-08-27）：在削减连击的"同时"，按 "实际降低的连击数 × 队伍战斗力总和%" 造成对方 HP 伤害。
        // - 实际降低连击数 = 削减前后差值（已封顶到 0，故对方连击过低时只算真正扣掉的部分）。
        // - 队伍战斗力总和 = 释放方所在队伍（ownerSide）的 combatSum。
        // - 系数 comboHpDamageRate 默认 0.01（= 战斗力总和的 1%，对应"战斗力总和%"），可在 Inspector / 技能库调参面板调整。
        // 仅 effectType == "DogHowl" 生效，不影响玩家自身技能（小熊）。
        if (skill != null && skill.effectType == "DogHowl" && skill.comboHpDamageRate > 0f && actualReduced > 0)
        {
            if (_scoreMgr == null) _scoreMgr = FindFirstObjectByType<ScoreManager>();
            if (_battleSys == null) _battleSys = FindFirstObjectByType<CharacterBattleSystem>();
            if (_scoreMgr != null && _battleSys != null)
            {
                float combatSum = _battleSys.GetCombatSum(ownerSide);
                int dmg = Mathf.CeilToInt(actualReduced * combatSum * skill.comboHpDamageRate);
                _scoreMgr.TakeDamage(1 - ownerSide, dmg);
                Debug.Log(string.Format("[ActiveSkill {0}(side{1}, id={2})] 额外HP伤害 -> 实际降低连击 {3} × 队伍战斗力 {4} × {5} = 对方 -{6} HP",
                    skill.displayName, ownerSide, owner != null ? owner.characterId : -1,
                    actualReduced, combatSum, skill.comboHpDamageRate, dmg));
            }
        }

        SpawnShockwave();

        Debug.Log(string.Format("[ActiveSkill {0}(side{1}, id={2})] 发射音浪 -> 削减对方 {3} 连击（命中 {4}/{5}）",
            skill != null ? skill.displayName : "?",
            ownerSide, owner != null ? owner.characterId : -1,
            reduce, completedCount, charmQuotaTotal));
    }

    private void SpawnShockwave()
    {
        if (marker == null) return;
        Vector3 from = marker.transform.position;
        Vector3 to = from + new Vector3(ownerSide == 0 ? 8f : -8f, 0f, 0f);

        if (skill != null && skill.releaseSfxClip != null)
            AudioSource.PlayClipAtPoint(skill.releaseSfxClip, from);

        GameObject go;
        if (skill != null && skill.shockwavePrefab != null)
        {
            go = Instantiate(skill.shockwavePrefab, from, Quaternion.identity);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "DogShockwave";
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                r.material.EnableKeyword("_EMISSION");
                r.material.SetColor("_EmissionColor", skill != null ? skill.releaseGlow : Color.yellow);
                r.material.SetColor("_BaseColor", skill != null ? skill.releaseGlow : Color.yellow);
            }
            go.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
        }
        var fx = go.AddComponent<ShockwaveFx>();
        fx.from = from;
        fx.to = to;
        fx.duration = 0.45f;
    }
}

/// <summary>音波 VFX：从 from 快速平移+拉长到 to，结束后销毁。</summary>
public class ShockwaveFx : MonoBehaviour
{
    public Vector3 from;
    public Vector3 to;
    public float duration = 0.45f;
    private float t;

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / duration);
        transform.position = Vector3.Lerp(from, to, k);
        float s = Mathf.Lerp(0.3f, 1.2f, k);
        transform.localScale = new Vector3(s, s, s * 2f);
        if (k >= 1f) Destroy(gameObject);
    }
}

/// <summary>炸弹投射体 VFX：从 from 飞到 to，落地触发 onImpact 后销毁。</summary>
public class BombFx : MonoBehaviour
{
    public Vector3 from;
    public Vector3 to;
    public float duration = 0.5f;
    public System.Action onImpact;
    private float t;

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / duration);
        transform.position = Vector3.Lerp(from, to, k);
        float s = Mathf.Lerp(0.4f, 0.9f, k);
        transform.localScale = new Vector3(s, s, s);
        if (k >= 1f)
        {
            onImpact?.Invoke();
            Destroy(gameObject);
        }
    }
}

/// <summary>爆炸闪效 VFX：快速放大并淡出后销毁（占位）。</summary>
public class ImpactFx : MonoBehaviour
{
    public float duration = 0.35f;
    private float t;

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / duration);
        float s = Mathf.Lerp(0.6f, 2.2f, k);
        transform.localScale = new Vector3(s, s, s);
        var r = GetComponent<Renderer>();
        if (r != null && r.material != null)
        {
            Color c = r.material.color;
            c.a = 1f - k;
            r.material.color = c;
        }
        if (k >= 1f) Destroy(gameObject);
    }
}

/// <summary>治疗特效 VFX：淡绿方块升起放大淡出后销毁（占位）。</summary>
public class HealFx : MonoBehaviour
{
    public float duration = 0.8f;
    private float t;

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / duration);
        float s = Mathf.Lerp(0.6f, 1.8f, k);
        transform.localScale = new Vector3(s, s, s);
        var r = GetComponent<Renderer>();
        if (r != null && r.material != null)
        {
            Color c = r.material.color;
            c.a = (1f - k) * 0.5f;
            r.material.color = c;
        }
        if (k >= 1f) Destroy(gameObject);
    }
}
