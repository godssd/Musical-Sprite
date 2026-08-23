using UnityEngine;

/// <summary>
/// 对手按键的视觉反馈。
/// 当 AI 按下某条轨道时，让该轨道对应的触控区短暂亮起。
/// </summary>
public class OpponentVisualFeedback : MonoBehaviour
{
    [Header("引用")]
    public OpponentInput opponentInput;
    public NoteSpawner rightSpawner;
    public TouchZoneBuilder touchZoneBuilder;

    [Header("反馈材质")]
    [Tooltip("对手按键时触控区临时显示的颜色材质")]
    public Material feedbackMaterial;

    [Tooltip("亮起持续时间")]
    public float flashDuration = 0.15f;

    private Renderer[,] zoneRenderers;
    private float[,] flashTimers;

    void Start()
    {
        if (opponentInput != null)
            opponentInput.OnPressLane += OnOpponentPress;
    }

    void Update()
    {
        if (zoneRenderers == null) return;

        int lanes = zoneRenderers.GetLength(1);
        for (int lane = 0; lane < lanes; lane++)
        {
            if (flashTimers[1, lane] > 0f && zoneRenderers[1, lane] != null)
            {
                flashTimers[1, lane] -= Time.deltaTime;
                if (flashTimers[1, lane] <= 0f)
                {
                    zoneRenderers[1, lane].enabled = false;
                }
            }
        }
    }

    private void OnOpponentPress(int lane)
    {
        if (zoneRenderers == null) CacheRenderers();
        if (zoneRenderers == null || zoneRenderers.GetLength(1) <= lane) return;

        Renderer rend = zoneRenderers[1, lane];
        if (rend != null)
        {
            rend.enabled = true;
            if (feedbackMaterial != null)
                rend.material = feedbackMaterial;
            flashTimers[1, lane] = flashDuration;
        }
    }

    private void CacheRenderers()
    {
        // 简单实现：遍历 TouchZoneBuilder 生成的子物体
        if (touchZoneBuilder == null) return;

        // 预期 TouchZoneBuilder 生成一个名为 TouchZones_Side1 的根物体
        Transform root = touchZoneBuilder.transform.Find("TouchZones_Side1");
        if (root == null) return;

        zoneRenderers = new Renderer[2, rightSpawner.laneCount];
        flashTimers = new float[2, rightSpawner.laneCount];

        foreach (Transform child in root)
        {
            TouchZone tz = child.GetComponent<TouchZone>();
            Renderer rend = child.GetComponent<Renderer>();
            if (tz != null && rend != null)
            {
                zoneRenderers[tz.side, tz.lane] = rend;
                rend.enabled = false;
            }
        }
    }

    void OnDestroy()
    {
        if (opponentInput != null)
            opponentInput.OnPressLane -= OnOpponentPress;
    }
}
