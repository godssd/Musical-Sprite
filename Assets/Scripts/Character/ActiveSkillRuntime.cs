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
    private float slotCooldown;           // 本槽冷却（秒），来自角色文档「技能冷却」列
    private bool needsEnergy = true;      // 是否需要能量门槛（无能量技能=false：跳过充能/能量满/清空）
    public SkillInputStep[] inputSequence; // 本槽输入序列（供 SkillInputUI 匹配）

    /// <summary>供 SkillInputUI 判断：该技能是否需要"能量满"才能开始输入。无能量技能=false（随时可输入）。</summary>
    public bool NeedsEnergyGate => needsEnergy;
    private int charmQuotaTotal;          // 本次释放可附魔的总单位数（= skill.charmedNoteCount，仅参考/调试）
    private HashSet<Note> charmedNotes = new HashSet<Note>();          // 当前已被附魔、尚未结算消失的普通音符对象
    private Dictionary<HoldNote, int> charmedHoldNodes = new Dictionary<HoldNote, int>(); // 每条链尚未结算的附魔节点数
    private int completedCount;           // 成功数（非 MISS，用于削减对方连击的强度）
    private bool requestClosed;           // spawner 端配额是否已全部分配完
    private float cooldownLeft;
    private FeverState releaseFever = FeverState.None;  // 技能释放（能量清空）瞬间捕获的过热档；整段连叫锁定，后续不重判（2026-08-27）
    private ScoreManager _scoreMgr;                      // 懒加载缓存：用于造成对方 HP 伤害（大狗叫额外效果）
    private CharacterBattleSystem _battleSys;            // 懒加载缓存：用于读取释放方队伍战斗力总和

    public void Setup(CharacterClass owner, CharacterCubeMarker marker, NoteSpawner spawner, ComboDisplay oppCombo, int side, FeverManager fever, SkillSO skill, float cooldown, bool needsEnergy, SkillInputStep[] inputSequence)
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
    }

    void Update()
    {
        if (phase == Phase.Cooldown)
        {
            cooldownLeft -= Time.deltaTime;
            if (cooldownLeft <= 0f)
            {
                phase = Phase.Standby;
                if (owner != null) owner.SetSkillBusy(false);  // 技能彻底结束：若能量已再次充满则恢复冒烟
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
        if (needsEnergy && !owner.isFullyCharged) return;   // 仅需要能量的技能才卡能量门槛
        if (skill == null) return;

        phase = Phase.Grow;
        completedCount = 0;
        charmedNotes.Clear();
        charmedHoldNodes.Clear();
        requestClosed = false;

        // 释放成功：需要能量的技能立刻清空能量（冒气停止）；无能量技能跳过充能/清空流程与表现
        if (owner != null)
        {
            if (needsEnergy) owner.ConsumeEnergy(owner.maxEnergy);
            owner.SetSkillBusy(true);   // 整个技能进行期（含过热连叫）抑制"能量充满冒烟"，回到 Standby 时才可能恢复
        }

        // 技能释放瞬间（能量清空此刻）捕获过热档，整段连叫锁定；后续附魔期即便断连/掉出过热也不再重判（2026-08-27 要求）。
        releaseFever = (feverManager != null) ? feverManager.GetState(ownerSide) : FeverState.None;

        if (marker != null) marker.GrowGlow();
        if (ownerSpawner != null)
            ownerSpawner.RequestCharm(this, Mathf.Max(1, skill.charmedNoteCount));

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
        if (success) completedCount++;
        if (phase == Phase.Grow) phase = Phase.Charming;
        if (success && (phase == Phase.Grow || phase == Phase.Charming) && marker != null) marker.PulseGlow();
        TrySettleFromCharm();
    }

    /// <summary>被附魔的 Hold/Linked 某节点成功结算（CLEAR/头节点命中）时由 HoldNote 逐节点调用。</summary>
    // 长按逐节点成功：更新成功数、闪烁，并立即消费该节点的等待计数。
    public void OnCharmNodeSuccess(HoldNote hn)
    {
        completedCount++;
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

        Debug.Log(string.Format("[ActiveSkill {0}(side{1}, id={2})] Settle -> 命中 {3} 个附魔单位，过热={4} 发 {5} 次",
            skill != null ? skill.displayName : "?",
            ownerSide, owner != null ? owner.characterId : -1,
            completedCount,
            st.ToString(),
            shots));

        if (marker != null) marker.PulseGlow();
        StartCoroutine(FireSequence(shots));
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
        // 冷却取本槽在角色文档配置的「技能冷却」；0 / 未填 = 无冷却（立刻回到 Standby 可再次释放）。
        cooldownLeft = slotCooldown;
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
        // 仅 effectType == "DogHowl" 生效，不影响玩家自身技能（宝宝）。
        if (skill != null && skill.effectType == "DogHowl" && skill.comboHpDamageRate > 0f && actualReduced > 0)
        {
            if (_scoreMgr == null) _scoreMgr = FindFirstObjectByType<ScoreManager>();
            if (_battleSys == null) _battleSys = FindFirstObjectByType<CharacterBattleSystem>();
            if (_scoreMgr != null && _battleSys != null)
            {
                float combatSum = _battleSys.GetCombatSum(ownerSide);
                int dmg = Mathf.RoundToInt(actualReduced * combatSum * skill.comboHpDamageRate);
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
