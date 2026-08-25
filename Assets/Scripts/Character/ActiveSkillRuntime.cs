using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 队伍角色的主动技能运行时状态机（大狗叫等）。
/// 由 CharacterBattleSystem 在 Start 时为每个队伍角色（非玩家）的 marker 挂载并 Setup。
///
/// 生命周期：
///   Standby  →（玩家按对 inputSequence 且能量满）→ Grow
///   Grow     → 角色变大+持续发光；同时向 ownerSpawner 请求附魔接下来 charmedNoteCount 个附魔单位
///   Charming → 每个被附魔单位结算（普通 tap 命中/MISS、Hold/Linked 整体 COMPLETE/BREAK/MISSHEAD）回调 OnCharmedNoteResolved/OnCharmedHoldResolved；
///              当所有被附魔单位全部结算且配额外所有链接出去的音符也消费完 → 立刻 Settle（不再有时间下限）
///   Releasing→ 发射音波冲向对面 + 按 完成数×reduceComboPerCharmedNote 削减对方连击 + 角色缩回熄灭 + 清空能量
///   Cooldown → 冷却倒计时后回到 Standby
///
/// 释放时机 = 等所有被附魔单位（含整条 Hold/Linked 链接链）结算完毕（CLEAR/命中 算 success=true，断连/漏击算 success=false），立刻释放狗叫。
/// 效果强度（连击削减量）= success 数 × reduceComboPerCharmedNote；效果强度取决于玩家的命中而非时间。
/// </summary>
public class ActiveSkillRuntime : MonoBehaviour
{
    public enum Phase { Standby, Grow, Charming, Releasing, Cooldown }

    [HideInInspector] public CharacterClass owner;
    [HideInInspector] public CharacterCubeMarker marker;
    [HideInInspector] public NoteSpawner ownerSpawner;
    [HideInInspector] public ComboDisplay opponentCombo;
    [HideInInspector] public int ownerSide = 0;
    public Phase phase = Phase.Standby;

    private SkillSO skill;
    private int charmRemaining;     // 待附魔名额（spawn 时递减）
    private int completedCount;     // 完成数（非 MISS）
    private List<Note> charmedNotes = new List<Note>();
    private float cooldownLeft;
    private float settleIdleTimer;          // 兜底计时：已无 outstanding 附魔单位但配额还没消费完（如歌里剩余音符 < charmedNoteCount），超过 0.6s 强制 Settle
    private List<HoldNote> charmedHolds = new List<HoldNote>();  // 整条 Hold/Linked 链接链（链整体算一个附魔单位）

    public void Setup(CharacterClass owner, CharacterCubeMarker marker, NoteSpawner spawner, ComboDisplay oppCombo, int side)
    {
        this.owner = owner;
        this.marker = marker;
        this.ownerSpawner = spawner;
        this.opponentCombo = oppCombo;
        this.ownerSide = side;
        this.skill = owner != null ? owner.activeSkill : null;
    }

    void Update()
    {
        if (phase == Phase.Cooldown)
        {
            cooldownLeft -= Time.deltaTime;
            if (cooldownLeft <= 0f) phase = Phase.Standby;
            return;
        }

        // 安全兜底（仅在舞台真的没有可附魔音符时触发，避免「歌里剩下的音符 < charmedNoteCount」导致永久卡死）：
        // 已附魔的音符全部解决（notes + holds 都清空），且当前没有任何 outstanding 附魔单位 + 等待 0.6s 仍未消费完配额 → 强制释放（按真实完成数结算）。
        if ((phase == Phase.Grow || phase == Phase.Charming) &&
            charmedNotes.Count == 0 && charmedHolds.Count == 0 && charmRemaining > 0)
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
        charmRemaining = Mathf.Max(1, skill.charmedNoteCount);
        charmedNotes.Clear();
        charmedHolds.Clear();
        settleIdleTimer = 0f;

        if (marker != null) marker.GrowGlow();
        if (ownerSpawner != null)
            ownerSpawner.RequestCharm(this, charmRemaining);

        Debug.Log(string.Format("[ActiveSkill {0}(side{1}, id={2})] BeginCast → 等待 {3} 个附魔单位结算完毕即释放",
            skill != null ? skill.displayName : "?",
            ownerSide, owner != null ? owner.characterId : -1,
            charmRemaining));
    }

    /// <summary>NoteSpawner 给本技能成功附魔了一个音符时调用。</summary>
    public void OnNoteCharmed(Note n)
    {
        if (n == null) return;
        if (charmedNotes.Contains(n)) return;
        charmedNotes.Add(n);
        // 关键修复：每次附魔消耗一份配额，让 Settle 条件能正确触发
        charmRemaining = Mathf.Max(0, charmRemaining - 1);
        settleIdleTimer = 0f;
    }

    /// <summary>spawner 端把本技能的 activeCharms 条目移除时回调（配额用完，或该请求被作废清理）。</summary>
    public void OnCharmRequestClosed()
    {
        if (phase == Phase.Grow || phase == Phase.Charming)
        {
            if (charmedNotes.Count == 0 && charmedHolds.Count == 0) Settle();
        }
    }

    /// <summary>HoldNote（整条链接链）被 charm 时由 NoteSpawner 调用，加入 charmedHolds + 消耗 1 配额。</summary>
    public void OnHoldCharmed(HoldNote hn)
    {
        if (hn == null) return;
        if (charmedHolds.Contains(hn)) return;
        charmedHolds.Add(hn);
        charmRemaining = Mathf.Max(0, charmRemaining - 1);
        settleIdleTimer = 0f;
    }

    /// <summary>被附魔的普通 tap 音符结算（命中/MISS/消失）时由 Note 调用。success=true 表示非 MISS。</summary>
    public void OnCharmedNoteResolved(Note n, bool success)
    {
        if (n != null && charmedNotes.Contains(n)) charmedNotes.Remove(n);
        if (skill != null && (skill.onlyCountNonMiss ? success : true))
            completedCount++;
        if (phase == Phase.Grow && (charmedNotes.Count > 0 || charmedHolds.Count > 0)) phase = Phase.Charming;
        TrySettleFromCharm();
    }

    /// <summary>被附魔的 Hold/Linked 整条链接链结算时由 HoldNote 调用（COMPLETE/BREAK/MISSHEAD 都会触发）。
    /// 整条链作为 1 个附魔单位；COMPLETE 算 success=true；BREAK/MISSHEAD 算 success=false。</summary>
    public void OnCharmedHoldResolved(HoldNote hn, bool success)
    {
        if (hn != null && charmedHolds.Contains(hn)) charmedHolds.Remove(hn);
        if (skill != null && (skill.onlyCountNonMiss ? success : true))
            completedCount++;
        if (phase == Phase.Grow && (charmedNotes.Count > 0 || charmedHolds.Count > 0)) phase = Phase.Charming;
        TrySettleFromCharm();
    }

    /// <summary>所有附魔单位都被消化 + spawner 配额全部用完 → 立刻 Settle；否则重置 idle 计时等待剩下的。</summary>
    private void TrySettleFromCharm()
    {
        if (phase != Phase.Grow && phase != Phase.Charming) return;
        if (charmedNotes.Count == 0 && charmedHolds.Count == 0 && charmRemaining <= 0)
            Settle();
        else if (charmedNotes.Count == 0 && charmedHolds.Count == 0)
            settleIdleTimer = 0f;
    }

    /// <summary>由 NoteSpawner.RequestCharm 调用，记录本技能可附魔的名额。</summary>
    public void SetCharmQuota(int q) { charmRemaining = q; }

    private void Settle()
    {
        if (phase == Phase.Releasing || phase == Phase.Cooldown) return;

        phase = Phase.Releasing;

        int reduce = completedCount * (skill.reduceComboPerCharmedNote > 0 ? skill.reduceComboPerCharmedNote : 3);
        if (opponentCombo != null) opponentCombo.ReduceBy(reduce);

        SpawnShockwave();

        if (marker != null) marker.ShrinkUnglow();
        if (owner != null) owner.ConsumeEnergy(owner.maxEnergy);

        phase = Phase.Cooldown;
        cooldownLeft = skill.cooldown > 0f ? skill.cooldown : 20f;

        Debug.Log(string.Format("[ActiveSkill {0}(side{1}, id={2})] Settle → 命中 {3}/{4} 削减对方 {5} 连击",
            skill != null ? skill.displayName : "?",
            ownerSide, owner != null ? owner.characterId : -1,
            completedCount, charmRemaining, reduce));
    }

    private void SpawnShockwave()
    {
        if (marker == null) return;
        Vector3 from = marker.transform.position;
        Vector3 to = from + new Vector3(ownerSide == 0 ? 8f : -8f, 0f, 0f);

        // P4 脚手架：若技能配了释放音效，在此播放（音频资源由美术/音频同学提供后挂到 SkillSO.releaseSfxClip）
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
