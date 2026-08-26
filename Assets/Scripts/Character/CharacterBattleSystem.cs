using UnityEngine;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 角色战斗系统（P2）。
/// - 启动时把所有 CharacterDataSO 装配为 CharacterClass 实例并注册到 CharacterRoster。
/// - 如果 Inspector 里 allCharacters 为空，则用 DefaultSkillBootstrap 临时造 5+5 个默认角色，保证游戏能跑。
/// - 暴露 GetMaxHP / GetCombatSum / TryCastActive 给 ScoreManager 与 SkillInputUI。
/// - 主动技能的能量消耗与效果转发：先打成 Debug.Log 桩（P2 末）。
///
/// 挂载：场景任意 GameObject；ScoreManager 启动时若缺失会自动补建。
/// </summary>
public class CharacterBattleSystem : MonoBehaviour
{
    [Header("角色数据（P2 手动或经 Character Importer 导入）")]
    public CharacterDataSO[] allCharacters;

    [Header("调试：自动补全场上的 CharacterCubeMarker（按 x 划分侧，按 z 划分 lane）")]
    public bool autoFillMarkersOnStart = true;

    public int[] MaxHPBySide { get; private set; } = new int[2];
    public float[] CombatSumBySide { get; private set; } = new float[2];

    void Awake()
    {
        InitializeFromData();
    }

    void Start()
    {
        // 补 marker（如果 Awake 时尚未存在就再做一次）
        if (autoFillMarkersOnStart) AutoFillMarkers();
        // 上色：按角色身份色给所有已标记方块上色（两侧都上）
        ColorAllMarkers();
        // P2：让 EnergyVFXPlaceholder 自动填充锚点
        var energyVfx = FindFirstObjectByType<EnergyVFXPlaceholder>();
        if (energyVfx != null) energyVfx.RebuildFromRosterAndMarkers();
        // 主动技能运行时：为每个队伍角色挂载 ActiveSkillRuntime（大狗叫等）
        SetupSkillRuntimes();
    }

    /// <summary>为每个角色（玩家 + 队伍）的 marker 挂载 ActiveSkillRuntime（每个主动槽一个）与 PassiveSkillController。</summary>
    private bool skillRuntimesReady = false;
    private void SetupSkillRuntimes()
    {
        if (skillRuntimesReady) return;
        SkillSO.RebuildIndex();

        var spawners = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        var combos = FindObjectsByType<ComboDisplay>(FindObjectsSortMode.None);
        var markers = FindObjectsByType<CharacterCubeMarker>(FindObjectsSortMode.None);
        var fever = FindFirstObjectByType<FeverManager>();

        for (int side = 0; side < 2; side++)
        {
            var spawner = System.Array.Find(spawners, s => s.side == side);
            var oppCombo = System.Array.Find(combos, c => c.side == (1 - side));

            // 玩家自身角色（无音轨、无能量门槛；其主动技能随时可释放）
            var playerInst = CharacterRoster.GetPlayer(side);
            if (playerInst != null)
            {
                var pMarker = System.Array.Find(markers, m => m != null && m.IsPlayer && m.side == side);
                AttachSkillRuntimes(playerInst, pMarker, spawner, oppCombo, side, fever);
                AttachPassiveController(playerInst, pMarker);
            }

            // 队伍角色（lane 0..3，有音轨；主动技能按能量门槛释放）
            for (int lane = 0; lane < 4; lane++)
            {
                var inst = CharacterRoster.GetTeam(side, lane);
                if (inst == null) continue;
                var marker = System.Array.Find(markers, m => m != null && !m.IsPlayer && m.side == side && m.laneIndex == lane);
                AttachSkillRuntimes(inst, marker, spawner, oppCombo, side, fever);
                AttachPassiveController(inst, marker);
            }
        }
        skillRuntimesReady = true;
        Debug.Log("[CharacterBattleSystem] 已为每个角色挂载主动技能运行时与被动控制器（能力1~能力5）");
    }

    /// <summary>为每个「主动槽」在该角色的 marker 上挂一个 ActiveSkillRuntime。
    /// 输入序列以角色文档「输入方式」为准（←↓→/AAB 等），兜底用 SkillSO.inputSequence。</summary>
    private void AttachSkillRuntimes(CharacterClass inst, CharacterCubeMarker marker, NoteSpawner spawner, ComboDisplay oppCombo, int side, FeverManager fever)
    {
        if (inst == null || marker == null || inst.activeSlots == null) return;
        foreach (var slot in inst.activeSlots)
        {
            if (slot == null || !slot.Exists || slot.IsPassive) continue;
            var rt = marker.gameObject.AddComponent<ActiveSkillRuntime>();
            SkillInputStep[] seq = SkillSO.ParseInputMethod(slot.inputMethod);
            if (seq.Length == 0 && slot.skill != null) seq = slot.skill.inputSequence;
            rt.Setup(inst, marker, spawner, oppCombo, side, fever, slot.skill, slot.cooldown, slot.NeedsEnergy, seq);
        }
    }

    /// <summary>给角色 marker 挂 PassiveSkillController，跟踪其所有被动槽（输入方式留空的能力）。</summary>
    private void AttachPassiveController(CharacterClass inst, CharacterCubeMarker marker)
    {
        if (inst == null || marker == null || inst.passiveSlots == null || inst.passiveSlots.Count == 0) return;
        var ctrl = marker.GetComponent<PassiveSkillController>();
        if (ctrl == null) ctrl = marker.gameObject.AddComponent<PassiveSkillController>();
        ctrl.owner = inst;
        ctrl.passiveSlots = inst.passiveSlots;
        ctrl.enabled = true;
    }

    private void InitializeFromData()
    {
        CharacterRoster.Clear();

        bool usedDefault = false;
        if (allCharacters == null || allCharacters.Length == 0)
        {
#if UNITY_EDITOR
            // 场景未手动配置 allCharacters：优先加载「角色导入器」的输出目录（Assets/Data/Characters），
            // 让用户在表格里改的 hp / 战斗力 / 技能冷却 等真正生效，而不是被写死的默认数据覆盖。
            var imported = LoadImportedCharacters();
            if (imported != null && imported.Length > 0)
            {
                allCharacters = imported;
                Debug.Log($"[CharacterBattleSystem] 未手动配置 allCharacters，已从 Assets/Data/Characters 加载 {imported.Length} 个已导入角色（表格改动生效）。");
            }
            else
#endif
            {
                // 临时默认数据（飞书 1:1：宝宝 + 大狗 + 嘟嘟 + 爱格 + 小黑），让系统可启动
                allCharacters = BuildDefaultCharacterArray();
                usedDefault = true;
            }
        }

        // 不变量：同一 side 内，一个 characterId 不能被两个 lane 重复上场（两侧互不影响，镜像阵容合法）。
        for (int side = 0; side < 2; side++)
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            int maxHP = 0;
            float combat = 0f;
            var sides = allCharacters.Where(c => c != null);
            // 玩家自身
            CharacterClass playerC = null;
            foreach (var d in sides.Where(c => c.isPlayer))
            {
                if (!seen.Add(d.characterId))
                {
                    Debug.LogWarning($"[CharacterBattleSystem] characterId={d.characterId} ({d.displayName}) 重复，跳过玩家自身 side={side}");
                    continue;
                }
                var inst = CharacterClass.FromData(d);
                if (inst == null) continue;
                playerC = inst;
                CharacterRoster.RegisterPlayer(side, inst);
                maxHP += inst.maxHP;
                combat += inst.combatPower;
                break;
            }
            // 4 个队伍角色（lane 0..3）
            for (int lane = 0; lane < 4; lane++)
            {
                CharacterDataSO pick = null;
                foreach (var d in sides.Where(c => !c.isPlayer && c.laneIndex == lane))
                {
                    pick = d; break;
                }
                if (pick == null) continue;
                if (!seen.Add(pick.characterId))
                {
                    Debug.LogWarning($"[CharacterBattleSystem] characterId={pick.characterId} ({pick.displayName}) 重复，跳过 side={side} lane={lane}");
                    continue;
                }
                var inst = CharacterClass.FromData(pick);
                CharacterRoster.RegisterTeam(side, lane, inst);
                maxHP += inst.maxHP;
                combat += inst.combatPower;

                // 订阅能量事件 -> EnergyVFXPlaceholder 自动收得到
                if (inst.HasActiveSkill)
                {
                    int capturedSide = side;
                    int capturedLane = lane;
                    int capturedId = inst.characterId;
                    inst.OnEnergyFull += (id) => Debug.Log($"[Battle] side={capturedSide} lane={capturedLane} charId={id} 能量已满");
                    inst.OnEnergyDepleted += (id) => Debug.Log($"[Battle] side={capturedSide} lane={capturedLane} charId={id} 能量耗尽");
                }
            }
            MaxHPBySide[side] = maxHP;
            CombatSumBySide[side] = combat;
        }

        Debug.Log($"[CharacterBattleSystem] 已装载角色：leftMaxHP={MaxHPBySide[0]} L_combat={CombatSumBySide[0]} | rightMaxHP={MaxHPBySide[1]} R_combat={CombatSumBySide[1]} | default={usedDefault} | 双方阵容均为 characterId 1..5（玩家=宝宝，队伍=大狗/嘟嘟/爱格/小黑，镜像布置）");
        // 注：AutoFillMarkers 放到 Start 执行，避免 Awake 太早拿不到 NoteSpawner。
    }

    /// <summary>为场上所有 Renderer 加 CharacterCubeMarker；按 transform.x 分侧、按 transform.z 用 laneSpacing 估算 lane。</summary>
    public void AutoFillMarkers()
    {
        float laneSpacing = GuessLaneSpacing();
        var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        int added = 0;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (r.GetComponent<CharacterCubeMarker>() != null) continue;
            // 跳过 UI Canvas/Overlay 物件
            if (r.transform.parent != null && r.transform.parent.GetComponentInParent<Canvas>() != null) continue;
            var go = r.gameObject;
            // 玩家自身 cube 比队立方块大（粗略判断：scale 体积更大的为 player）
            bool bigSize = r.bounds.size.sqrMagnitude > 1.5f;
            int side = go.transform.position.x < 0f ? 0 : 1;
            int lane;
            if (bigSize) lane = -1;
            else
            {
                // z 由 NoteSpawner 约定：lane i = (i-1.5) * laneSpacing → i = z/laneSpacing + 1.5
                float z = go.transform.position.z;
                lane = Mathf.Clamp(Mathf.RoundToInt(z / laneSpacing + 1.5f), 0, 3);
            }
            var marker = go.AddComponent<CharacterCubeMarker>();
            marker.side = side;
            marker.laneIndex = lane;
            ColorMarker(marker);
            added++;
        }
        Debug.Log($"[CharacterBattleSystem] 自动补 CharacterCubeMarker x {added}（laneSpacing={laneSpacing}）");
    }

    /// <summary>按角色身份色给单个已标记方块上色：查 CharacterRoster 拿到该角色 blockColor。</summary>
    public void ColorMarker(CharacterCubeMarker m)
    {
        if (m == null) return;
        var go = m.gameObject;
        // 跳过明显的非角色物件（音符 / 判定线 / UI 等），避免误上色
        string n = go.name;
        if (n.IndexOf("Note", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("Line", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("Judge", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("Touch", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("Center", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("Score", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("Combo", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("HP", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("Fever", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("Overlay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("Banner", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        CharacterClass c = m.IsPlayer ? CharacterRoster.GetPlayer(m.side) : CharacterRoster.GetTeam(m.side, m.laneIndex);
        Color col = (c != null) ? c.blockColor : Color.yellow;
        var rend = m.GetComponent<Renderer>();
        if (rend != null) rend.material.color = col;
    }

    /// <summary>给场景内所有 CharacterCubeMarker 重新上色（MS Debug 一键可用）。</summary>
    public void ColorAllMarkers()
    {
        var markers = FindObjectsByType<CharacterCubeMarker>(FindObjectsSortMode.None);
        int n = 0;
        foreach (var m in markers) { ColorMarker(m); n++; }
        Debug.Log($"[CharacterBattleSystem] 已按角色身份色上色 x {n} 个方块（宝宝灰 / 大狗橙 / 嘟嘟绿 / 爱格白 / 小黑黑）");
    }

#if UNITY_EDITOR
    /// <summary>从「角色导入器」的输出目录（Assets/Data/Characters）加载所有已导入的 CharacterDataSO。
    /// 仅 Editor 下可用（依赖 AssetDatabase）；玩家构建里走 #else 的默认数据或场景里手动配好的 allCharacters。</summary>
    private static CharacterDataSO[] LoadImportedCharacters()
    {
        string dir = "Assets/Data/Characters";
        if (!AssetDatabase.IsValidFolder(dir)) return null;
        var guids = AssetDatabase.FindAssets("t:CharacterDataSO", new[] { dir });
        var list = new System.Collections.Generic.List<CharacterDataSO>();
        foreach (var g in guids)
        {
            var so = AssetDatabase.LoadAssetAtPath<CharacterDataSO>(AssetDatabase.GUIDToAssetPath(g));
            if (so == null) continue;
            // 只加载游戏内 5 名角色（characterId 1=玩家，2..5=四条音轨）。
            // 忽略飞书表里更靠后的占位/未启用行（它们被导入器以 laneIndex=-1 标记为"仅花名册"，不会进战斗）。
            if (so.characterId < 1 || so.characterId > 5) continue;
            list.Add(so);
        }
        return list.Count > 0 ? list.ToArray() : null;
    }
#endif

    private float GuessLaneSpacing()
    {
        var spawner = FindFirstObjectByType<NoteSpawner>();
        if (spawner != null && spawner.laneSpacing > 0f) return spawner.laneSpacing;
        return 1.5f;
    }

    private static CharacterDataSO[] BuildDefaultCharacterArray()
    {
        // 10 个：2 玩家 + 4 队伍（共享队伍数据，两侧各司其职）
        var arr = new CharacterDataSO[10];
        for (int side = 0; side < 2; side++)
        {
            arr[side * 5 + 0] = DefaultSkillBootstrap.MakeDefaultPlayer(side);
            for (int lane = 0; lane < 4; lane++)
                arr[side * 5 + 1 + lane] = DefaultSkillBootstrap.MakeDefaultTeam(side, lane);
        }
        return arr;
    }

    public int GetMaxHP(int side) => MaxHPBySide[Mathf.Clamp(side, 0, 1)];
    public float GetCombatSum(int side) => CombatSumBySide[Mathf.Clamp(side, 0, 1)];

    /// <summary>尝试施放指定 lane 的队伍角色主动技能（side 只支持 0 = 左侧玩家）。</summary>
    public bool TryCastActive(int side, int laneIndex)
    {
        if (side != 0) return false;
        var c = CharacterRoster.GetTeam(side, laneIndex);
        if (c == null || !c.HasActiveSkill) return false;
        if (c.currentEnergy < c.maxEnergy) return false;
        var skill = c.activeSkill;
        c.ConsumeEnergy(c.maxEnergy);
        Debug.Log($"[Skill] side {side} lane {laneIndex} 释放 {skill.displayName} ({skill.effectType})");
        // P2 末：在 ScoreManager 对对侧玩家造成 effectParamsJSON 里的 damage
        return true;
    }
}
