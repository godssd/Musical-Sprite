using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 移动端触摸输入管理器。
/// 从主相机发射射线，检测落在哪个 TouchZone 上，触发对应玩家的轨道输入。
/// 同时支持鼠标点击，方便在编辑器里测试。
///
/// 与长按音符配合：按下(Began/Down) -> TriggerLaneDown，松开(Ended/Up) -> TriggerLaneUp。
/// 这样玩家可按住轨道并在跨轨长按时沿连接线滑动跟随。
/// </summary>
public class TouchInputManager : MonoBehaviour
{
    [Header("引用")]
    public Camera gameCamera;
    public NoteSpawner leftSpawner;
    public NoteSpawner rightSpawner;

    [Header("射线层")]
    [Tooltip("触控区域所在的 Layer。需要在 Inspector 里设置 TouchZone 的 Layer")]
    public LayerMask touchLayer;

    [Header("调试")]
    [Tooltip("是否在控制台打印触摸信息")]
    public bool logTouches = false;

    // 跟踪每个活跃触摸当前所在轨道，松开时正确上报对应轨道
    private Dictionary<int, int> touchLane = new Dictionary<int, int>();
    private int mouseLane = -1;

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return;

        if (gameCamera == null) gameCamera = Camera.main;

        // 处理触摸
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Began)
            {
                int lane = ProcessPointerDown(touch.position);
                touchLane[touch.fingerId] = lane;
            }
            else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                if (touchLane.TryGetValue(touch.fingerId, out int lane))
                {
                    ProcessPointerUp(lane);
                    touchLane.Remove(touch.fingerId);
                }
            }
        }

        // 编辑器/PC 测试：鼠标左键
        if (Input.GetMouseButtonDown(0))
        {
            mouseLane = ProcessPointerDown(Input.mousePosition);
        }
        else if (Input.GetMouseButtonUp(0))
        {
            if (mouseLane >= 0)
            {
                ProcessPointerUp(mouseLane);
                mouseLane = -1;
            }
        }
    }

    private int ProcessPointerDown(Vector2 screenPos)
    {
        if (gameCamera == null) return -1;

        Ray ray = gameCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, touchLayer))
        {
            TouchZone zone = hit.collider.GetComponent<TouchZone>();
            if (zone == null) zone = hit.collider.GetComponentInParent<TouchZone>();

            if (zone != null)
            {
                if (logTouches)
                    Debug.Log($"Touch Down side={zone.side} lane={zone.lane}");

                if (zone.side == 0 && leftSpawner != null)
                    leftSpawner.TriggerLaneDown(zone.lane);
                else if (zone.side == 1 && rightSpawner != null)
                    rightSpawner.TriggerLaneDown(zone.lane);
                return zone.lane;
            }
        }
        return -1;
    }

    private void ProcessPointerUp(int lane)
    {
        // lane 仅用于本地跟踪；具体 side 由 spawner 自身记录，这里分别对两侧下发松开
        if (leftSpawner != null) leftSpawner.TriggerLaneUp(lane);
        if (rightSpawner != null) rightSpawner.TriggerLaneUp(lane);
    }
}
