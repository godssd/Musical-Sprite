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
/// - 附魔单位记账：直接跟踪每个被附魔的音符对象（tap=Note / 长按=HoldNote）是否仍在场，集合为空且配额关闭才结算，避免「计数口径」与「场上实际音符数」错位导致提前放狗叫（Issue 1 修复）。
/// - 多角色技能同时释放：NoteSpawner 用 FIFO（最旧请求优先）+ 不可二次附魔守卫，后放的技能自动排到后面的音符。
/// - 附魔音符命中时：变大的大狗闪烁（PulseGlow），由 success 分支触发；被附魔音符命中不闪该 lane 角色（见 BattleVisualsController）。
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
    private HashSet<HoldNote> charmedHolds = new HashSet<HoldNote>();  // 当前已被附魔、尚未整体结算消失的长按音符对象
    private int completedCount;           // 成功数（非 MISS，用于削减对方连击的强度）
    private bool requestClosed;           // spawner 端配额是否已全部分配完
    private float cooldownLeft;

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
            && (charmedNotes.Count > 0 || charmedHolds.Count > 0)
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
        charmedHolds.Clear();
        requestClosed = false;

        // 释放成功：需要能量的技能立刻清空能量（冒气停止）；无能量技能跳过充能/清空流程与表现
        if (owner != null)
        {
            if (needsEnergy) owner.ConsumeEnergy(owner.maxEnergy);
            owner.SetSkillBusy(true);   // 整个技能进行期（含过热连叫）抑制"能量充满冒烟"，回到 Standby 时才可能恢复
        }

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

    /// <summary>HoldNote（整条链接链）被 charm 时由 NoteSpawner 调用：占 N 个附魔单位（N = 节点数）。</summary>
    public void OnHoldCharmed(HoldNote hn, int nodeCount)
    {
        if (hn == null || nodeCount <= 0) return;
        // 整条长按链作为一个对象跟踪；节点数只用于后续削减强度参考，不再参与结算时机计数。
        charmedHolds.Add(hn);
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
    // 长按逐节点成功：只更新「成功数」与「附魔命中闪烁」；不触发结算（整条链结束由 OnCharmHoldResolved 统一结算）。
    public void OnCharmNodeSuccess()
    {
        completedCount++;
        if (phase == Phase.Grow) phase = Phase.Charming;
        if ((phase == Phase.Grow || phase == Phase.Charming) && marker != null) marker.PulseGlow();
    }

    /// <summary>被附魔的 Hold/Linked 某节点失败结算（断连/漏击）时由 HoldNote 逐节点调用。</summary>
    public void OnCharmNodeFail()
    {
        if (phase == Phase.Grow) phase = Phase.Charming;
    }

    /// <summary>整条被附魔的长按链真正结算完毕（命中/断连/漏击后销毁）时由 HoldNote 调用：从集合中移除并触发结算判定。</summary>
    public void OnCharmHoldResolved(HoldNote hn)
    {
        charmedHolds.Remove(hn);
        TrySettleFromCharm();
    }

    /// <summary>所有附魔音符对象都已结算消失（集合空），且 spawner 配额已分配完 -> 立刻 Settle；否则等待剩下的。</summary>
    private void TrySettleFromCharm()
    {
        if (phase != Phase.Grow && phase != Phase.Charming) return;
        if (requestClosed && charmedNotes.Count == 0 && charmedHolds.Count == 0)
            Settle();
    }

    /// <summary>由 NoteSpawner.RequestCharm 调用，记录本技能可附魔的总名额（参考用）。</summary>
    public void SetCharmQuota(int q) { charmQuotaTotal = q; }

    private void Settle()
    {
        if (phase == Phase.Releasing || phase == Phase.Cooldown) return;
        phase = Phase.Releasing;

        // 过热判定：发射瞬间查该侧 Fever 状态，决定发 1/2/3 次
        int shots = 1;
        FeverState st = FeverState.None;
        if (feverManager != null)
        {
            st = feverManager.GetState(ownerSide);
            if (st == FeverState.SuperFever) shots = 3;
            else if (st == FeverState.Fever) shots = 2;
        }

        Debug.Log(string.Format("[ActiveSkill {0}(side{1}, id={2})] Settle -> 命中 {3} 个附魔单位，过热={4} 发 {5} 次",
            skill != null ? skill.displayName : "?",
            ownerSide, owner != null ? owner.characterId : -1,
            completedCount,
            st.ToString(),
            shots));

        StartCoroutine(FireSequence(shots));
    }

    /// <summary>按过热次数连发音浪；最后一次发射完后才缩小并进入冷却。</summary>
    private System.Collections.IEnumerator FireSequence(int shots)
    {
        for (int i = 0; i < shots; i++)
        {
            if (i > 0)
            {
                // 过热：等待 3s 后再次发光并射出音浪（效果与第一次相同）
                yield return new WaitForSeconds(3f);
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
        if (opponentCombo != null) opponentCombo.ReduceBy(reduce);
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
