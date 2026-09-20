using UnityEngine;

/// <summary>
/// 挂在每个音符实例上的数据组件。
/// </summary>
public class Note : MonoBehaviour
{
    [HideInInspector] public float hitTime;
    [HideInInspector] public int lane;
    [HideInInspector] public int laneSpan = 1; // Linked 固定为 2，覆盖 lane 与 lane+1
    [HideInInspector] public int side;        // 0=左玩家要接，1=右玩家要接
    [HideInInspector] public bool isSmallTap = false; // 小型点击音符：半径更小、统一 PASS
    [HideInInspector] public bool isChainTap = false;
    [HideInInspector] public int chainTapRemaining = 0;
    [HideInInspector] public int chainTapRequired = 0;
    [HideInInspector] public bool chainTapWaiting = false;
    [HideInInspector] public bool isHit = false;
    [HideInInspector] public bool isVisible = false;

    [HideInInspector] public string finalRank = "MISS";

    /// <summary>若本音符被某主动技能附魔，则指向其运行时；结算时回调通知完成数。</summary>
    [HideInInspector] public ActiveSkillRuntime charmOwner;

    /// <summary>本音符是否曾经被附魔（即使结算后 charmOwner 被置空也保持 true）。
    /// 供 BattleVisualsController 判断"命中时该音轨角色是否应闪烁"：被附魔的音符命中由施法大狗闪，而非该 lane 角色。</summary>
    [HideInInspector] public bool wasCharmed = false;

    /// <summary>连点音符被附魔的数字个数（= 本次分配时消费的名额；≤ chainTapRequired）。
    /// 每个数字命中消耗 1 个：触发 1 次技能效果；耗尽后该音符恢复原色、后续数字不再触发效果（见附魔规则.md P2）。</summary>
    [HideInInspector] public int enchantedHitCount = 0;

    public bool CoversLane(int targetLane)
    {
        return targetLane >= lane && targetLane < lane + laneSpan;
    }

    /// <summary>
    /// 触发命中反馈动画（由 NoteSpawner 调用）。
    /// </summary>
    public void Hit(string rank)
    {
        if (isHit) return;
        isHit = true;
        finalRank = rank;

        // 附魔音符结算：非 MISS 即算完成（PASS 也算命中）
        if (charmOwner != null)
        {
            charmOwner.OnCharmedNoteResolved(this, rank != "MISS");
            charmOwner = null;
        }

        NoteMover mover = GetComponent<NoteMover>();
        if (mover != null)
        {
            mover.PlayHitAnimation(rank);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 被主动技能（清屏）直接清除：视为最佳命中（PERFECT），播放大白消失表现。
    /// 不注入常规 OnJudge 计分流程（由技能运行时单独弹评价 / 充能），仅做表现与清理。
    /// </summary>
    public void MarkClearedBySkill()
    {
        if (isHit) return;
        isHit = true;
        finalRank = "PERFECT";
        // 若恰为某附魔技能对象，先结算（一般不会同时发生）
        if (charmOwner != null)
        {
            charmOwner.OnCharmedNoteResolved(this, true);
            charmOwner = null;
        }
        NoteMover mover = GetComponent<NoteMover>();
        if (mover != null) mover.PlaySkillClear();
        else Destroy(gameObject);
    }

    /// <summary>
    /// 触发 Miss（漏击）消失反馈：先快速缩小，缩完再由 NoteSpawner 出 MISS。
    /// </summary>
    public void Miss()
    {
        if (isHit) return;
        isHit = true;
        finalRank = "MISS";

        // 附魔音符结算：MISS 不计完成，但已消费一个附魔名额
        if (charmOwner != null)
        {
            charmOwner.OnCharmedNoteResolved(this, false);
            charmOwner = null;
        }

        NoteMover mover = GetComponent<NoteMover>();
        if (mover != null)
        {
            mover.BeginMiss();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public bool IsChainTapExpired(float songTime)
    {
        NoteMover mover = GetComponent<NoteMover>();
        return isChainTap && chainTapWaiting && mover != null && mover.ChainTapDeadline >= 0f
            && songTime > mover.ChainTapDeadline;
    }

    /// <summary>
    /// 连点音符的一次有效命中。
    /// 递减时机已挪到「撤退最远点」（由 NoteMover.Update 回调 OnChainRetreatFarthest），
    /// 这里只负责：显示当前数 + 命中切 Select（D，非最后），或最后一下（当前数=1）走普通命中反馈（E，CLEAR 完成）。
    /// 评分用 rank 仍由 NoteSpawner 按「当前未递减数」计算，故本方法不改分。
    /// </summary>
    public bool RegisterChainTapHit(float songTime, string rank)
    {
        if (!isChainTap || isHit || chainTapRemaining <= 0) return false;

        NoteMover mover = GetComponent<NoteMover>();   // 方法内只声明一次，E/D 两个分支共用
        int displayR = chainTapRemaining;   // 本次命中显示的当前数（递减在此处不做）

        // E：最后一下（当前数 == 1）命中 → 不后退，走普通命中反馈（CLEAR 完成：Select 显 + 放大淡出销毁）
        if (displayR == 1)
        {
            isHit = true;
            finalRank = rank;
            if (charmOwner != null)
            {
                // 这最后一击若仍属附魔（enchantedHitCount>0），先单独计一次效果
                if (wasCharmed && enchantedHitCount > 0)
                {
                    charmOwner.OnCharmedNoteDigitHit(this);
                    enchantedHitCount = 0;
                }
                // 连点效果已全部由逐数字 OnCharmedNoteDigitHit 处理，这里只结算（移除+结算技能），不再重复计效果
                charmOwner.OnCharmedNoteResolved(this, true, false);
                charmOwner = null;
            }
            if (mover != null) mover.PlayHitAnimation(rank);
            else Destroy(gameObject);
            return true;
        }

        // D：非最后命中 → 记录等待、显示当前数 + 切 Select，开始撤退；最远点由 mover 回调递减并切回非命中态
        chainTapWaiting = true;
        finalRank = rank;
        if (mover != null)
            mover.RegisterChainTapHit(displayR, chainTapRequired, songTime);
        // P2：逐附魔数字触发效果 + 换皮数字递减；附魔次数耗尽则恢复原色（后续数字不再触发效果）
        if (charmOwner != null && wasCharmed && enchantedHitCount > 0)
        {
            charmOwner.OnCharmedNoteDigitHit(this);
            enchantedHitCount--;
            if (mover != null)
            {
                mover.UpdateChainEnchantDigit(displayR);   // 显示当前数皮肤
                if (enchantedHitCount <= 0) mover.RestoreChainBase();
            }
        }
        return false;
    }

    /// <summary>
    /// 连点音符撤退到最远点（1 单位）时由 NoteMover 回调：数字 -1，返回新的剩余次数。
    /// 仅在此处递减，保证「命中显示当前数 → 撤退 → 最远点显示-1 → 回来」的循环节奏。
    /// </summary>
    public int OnChainRetreatFarthest()
    {
        chainTapRemaining = Mathf.Max(0, chainTapRemaining - 1);
        return chainTapRemaining;
    }
}
