using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 处理红方（本地玩家）的触摸 / 鼠标输入：从屏幕坐标发射射线命中 TouchZone，
/// 再调用 leftSpawner.TriggerLaneDown / TriggerLaneUp 触发对应轨道。
///
/// 蓝方输入来自网络，不在此处理。
/// 设计要点：
/// - 每个手指（触摸）或鼠标（编辑器测试用固定 id = -1）记录当前按下的 lane；
/// - 手指在轨道间滑动时，先释放旧轨道再按下新轨道；
/// - 同一条轨道可被多指按住，用 laneHoldCount 计数，最后一指抬起才下发 TriggerLaneUp。
/// </summary>
public class TouchInputManager : MonoBehaviour
{
    [Tooltip("用于屏幕坐标转射线（留空自动取 Camera.main）")]
    public Camera gameCamera;

    [Tooltip("红方发射器（本地玩家）")]
    public NoteSpawner leftSpawner;

    [Tooltip("触控区所在层（默认 Layer 6 = TouchZone）")]
    public LayerMask touchLayer = 1 << 6;

    [Tooltip("是否在 Console 打印触摸调试")]
    public bool logTouches = false;

    // 每个手指当前按下的 lane（-1 表示空闲），key 为 fingerId（鼠标固定为 -1）
    private Dictionary<int, int> fingerLane = new Dictionary<int, int>();
    // 每条轨道当前被按住的指头数（用于多指 / 滑动场景，避免误触发抬起）
    private int[] laneHoldCount = new int[4];
    private RaycastHit[] hitBuffer = new RaycastHit[8];

    void Start()
    {
        if (gameCamera == null) gameCamera = Camera.main;
    }

    void Update()
    {
        if (leftSpawner == null) return;
        if (gameCamera == null) gameCamera = Camera.main;
        if (gameCamera == null) return;

        // 1. 处理所有触摸
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            int lane = LaneAtScreenPoint(t.position);
            UpdateFinger(t.fingerId, lane, t.phase);
        }

        // 2. 鼠标（PC / 编辑器测试用）：当作一个固定 id = -1 的指针
        if (Input.mousePresent)
        {
            if (Input.GetMouseButton(0) || Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0))
            {
                int mlane = LaneAtScreenPoint(Input.mousePosition);
                TouchPhase mp;
                if (Input.GetMouseButtonDown(0)) mp = TouchPhase.Began;
                else if (Input.GetMouseButtonUp(0)) mp = TouchPhase.Ended;
                else mp = fingerLane.ContainsKey(-1) ? TouchPhase.Moved : TouchPhase.Stationary;
                UpdateFinger(-1, mlane, mp);
            }
            else if (fingerLane.ContainsKey(-1))
            {
                // 鼠标没按但状态里还记着，强制抬起
                UpdateFinger(-1, -1, TouchPhase.Ended);
            }
        }
    }

    /// <summary>
    /// 屏幕坐标 → 命中的 TouchZone.lane（未命中返回 -1）。
    /// </summary>
    private int LaneAtScreenPoint(Vector2 screen)
    {
        Ray ray = gameCamera.ScreenPointToRay(screen);
        int hits = Physics.RaycastNonAlloc(ray, hitBuffer, 200f, touchLayer);
        for (int i = 0; i < hits; i++)
        {
            TouchZone tz = hitBuffer[i].collider.GetComponent<TouchZone>();
            if (tz != null) return tz.lane;
        }
        return -1;
    }

    private void UpdateFinger(int fingerId, int lane, TouchPhase phase)
    {
        if (phase == TouchPhase.Began)
        {
            if (lane >= 0 && lane < 4)
            {
                fingerLane[fingerId] = lane;
                laneHoldCount[lane]++;
                leftSpawner.TriggerLaneDown(lane);
                if (logTouches) Debug.Log($"[Touch] finger {fingerId} down lane {lane}");
            }
        }
        else if (phase == TouchPhase.Moved || phase == TouchPhase.Stationary)
        {
            if (fingerLane.TryGetValue(fingerId, out int cur))
            {
                if (lane != cur)
                {
                    // 滑到了另一条轨道：释放旧轨道，按下新轨道
                    ReleaseLane(cur);
                    if (lane >= 0 && lane < 4)
                    {
                        fingerLane[fingerId] = lane;
                        laneHoldCount[lane]++;
                        leftSpawner.TriggerLaneDown(lane);
                        if (logTouches) Debug.Log($"[Touch] finger {fingerId} move -> lane {lane}");
                    }
                    else
                    {
                        fingerLane.Remove(fingerId);
                    }
                }
            }
            else if (lane >= 0 && lane < 4)
            {
                // 之前在空白处，现在进入某条轨道
                fingerLane[fingerId] = lane;
                laneHoldCount[lane]++;
                leftSpawner.TriggerLaneDown(lane);
            }
        }
        else if (phase == TouchPhase.Ended || phase == TouchPhase.Canceled)
        {
            ReleaseFinger(fingerId);
        }
    }

    private void ReleaseFinger(int fingerId)
    {
        if (fingerLane.TryGetValue(fingerId, out int lane))
        {
            ReleaseLane(lane);
            fingerLane.Remove(fingerId);
        }
    }

    private void ReleaseLane(int lane)
    {
        if (lane >= 0 && lane < 4)
        {
            laneHoldCount[lane] = Mathf.Max(0, laneHoldCount[lane] - 1);
            if (laneHoldCount[lane] == 0)
            {
                leftSpawner.TriggerLaneUp(lane);
                if (logTouches) Debug.Log($"[Touch] lane {lane} up");
            }
        }
    }
}
