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
/// 表头列含义（兼容飞书新表与旧表）：
///   编号 / id                  → 角色编号
///   角色 / name                → 显示名
///   角色类型 / type            → 玩家自身角色 / 队伍角色（可选，缺则按 id 推断）
///   职业 / profession          → 职业
///   hp / 血量                  → 角色血量
///   战斗力 / combat            → 战斗力数值
///   能力1 / activeDesc         → 主动技能(必杀)描述（写回 SO.activeSkillDescription）
///   能力1.技能冷却 / cooldown   → 释放后冷却秒数（写回 SO.skillCooldown；0=用兜底 20s。技能库 SkillSO 不再持有 cooldown）
///   能力1.技能引用ID / activeSkillId → 反查 SkillSO 引用（可空，缺则 import 警告）
///   能力1.能量需求 / activeEnergy → 主动技能能量需求（"无" / 整数）
///   能力1.过热状态 / passiveDesc → 过热描述（写回 SO.passiveSkillDescription）
///   能力1.超级过热状态          → 超级过热描述（暂仅展示，写回 SO.passiveSkillDescription 末尾）
///   能力1.输入方式              → 输入序列（仅展示，运行时走 SkillInputUI）
///
/// 注：飞书新表为每个能力(能力1/能力2/能力3)都重复 技能冷却/技能引用ID/能量需求/过热状态/超级过热状态/输入方式 一组列。
///     导入器按"同名列首次出现"规则，只消费「能力1」那一组；能力2/能力3 目前仅作为设计备注保留，待运行时扩展多技能后启用。
///     activeSkillId / passiveSkillId（英文旧列名）仍兼容。
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
            "支持飞书新表列名（编号 / 角色 / 角色类型 / 职业 / hp / 战斗力 / 能力1 / 技能冷却 / 技能引用ID / 能量需求 / 过热状态 / 超级过热状态 / 输入方式；能力2/能力3 同名列为预留）。\n" +
            "若工程里已有 Skill Maker 生成的 SkillSO，可在「技能引用ID」列填 skillId 自动反查（英文 activeSkillId 旧列名仍兼容）。\n" +
            "「技能冷却」列填秒数（如 10）；「无 / — / 空」表示用兜底 20s（技能库 SkillSO 已不再持有 cooldown，冷却完全由本列决定）。导入器只消费「能力1」那一组列。",
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
        int iProfession  = FindHeader(header, "职业", "profession");
        int iHp          = FindHeader(header, "hp", "血量");
        int iCombat      = FindHeader(header, "战斗力", "combat");
        int iActiveDesc  = FindHeader(header, "能力1", "activedesc", "active_skill_description");
        int iActiveCost  = FindHeader(header, "能量需求", "activeenergy", "active_energy");
        int iPassiveDesc = FindHeader(header, "过热状态", "passivedesc", "passive_skill_description");
        int iSuperDesc   = FindHeader(header, "超级过热状态", "superoverheat", "super_fever");
        int iCooldown    = FindHeader(header, "技能冷却", "技能冷却（秒）", "cooldown", "skillcooldown");
        int iNotes       = FindHeader(header, "备注", "notes");
        int iActiveId    = FindHeader(header, "activeskillid", "技能引用ID", "skillid");
        int iPassiveId   = FindHeader(header, "passiveskillid");
        int iType        = FindHeader(header, "type", "isplayer");
        int iLane        = FindHeader(header, "lane", "laneindex");

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
            string activeDesc = CellStr(row, iActiveDesc, "");
            int activeCost;
            {
                string s = CellStr(row, iActiveCost, "100").Trim();
                if (s == "无" || s == "" || s == "-") activeCost = 0;
                else if (int.TryParse(s, out int n)) activeCost = n;
                else activeCost = 100;
            }
            string passiveDesc = CellStr(row, iPassiveDesc, "");
            string superDesc = CellStr(row, iSuperDesc, "");
            if (!string.IsNullOrEmpty(superDesc))
                passiveDesc = (passiveDesc + "\n" + superDesc).Trim();

            // 技能冷却：数值=秒；"无"/"—"/"空" → 0（运行时回退兜底 20s）。完全来自角色文档，技能库 SkillSO 已不再持有 cooldown。
            float cooldown = CellFloat(row, iCooldown, 0f);

            // isPlayer / laneIndex 推断
            bool isPlayer; int lane;
            if (id == 1) { isPlayer = true; lane = -1; }
            else if (id >= 2 && id <= 5) { isPlayer = false; lane = id - 2; }
            else
            {
                // 飞书表里更靠后的角色（编号 >5）：当前游戏只上场 5 名，标记为"仅花名册"（不占音轨、不进战斗），避免误占 lane 0
                isPlayer = (iType >= 0 && iType < row.Count && row[iType].Trim().ToLower() == "player");
                lane = (iLane >= 0 && iLane < row.Count && !string.IsNullOrWhiteSpace(row[iLane])) ? CellInt(row, iLane, -1) : -1;
            }

            string activeSkillId = CellStr(row, iActiveId, "");
            string passiveSkillId = CellStr(row, iPassiveId, "");

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
            so.activeSkill = string.IsNullOrEmpty(activeSkillId) ? null : FindSkill(activeSkillId);
            so.passiveSkill = string.IsNullOrEmpty(passiveSkillId) ? null : FindSkill(passiveSkillId);
            so.blockColor = CharacterPalette.GetColor(id); // 按 characterId 自动上身份色（宝宝灰/大狗橙/嘟嘟绿/爱格白/小黑黑）

            if (isNew) { AssetDatabase.CreateAsset(so, path); created++; }
            else { EditorUtility.SetDirty(so); updated++; }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[CharacterImporter] 完成：新建 {created}，更新 {updated}");
        EditorUtility.DisplayDialog("导入完成", $"新建 {created} 个，更新 {updated} 个角色。", "确定");
    }

    private static int FindHeader(List<string> header, params string[] candidates)
    {
        for (int i = 0; i < header.Count; i++)
        {
            // 表头可能带换行/空格注释（如 "hp\n（1hp=1hp换为全队血量）"），只取第一行并去空格后比较
            string raw = header[i];
            int nl = raw.IndexOf('\n');
            string h = (nl >= 0 ? raw.Substring(0, nl) : raw).Trim().ToLower().Replace(" ", "");
            foreach (var c in candidates)
                if (h == c.ToLower().Replace(" ", "")) return i;
        }
        return -1;
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
