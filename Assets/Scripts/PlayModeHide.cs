using UnityEngine;

/// <summary>
/// 让挂此组件的对象仅在编辑态（Scene 视图）可见，进入 Play 模式即隐藏。
/// 退出 Play 后 Unity 会自动还原编辑态的 active 状态，可随时开回，零风险。
/// 用途：判定线（LeftHitLine / RightHitLine）运行时由提示灯作视觉参照，判定线不再需要显示。
/// 注意：同时会禁用其 BoxCollider，但触摸判定走 TouchInputManager 的 Layer 6 射线，与此 collider 无关，不影响输入。
/// </summary>
[DisallowMultipleComponent]
public class PlayModeHide : MonoBehaviour
{
    void Awake()
    {
        if (Application.isPlaying)
            gameObject.SetActive(false);
    }
}
