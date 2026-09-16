using UnityEngine;

/// <summary>
/// 沉睡控制器。被清屏技能命中后「释放者自身」陷入沉睡：灰方块 + 黑灯、禁命中 / 主动技能 / 被动技能，
/// 持续期间除非被解除。三种解除方式：①驱散类效果（Dispel）②持续时间到自动解除（Update 计时）③队伍生命值被扣除（BreakSleepOnDamage）。
/// 控制免疫可抵抗陷入沉睡。挂载：由 CharacterBattleSystem 自动补建；通过 Instance / EnsureInstance 访问。
/// </summary>
public class SleepController : MonoBehaviour
{
    public static SleepController Instance { get; private set; }

    // lane 索引：-1=玩家自身角色，0..3=队伍角色。内部数组用 index = lane + 1。
    private const int LANE_COUNT = 5;
    private bool[,] sleeping = new bool[2, LANE_COUNT];
    private float[,] timer = new float[2, LANE_COUNT];
    private bool[,] immune = new bool[2, LANE_COUNT];

    void Awake() { if (Instance == null) Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    private static int LaneToIndex(int lane) => Mathf.Clamp(lane + 1, 0, LANE_COUNT - 1);

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
            for (int i = 0; i < LANE_COUNT; i++)
            {
                if (sleeping[s, i] && timer[s, i] > 0f)
                {
                    timer[s, i] -= Time.deltaTime;
                    if (timer[s, i] <= 0f) WakeCharacterInternal(s, i);
                }
            }
        }
    }

    // ===================== 单角色沉睡 API =====================

    /// <summary>让指定 side 的指定角色沉睡 seconds 秒（lane=-1 为玩家自身角色，0..3 为队伍角色）。控制免疫则抵抗。</summary>
    public void SleepCharacter(int side, int lane, float seconds)
    {
        if (side < 0 || side > 1) return;
        int idx = LaneToIndex(lane);
        if (immune[side, idx]) { Debug.Log($"[Sleep] side{side} lane{lane} 控制免疫，抵抗沉睡"); return; }
        sleeping[side, idx] = true;
        timer[side, idx] = Mathf.Max(0.1f, seconds);
        ApplySleepVisualForCharacter(side, lane, true);
        Debug.Log($"[Sleep] side{side} lane{lane} 陷入沉睡 {seconds}s");
    }

    /// <summary>唤醒指定 side 的指定角色。</summary>
    public void WakeCharacter(int side, int lane)
    {
        if (side < 0 || side > 1) return;
        WakeCharacterInternal(side, LaneToIndex(lane));
    }

    private void WakeCharacterInternal(int side, int idx)
    {
        if (sleeping[side, idx])
        {
            sleeping[side, idx] = false;
            timer[side, idx] = 0f;
            ApplySleepVisualForCharacter(side, idx - 1, false);
        }
    }

    /// <summary>查询指定 side 的指定角色是否沉睡。</summary>
    public bool IsCharacterSleeping(int side, int lane)
    {
        if (side < 0 || side > 1) return false;
        return sleeping[side, LaneToIndex(lane)];
    }

    /// <summary>驱散指定 side 的指定角色（解除沉睡）。</summary>
    public void DispelCharacter(int side, int lane)
    {
        if (side < 0 || side > 1) return;
        int idx = LaneToIndex(lane);
        if (sleeping[side, idx])
        {
            Debug.Log($"[Sleep] side{side} lane{lane} 被驱散，解除控制效果");
            WakeCharacterInternal(side, idx);
        }
    }

    /// <summary>指定 side 的指定角色受击时解除沉睡。</summary>
    public void BreakSleepOnDamageForCharacter(int side, int lane)
    {
        if (side < 0 || side > 1) return;
        int idx = LaneToIndex(lane);
        if (sleeping[side, idx])
        {
            Debug.Log($"[Sleep] side{side} lane{lane} 受击，解除沉睡");
            WakeCharacterInternal(side, idx);
        }
    }

    /// <summary>设置指定 side 指定角色的控制免疫。</summary>
    public void SetImmuneCharacter(int side, int lane, bool v)
    {
        if (side < 0 || side > 1) return;
        immune[side, LaneToIndex(lane)] = v;
    }

    // ===================== 整侧兼容 API（保留旧语义，用于未来整侧控制技能）====================

    /// <summary>让某侧全部角色沉睡 seconds 秒。</summary>
    public void Sleep(int side, float seconds)
    {
        if (side < 0 || side > 1) return;
        for (int lane = -1; lane < 4; lane++)
            SleepCharacter(side, lane, seconds);
    }

    public void Wake(int side)
    {
        if (side < 0 || side > 1) return;
        for (int i = 0; i < LANE_COUNT; i++)
            WakeCharacterInternal(side, i);
    }

    public bool IsSideSleeping(int side)
    {
        if (side < 0 || side > 1) return false;
        for (int i = 0; i < LANE_COUNT; i++)
            if (sleeping[side, i]) return true;
        return false;
    }

    public void Dispel(int side)
    {
        if (side < 0 || side > 1) return;
        for (int lane = -1; lane < 4; lane++)
            DispelCharacter(side, lane);
    }

    /// <summary>受击解除沉睡：队伍 HP 被扣时，该侧所有沉睡角色全部解除（断弦高压的沉睡代价由全队扣血解除）。</summary>
    public void BreakSleepOnDamage(int side)
    {
        if (side < 0 || side > 1) return;
        for (int lane = -1; lane < 4; lane++)
            BreakSleepOnDamageForCharacter(side, lane);
    }

    public void SetImmune(int side, bool v)
    {
        if (side < 0 || side > 1) return;
        for (int lane = -1; lane < 4; lane++)
            SetImmuneCharacter(side, lane, v);
    }

    // ===================== 视觉 =====================

    private void ApplySleepVisualForCharacter(int side, int lane, bool on)
    {
        var m = (lane < 0) ? CharacterCubeMarker.GetAt(side, -1) : CharacterCubeMarker.GetAt(side, lane);
        if (m != null) m.ApplySleepVisual(on);

        // 提示灯：该侧全部 4 条音轨指示灯变黑且无法发光（保留旧视觉语义）
        // 单角色沉睡时只影响该角色所在 lane 的指示灯；玩家自身角色(lane=-1)沉睡不影响指示灯
        var bvc = FindFirstObjectByType<BattleVisualsController>();
        if (bvc != null && bvc.indicators != null && lane >= 0 && lane < 4)
        {
            int idx = side * 4 + lane;
            if (idx >= 0 && idx < bvc.indicators.Length && bvc.indicators[idx] != null)
                bvc.indicators[idx].SetSleep(on);
        }
    }
}
