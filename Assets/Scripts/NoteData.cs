using System;
using UnityEngine;

/// <summary>
/// 单个音符数据。
/// side: 0 = 左玩家要接（音符从右侧生成，向左飞）
///       1 = 右玩家要接（音符从左侧生成，向右飞）
/// </summary>
[Serializable]
public class NoteData
{
    /// <summary>
    /// 音符类型：
    /// - Tap：普通单击音符，圆心抵达判定线的时刻 = time。
    /// - Hold：长按音符，由「节点链」相连（至少 2 个节点：head → ... → tail）。
    ///   每个节点 i 的圆心的抵达判定线时刻 = holdTimes[i]；所在轨道 = holdLanes[i]。
    ///   按住后沿连接线从头节点滑到尾节点；每完成「一个节点 → 下一个节点」视为一次 CLEAR。
    ///   兼容旧谱面：若 holdLanes/holdTimes 为空，则退化成 2 节点（head=time/lane，tail=time+holdDuration/holdEndLane）。
    /// - SmallTap：旧版小型点击音符，仅保留用于兼容已有谱面，不再由编辑器创建。
    /// - Linked：连轨音符，固定覆盖相邻两轨，不区分大小圈。
    ///   holdLanes/holdTimes 为空时是双轨点击；有至少两个节点时是双轨长按，
    ///   holdLanes[i] 表示节点覆盖的第一条轨道，节点变化即可形成跨轨。
    /// </summary>
    public enum NoteType { Tap = 0, Hold = 1, SmallTap = 2, Linked = 3 }

    [Tooltip("该音符应该被击中的时间（秒，从歌曲开头算）。对 Hold 而言是起始音符(head)抵达判定线的时刻")]
    public float time;

    [Tooltip("轨道编号：0-3。Linked 表示覆盖的第一条轨道，只能取 0-2，例如 1 表示覆盖轨道 1 和 2")]
    public int lane;

    [Tooltip("0=左玩家接，1=右玩家接")]
    public int side;

    [Tooltip("音符类型：Tap=单击，Hold=单轨长按，SmallTap=旧版小型点击，Linked=固定覆盖相邻两轨的点击/长按")]
    public NoteType type = NoteType.Tap;

    // ===== 旧版单段 Hold 字段（保留以兼容现有谱面/编辑器，新多节点 Hold 优先读取 holdLanes/holdTimes） =====
    [Tooltip("（兼容字段）仅旧版 2 节点 Hold 使用：长按持续时长（秒）。结束音符(tail)抵达判定线时刻 = time + holdDuration")]
    public float holdDuration = 0f;

    [Tooltip("（兼容字段）仅旧版 2 节点 Hold 使用：结束音符(tail)所在轨道。可与 lane 不同，形成跨轨滑条")]
    public int holdEndLane = 0;

    // ===== 新多节点 Hold 字段 =====
    [Tooltip("多节点 Hold：每个节点的所在轨道（与 holdTimes 一一对应）。至少一个 head 与一个 tail，可含中间节点")]
    public int[] holdLanes;

    [Tooltip("多节点 Hold：每个节点圆心抵达判定线的时刻（秒，升序）。holdTimes[0] 必须等于 time（head 时刻）")]
    public float[] holdTimes;

    /// <summary>
    /// 逐节点宽度（与 holdLanes 一一对应）：1 = 普通单轨节点，2 = 连轨节点（覆盖相邻两轨）。
    /// 链接模式中不同节点的宽度可以不同（例如第一个节点是普通单轨，后面的节点是连轨），
    /// 因此宽度不再是整条链共用一个 type，而是每个节点独立记录。
    /// 为 null 时按 type 推断：Linked => 全部 2 轨；否则全部 1 轨（兼容旧谱面）。
    /// </summary>
    public int[] holdLaneSpans;

    /// <summary>
    /// 解析出 Hold 的节点轨道列表。优先使用 holdLanes（多节点）；
    /// 若为空，则退化成旧版 2 节点：[lane, holdEndLane]。
    /// </summary>
    public int[] GetHoldLanes()
    {
        if (holdLanes != null && holdLanes.Length >= 2) return holdLanes;
        return new int[] { lane, holdEndLane };
    }

    /// <summary>
    /// 解析出 Hold 的节点时刻列表（升序）。优先使用 holdTimes（多节点）；
    /// 若为空，则退化成旧版 2 节点：[time, time + holdDuration]。
    /// </summary>
    public float[] GetHoldTimes()
    {
        if (holdTimes != null && holdTimes.Length >= 2) return holdTimes;
        float tail = time + Mathf.Max(0.1f, holdDuration);
        return new float[] { time, tail };
    }

    /// <summary>
    /// 解析出节点 i 的宽度（1=普通单轨，2=连轨覆盖相邻两轨）。
    /// 优先使用 holdLaneSpans（逐节点）；为 null 时按 type 推断（Linked=>2，否则=>1）。
    /// </summary>
    public int GetNodeSpan(int i)
    {
        if (holdLaneSpans != null && i >= 0 && i < holdLaneSpans.Length)
            return holdLaneSpans[i] > 1 ? 2 : 1;
        return type == NoteType.Linked ? 2 : 1;
    }

    /// <summary>
    /// 该 Hold 是否由多节点（≥3 节点）构成。
    /// </summary>
    public bool IsMultiNodeHold()
    {
        return holdLanes != null && holdLanes.Length >= 3 && holdTimes != null && holdTimes.Length >= 3;
    }

    /// <summary>
    /// Linked 有至少两个有效节点时按双轨长按处理；否则按双轨点击处理。
    /// </summary>
    public bool IsLinkedHold()
    {
        return type == NoteType.Linked && holdLanes != null && holdTimes != null
            && holdLanes.Length >= 2 && holdTimes.Length >= 2;
    }
}
