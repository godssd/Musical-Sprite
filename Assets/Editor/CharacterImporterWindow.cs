using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// 角色导入器（Character Importer）。
/// 菜单：Tools > Musical Sprite > Character Importer。
/// 选择 Characters.xlsx → 解析 Sheet1 → 按 characterId 生成/更新 CharacterDataSO
/// 到 Assets/Data/Characters/，并把可选的 activeSkill / passiveSkill 按 skillId 反查为 SkillSO 引用。
///
/// 表头列含义（飞书新表，兼容旧表）：
///   编号 / id                  → 角色编号
///   角色 / name                → 显示名
///   角色类型 / type            → 玩家自身角色 / 队伍角色（可选，缺则按 id 推断）
///   职业 / profession          → 职业
///   hp / 血量                  → 角色血量
///   战斗力 / combat            → 战斗力数值
///   能力1 ~ 能力5              → 每个能力(能力N)独占一组 7 列：
///        能力N          → 该能力名称/描述（写回 SkillSlot.description）
///        技能冷却       → 释放后冷却秒数（写回 SkillSlot.cooldown；0/空=无冷却，立刻可再次释放）
///        技能引用ID     → 反查 SkillSO 引用（可空；空≠没有技能，见下）
///        能量需求（10分=1能量）→ 能量需求（"无"/空/0 = 无需能量，写回 energyCost=0）
///        过热状态       → 过热描述（写回 SkillSlot.overheatDesc）
///        超级过热状态   → 超级过热描述（写回 SkillSlot.superOverheatDesc）
///        输入方式       → 输入序列（←↓→/AAB 等；写回 SkillSlot.inputMethod）
///
/// 能力N 语义（与运行时一致）：
///   - 该「能力N」列组 技能引用ID / 输入方式 / 描述 全空  → 不存在该技能（视为没有）。
///   - 技能引用ID 非空          → 反查 SkillSO 引用（运行时效果由该 SkillSO 驱动）。
///   - 输入方式 非空            → 主动技能（按对序列 + 能量满 才释放）。
///   - 输入方式 空（但有内容）  → 被动技能（满足条件持续生效；P3 条件/效果路由）。
///   - 能量需求 = 无 / 空 / 0   → 无需能量：跳过充能 / 能量满 / 释放清空 的流程与表现。
///   注：被动技能与玩家主动技能通常没有 技能引用ID（由文案描述，并非 SkillSO 驱动），
///        因此不能只用 技能引用ID 判"是否存在"——只要有 输入方式 或 描述 即视为存在该能力。
///
/// 导入器消费全部 能力1~能力5 五组列，写入 SO.skills[5]（索引 0=能力1 … 4=能力5）。
/// 同时为向后兼容，从「首个主动槽」派生 SO.activeSkill / activeEnergyCost / skillCooldown 旧字段。
/// hp / 战斗力 表头带括号注释（如 hp（1比1转换为全队血量）），导入器会自动剥离括号注释后匹配。
///
/// isPlayer / laneIndex 由 importer 按 characterId 推断：
///   id == 1 → isPlayer = true, laneIndex = -1
///   id in {2..5} → isPlayer = false, laneIndex = id - 2
///   其他 id → 按表头 isPlayer / lane 列（若存在）；否则默认 false, 0
///
/// 依赖：SimpleXlsx（零依赖 xlsx 读取）。SkillSO 需先由 Skill Maker 生成。
/// </summary>
public class CharacterImporterWindow : EditorWindow
{
    private string xlsxPath = "";
    private Vector2 scroll;
    private List<List<string>> preview;

    [MenuItem("Tools/Musical Sprite/Character Importer")]
    public static void ShowWindow()
    {
        GetWindow<CharacterImporterWindow>("Character Importer");
    }

    private void OnGUI()
    {
        GUILayout.Label("角色导入器 (Character Importer)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "选择 Characters.xlsx → 解析 → 按 characterId 生成/更新 CharacterDataSO 到 Assets/Data/Characters/。\n" +
            "支持飞书新表列名（编号 / 角色 / 角色类型 / 职业 / hp / 战斗力 / 能力1~能力5；每组含 技能冷却 / 技能引用ID / 能量需求 / 过热状态 / 超级过热状态 / 输入方式）。\n" +
            "导入器消费全部 能力1~能力5 五组列，写入 SO.skills[5]。语义：技能引用ID/输入方式/描述 全空 = 无该技能；能量需求「无/空/0」= 无能量；输入方式空 = 被动技能。\n" +
            "「技能冷却」列填秒数（如 10）；「无 / — / 空 / 留空」= 无冷却（立刻可再次释放）。若工程里已有 Skill Maker 生成的 SkillSO，可在「技能引用ID」列填 skillId 自动反查。\n" +
            "hp / 战斗力 / 能量需求 等带括号注释的表头会被自动剥离注释后匹配。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        xlsxPath = EditorGUILayout.TextField("xlsx 路径", xlsxPath);
        if (GUILayout.Button("选择", GUILayout.Width(60)))
        {
            string p = EditorUtility.OpenFilePanel("选择 Characters.xlsx", "", "xlsx");
            if (!string.IsNullOrEmpty(p)) xlsxPath = p;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("预览", GUILayout.Width(80))) Preview();
        if (GUILayout.Button("导入", GUILayout.Width(80))) Import();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(8);
        if (preview != null)
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var row in preview)
                EditorGUILayout.LabelField(string.Join(" | ", row));
            EditorGUILayout.EndScrollView();
        }
    }

    private void Preview()
    {
        if (!File.Exists(xlsxPath)) { Debug.LogError("[CharacterImporter] 文件不存在: " + xlsxPath); return; }
        try
        {
            preview = SimpleXlsx.ReadSheet(xlsxPath, 0);
            Debug.Log("[CharacterImporter] 预览成功，共 " + preview.Count + " 行");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[CharacterImporter] 解析失败: " + e);
        }
    }

    private void Import()
    {
        if (!File.Exists(xlsxPath)) { Debug.LogError("[CharacterImporter] 文件不存在"); return; }
        List<List<string>> rows;
        try { rows = SimpleXlsx.ReadSheet(xlsxPath, 0); }
        catch (System.Exception e) { Debug.LogError("[CharacterImporter] 解析失败: " + e); return; }

        if (rows.Count < 2) { Debug.LogWarning("[CharacterImporter] 无数据行（至少需表头 + 1 行）"); return; }

        var header = rows[0];
        int iId          = FindHeader(header, "编号", "id");
        int iName        = FindHeader(header, "角色", "name", "displayname");
        int iType        = FindHeader(header, "角色类型", "type", "isplayer");
        int iProfession  = FindHeader(header, "职业", "profession");
        int iHp          = FindHeader(header, "hp", "血量");
        int iCombat      = FindHeader(header, "战斗力", "combat");
        int iLane        = FindHeader(header, "lane", "laneindex");
        int iNotes       = FindHeader(header, "备注", "notes");

        // 能力1~能力5：每组重复 技能冷却 / 技能引用ID / 能量需求 / 过热状态 / 超级过热状态 / 输入方式 各一列；
        // 另有一列「能力N」标题列。用 FindHeaderNth 取第 a 次出现（a=0..4）。
        FindHeaderNth(header, 5, out var iCooldown, "技能冷却", "cooldown", "skillcooldown");
        FindHeaderNth(header, 5, out var iSkillId,  "技能引用ID", "技能引用id", "skillid", "activeskillid");
        FindHeaderNth(header, 5, out var iEnergy,   "能量需求", "activeenergy", "active_energy", "energy");
        FindHeaderNth(header, 5, out var iOverheat, "过热状态", "passivedesc", "passive_skill_description");
        FindHeaderNth(header, 5, out var iSuper,    "超级过热状态", "superoverheat", "super_fever");
        FindHeaderNth(header, 5, out var iInput,    "输入方式", "inputmethod", "input");
        FindHeaderPrefixNth(header, 5, out var iAbilityName, "能力"); // 能力1..能力5 标题列（前缀匹配）

        if (iId < 0) { Debug.LogError("[CharacterImporter] 表头缺少「编号」列（或 id）"); return; }

        string dir = "Assets/Data/Characters";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        int created = 0, updated = 0;
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (iId >= row.Count) continue;
            string idStr = row[iId];
            if (string.IsNullOrWhiteSpace(idStr)) continue;

            if (!int.TryParse(idStr.Trim(), out int id))
            {
                Debug.LogWarning($"[CharacterImporter] 行 {r + 1} 编号无效（{idStr}），跳过");
                continue;
            }

            // 跳过空白行（飞书表里占位的空行：编号有，但「角色」与「hp」都空 → 不是真实角色，避免生成垃圾资产）
            string rawName = (iName >= 0 && iName < row.Count) ? (row[iName] ?? "") : "";
            string rawHp = (iHp >= 0 && iHp < row.Count) ? (row[iHp] ?? "") : "";
            if (string.IsNullOrWhiteSpace(rawName) && string.IsNullOrWhiteSpace(rawHp))
            {
                Debug.Log($"[CharacterImporter] 行 {r + 1} 为空行（无角色名且无 hp），跳过");
                continue;
            }

            string name = CellStr(row, iName, "Char" + id);
            string profession = CellStr(row, iProfession, "");
            int hp = CellInt(row, iHp, 100);
            float combat = CellFloat(row, iCombat, 10f);

            // isPlayer / laneIndex 推断
            bool isPlayer; int lane;
            if (id == 1) { isPlayer = true; lane = -1; }
            else if (id >= 2 && id <= 5) { isPlayer = false; lane = id - 2; }
            else
            {
                // 飞书表里更靠后的角色（编号 >5）：当前游戏只上场 5 名，标记为"仅花名册"（不占音轨、不进战斗），避免误占 lane 0
                isPlayer = (iType >= 0 && iType < row.Count && !string.IsNullOrWhiteSpace(row[iType]) && row[iType].Trim().ToLower() == "player");
                lane = (iLane >= 0 && iLane < row.Count && !string.IsNullOrWhiteSpace(row[iLane])) ? CellInt(row, iLane, -1) : -1;
            }

            // ---- 能力1~能力5：消费五组列，写入 skills[5] ----
            var skills = new SkillSlot[5];
            SkillSlot firstActive = null;
            SkillSlot firstPassive = null;
            for (int a = 0; a < 5; a++)
            {
                string skillId  = (iSkillId[a] >= 0)     ? CellStr(row, iSkillId[a], "") : "";
                string inputM   = (iInput[a] >= 0)       ? CellStr(row, iInput[a], "") : "";
                string desc     = (iAbilityName[a] >= 0) ? CellStr(row, iAbilityName[a], "") : "";
                string overheat = (iOverheat[a] >= 0)    ? CellStr(row, iOverheat[a], "") : "";
                string super    = (iSuper[a] >= 0)       ? CellStr(row, iSuper[a], "") : "";
                int energy      = (iEnergy[a] >= 0)      ? ParseEnergyCost(row, iEnergy[a]) : 0;
                float cd        = (iCooldown[a] >= 0)    ? CellFloat(row, iCooldown[a], 0f) : 0f;

                var slot = new SkillSlot
                {
                    skillId = skillId,
                    energyCost = energy,
                    cooldown = cd,
                    inputMethod = inputM,
                    description = desc,
                    overheatDesc = overheat,
                    superOverheatDesc = super,
                };
                // 反查 SkillSO（仅当有 skillId；被动/玩家主动可能无 skillId → skill=null，由文案/玩家指令驱动）
                slot.skill = string.IsNullOrEmpty(skillId) ? null : FindSkill(skillId);
                skills[a] = slot;

                if (!slot.Exists) continue;
                if (slot.IsPassive) { if (firstPassive == null) firstPassive = slot; }
                else { if (firstActive == null) firstActive = slot; }
            }

            // ---- 向后兼容：从首个主动槽派生旧字段（技能库 SkillSO 不再持有 cooldown/energy，完全来自角色文档）----
            int activeCost = (firstActive != null && firstActive.NeedsEnergy) ? firstActive.energyCost : 0;
            float cooldown = (firstActive != null) ? firstActive.cooldown : 0f;
            SkillSO activeSO = (firstActive != null) ? firstActive.skill : null;
            string activeDesc = (firstActive != null) ? firstActive.description : "";
            string passiveDesc = (firstPassive != null)
                ? (firstPassive.description + (string.IsNullOrEmpty(firstPassive.overheatDesc) ? "" : "\n" + firstPassive.overheatDesc))
                : "";

            string path = dir + "/" + Sanitize(name) + "_" + id + ".asset";
            CharacterDataSO so = AssetDatabase.LoadAssetAtPath<CharacterDataSO>(path);
            if (so == null) so = FindExistingByCharacterId(dir, id); // 同 id 但文件名不同（避免重复创建垃圾资产）
            bool isNew = so == null;
            if (isNew) so = ScriptableObject.CreateInstance<CharacterDataSO>();

            so.characterId = id;
            so.displayName = name;
            so.profession = profession;
            so.isPlayer = isPlayer;
            so.laneIndex = lane;
            so.maxHP = hp;
            so.combatPower = combat;
            so.activeSkillDescription = activeDesc;
            so.activeEnergyCost = activeCost;
            so.passiveSkillDescription = passiveDesc;
            so.skillCooldown = cooldown;
            so.activeSkill = activeSO;
            so.passiveSkill = (firstPassive != null) ? firstPassive.skill : null;
            so.skills = skills;   // 新多技能槽（能力1~能力5）
            so.blockColor = CharacterPalette.GetColor(id); // 按 characterId 自动上身份色（宝宝灰/大狗橙/嘟嘟绿/爱格白/小黑黑）

            if (isNew) { AssetDatabase.CreateAsset(so, path); created++; }
            else { EditorUtility.SetDirty(so); updated++; }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[CharacterImporter] 完成：新建 {created}，更新 {updated}（已写入技能槽 skills[5]）");
        EditorUtility.DisplayDialog("导入完成", $"新建 {created} 个，更新 {updated} 个角色。\n已按 能力1~能力5 写入 skills[5] 多技能槽。", "确定");
    }

    /// <summary>解析「能量需求」单元格：无/空/-/—/空 = 0（无能量，跳过充能/清空）；整数原样；无法解析 → 0。</summary>
    private static int ParseEnergyCost(List<string> row, int idx)
    {
        string s = CellStr(row, idx, "0").Trim();
        if (s == "无" || s == "" || s == "-" || s == "—" || s == "空") return 0;
        if (int.TryParse(s, out int n)) return n;
        return 0; // 无法解析 → 视为无能量
    }

    /// <summary>归一化表头：去换行、去括号注释（全角（）/半角()）、去空格、转小写。
    /// 例： "hp（1比1转换为全队血量）" → "hp"；"能量需求（10分=1能量）" → "能量需求"。</summary>
    private static string NormalizeHeader(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string h = raw;
        int nl = h.IndexOf('\n'); if (nl >= 0) h = h.Substring(0, nl);   // 只取第一行
        int p = h.IndexOf('（'); if (p < 0) p = h.IndexOf('(');          // 剥离括号注释
        if (p >= 0) h = h.Substring(0, p);
        return h.Trim().ToLower().Replace(" ", "");
    }

    private static int FindHeader(List<string> header, params string[] candidates)
    {
        for (int i = 0; i < header.Count; i++)
        {
            string h = NormalizeHeader(header[i]);
            foreach (var c in candidates)
                if (h == NormalizeHeader(c)) return i;
        }
        return -1;
    }

    /// <summary>返回某候选名在表头中第 0..count-1 次出现的列索引（用于重复的 能力1~能力5 子列）。找不到的位置填 -1。</summary>
    private static void FindHeaderNth(List<string> header, int count, out int[] result, params string[] candidates)
    {
        result = new int[count];
        for (int k = 0; k < count; k++) result[k] = -1;
        int found = 0;
        for (int i = 0; i < header.Count && found < count; i++)
        {
            string h = NormalizeHeader(header[i]);
            foreach (var c in candidates)
            {
                if (h == NormalizeHeader(c)) { result[found++] = i; break; }
            }
        }
    }

    /// <summary>返回表头中以 prefix 开头的第 0..count-1 个列索引（用于 能力1..能力5 标题列）。找不到的位置填 -1。</summary>
    private static void FindHeaderPrefixNth(List<string> header, int count, out int[] result, string prefix)
    {
        result = new int[count];
        for (int k = 0; k < count; k++) result[k] = -1;
        int found = 0;
        string np = NormalizeHeader(prefix);
        for (int i = 0; i < header.Count && found < count; i++)
        {
            if (NormalizeHeader(header[i]).StartsWith(np)) result[found++] = i;
        }
    }

    private static string CellStr(List<string> row, int idx, string fallback)
    {
        if (idx < 0 || idx >= row.Count) return fallback;
        var s = row[idx];
        return string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();
    }

    private static int CellInt(List<string> row, int idx, int fallback)
    {
        if (idx < 0 || idx >= row.Count) return fallback;
        var s = row[idx];
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        return int.TryParse(s.Trim(), out var v) ? v : fallback;
    }

    private static float CellFloat(List<string> row, int idx, float fallback)
    {
        if (idx < 0 || idx >= row.Count) return fallback;
        var s = row[idx];
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        return float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static SkillSO FindSkill(string skillId)
    {
        var guids = AssetDatabase.FindAssets("t:SkillSO");
        foreach (var g in guids)
        {
            var so = AssetDatabase.LoadAssetAtPath<SkillSO>(AssetDatabase.GUIDToAssetPath(g));
            if (so != null && so.skillId == skillId) return so;
        }
        Debug.LogWarning("[CharacterImporter] 找不到技能 skillId=" + skillId + "（请先用 Skill Maker 生成）");
        return null;
    }

    private static string Sanitize(string s) => string.IsNullOrEmpty(s) ? "Char" : s.Replace(" ", "_");

    /// <summary>在 dir 内按 characterId 查找已存在的 CharacterDataSO（文件名与 characterId 不一定同名时用）。</summary>
    private static CharacterDataSO FindExistingByCharacterId(string dir, int id)
    {
        if (!AssetDatabase.IsValidFolder(dir)) return null;
        var guids = AssetDatabase.FindAssets("t:CharacterDataSO", new[] { dir });
        foreach (var g in guids)
        {
            var so = AssetDatabase.LoadAssetAtPath<CharacterDataSO>(AssetDatabase.GUIDToAssetPath(g));
            if (so != null && so.characterId == id) return so;
        }
        return null;
    }
}
