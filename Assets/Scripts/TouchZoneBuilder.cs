using UnityEngine;

/// <summary>
/// 场景搭建时，为红方（本地玩家，leftSpawner）的 4 条轨道生成触控区。
/// 每个触控区是一个位于 TouchZone 层（默认 Layer 6）的不可见 BoxCollider 方块，
/// 位于红方演奏区的**左/前边缘**（玩家端），供 TouchInputManager 射线检测。
///
/// 联机对战下蓝方（rightSpawner）由网络驱动，不生成触控区。
///
/// 默认参数贴合 SceneSetupWindow 搭出来的红方场地：红方场 X∈[-8, 0]、Z∈[-3.75, 3.75]，
/// 4 条轨道 Z 分别为 -2.25 / -0.75 / +0.75 / +2.25。触控区紧贴 X=-8 的玩家端边缘，
/// 大小约 2×1.3，方便手指点击而不挡视线。
/// </summary>
public class TouchZoneBuilder : MonoBehaviour
{
    [Tooltip("红方发射器（本地玩家的 4 条轨道）")]
    public NoteSpawner leftSpawner;

    [Tooltip("触控区所在的层（对应 ProjectSettings 里 Layer 6 = TouchZone）")]
    public int touchLayer = 6;

    [Tooltip("单个触控区的高度（Y 方向厚度，越大越容易在俯视下被射线命中）")]
    public float touchHeight = 1.5f;

    [Tooltip("是否显示调试方块（半透明），便于确认触控区位置")]
    public bool showDebugVisual = true;

    [Tooltip("调试用半透明材质（如 M_TouchDebug）")]
    public Material debugMaterial;

    [Tooltip("触控区在 X 方向的中心（默认 -7，紧贴红方场地 X=-8 的玩家端边缘）")]
    public float centerX = -7f;

    [Tooltip("触控区在 X 方向的半宽（默认 1，即 X 方向跨度 2 个单位，呈小垫子）")]
    public float halfWidth = 1f;

    [Tooltip("触控区在 Z 方向的半厚度（默认 0.65，略大于车道间距一半，方便指头按）")]
    public float halfDepth = 0.65f;

    void Awake()
    {
        Build();
    }

    /// <summary>
    /// 为红方每条轨道生成一个触控区。可被工具重复调用（先在 SetupScene 清理旧物体）。
    /// </summary>
    public void Build()
    {
        if (leftSpawner == null) return;

        int count = leftSpawner.laneCount;
        float spacing = leftSpawner.laneSpacing;

        for (int lane = 0; lane < count; lane++)
        {
            // 与 NoteSpawner 完全一致的轨道 Z 坐标
            float z = (lane - (count - 1) * 0.5f) * spacing;

            GameObject zone = new GameObject($"TouchZone_Lane{lane}");
            zone.transform.SetParent(transform, false);
            zone.transform.position = new Vector3(centerX, touchHeight * 0.5f, z);
            zone.transform.localScale = new Vector3(halfWidth * 2f, touchHeight, halfDepth * 2f);
            zone.layer = touchLayer;

            BoxCollider col = zone.AddComponent<BoxCollider>();
            col.isTrigger = true;

            TouchZone tz = zone.AddComponent<TouchZone>();
            tz.lane = lane;

            if (showDebugVisual && debugMaterial != null)
            {
                MeshRenderer mr = zone.AddComponent<MeshRenderer>();
                mr.material = debugMaterial;
            }
        }
    }
}
