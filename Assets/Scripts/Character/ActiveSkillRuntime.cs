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
/// - 附魔单位记账：tap 占 1，N 节点 Hold 占 N（一个节点 = 1 个附魔单位），统一用 charmUnitsOutstanding 计数。
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
    private int charmQuotaTotal;          // 本次释放可附魔的总单位数（= skill.charmedNoteCount）
    private int charmUnitsOutstanding;    // 已分配给本技能、但尚未结算的附魔单位数（tap +-1, hold +-N）
    private int completedCount;           // 成功数（非 MISS）
    private bool requestClosed;           // spawner 端配额是否已全部分配完
    private float cooldownLeft;
    private float settleIdleTimer;        // 兜底：已无 outstanding 但配额未用完（歌结束），超过 0.6s 强制 Settle

    public void Setup(CharacterClass owner, CharacterCubeMarker marker, NoteSpawner spawner, ComboDisplay oppCombo, int side, FeverManager fever)
    {
        this.owner = owner;
        this.marker = marker;
        this.ownerSpawner = spawner;
        this.opponentCombo = oppCombo;
        this.ownerSide = side;
        this.feverManager = fever;
        this.skill = owner != null ? owner.activeSkill : null;
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

        // 安全兜底（仅在舞台真的没有可附魔音符时触发，避免「歌里剩下的音符 < charmedNoteCount」导致永久卡死）：
        // 已无 outstanding 附魔单位，但请求尚未关闭（配额没用完）-> 等待 0.6s 仍无新音符 -> 强制释放（按真实完成数结算）。
        if ((phase == Phase.Grow || phase == Phase.Charming) && charmUnitsOutstanding == 0 && !requestClosed)
        {
            settleIdleTimer += Time.deltaTime;
            if (settleIdleTimer > 0.6f)
            {
                settleIdleTimer = 0f;
                if (phase != Phase.Releasing && phase != Phase.Cooldown) Settle();
            }
        }
        else
        {
            settleIdleTimer = 0f;
        }
    }

    /// <summary>主动释放入口：只有能量满 + Standby 才可开始。由 SkillInputUI 在序列按对后调用。</summary>
    public void BeginCast()
    {
        if (phase != Phase.Standby) return;
        if (owner == null || !owner.HasActiveSkill) return;
        if (!owner.isFullyCharged) return;   // 能量未满不允许释放
        if (skill == null) return;

        phase = Phase.Grow;
        completedCount = 0;
        charmUnitsOutstanding = 0;
        requestClosed = false;
        settleIdleTimer = 0f;

        // 释放成功：立刻清空能量（大狗身上的冒气表现随之停止，因为 EnergyVFXPlaceholder 订阅了 OnEnergyDepleted）
        if (owner != null)
        {
            owner.ConsumeEnergy(owner.maxEnergy);
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
        charmUnitsOutstanding++;
        settleIdleTimer = 0f;
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
        charmUnitsOutstanding += nodeCount;
        settleIdleTimer = 0f;
    }

    /// <summary>被附魔的普通 tap 音符结算（命中/MISS/消失）时由 Note 调用。success=true 表示非 MISS。</summary>
    public void OnCharmedNoteResolved(Note n, bool success)
    {
        charmUnitsOutstanding = Mathf.Max(0, charmUnitsOutstanding - 1);
        if (success) completedCount++;
        if (phase == Phase.Grow) phase = Phase.Charming;
        if (success && (phase == Phase.Grow || phase == Phase.Charming) && marker != null) marker.PulseGlow();
        TrySettleFromCharm();
    }

    /// <summary>被附魔的 Hold/Linked 某节点成功结算（CLEAR/头节点命中）时由 HoldNote 逐节点调用。</summary>
    public void OnCharmNodeSuccess()
    {
        charmUnitsOutstanding = Mathf.Max(0, charmUnitsOutstanding - 1);
        completedCount++;
        if (phase == Phase.Grow) phase = Phase.Charming;
        if ((phase == Phase.Grow || phase == Phase.Charming) && marker != null) marker.PulseGlow();
        TrySettleFromCharm();
    }

    /// <summary>被附魔的 Hold/Linked 某节点失败结算（断连/漏击）时由 HoldNote 逐节点调用。</summary>
    public void OnCharmNodeFail()
    {
        charmUnitsOutstanding = Mathf.Max(0, charmUnitsOutstanding - 1);
        if (phase == Phase.Grow) phase = Phase.Charming;
        TrySettleFromCharm();
    }

    /// <summary>所有附魔单位都被消化 + spawner 配额全部用完 -> 立刻 Settle；否则重置 idle 计时等待剩下的。</summary>
    private void TrySettleFromCharm()
    {
        if (phase != Phase.Grow && phase != Phase.Charming) return;
        if (charmUnitsOutstanding == 0)
        {
            if (requestClosed) Settle();
            else settleIdleTimer = 0f;  // 等 0.6s（可能有后续音符被附魔）
        }
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
        cooldownLeft = skill.cooldown > 0f ? skill.cooldown : 20f;
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
