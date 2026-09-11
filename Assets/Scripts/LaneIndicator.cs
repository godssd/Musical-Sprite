using UnityEngine;

/// <summary>
/// 轨道指示灯（角色前的竖直线）。
/// 按键/触摸时短暂发光并放大；按住期间持续亮（Hold），松开即灭。
/// 提示灯渲染队列抬到连轨连线之上（renderQueue=3050），避免被连线遮挡。
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
    private bool isHeld = false;            // 当前是否处于"按住保持亮"状态
    private Vector3 baseScale;
    private bool sleeping = false;   // 沉睡中：变黑且无法发光（由 SleepController 调用 SetSleep）

    void Start()
    {
        baseScale = transform.localScale;
    }

    void Update()
    {
        if (targetRenderer == null) return;
        // 提示灯层级：每帧确保渲染在连轨连线之前（连线默认 Transparent=3000）。
        // 用 material getter 拿到运行时实例再设，不污染材质资产文件。
        targetRenderer.material.renderQueue = 3050;

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
                // flash 结束：仍按住则保持亮，否则回 idle
                if (isHeld) SetHeldLit();
                else SetIdle();
            }
        }
    }

    /// <summary>点按瞬间闪烁（保留原行为）。</summary>
    public void Flash()
    {
        if (targetRenderer == null || sleeping) return;

        timer = flashDuration;
        isFlashing = true;
        targetRenderer.material = activeMaterial;
        Vector3 flashScale = baseScale;
        flashScale.y *= popScale;
        transform.localScale = flashScale;
    }

    /// <summary>按住保持亮 / 松开灭。点按与长按都适用：按下亮、松开灭，长按期间持续亮。</summary>
    public void Hold(bool on)
    {
        if (targetRenderer == null || sleeping) return;

        isHeld = on;
        if (on)
        {
            // 按下立即进入"亮"状态（带一次弹跳反馈）
            timer = flashDuration;
            isFlashing = true;
            targetRenderer.material = activeMaterial;
            Vector3 flashScale = baseScale;
            flashScale.y *= popScale;
            transform.localScale = flashScale;
        }
        else
        {
            SetIdle();
        }
    }

    /// <summary>flash 结束后仍被按住时保持常亮（不灭）。</summary>
    private void SetHeldLit()
    {
        if (targetRenderer == null) return;
        isFlashing = false;
        transform.localScale = baseScale;
        if (activeMaterial != null)
            targetRenderer.material = activeMaterial;
    }

    private void SetIdle()
    {
        if (targetRenderer == null) return;

        isFlashing = false;
        isHeld = false;            // 防御：回 idle 时清掉按住状态
        transform.localScale = baseScale;

        if (idleMaterial != null)
            targetRenderer.material = idleMaterial;
    }

    /// <summary>沉睡：提示灯变黑且无法发光（按下/长按均无效）。由 SleepController 在沉睡时调用。</summary>
    public void SetSleep(bool on)
    {
        if (targetRenderer == null) return;
        sleeping = on;
        if (on)
        {
            isFlashing = false;
            isHeld = false;
            transform.localScale = baseScale;
            if (idleMaterial != null) targetRenderer.material = idleMaterial;
            targetRenderer.material.color = Color.black;   // 变黑色，且因 sleeping 守卫无法再发光
        }
        else
        {
            if (idleMaterial != null) targetRenderer.material = idleMaterial;
        }
    }
}
