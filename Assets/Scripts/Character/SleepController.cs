using UnityEngine;

/// <summary>
/// 沉睡控制器。被清屏技能命中后敌方陷入沉睡：灰方块 + 黑灯、禁命中 / 技能 / 被动，
/// 受击解除，控制免疫可抵抗。挂载：由 CharacterBattleSystem 自动补建；通过 Instance 访问。
/// </summary>
public class SleepController : MonoBehaviour
{
    public static SleepController Instance { get; private set; }

    private bool[] sleeping = new bool[2] { false, false };
    private float[] timer = new float[2] { 0f, 0f };
    private bool[] immune = new bool[2] { false, false };

    void Awake() { if (Instance == null) Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>取得唯一实例；若 Instance 尚未就绪（Awake 延迟）则尝试查找并赋值，确保沉睡可靠生效。</summary>
    public static SleepController EnsureInstance()
    {
        if (Instance == null) Instance = FindFirstObjectByType<SleepController>();
        return Instance;
    }

    void Update()
    {
        for (int s = 0; s < 2; s++)
        {
            if (sleeping[s] && timer[s] > 0f)
            {
                timer[s] -= Time.deltaTime;
                if (timer[s] <= 0f) Wake(s);
            }
        }
    }

    /// <summary>让某侧沉睡 seconds 秒（控制免疫则抵抗）。</summary>
    public void Sleep(int side, float seconds)
    {
        if (side < 0 || side > 1) return;
        if (immune[side]) { Debug.Log($"[Sleep] side{side} 控制免疫，抵抗沉睡"); return; }
        sleeping[side] = true;
        timer[side] = Mathf.Max(0.1f, seconds);
        ApplySleepVisual(side, true);
        Debug.Log($"[Sleep] side{side} 陷入沉睡 {seconds}s");
    }

    public void Wake(int side)
    {
        if (side < 0 || side > 1) return;
        sleeping[side] = false;
        timer[side] = 0f;
        ApplySleepVisual(side, false);
    }

    public bool IsSideSleeping(int side) => side >= 0 && side <= 1 && sleeping[side];

    /// <summary>受击解除沉睡（ScoreManager.TakeDamage 调用）。</summary>
    public void BreakSleepOnDamage(int side)
    {
        if (side >= 0 && side <= 1 && sleeping[side])
        {
            Debug.Log($"[Sleep] side{side} 受击，解除沉睡");
            Wake(side);
        }
    }

    /// <summary>控制免疫：免疫时清屏 / 控制技能无法使其沉睡。</summary>
    public void SetImmune(int side, bool v)
    {
        if (side >= 0 && side <= 1) immune[side] = v;
    }

    private void ApplySleepVisual(int side, bool on)
    {
        for (int lane = -1; lane < 4; lane++)
        {
            var m = (lane < 0) ? CharacterCubeMarker.GetAt(side, -1) : CharacterCubeMarker.GetAt(side, lane);
            if (m != null) m.ApplySleepVisual(on);
        }
    }
}
