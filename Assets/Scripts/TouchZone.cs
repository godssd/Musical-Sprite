using UnityEngine;

/// <summary>
/// 标记一个触控区：玩家在该区域内按下 / 抬起，对应 lane 的按键。
/// 仅供 TouchInputManager 运行时射线检测使用，本身不参与判定。
/// 仅红方（本地玩家）生成触控区；蓝方由网络驱动，不生成。
/// </summary>
public class TouchZone : MonoBehaviour
{
    [Tooltip("该触控区对应的轨道（0=最下，3=最上，与 NoteSpawner.laneCount 一致）")]
    public int lane = 0;
}
