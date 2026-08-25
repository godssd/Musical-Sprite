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
    /// 连点音符的一次有效命中。未到 0 时保留在 activeNotes 中，归零时才结束。
    /// </summary>
    public bool RegisterChainTapHit(float songTime, string rank)
    {
        if (!isChainTap || isHit || chainTapRemaining <= 0) return false;

        chainTapWaiting = true;
        chainTapRemaining = Mathf.Max(0, chainTapRemaining - 1);
        finalRank = rank;

        NoteMover mover = GetComponent<NoteMover>();
        if (mover != null)
            mover.RegisterChainTapHit(chainTapRemaining, chainTapRequired, songTime);

        if (chainTapRemaining <= 0)
        {
            isHit = true;
            if (mover != null) mover.CompleteChainTap();
            return true;
        }
        return false;
    }
}
