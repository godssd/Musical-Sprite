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
/// 挂载：自动（无需手动）。
/// </summary>
public class PassiveSkillController : MonoBehaviour
{
    [Header("运行时注入（由 CharacterBattleSystem 设置）")]
    public CharacterClass owner;
    public List<SkillSlot> passiveSlots = new List<SkillSlot>();

    private HashSet<string> active = new HashSet<string>();

    void Update()
    {
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
                Debug.Log($"[Passive] side={owner != null ? owner.characterId : -1} 被动技能「{key}」生效（满足条件）");
            }
            else if (!cond && on)
            {
                active.Remove(key);
                Debug.Log($"[Passive] side={owner != null ? owner.characterId : -1} 被动技能「{key}」失效（条件不再满足）");
            }
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
