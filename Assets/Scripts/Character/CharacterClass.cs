using UnityEngine;
using System;

/// <summary>
/// 角色运行时实例（非 MonoBehaviour，由 CharacterDataSO 装配，战斗初始化时创建）。
/// 持有可变状态：HP / 能量；提供能量充能 / 消耗 / 受伤接口与事件。
/// </summary>
[Serializable]
public class CharacterClass
{
    public int characterId;
    public string displayName;
    public bool isPlayer;
    public int laneIndex;
    public float combatPower;
    public Color blockColor = Color.yellow;

    public int maxHP;
    public int currentHP;

    // 能量（仅队伍角色且有主动技能时有效）
    public float maxEnergy;        // 装配时缓存：优先 SO.activeEnergyCost，否则 SkillSO.energyCost，再否则 100
    public float currentEnergy;
    public bool isFullyCharged;
    public bool skillBusy;          // 主动技能进行中（Grow/Charming/Releasing/Cooldown）为 true；期间抑制"能量充满冒烟"，结束回到 Standby 时若能量已满则补发

    public SkillSO activeSkill;       // 兼容旧运行时：首个主动(非被动)槽的 SkillSO
    public SkillSO passiveSkill;      // 兼容旧：首个被动槽的 SkillSO（仅展示）

    // 多技能槽：与 CharacterDataSO.skills 对应（索引 0=能力1 … 4=能力5）
    public SkillSlot[] skills;
    public List<SkillSlot> passiveSlots = new List<SkillSlot>();   // inputMethod 空的槽（被动）
    public List<SkillSlot> activeSlots = new List<SkillSlot>();    // inputMethod 非空（主动）的槽（供运行时逐个建 ActiveSkillRuntime）
    public List<SkillSO> activeSkills = new List<SkillSO>();        // 兼容旧引用：首个主动槽的 SkillSO

    // 技能冷却（秒）；由 CharacterDataSO.skills[首个主动槽].cooldown 装配（完全来自角色文档）。0 = 无冷却
    public float skillCooldown = 0f;

    public bool HasActiveSkill => (activeSkill != null) || (activeSlots != null && activeSlots.Count > 0);
    public bool HasPassiveSkill => passiveSkill != null || (passiveSlots != null && passiveSlots.Count > 0);

    public event Action<int> OnEnergyFull;       // 参数 characterId
    public event Action<int> OnEnergyDepleted;   // 参数 characterId

    /// <summary>由 CharacterDataSO 装配出一个运行时实例。</summary>
    public static CharacterClass FromData(CharacterDataSO data)
    {
        var c = new CharacterClass
        {
            characterId = data.characterId,
            displayName = data.displayName,
            isPlayer = data.isPlayer,
            laneIndex = data.laneIndex,
            combatPower = data.combatPower,
            blockColor = data.blockColor,
            maxHP = data.maxHP,
            currentHP = data.maxHP,
        };

        // 多技能槽：缓存并派生 active / passive 列表
        c.skills = data.skills;
        c.passiveSlots = new List<SkillSlot>();
        c.activeSlots = new List<SkillSlot>();
        c.activeSkills = new List<SkillSO>();
        SkillSlot firstActive = null;
        if (data.skills != null)
        {
            foreach (var s in data.skills)
            {
                if (s == null || !s.Exists) continue;
                if (s.IsPassive)
                {
                    c.passiveSlots.Add(s);
                    if (c.passiveSkill == null) c.passiveSkill = s.skill;
                }
                else
                {
                    c.activeSlots.Add(s);                 // 主动槽：运行时逐个建 ActiveSkillRuntime
                    if (s.skill != null) c.activeSkills.Add(s.skill);
                    if (firstActive == null) firstActive = s;
                }
            }
        }

        // 兼容旧运行时：activeSkill = 首个主动槽的 SkillSO；skillCooldown / maxEnergy 取该槽
        if (firstActive != null)
        {
            c.activeSkill = firstActive.skill;
            c.skillCooldown = firstActive.cooldown;
            c.maxEnergy = firstActive.NeedsEnergy ? firstActive.energyCost : 0f;
        }
        else
        {
            c.activeSkill = data.activeSkill;        // 旧字段兜底
            c.skillCooldown = data.skillCooldown;
            c.maxEnergy = data.activeEnergyCost > 0
                ? data.activeEnergyCost
                : (data.activeSkill != null ? data.activeSkill.energyCost : 0f);
        }
        c.maxEnergy = Mathf.Max(0f, c.maxEnergy);   // 无能量需求 = 0（不再兜底 100，避免玩家/无能量技能误冒烟）
        c.currentEnergy = 0f;
        c.isFullyCharged = false;
        return c;
    }

    /// <summary>按 10:1 之外的逻辑由外部调用：传入 actualScore/10 即可。分数本身不损失。</summary>
    public void AddEnergy(float amount)
    {
        if (!HasActiveSkill) return;
        if (amount <= 0f) return;
        currentEnergy += amount;
        bool nowFull = maxEnergy > 0f && currentEnergy >= maxEnergy;
        if (nowFull && !isFullyCharged)
        {
            isFullyCharged = true;
            // 技能进行中时即使能量再次充满也抑制冒烟：等待 ActiveSkillRuntime 调 SetSkillBusy(false) 时再补发
            if (!skillBusy) OnEnergyFull?.Invoke(characterId);
        }
    }

    /// <summary>由 ActiveSkillRuntime 调用：标记技能是否处于进行中。
    /// 进行中时即使能量再次充满也抑制冒烟；结束回到 Standby 时若能量已满则立即补发 OnEnergyFull 让冒烟恢复。</summary>
    public void SetSkillBusy(bool busy)
    {
        if (skillBusy == busy) return;
        skillBusy = busy;
        if (!busy && isFullyCharged)
            OnEnergyFull?.Invoke(characterId);
    }

    /// <summary>施放主动技能时调用，扣除能量；低于上限则取消"已充满"状态（停止上浮方块表现）。</summary>
    public void ConsumeEnergy(float cost)
    {
        if (!HasActiveSkill) return;
        currentEnergy = Mathf.Max(0f, currentEnergy - cost);
        if (isFullyCharged && currentEnergy < maxEnergy)
        {
            isFullyCharged = false;
            OnEnergyDepleted?.Invoke(characterId);
        }
    }

    public void TakeDamage(int dmg)
    {
        currentHP = Mathf.Max(0, currentHP - dmg);
    }
}
