using UnityEngine;

/// <summary>
/// 触摸区域标识。挂载在每个触控区上，标明它属于哪一侧的哪条轨道。
/// </summary>
public class TouchZone : MonoBehaviour
{
    [Tooltip("0 = 左玩家触控区，1 = 右玩家触控区")]
    public int side;

    [Tooltip("轨道编号：0-3")]
    public int lane;
}
