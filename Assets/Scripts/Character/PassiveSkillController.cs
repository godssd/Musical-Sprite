using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 被动技能控制器（能力N 中「输入方式留空」的槽）。
/// 挂载在角色 cube 的 CharacterCubeMarker 上，由 CharacterBattleSystem.AttachPassiveController 注入：
///   - owner：该角色运行时实例（提供 characterId / 身份等）
///   - passiveSlots：本角色所有被动槽（SkillSlot.IsPassive == true）
///
/// 行为：
///   - 每个被动技能独立做「条件检定」。满足条件 → 标记为生效（active），首次生效时 Debug.Log；
///     生效期间若条件不再满足 → 标记失效并 Debug.Log（符合「满足条件持续生效，不满足则失效」的语义）。
///   - EvaluateCondition 当前为占位（恒为 true）：真实条件（如「处于全体进攻状态」「HP 低于阈值」）
///     在 P3 接入具体技能效果路由时按 skillId/描述实现。
///   - 被动技能的实际「效果」（如战斗力提升 / 受伤减少 / 间隔附魔）同样留待 P3 路由层实现；
///     本控制器只负责「是否生效」的状态跟踪与日志，不影响数值。
///
/// 事件驱动型被动（2026-09-27 接入）：真是条好狗（effectType=GoodDog）
///   - 订阅 CharacterCubeMarker.OnHitAnimPlayed（仅当真正播放受击动画时触发，天然已满足「单次伤害 ≥ 门槛」条件）。
///   - 仅响应「自身」受击动画（marker == 本控制器所属 CharacterCubeMarker），全队其余成员的受击不会误触发。
///   - 触发：自身充能 = ceil(owner.combatPower × goodDogCombatRate)；随后进入「被动槽 cooldown（来自角色文档）」秒冷却（冷却中不重复触发）。
///   - 充能走 CharacterClass.AddEnergy（直接加能量、不留存、不进 BuffController 战力乘区），符合「能量类被动」设计。
///   - 冷却始终递减（即使沉睡，醒来即就绪）；沉睡期间不触发（与「沉睡被动失效」语义一致）。
///
/// 挂载：自动（无需手动）。
/// </summary>
public class PassiveSkillController : MonoBehaviour
{
    [Header("运行时注入（由 CharacterBattleSystem 设置）")]
    public CharacterClass owner;
    public int ownerSide = 0;
    public int ownerLane = -1;
    public List<SkillSlot> passiveSlots = new List<SkillSlot>();

    private HashSet<string> active = new HashSet<string>();
    private CharacterCubeMarker ownerMarker;
    // 真是条好狗 每技能独立冷却剩余秒数（key = skillId）
    private Dictionary<string, float> goodDogCdLeft = new Dictionary<string, float>();

    void Awake()
    {
        ownerMarker = GetComponent<CharacterCubeMarker>();
    }

    void OnEnable()
    {
        CharacterCubeMarker.OnHitAnimPlayed += OnHitAnimPlayed;
    }

    void OnDisable()
    {
        CharacterCubeMarker.OnHitAnimPlayed -= OnHitAnimPlayed;
    }

    void Update()
    {
        // 好狗冷却始终递减（即使沉睡，醒来即就绪）
        if (goodDogCdLeft.Count > 0)
        {
            var keys = new List<string>(goodDogCdLeft.Keys);
            foreach (var k in keys)
            {
                goodDogCdLeft[k] -= Time.deltaTime;
                if (goodDogCdLeft[k] <= 0f) goodDogCdLeft.Remove(k);
            }
        }

        // 沉睡期间被动技能失效（不检定、不生效）
        if (SleepController.Instance != null && SleepController.Instance.IsCharacterSleeping(ownerSide, ownerLane)) return;
        if (owner == null || passiveSlots == null) return;
        foreach (var s in passiveSlots)
        {
            if (s == null || !s.Exists || !s.IsPassive) continue;
            string key = string.IsNullOrEmpty(s.skillId) ? ("desc:" + s.description) : s.skillId;
            bool cond = EvaluateCondition(s);
            bool on = active.Contains(key);
            if (cond && !on)
            {
                active.Add(key);
                Debug.Log($"[Passive] side={(owner != null ? owner.characterId : -1)} 被动技能「{key}」生效（满足条件）");
            }
            else if (!cond && on)
            {
                active.Remove(key);
                Debug.Log($"[Passive] side={(owner != null ? owner.characterId : -1)} 被动技能「{key}」失效（条件不再满足）");
            }
        }
    }

    /// <summary>受击动画播放事件：仅当其等于本角色 marker 时，遍历被动槽处理 真是条好狗（GoodDog）。</summary>
    private void OnHitAnimPlayed(CharacterCubeMarker marker)
    {
        if (marker == null) return;
        if (ownerMarker == null) ownerMarker = GetComponent<CharacterCubeMarker>();
        if (marker != ownerMarker) return;   // 只响应自身受击动画，全队其余成员的受击不误触发

        if (owner == null || passiveSlots == null) return;
        if (SleepController.Instance != null && SleepController.Instance.IsCharacterSleeping(ownerSide, ownerLane)) return;

        foreach (var s in passiveSlots)
        {
            if (s == null || !s.Exists || !s.IsPassive) continue;
            var so = SkillSO.FindById(s.skillId);
            if (so == null || so.effectType != "GoodDog") continue;
            if (goodDogCdLeft.ContainsKey(s.skillId)) continue;   // 冷却中，跳过

            // 充能 = ceil(自身战斗力 × 系数)；直接加能量、不留存、不进 Buff 战力乘区
            int energy = Mathf.CeilToInt(owner.combatPower * so.goodDogCombatRate);
            owner.AddEnergy(energy);
            if (s.cooldown > 0f) goodDogCdLeft[s.skillId] = s.cooldown;   // 冷却来自角色文档被动槽（不是 SkillSO，符合技能库不持有冷却铁律）
            Debug.Log($"[Passive:好狗] side={owner.characterId} 受击动画触发，充能 +{energy}（战斗力×{so.goodDogCombatRate}，冷却 {s.cooldown}s）");
        }
    }

    /// <summary>被动技能「是否满足条件」。占位实现恒为 true；P3 按各技能具体条件实现。</summary>
    private bool EvaluateCondition(SkillSlot s)
    {
        // TODO(P3): 依据 s.skillId / s.description 实现具体条件检定
        // 例：s.skillId == "xxx" && BattleContext.IsInState("全体进攻")
        return true;
    }

    /// <summary>查询某被动技能当前是否生效。</summary>
    public bool IsActive(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return false;
        return active.Contains(skillId);
    }
}
