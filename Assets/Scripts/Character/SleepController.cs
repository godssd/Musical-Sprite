using UnityEngine;

/// <summary>
/// 沉睡控制器。被清屏技能命中后「释放者自身」陷入沉睡：灰方块 + 黑灯、禁命中 / 主动技能 / 被动技能，
/// 持续期间除非被解除。三种解除方式：①驱散类效果（Dispel）②持续时间到自动解除（Update 计时）③队伍生命值被扣除（BreakSleepOnDamage）。
/// 控制免疫可抵抗陷入沉睡。挂载：由 CharacterBattleSystem 自动补建；通过 Instance / EnsureInstance 访问。
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

    /// <summary>驱散：清除某侧持续性控制效果（当前实现=解除沉睡）。由驱散类技能（附魔音符成功结算）调用。
    /// 与 Wake 的区别：语义为"被外部效果解除"，日志不同；未来可在此扩展清除其它控制效果（如眩晕/束缚）。</summary>
    public void Dispel(int side)
    {
        if (side < 0 || side > 1) return;
        if (sleeping[side])
        {
            Debug.Log($"[Sleep] side{side} 被驱散，解除控制效果");
            Wake(side);
        }
    }

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
        // 角色方块：同一侧所有角色（含玩家自身 cube）变灰 + 缩小 + 黑灯
        for (int lane = -1; lane < 4; lane++)
        {
            var m = (lane < 0) ? CharacterCubeMarker.GetAt(side, -1) : CharacterCubeMarker.GetAt(side, lane);
            if (m != null) m.ApplySleepVisual(on);
        }
        // 提示灯：该侧全部 4 条音轨指示灯变黑且无法发光（睡眠异常(表现)时）
        var bvc = FindFirstObjectByType<BattleVisualsController>();
        if (bvc != null && bvc.indicators != null)
        {
            for (int lane = 0; lane < 4; lane++)
            {
                int idx = side * 4 + lane;
                if (idx >= 0 && idx < bvc.indicators.Length && bvc.indicators[idx] != null)
                    bvc.indicators[idx].SetSleep(on);
            }
        }
    }
}
