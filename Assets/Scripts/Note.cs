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
    [HideInInspector] public bool isHit = false;
    [HideInInspector] public bool isVisible = false;

    [HideInInspector] public string finalRank = "MISS";

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
}
