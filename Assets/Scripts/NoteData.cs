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
    [Tooltip("该音符应该被击中的时间（秒，从歌曲开头算）")]
    public float time;

    [Tooltip("轨道编号：0-3")]
    public int lane;

    [Tooltip("0=左玩家接，1=右玩家接")]
    public int side;
}
