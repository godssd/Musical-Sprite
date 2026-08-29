using UnityEngine;
using System;
using System.Collections.Generic;

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
    public GameObject modelPrefab;   // 美术接入：角色外观预制体（由 CharacterDataSO.modelPrefab 注入；空=占位 cube）

    public int maxHP;
    public int currentHP;

    // 能量（仅队伍角色且有主动技能时有效）
    // 标量字段（maxEnergy/currentEnergy/isFullyCharged/skillBusy）= 首个有能量槽的镜像，供旧代码/单主动槽场景使用。
    public float maxEnergy;        // 镜像：首个有能量槽的 max（无能量槽则取首个主动槽）
    public float currentEnergy;
    public bool isFullyCharged;
    public bool skillBusy;          // 主动技能进行中（Grow/Charming/Releasing/Cooldown）为 true；期间抑制"能量充满冒烟"，结束回到 Standby 时若能量已满则补发

    // 多主动技能能量：与 activeSlots 一一对应（索引 = 主动槽序）。无能量主动槽 maxEnergies[i]=0（不显示能量槽）。
    public float[] maxEnergies;
    public float[] currentEnergies;
    public bool[] isFullyChargedArr;
    public bool[] skillBusyArr;

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

    public event Action<int, int> OnEnergyFull;       // (characterId, slotIndex)
    public event Action<int, int> OnEnergyDepleted;   // (characterId, slotIndex)

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
            modelPrefab = data.modelPrefab,
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

        // 兼容旧运行时：activeSkill = 首个主动槽的 SkillSO；skillCooldown 取该槽
        if (firstActive != null)
        {
            c.activeSkill = firstActive.skill;
            c.skillCooldown = firstActive.cooldown;
        }
        else
        {
            c.activeSkill = data.activeSkill;        // 旧字段兜底
            c.skillCooldown = data.skillCooldown;
        }

        // 多主动技能能量槽数组：与 activeSlots 一一对应；无能量主动槽 max = 0（不显示能量槽）。
        int n = c.activeSlots != null ? c.activeSlots.Count : 0;
        c.maxEnergies = new float[n];
        c.currentEnergies = new float[n];
        c.isFullyChargedArr = new bool[n];
        c.skillBusyArr = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var s = c.activeSlots[i];
            c.maxEnergies[i] = (s != null && s.NeedsEnergy) ? Mathf.Max(0f, s.energyCost) : 0f;
        }

        // 兼容标量（首个有能量槽；没有则取首个主动槽；都不存在则 0）：供旧代码 / 单槽场景使用
        int fe = 0;
        for (; fe < n; fe++) if (c.maxEnergies[fe] > 0f) break;
        if (fe >= n) fe = 0;
        c.maxEnergy = (n > 0) ? c.maxEnergies[fe] : 0f;
        c.currentEnergy = 0f;
        c.isFullyCharged = false;
        c.skillBusy = false;
        return c;
    }

    /// <summary>充能：所有有能量需求的主动槽一起加相同能量（不平分，按用户 2026-08-27 要求）。
    /// 无能量主动槽（maxEnergies[i]=0，如主角多个技能）不参与、也不显示能量槽。</summary>
    public void AddEnergy(float amount)
    {
        if (!HasActiveSkill) return;
        if (amount <= 0f) return;
        if (maxEnergies != null)
        {
            for (int i = 0; i < maxEnergies.Length; i++)
            {
                if (maxEnergies[i] <= 0f) continue;                 // 无能量槽：跳过
                currentEnergies[i] = Mathf.Min(maxEnergies[i], currentEnergies[i] + amount);
                bool nowFull = currentEnergies[i] >= maxEnergies[i];
                if (nowFull && !isFullyChargedArr[i])
                {
                    isFullyChargedArr[i] = true;
                    // 技能进行中时即使能量再次充满也抑制冒烟：等待 SetSlotBusy(i,false) 时再补发
                    if (!skillBusyArr[i]) OnEnergyFull?.Invoke(characterId, i);
                }
            }
        }
        SyncScalarFromSlot0();
    }

    /// <summary>兼容旧调用：设置首个有能量槽的 busy 状态。</summary>
    public void SetSkillBusy(bool busy) => SetSlotBusy(FirstEnergySlotIndex(), busy);

    /// <summary>按主动槽索引标记技能是否处于进行中（含冷却）。
    /// 进行中（含冷却）时即使该槽能量再次充满也抑制冒烟。
    /// 冷却结束（busy=false）时做一次边界兜底：
    ///   - 如果该槽在技能+CD 期间 currentEnergies[i] 已 ≥ maxEnergies[i]（被抑制未触发 OnEnergyFull）
    ///     → 立刻补发一次 OnEnergyFull(characterId,i)，保证能量满时一定会冒烟；
    ///   - 否则按预期重置充满标记，等下一次真实跨越阈值时再触发。
    /// 满足「技能处于 CD 时不能冒烟」+「CD 结束后若能量已满则必然冒烟（防止间歇性漏发）」。</summary>
    public void SetSlotBusy(int i, bool busy)
    {
        if (i < 0 || i >= skillBusyArr.Length) return;
        if (skillBusyArr[i] == busy) return;
        skillBusyArr[i] = busy;
        if (!busy)
        {
            // 兜底补发：CD 期间能量已被加满但 isFullyChargedArr 仍 false（被抑制未冒烟）→ 补发一次
            if (maxEnergies[i] > 0f && currentEnergies[i] >= maxEnergies[i] - 0.0001f && !isFullyChargedArr[i])
            {
                isFullyChargedArr[i] = true;
                OnEnergyFull?.Invoke(characterId, i);
            }
            else
            {
                // 常规分支：重置充满标记
                isFullyChargedArr[i] = false;
            }
        }
        SyncScalarFromSlot0();
    }

    /// <summary>兼容旧调用：扣除首个有能量槽的能量。</summary>
    public void ConsumeEnergy(float cost) => ConsumeSlot(FirstEnergySlotIndex());

    /// <summary>施放第 i 个主动槽技能时调用，扣除该槽能量并触发耗尽表现（停止上浮方块等）。</summary>
    public void ConsumeSlot(int i)
    {
        if (i < 0 || i >= maxEnergies.Length) return;
        if (maxEnergies[i] <= 0f) return;                 // 无能量槽：无能量可扣
        currentEnergies[i] = 0f;
        if (isFullyChargedArr[i])
        {
            isFullyChargedArr[i] = false;
            OnEnergyDepleted?.Invoke(characterId, i);
        }
        SyncScalarFromSlot0();
    }

    /// <summary>第 i 个主动槽能量是否已充满（达到其自有上限）。</summary>
    public bool IsSlotFull(int i) =>
        i >= 0 && i < maxEnergies.Length && maxEnergies[i] > 0f && currentEnergies[i] >= maxEnergies[i];

    /// <summary>第 i 个主动槽是否处于进行中（含冷却）。</summary>
    public bool IsSlotBusy(int i) =>
        i >= 0 && i < skillBusyArr.Length && skillBusyArr[i];

    /// <summary>首个有能量的主动槽索引；都没有则 0（兼容单槽/旧数据）。</summary>
    public int FirstEnergySlotIndex()
    {
        if (maxEnergies == null || maxEnergies.Length == 0) return 0;
        for (int i = 0; i < maxEnergies.Length; i++) if (maxEnergies[i] > 0f) return i;
        return 0;
    }

    /// <summary>开局充满所有有能量的槽（测试开关 startWithFullSkillEnergy 用）。</summary>
    public void FillAllEnergySlots()
    {
        if (maxEnergies == null) return;
        for (int i = 0; i < maxEnergies.Length; i++)
        {
            if (maxEnergies[i] <= 0f) continue;
            currentEnergies[i] = maxEnergies[i];
            if (!isFullyChargedArr[i])
            {
                isFullyChargedArr[i] = true;
                if (!skillBusyArr[i]) OnEnergyFull?.Invoke(characterId, i);
            }
        }
        SyncScalarFromSlot0();
    }

    /// <summary>把兼容标量（maxEnergy/currentEnergy/isFullyCharged/skillBusy）同步为「首个有能量槽」的镜像，供旧代码/单槽使用。</summary>
    private void SyncScalarFromSlot0()
    {
        int fe = FirstEnergySlotIndex();
        if (maxEnergies != null && fe < maxEnergies.Length)
        {
            maxEnergy = maxEnergies[fe];
            currentEnergy = currentEnergies[fe];
            isFullyCharged = isFullyChargedArr[fe];
            skillBusy = skillBusyArr[fe];
        }
    }

    public void TakeDamage(int dmg)
    {
        currentHP = Mathf.Max(0, currentHP - dmg);
    }
}
