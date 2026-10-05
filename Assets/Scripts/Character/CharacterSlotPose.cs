using UnityEngine;

/// <summary>
/// 角色槽位固定位姿（挂到场景预存的 Band/Cube 占位物体上）。
/// CharacterCubeMarker 实例化 Spine 模型时读取同物体上的本组件，把偏移叠加到模型本地 Transform。
/// 位姿跟着 lane 槽位走，换角色、换位置时不重置。
/// </summary>
public class CharacterSlotPose : MonoBehaviour
{
    [Tooltip("实例化 modelPrefab 后在 prefab 本地位置基础上追加的偏移。不同 lane 可单独调")]
    public Vector3 positionOffset;

    [Tooltip("欧拉角偏移，叠加在 prefab 本地旋转之上")]
    public Vector3 rotationOffset;

    [Tooltip("缩放倍数，与 prefab 本地缩放相乘")]
    public Vector3 scaleOffset = Vector3.one;
}
