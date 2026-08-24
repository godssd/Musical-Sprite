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

    // 跟踪每个活跃触摸当前所在侧/轨道，拖拽换轨时自动松开旧轨、按下新轨
    private Dictionary<int, int> touchLane = new Dictionary<int, int>();
    private Dictionary<int, int> touchSide = new Dictionary<int, int>();
    private int mouseLane = -1;
    private int mouseSide = -1;

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
                if (TryResolvePointer(touch.position, out int side, out int lane))
                {
                    TriggerLaneDown(side, lane);
                    touchSide[touch.fingerId] = side;
                    touchLane[touch.fingerId] = lane;
                }
            }
            else if (touch.phase == TouchPhase.Moved)
            {
                bool hasOldSide = touchSide.TryGetValue(touch.fingerId, out int oldSide);
                bool hasOldLane = touchLane.TryGetValue(touch.fingerId, out int oldLane);

                if (TryResolvePointer(touch.position, out int newSide, out int newLane))
                {
                    if (hasOldSide && hasOldLane && (newSide != oldSide || newLane != oldLane))
                    {
                        TriggerLaneUp(oldSide, oldLane);
                        TriggerLaneDown(newSide, newLane);
                        touchSide[touch.fingerId] = newSide;
                        touchLane[touch.fingerId] = newLane;
                    }
                    else if (hasOldSide && !hasOldLane)
                    {
                        // 之前拖出 TouchZone，现在重新进入：直接按下
                        TriggerLaneDown(newSide, newLane);
                        touchSide[touch.fingerId] = newSide;
                        touchLane[touch.fingerId] = newLane;
                    }
                }
                else if (hasOldLane)
                {
                    // 拖到没有 TouchZone 的区域：松开旧轨，但保留 side 跟踪，再次进入时重新按下
                    TriggerLaneUp(oldSide, oldLane);
                    touchLane.Remove(touch.fingerId);
                }
            }
            else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                if (touchSide.TryGetValue(touch.fingerId, out int side) &&
                    touchLane.TryGetValue(touch.fingerId, out int lane))
                {
                    TriggerLaneUp(side, lane);
                    touchSide.Remove(touch.fingerId);
                    touchLane.Remove(touch.fingerId);
                }
            }
        }

        // 编辑器/PC 测试：鼠标左键（支持按住并拖动换轨）
        if (Input.GetMouseButtonDown(0))
        {
            if (TryResolvePointer(Input.mousePosition, out int side, out int lane))
            {
                TriggerLaneDown(side, lane);
                mouseSide = side;
                mouseLane = lane;
            }
        }
        else if (Input.GetMouseButton(0) && mouseLane >= 0)
        {
            if (TryResolvePointer(Input.mousePosition, out int newSide, out int newLane))
            {
                if (newSide != mouseSide || newLane != mouseLane)
                {
                    TriggerLaneUp(mouseSide, mouseLane);
                    TriggerLaneDown(newSide, newLane);
                    mouseSide = newSide;
                    mouseLane = newLane;
                }
            }
            else
            {
                TriggerLaneUp(mouseSide, mouseLane);
                mouseLane = -1;
            }
        }
        else if (Input.GetMouseButton(0) && mouseLane < 0 && mouseSide >= 0)
        {
            // 按住鼠标拖出 TouchZone 后又拖回来：重新按下
            if (TryResolvePointer(Input.mousePosition, out int newSide, out int newLane))
            {
                TriggerLaneDown(newSide, newLane);
                mouseSide = newSide;
                mouseLane = newLane;
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            if (mouseLane >= 0)
            {
                TriggerLaneUp(mouseSide, mouseLane);
                mouseLane = -1;
                mouseSide = -1;
            }
        }
    }

    /// <summary>
    /// 射线检测返回当前指针落在哪个 side/lane 的 TouchZone 上。
    /// 没命中返回 false。
    /// </summary>
    private bool TryResolvePointer(Vector2 screenPos, out int side, out int lane)
    {
        side = -1;
        lane = -1;
        if (gameCamera == null) return false;

        Ray ray = gameCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, touchLayer))
        {
            TouchZone zone = hit.collider.GetComponent<TouchZone>();
            if (zone == null) zone = hit.collider.GetComponentInParent<TouchZone>();

            if (zone != null)
            {
                side = zone.side;
                lane = zone.lane;
                return true;
            }
        }
        return false;
    }

    private void TriggerLaneDown(int side, int lane)
    {
        if (logTouches)
            Debug.Log($"Touch Down side={side} lane={lane}");

        if (side == 0 && leftSpawner != null)
            leftSpawner.TriggerLaneDown(lane);
        else if (side == 1 && rightSpawner != null)
            rightSpawner.TriggerLaneDown(lane);
    }

    private void TriggerLaneUp(int side, int lane)
    {
        if (side == 0 && leftSpawner != null)
            leftSpawner.TriggerLaneUp(lane);
        else if (side == 1 && rightSpawner != null)
            rightSpawner.TriggerLaneUp(lane);
    }
}
