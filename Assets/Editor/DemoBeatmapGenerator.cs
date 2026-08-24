using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 编辑器工具：一键生成测试用随机谱面。
/// 菜单路径：Tools > Musical Sprite > Create Demo Beatmap
///
/// 节奏约束（让谱面"踩在拍子上"，不再杂乱）：
/// - BPM 固定 128，一拍 = 60/128 ≈ 0.469s。
/// - 所有音符时刻吸附到拍网格：整数拍、半拍（八分）、四分（十六分）三种网格。
/// - 每次生成会在"整拍 / 半拍 / 四分"三种细分中随机选一个作为本次基础网格，
///   同时整体拍点集合也会混入少量更粗/更细的网格，避免每首都一样密或一样疏。
/// - 密度三档（稀疏 / 中等 / 密集）决定在可用拍点里填充多少比例。
/// - 左右玩家谱面完全相同（同一份 time/lane 同时写入 side=0 与 side=1）。
/// </summary>
public class DemoBeatmapGenerator : MonoBehaviour
{
    public enum Density { Sparse, Medium, Dense }

    private const float Bpm = 128f;
    private const float LeadIn = 2f;          // 前奏留白（秒），也用于把首个音符推到整拍之后
    private const float Tail = 2f;            // 结尾留白（秒）

    [MenuItem("Tools/Musical Sprite/Create Demo Beatmap")]
    public static void CreateDemoBeatmap()
    {
        // 默认中等密度
        CreateDemoBeatmapWithBeats(120, Density.Medium);
    }

    // 调试窗口调用：totalBeats 决定谱面总拍数（时间窗口长度），density 决定密度档位。
    public static BeatmapSO CreateDemoBeatmapWithBeats(int totalBeats, Density density = Density.Medium)
    {
        BeatmapSO beatmap = ScriptableObject.CreateInstance<BeatmapSO>();
        beatmap.bpm = Bpm;
        beatmap.notes = GenerateBeatmap(totalBeats, density);

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
        float window = LeadIn + totalBeats * beatDur + Tail;
        Debug.Log($"随机测试谱面已生成：{path}，共 {beatmap.notes.Length / 2} 个音符（左右各一份），BPM {Bpm}，密度={density}，时间窗口≈{window:F1}s");
        return beatmap;
    }

    private static NoteData[] GenerateBeatmap(int totalBeats, Density density)
    {
        float beatDur = 60f / Bpm;
        float endTime = LeadIn + totalBeats * beatDur;

        // 1) 构建拍点集合：以十六分音符（最小网格）为基底，全部吸附到拍。
        //    再按"本次基础细分"做稀疏化，并人为混入少量其他细分的拍点，增加变化。
        int baseSubdiv = PickRandomSubdivision(); // 1=整拍, 2=半拍, 4=四分
        List<float> grid = new List<float>();
        for (float t = LeadIn; t <= endTime - 1e-4f; t += beatDur / 4f)
        {
            float beatPos = (t - LeadIn) / beatDur;          // 距离开头的拍数（浮点）
            bool onBase = Mathf.Abs(beatPos * (float)baseSubdiv - Mathf.Round(beatPos * baseSubdiv)) < 1e-3f;
            // 基础细分点必留；再随机保留约 15% 的更细点，制造少量切分/连打变化
            if (onBase || Random.value < 0.15f)
                grid.Add(t);
        }
        if (grid.Count == 0) grid.Add(LeadIn + beatDur); // 兜底

        // 2) 按密度决定保留多少拍点
        float keepRatio = density == Density.Sparse ? 0.35f : density == Density.Medium ? 0.6f : 0.9f;
        List<float> usedTimes = new List<float>();
        foreach (float t in grid)
        {
            if (Random.value <= keepRatio) usedTimes.Add(t);
        }
        usedTimes.Sort();

        // 3) 生成音符：时刻已在拍上；轨道完全随机；左右完全相同。
        //    - 约 10% 为小型点击音符（SmallTap，半径更小、统一 PASS）
        //    - 约 12% 为长按音符（Hold）：其中约 40% 为多节点（3~4 节点），其余为 2 节点（head→tail）
        //    - 其余为普通 Tap
        List<NoteData> notes = new List<NoteData>();
        foreach (float t in usedTimes)
        {
            int lane = Random.Range(0, 4);
            float roll = Random.value;
            if (roll < 0.10f)
            {
                // 小型点击音符
                notes.Add(MakeSmallTap(t, lane, 0));
                notes.Add(MakeSmallTap(t, lane, 1));
            }
            else if (roll < 0.22f)
            {
                // 长按音符
                int nodeCount = Random.value < 0.4f ? Random.Range(3, 5) : 2; // 2 或 3~4 节点
                int[] lanes;
                float[] times;
                if (nodeCount <= 2)
                {
                    int endLane = Random.Range(0, 4);
                    int holdBeats = Random.Range(1, 5);
                    float dur = holdBeats * beatDur;
                    lanes = new int[] { lane, endLane };
                    times = new float[] { t, t + dur };
                }
                else
                {
                    lanes = new int[nodeCount];
                    times = new float[nodeCount];
                    lanes[0] = lane;
                    times[0] = t;
                    int curLane = lane;
                    float curTime = t;
                    for (int i = 1; i < nodeCount; i++)
                    {
                        // 每个节点随机换轨（0~3），时长吸附到整拍（1~3 拍）
                        curLane = Random.Range(0, 4);
                        int segBeats = Random.Range(1, 4);
                        curTime += segBeats * beatDur;
                        lanes[i] = curLane;
                        times[i] = curTime;
                    }
                }
                notes.Add(MakeHold(t, lane, 0, lanes, times));
                notes.Add(MakeHold(t, lane, 1, lanes, times));
            }
            else
            {
                notes.Add(MakeNote(t, lane, 0));
                notes.Add(MakeNote(t, lane, 1));
            }
        }

        notes.Sort((a, b) => a.time.CompareTo(b.time));
        return notes.ToArray();
    }

    private static NoteData MakeNote(float time, int lane, int side)
    {
        return new NoteData
        {
            time = time,
            lane = lane,
            side = side,
            type = NoteData.NoteType.Tap
        };
    }

    private static NoteData MakeSmallTap(float time, int lane, int side)
    {
        return new NoteData
        {
            time = time,
            lane = lane,
            side = side,
            type = NoteData.NoteType.SmallTap
        };
    }

    private static NoteData MakeHold(float time, int lane, int side, int[] lanes, float[] times)
    {
        return new NoteData
        {
            time = time,
            lane = lane,
            side = side,
            type = NoteData.NoteType.Hold,
            holdLanes = (int[])lanes.Clone(),
            holdTimes = (float[])times.Clone()
        };
    }

    // 随机选一种基础细分：整拍(1) / 半拍(2) / 四分(4)
    private static int PickRandomSubdivision()
    {
        int r = Random.Range(0, 3);
        return r == 0 ? 1 : r == 1 ? 2 : 4;
    }
}
