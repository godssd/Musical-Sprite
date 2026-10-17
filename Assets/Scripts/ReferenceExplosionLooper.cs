using UnityEngine;

/// <summary>
/// 参考爆炸强制循环器（仅用于 VFX 沙盒观看）。
///
/// 【为什么需要】参考包 prefab 的 ParticleSystem.main.loop 在多次循环后 burst 不再触发，
/// 实测表现为「播 1~2 次就停」。与其依赖 loop，不如到点强制 Stop→Play 重启一轮。
///
/// 【用法】VFX/4) 放置的 ReferenceExplosion 实例会自动挂上本组件；
///        进 Play 后每隔 loopInterval 秒重启一次爆炸，方便反复观察。
///
/// ⛔ 仅在 Play 中工作（Application.isPlaying 守卫），编辑态什么都不做，避免污染场景。
/// </summary>
public class ReferenceExplosionLooper : MonoBehaviour
{
    [Tooltip("每隔多少秒重播一次（建议略大于参考爆炸的完整时长，避免打断）")]
    public float loopInterval = 4.0f;

    private float _timer;

    void Start()
    {
        if (!Application.isPlaying) return;
        _timer = Mathf.Max(0.1f, loopInterval);
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            _timer = Mathf.Max(0.1f, loopInterval);
            Restart();
        }
    }

    /// <summary>强制重启一轮爆炸（也可手动调用）。</summary>
    [ContextMenu("重播一次")]
    public void Restart()
    {
        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }
    }
}
