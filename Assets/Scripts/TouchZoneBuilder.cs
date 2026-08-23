using UnityEngine;

/// <summary>
/// 运行时自动生成触控区域。
/// 根据 NoteSpawner 的判定线和轨道数量，在黄线到屏幕边缘之间生成透明 BoxCollider 触摸区。
/// </summary>
public class TouchZoneBuilder : MonoBehaviour
{
    [Header("左右发射器")]
    public NoteSpawner leftSpawner;
    public NoteSpawner rightSpawner;

    [Header("触控区参数")]
    [Tooltip("左玩家触控区延伸到多左（X 坐标）")]
    public float leftEdgeX = -8f;

    [Tooltip("右玩家触控区延伸到多右（X 坐标）")]
    public float rightEdgeX = 8f;

    [Tooltip("触控区 Y 高度")]
    public float touchHeight = 1f;

    [Tooltip("是否生成时显示半透明调试用材质")]
    public bool showDebugVisual = false;

    [Tooltip("调试用材质（可选）")]
    public Material debugMaterial;

    [Tooltip("触控区域所在的 Layer（必须是射线能检测到的 Layer）")]
    public int touchLayer = 6;

    void Start()
    {
        if (leftSpawner != null) BuildZones(leftSpawner, leftEdgeX);
        if (rightSpawner != null) BuildZones(rightSpawner, rightEdgeX);
    }

    private void BuildZones(NoteSpawner spawner, float edgeX)
    {
        if (spawner.hitPoint == null) return;

        float hitX = spawner.hitPoint.position.x;
        float centerX = (hitX + edgeX) * 0.5f;
        float sizeX = Mathf.Abs(edgeX - hitX);
        float laneSpacing = spawner.laneSpacing;

        GameObject root = new GameObject($"TouchZones_Side{spawner.side}");
        root.transform.SetParent(transform);
        root.transform.position = Vector3.zero;

        for (int lane = 0; lane < spawner.laneCount; lane++)
        {
            float z = (lane - (spawner.laneCount - 1) * 0.5f) * laneSpacing;
            float zCenter = spawner.hitPoint.position.z + z;

            GameObject zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            zone.name = $"TouchZone_Side{spawner.side}_Lane{lane}";
            zone.layer = touchLayer;
            zone.transform.SetParent(root.transform);
            zone.transform.position = new Vector3(centerX, 0.5f, zCenter);
            zone.transform.localScale = new Vector3(sizeX, touchHeight, laneSpacing * 0.95f);

            // 移除默认 Cube 的材质显示
            MeshRenderer rend = zone.GetComponent<MeshRenderer>();
            if (showDebugVisual && debugMaterial != null)
            {
                rend.material = debugMaterial;
            }
            else
            {
                rend.enabled = false;
            }

            TouchZone tz = zone.AddComponent<TouchZone>();
            tz.side = spawner.side;
            tz.lane = lane;
        }
    }
}
