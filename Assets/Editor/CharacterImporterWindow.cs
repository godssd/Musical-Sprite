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
/// 表头列含义（兼容中英文）：
///   编号 / id             → 角色编号
///   角色 / name           → 显示名
///   职业 / profession     → 职业
///   hp / 血量             → 角色血量
///   战斗力 / combat       → 战斗力数值
///   能力1 / activeDesc    → 主动技能描述（写回 SO.activeSkillDescription）
///   能量需求 / activeEnergy → 主动技能能量需求（"无" / 整数）
///   过热状态 / passiveDesc → 被动/过热描述（写回 SO.passiveSkillDescription）
///   备注 / notes          → 仅展示，不入 SO
///   activeSkillId         → 反查 SkillSO 引用（可空，缺则 import 警告）
///   passiveSkillId        → 反查 SkillSO 引用（可空）
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
            "支持飞书原表 1:1 列名（编号 / 角色 / 职业 / hp / 战斗力 / 能力1 / 能量需求 / 过热状态 / 备注）。\n" +
            "若工程里已有 Skill Maker 生成的 SkillSO，可额外列 activeSkillId / passiveSkillId 自动反查。",
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
        int iNotes       = FindHeader(header, "备注", "notes");
        int iActiveId    = FindHeader(header, "activeskillid");
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

            // isPlayer / laneIndex 推断
            bool isPlayer; int lane;
            if (id == 1) { isPlayer = true; lane = -1; }
            else if (id >= 2 && id <= 5) { isPlayer = false; lane = id - 2; }
            else
            {
                isPlayer = (iType >= 0 && iType < row.Count && row[iType].Trim().ToLower() == "player");
                lane = (iLane >= 0 && iLane < row.Count && !string.IsNullOrWhiteSpace(row[iLane])) ? CellInt(row, iLane, 0) : 0;
            }

            string activeSkillId = CellStr(row, iActiveId, "");
            string passiveSkillId = CellStr(row, iPassiveId, "");

            string path = dir + "/" + Sanitize(name) + "_" + id + ".asset";
            CharacterDataSO so = AssetDatabase.LoadAssetAtPath<CharacterDataSO>(path);
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
            string h = header[i].Trim().ToLower();
            foreach (var c in candidates)
                if (h == c.ToLower()) return i;
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
}
