using UnityEngine;

/// <summary>
/// 移动端触摸输入管理器。
/// 从主相机发射射线，检测落在哪个 TouchZone 上，触发对应玩家的轨道输入。
/// 同时支持鼠标点击，方便在编辑器里测试。
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

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return;

        // 处理触摸
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Began)
            {
                ProcessPointer(touch.position);
            }
        }

        // 编辑器/PC 测试：鼠标左键
        if (Input.GetMouseButtonDown(0))
        {
            ProcessPointer(Input.mousePosition);
        }
    }

    private void ProcessPointer(Vector2 screenPos)
    {
        if (gameCamera == null) gameCamera = Camera.main;
        if (gameCamera == null) return;

        Ray ray = gameCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, touchLayer))
        {
            TouchZone zone = hit.collider.GetComponent<TouchZone>();
            if (zone == null) zone = hit.collider.GetComponentInParent<TouchZone>();

            if (zone != null)
            {
                if (logTouches)
                    Debug.Log($"Touch side={zone.side} lane={zone.lane}");

                if (zone.side == 0 && leftSpawner != null)
                {
                    leftSpawner.TriggerLaneInput(zone.lane);
                }
                else if (zone.side == 1 && rightSpawner != null)
                {
                    rightSpawner.TriggerLaneInput(zone.lane);
                }
            }
        }
    }
}
