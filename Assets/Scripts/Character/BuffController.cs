using UnityEngine;

/// <summary>
/// a/b 双槽 Buff 控制器（每侧各一 a 槽、一 b 槽）。
/// - a 类（进攻 / 防御）互斥顶替：新 a 覆盖旧 a。
/// - b 类互斥顶替：新 b 覆盖旧 b（仅小黑个人战力生效）。
/// - a 与 b 互不冲突，战力上相乘叠加（见 GetCombatSum）。
/// 挂载：由 CharacterBattleSystem 自动补建；通过 Instance 访问。
/// </summary>
public enum BuffSlot { None = 0, A = 1, B = 2 }
public enum BuffSubType { Offense = 0, Defense = 1 }

public class BuffController : MonoBehaviour
{
    public static BuffController Instance { get; private set; }

    private const int XiaoHeiId = 5; // 小黑 characterId（小黑_5.asset）

    // 每侧 a 槽状态
    private BuffSlot[] aSlot = new BuffSlot[2] { BuffSlot.None, BuffSlot.None };
    private BuffSubType[] aSub = new BuffSubType[2] { BuffSubType.Offense, BuffSubType.Offense };
    private float[] aCombatMult = new float[2] { 1f, 1f };
    private float[] aDmgReduce = new float[2] { 0f, 0f };
    private float[] aTimer = new float[2] { 0f, 0f };

    // 每侧 b 槽状态（仅小黑个人战力）
    private BuffSlot[] bSlot = new BuffSlot[2] { BuffSlot.None, BuffSlot.None };
    private float[] bCombatMult = new float[2] { 1f, 1f };
    private float[] bTimer = new float[2] { 0f, 0f };

    // 各侧 VFX 实例引用（buff 到期/被顶替时 Destroy 收尾）
    private GameObject[] defenseAuraA = new GameObject[2];
    private GameObject[] offenseAuraA = new GameObject[2];
    private GameObject[] powerAuraB   = new GameObject[2];

    void Awake() { if (Instance == null) Instance = this; }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        for (int s = 0; s < 2; s++)
        {
            DestroyAura(ref defenseAuraA[s]);
            DestroyAura(ref offenseAuraA[s]);
            DestroyAura(ref powerAuraB[s]);
        }
    }

    void Update()
    {
        for (int s = 0; s < 2; s++)
        {
            if (aSlot[s] == BuffSlot.A && aTimer[s] > 0f)
            {
                aTimer[s] -= Time.deltaTime;
                if (aTimer[s] <= 0f) ClearBuff(s, BuffSlot.A);
            }
            if (bSlot[s] == BuffSlot.B && bTimer[s] > 0f)
            {
                bTimer[s] -= Time.deltaTime;
                if (bTimer[s] <= 0f) ClearBuff(s, BuffSlot.B);
            }
        }
    }

    /// <summary>应用 a 类 buff（进攻=战力×mult；防御=减伤 reduce）。互斥顶替旧 a。</summary>
    public void SetABuff(int side, BuffSubType sub, float combatMult, float dmgReduce)
    {
        if (side < 0 || side > 1) return;
        aSlot[side] = BuffSlot.A;
        aSub[side] = sub;
        aCombatMult[side] = combatMult > 0f ? combatMult : 1f;
        aDmgReduce[side] = Mathf.Clamp01(dmgReduce);
        ApplyAurasForSide(side);   // 立刻挂对应视觉（互斥顶替 = 旧 aura 会被覆盖）
    }

    /// <summary>应用 b 类 buff（仅小黑个人战力×mult）。互斥顶替旧 b。</summary>
    public void SetBBuff(int side, float combatMult)
    {
        if (side < 0 || side > 1) return;
        bSlot[side] = BuffSlot.B;
        bCombatMult[side] = combatMult > 0f ? combatMult : 1f;
        ApplyAurasForSide(side);
    }

    /// <summary>设置 buff 持续时间（秒）。<=0 表示持续到被同类顶替。</summary>
    public void SetBuffDuration(int side, BuffSlot slot, float duration)
    {
        if (side < 0 || side > 1) return;
        if (slot == BuffSlot.A) aTimer[side] = duration;
        else if (slot == BuffSlot.B) bTimer[side] = duration;
    }

    public void ClearBuff(int side, BuffSlot slot)
    {
        if (side < 0 || side > 1) return;
        if (slot == BuffSlot.A)
        {
            aSlot[side] = BuffSlot.None;
            aCombatMult[side] = 1f;
            aDmgReduce[side] = 0f;
            aTimer[side] = 0f;
            DestroyAura(ref defenseAuraA[side]);
            DestroyAura(ref offenseAuraA[side]);
        }
        else if (slot == BuffSlot.B)
        {
            bSlot[side] = BuffSlot.None;
            bCombatMult[side] = 1f;
            bTimer[side] = 0f;
            DestroyAura(ref powerAuraB[side]);
        }
    }

    public bool HasABuff(int side) => side >= 0 && side <= 1 && aSlot[side] == BuffSlot.A;
    public bool HasBBuff(int side) => side >= 0 && side <= 1 && bSlot[side] == BuffSlot.B;
    public BuffSubType GetASubType(int side) => (side >= 0 && side <= 1) ? aSub[side] : BuffSubType.Offense;

    /// <summary>该侧受到的伤害减免比例（0~1）。仅 a 防御生效。</summary>
    public float GetDamageReduction(int side)
    {
        if (side < 0 || side > 1) return 0f;
        return aSlot[side] == BuffSlot.A && aSub[side] == BuffSubType.Defense ? aDmgReduce[side] : 0f;
    }

    /// <summary>实时战力总和（含 a/b buff）：Σ_c[ base(c) × (a进攻?×aMult:1) × (c==小黑 && b激活?×bMult:1) ]。</summary>
    public float GetCombatSum(int side)
    {
        if (side < 0 || side > 1) return 0f;
        float sum = 0f;
        var player = CharacterRoster.GetPlayer(side);
        if (player != null) sum += AdjustedCombat(player, side);
        for (int lane = 0; lane < 4; lane++)
        {
            var c = CharacterRoster.GetTeam(side, lane);
            if (c != null) sum += AdjustedCombat(c, side);
        }
        return sum;
    }

    private float AdjustedCombat(CharacterClass c, int side)
    {
        if (c == null) return 0f;
        float v = c.combatPower;
        if (aSlot[side] == BuffSlot.A && aSub[side] == BuffSubType.Offense) v *= aCombatMult[side];
        if (bSlot[side] == BuffSlot.B && c.characterId == XiaoHeiId) v *= bCombatMult[side];
        return v;
    }

    // ============================================================
    // 视觉挂接（buff 激活 = spawn aura；过期/被顶替 = Destroy aura）
    // ============================================================

    private void ApplyAurasForSide(int side)
    {
        // 防御 / 进攻 同属于 a 类，互斥；每次 SetABuff 时按"当前 aSub[side]"统一刷一次视觉
        DestroyAura(ref defenseAuraA[side]);
        DestroyAura(ref offenseAuraA[side]);

        if (aSlot[side] == BuffSlot.A)
        {
            Vector3 teamCenter = FindTeamCenter(side);
            Vector3 facing = FindTeamFacing(side);
            if (aSub[side] == BuffSubType.Defense)
                defenseAuraA[side] = DefenseAuraFx.Spawn(teamCenter, facing);
            else
                offenseAuraA[side] = OffenseAuraFx.Spawn(teamCenter);
        }

        // b 类：仅在对应侧存在小黑时 spawn 旋转方块
        if (bSlot[side] == BuffSlot.B)
        {
            var xhMarker = FindXiaoHeiMarker(side);
            if (xhMarker != null) powerAuraB[side] = SelfPowerAuraFx.Spawn(xhMarker.transform);
        }
        else
        {
            DestroyAura(ref powerAuraB[side]);
        }
    }

    private static void DestroyAura(ref GameObject aura)
    {
        if (aura == null) return;
        Destroy(aura);
        aura = null;
    }

    /// <summary>查找该侧"队伍中心点"：取该侧第一个 NoteSpawner 的 hitPoint（玩家一侧判定线中心）。没有 spawner 就回落到队伍角色 marker 平均位置。</summary>
    private Vector3 FindTeamCenter(int side)
    {
        var spawners = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var s in spawners)
            if (s != null && s.side == side && s.hitPoint != null) return s.hitPoint.position;
        // 回退：marker 平均位置
        var markers = FindObjectsByType<CharacterCubeMarker>(FindObjectsSortMode.None);
        Vector3 sum = Vector3.zero; int n = 0;
        foreach (var m in markers)
        {
            if (m != null && m.side == side) { sum += m.transform.position; n++; }
        }
        return n > 0 ? sum / n : Vector3.zero;
    }

    /// <summary>该侧"面对敌方"的朝向向量（队伍在场地左半时朝向 +X；在右半时朝向 -X）。用作护罩膜正前方参考。</summary>
    private Vector3 FindTeamFacing(int side)
    {
        return side == 0 ? Vector3.right : Vector3.left;
    }

    /// <summary>在该侧查找小黑（characterId=5）的 CharacterCubeMarker。玩家自身的小黑也支持。</summary>
    private CharacterCubeMarker FindXiaoHeiMarker(int side)
    {
        var markers = FindObjectsByType<CharacterCubeMarker>(FindObjectsSortMode.None);
        foreach (var m in markers)
        {
            if (m == null || m.side != side) continue;
            var c = m.IsPlayer
                ? CharacterRoster.GetPlayer(side)
                : CharacterRoster.GetTeam(side, m.laneIndex);
            if (c != null && c.characterId == XiaoHeiId) return m;
        }
        return null;
    }
}
