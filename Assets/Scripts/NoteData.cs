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
    /// - Hold：长按音符，由「起始音符(head)」与「结束音符(tail)」用连接线相连。
    ///   起始音符圆心抵达判定线时刻 = time；结束音符抵达判定线时刻 = time + holdDuration。
    ///   起始在 lane，结束在 holdEndLane（可 != lane，形成跨轨滑条）。
    /// </summary>
    public enum NoteType { Tap = 0, Hold = 1 }

    [Tooltip("该音符应该被击中的时间（秒，从歌曲开头算）。对 Hold 而言是起始音符(head)抵达判定线的时刻")]
    public float time;

    [Tooltip("轨道编号：0-3（Hold 时为起始音符所在轨道）")]
    public int lane;

    [Tooltip("0=左玩家接，1=右玩家接")]
    public int side;

    [Tooltip("音符类型：Tap=单击，Hold=长按（按住并沿连接线滑动到结束音符）")]
    public NoteType type = NoteType.Tap;

    [Tooltip("仅 Hold 使用：长按持续时长（秒）。结束音符(tail)抵达判定线时刻 = time + holdDuration")]
    public float holdDuration = 0f;

    [Tooltip("仅 Hold 使用：结束音符(tail)所在轨道。可与 lane 不同，形成跨轨滑条")]
    public int holdEndLane = 0;
}
