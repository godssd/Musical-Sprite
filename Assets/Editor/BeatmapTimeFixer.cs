using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

/// <summary>
/// 谱面修复工具：把 .asset（游戏运行时真正加载的 BeatmapSO）里被平移/错乱的 note.time，
/// 按索引从同名的 .json（编辑器显示用的正确镜像）一一覆盖回去。
/// 只改 time 字段，保留 guid / holds / lanes / audioClip / markers 全部不动，
/// 因此场景里 Spawner 的 beatmap 引用（靠 guid）依然有效，无需重新拖拽。
///
/// 用法：Unity 菜单 Musical Sprite > 修复谱面时间(从JSON重建asset时间)
/// 会在 Console 打印每个谱面修复前后的首音符 time，方便确认是否真的存在平移。
/// </summary>
public class BeatmapTimeFixer : EditorWindow
{
    [MenuItem("Musical Sprite/修复谱面时间(从JSON重建asset时间)")]
    static void FixAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:BeatmapSO");
        if (guids.Length == 0)
        {
            Debug.LogWarning("[BeatmapTimeFixer] 工程内未找到任何 BeatmapSO 资产。");
            return;
        }

        int fixedCount = 0;
        foreach (var g in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(g);
            string jsonPath = Path.ChangeExtension(assetPath, ".json");
            if (!File.Exists(jsonPath))
            {
                Debug.Log($"[BeatmapTimeFixer] 跳过（无配套 .json）：{assetPath}");
                continue;
            }

            // 从 .json 取正确时间（顺序即音符顺序）
            string json = File.ReadAllText(jsonPath);
            var jsonTimes = new List<float>();
            foreach (Match m in Regex.Matches(json, "\"time\"\\s*:\\s*([-\\d.]+)"))
                jsonTimes.Add(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

            // 从 .asset 取当前时间（顺序即音符顺序）
            string assetText = File.ReadAllText(assetPath);
            var assetMatches = new List<Match>();
            foreach (Match m in Regex.Matches(assetText, "(?m)^\\s*time:\\s*([-\\d.]+)"))
                assetMatches.Add(m);

            if (jsonTimes.Count != assetMatches.Count)
            {
                Debug.LogWarning($"[BeatmapTimeFixer] 跳过（数量不一致，可能已损坏）：{assetPath}  json={jsonTimes.Count} asset={assetMatches.Count}");
                continue;
            }

            // 记录修复前首音符
            float beforeFirst = float.Parse(assetMatches[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

            // 从后往前替换，保证索引不偏移
            var sb = new StringBuilder(assetText);
            for (int i = assetMatches.Count - 1; i >= 0; i--)
            {
                int numStart = assetMatches[i].Groups[1].Index;
                int numLen = assetMatches[i].Groups[1].Length;
                sb.Remove(numStart, numLen);
                sb.Insert(numStart, jsonTimes[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }

            File.WriteAllText(assetPath, sb.ToString());
            AssetDatabase.ImportAsset(assetPath);

            float afterFirst = jsonTimes[0];
            Debug.Log($"[BeatmapTimeFixer] 已修复：{assetPath}  （{jsonTimes.Count} 个音符，首音符 time {beforeFirst} → {afterFirst}）");
            fixedCount++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[BeatmapTimeFixer] 完成，共修复 {fixedCount} 个谱面。若首音符 time 前后一致说明该谱面本就正确（无需改动）。");
    }
}
