using UnityEngine;

/// <summary>
/// 轨道指示灯（角色前的竖直线）。
/// 按键/触摸时短暂发光并放大。
/// </summary>
public class LaneIndicator : MonoBehaviour
{
    [Header("显示")]
    public Renderer targetRenderer;
    public Material activeMaterial;
    public Material idleMaterial;

    [Header("发光动画")]
    public float flashDuration = 0.12f;
    public float popScale = 3.0f;

    private float timer = 0f;
    private bool isFlashing = false;
    private Vector3 baseScale;

    void Start()
    {
        baseScale = transform.localScale;
    }

    void Update()
    {
        if (isFlashing)
        {
            timer -= Time.deltaTime;

            float t = Mathf.Clamp01(timer / flashDuration);
            // 只在高度方向弹起，保持横向长度不变
            Vector3 flashScale = baseScale;
            flashScale.y *= popScale;
            transform.localScale = Vector3.Lerp(baseScale, flashScale, t);

            if (timer <= 0f)
            {
                SetIdle();
            }
        }
    }

    public void Flash()
    {
        if (targetRenderer == null) return;

        timer = flashDuration;
        isFlashing = true;
        targetRenderer.material = activeMaterial;
        Vector3 flashScale = baseScale;
        flashScale.y *= popScale;
        transform.localScale = flashScale;
    }

    private void SetIdle()
    {
        if (targetRenderer == null) return;

        isFlashing = false;
        transform.localScale = baseScale;

        if (idleMaterial != null)
            targetRenderer.material = idleMaterial;
    }
}
