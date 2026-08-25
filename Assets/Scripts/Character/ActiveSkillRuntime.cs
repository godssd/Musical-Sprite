using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 队伍角色的主动技能运行时状态机（大狗叫等）。
/// 由 CharacterBattleSystem 在 Start 时为每个队伍角色（非玩家）的 marker 挂载并 Setup。
///
/// 生命周期：
///   Standby  →（玩家按对 inputSequence 且能量满）→ Grow
///   Grow     → 角色变大+持续发光；同时向 ownerSpawner 请求附魔接下来 charmedNoteCount 个音符
///   Charming → 每个被附魔音符结算（命中/MISS/消失）回调 OnCharmedNoteResolved；全部消费完后 → Settle
///   Releasing→ 发射音波冲向对面 + 按 完成数×reduceComboPerCharmedNote 削减对方连击 + 角色缩回熄灭 + 清空能量
///   Cooldown → 冷却倒计时后回到 Standby
///
/// 无时间兜底：6 个音符要全部被命中或 MISS/消失才结算（符合设计）。
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
    private float settleIdleTimer;  // 兜底计时：已无 outstanding 附魔音符但配额未填满，超过 0.6s 强制 Settle
    private float castStartTime;    // BeginCast 时刻（Time.time），用于强制最少蓄力时间，避免「6 个附魔音符被打完后瞬间释放」
    private const float MIN_CAST_DURATION = 1.2f;   // 蓄力下限：按完序列后至少 1.2s 才能 Settle（让玩家有「变大发光蓄力中」的视觉感）

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

        // 蓄力下限检查：按完序列后到 1.2s 之前一律不允许 Settle（即便附魔音符都被打完了）。
        // 这是为了避免「释放 → 立刻叫」的感知：玩家需要看到角色持续发光、变大一段时间。
        float elapsed = Time.time - castStartTime;
        if (elapsed < MIN_CAST_DURATION) return;

        // 安全兜底（仅在舞台真的没有可附魔音符时触发，避免「歌里剩下的音符 < charmedNoteCount」导致永久卡死）：
        // 已附魔的音符全部解决，且当前没有任何 outstanding 附魔音符 + 等待 0.6s 仍未消费完配额 → 强制释放（按真实完成数结算）。
        if ((phase == Phase.Grow || phase == Phase.Charming) &&
            charmedNotes.Count == 0 && charmRemaining > 0)
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
        settleIdleTimer = 0f;
        castStartTime = Time.time;

        if (marker != null) marker.GrowGlow();
        if (ownerSpawner != null)
            ownerSpawner.RequestCharm(this, charmRemaining);

        Debug.Log(string.Format("[ActiveSkill {0}(side{1}, id={2})] BeginCast → 等待 {3} 个附魔音符（最少 {4:F1}s 蓄力）",
            skill != null ? skill.displayName : "?",
            ownerSide, owner != null ? owner.characterId : -1,
            charmRemaining, MIN_CAST_DURATION));
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
        // 即使配额外没有全部 charmed（歌里音符已耗尽），只要 outstanding 全部解决，就允许释放
        if (phase == Phase.Grow || phase == Phase.Charming)
        {
            if (charmedNotes.Count == 0) Settle();
        }
    }

    /// <summary>被附魔音符结算（命中/MISS/消失）时由 Note 调用。success=true 表示非 MISS。</summary>
    public void OnCharmedNoteResolved(Note n, bool success)
    {
        if (n != null && charmedNotes.Contains(n)) charmedNotes.Remove(n);
        if (skill != null && (skill.onlyCountNonMiss ? success : true))
            completedCount++;
        if (phase == Phase.Grow && charmedNotes.Count > 0) phase = Phase.Charming;

        if (charmedNotes.Count == 0 && charmRemaining <= 0)
            Settle();
        else if (charmedNotes.Count == 0)
            settleIdleTimer = 0f;   // 重新开始 idle 计时
    }

    /// <summary>由 NoteSpawner.RequestCharm 调用，记录本技能可附魔的名额。</summary>
    public void SetCharmQuota(int q) { charmRemaining = q; }

    private void Settle()
    {
        if (phase == Phase.Releasing || phase == Phase.Cooldown) return;

        // 蓄力下限：开始到 1.2s 之前一律不允许 Settle（即使 charmedNotes 都已结算）。
        // 与 Update 的早期 return 互为冗余；这里再次兜底以防有其他路径调 Settle。
        if (Time.time - castStartTime < MIN_CAST_DURATION) return;

        phase = Phase.Releasing;

        int reduce = completedCount * (skill.reduceComboPerCharmedNote > 0 ? skill.reduceComboPerCharmedNote : 3);
        if (opponentCombo != null) opponentCombo.ReduceBy(reduce);

        SpawnShockwave();

        if (marker != null) marker.ShrinkUnglow();
        if (owner != null) owner.ConsumeEnergy(owner.maxEnergy);

        phase = Phase.Cooldown;
        cooldownLeft = skill.cooldown > 0f ? skill.cooldown : 20f;

        Debug.Log(string.Format("[ActiveSkill {0}(side{1}, id={2})] Settle → 命中 {3}/{4} 削减对方 {5} 连击 ({6:F2}s 后)",
            skill != null ? skill.displayName : "?",
            ownerSide, owner != null ? owner.characterId : -1,
            completedCount, charmRemaining, reduce,
            Time.time - castStartTime));
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
