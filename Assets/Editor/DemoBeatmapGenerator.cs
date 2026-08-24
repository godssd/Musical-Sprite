using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 编辑器工具：一键生成测试用随机谱面。
/// 菜单路径：Tools > Musical Sprite > Create Demo Beatmap
///
/// 随机约束：
/// - BPM 固定 128。
/// - 音符总数在 100~180 之间随机。
/// - 每个音符的出现时刻与所在轨道（0~3）完全随机。
/// - 同一侧（side）相邻音符最小间隔 0.18s，避免重叠到无法判定。
/// - 左右玩家谱面完全相同（同一份 time/lane 同时写入 side=0 与 side=1）。
/// </summary>
public class DemoBeatmapGenerator : MonoBehaviour
{
    private const float Bpm = 128f;
    private const int MinNotes = 100;
    private const int MaxNotes = 180;
    private const float LeadIn = 2f;          // 前奏留白（秒）
    private const float MinGap = 0.18f;        // 同侧相邻音符最小时间间隔（秒）

    [MenuItem("Tools/Musical Sprite/Create Demo Beatmap")]
    public static void CreateDemoBeatmap()
    {
        CreateDemoBeatmapWithBeats(120);
    }

    // 保留旧签名给调试窗口调用：totalBeats 仅用于决定时间窗口下限长度，
    // 实际音符数量由随机约束（100~180）决定。
    public static BeatmapSO CreateDemoBeatmapWithBeats(int totalBeats)
    {
        BeatmapSO beatmap = ScriptableObject.CreateInstance<BeatmapSO>();
        beatmap.bpm = Bpm;
        beatmap.notes = GenerateRandomNotes(totalBeats);

        string path = "Assets/Beatmaps/DemoBeatmap.asset";
        if (!AssetDatabase.IsValidFolder("Assets/Beatmaps"))
        {
            AssetDatabase.CreateFolder("Assets", "Beatmaps");
        }
        // 删除旧资源（如果存在）再创建，避免 GUID 冲突或重复资源
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(beatmap, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = beatmap;

        float beatDur = 60f / Bpm;
        float windowFromBeats = LeadIn + totalBeats * beatDur;
        Debug.Log($"随机测试谱面已生成：{path}，共 {beatmap.notes.Length / 2} 个音符（左右各一份），BPM {Bpm}，时间窗口≈{windowFromBeats:F1}s");
        return beatmap;
    }

    private static NoteData[] GenerateRandomNotes(int totalBeats)
    {
        float beatDur = 60f / Bpm;

        // 随机音符总数（左右各一份，所以最终数组长度是此值的 2 倍）
        int noteCount = Random.Range(MinNotes, MaxNotes + 1);

        // 时间窗口：至少能容纳 noteCount 个（最小间隔）音符，且不低于 totalBeats 给出的长度
        float windowFromCount = LeadIn + noteCount * MinGap + 4f;
        float windowFromBeats = LeadIn + totalBeats * beatDur;
        float endTime = Mathf.Max(windowFromCount, windowFromBeats);

        // 用拒绝采样生成互不重叠（间隔 >= MinGap）的随机时刻
        List<float> times = new List<float>();
        int attempts = 0;
        int maxAttempts = noteCount * 40;
        while (times.Count < noteCount && attempts < maxAttempts)
        {
            attempts++;
            float t = LeadIn + Random.value * (endTime - LeadIn);
            bool ok = true;
            for (int k = 0; k < times.Count; k++)
            {
                if (Mathf.Abs(times[k] - t) < MinGap) { ok = false; break; }
            }
            if (ok) times.Add(t);
        }
        times.Sort();

        // 生成音符：时刻与轨道完全随机；左右两侧谱面完全相同。
        // 约 12% 概率为长按音符（Hold）：随机结束音轨（可跨轨）+ 随机持续时长。
        List<NoteData> notes = new List<NoteData>();
        for (int i = 0; i < times.Count; i++)
        {
            int lane = Random.Range(0, 4);
            bool isHold = Random.value < 0.12f;
            if (isHold)
            {
                int endLane = Random.Range(0, 4);
                float dur = Random.Range(0.4f, 1.2f);
                notes.Add(new NoteData
                {
                    time = times[i],
                    lane = lane,
                    side = 0,
                    type = NoteData.NoteType.Hold,
                    holdDuration = dur,
                    holdEndLane = endLane
                });
                notes.Add(new NoteData
                {
                    time = times[i],
                    lane = lane,
                    side = 1,
                    type = NoteData.NoteType.Hold,
                    holdDuration = dur,
                    holdEndLane = endLane
                });
            }
            else
            {
                notes.Add(new NoteData { time = times[i], lane = lane, side = 0 });
                notes.Add(new NoteData { time = times[i], lane = lane, side = 1 });
            }
        }

        notes.Sort((a, b) => a.time.CompareTo(b.time));
        return notes.ToArray();
    }
}
