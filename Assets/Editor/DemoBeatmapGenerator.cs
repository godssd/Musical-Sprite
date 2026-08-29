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
/// - 所有音符时刻吸附到整拍，不会落在第 n 拍与第 n+1 拍之间。
/// - 密度三档（稀疏 / 中等 / 密集）决定在可用拍点里填充多少比例。
/// - 左右玩家谱面完全相同（同一份 time/lane 同时写入 side=0 与 side=1）。
/// </summary>
public class DemoBeatmapGenerator : MonoBehaviour
{
    public enum Density { Sparse, Medium, Dense }

    internal const float Bpm = 128f;
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
        MusicalSprite.Editor.BeatmapEditorWindow.ExportBeatmapText(beatmap, path);
        AssetDatabase.Refresh();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = beatmap;

        float beatDur = 60f / Bpm;
        float window = LeadIn + totalBeats * beatDur + Tail;
        Debug.Log($"随机测试谱面已生成：{path}，共 {beatmap.notes.Length / 2} 个音符（左右各一份），BPM {Bpm}，密度={density}，时间窗口≈{window:F1}s");
        return beatmap;
    }

    internal static NoteData[] GenerateBeatmap(int totalBeats, Density density)
    {
        float beatDur = 60f / Bpm;
        float endTime = LeadIn + totalBeats * beatDur;

        // 1) 构建整拍集合。使用全局歌曲时间计算，避免 LeadIn 偏移导致音符落在拍间。
        List<float> grid = new List<float>();
        int firstBeat = Mathf.CeilToInt(LeadIn / beatDur);
        int lastBeat = Mathf.FloorToInt(endTime / beatDur);
        for (int beat = firstBeat; beat <= lastBeat; beat++)
            grid.Add(beat * beatDur);
        if (grid.Count == 0) grid.Add(Mathf.Ceil(LeadIn / beatDur) * beatDur); // 兜底

        // 2) 按密度决定保留多少拍点
        float keepRatio = density == Density.Sparse ? 0.35f : density == Density.Medium ? 0.6f : 0.9f;
        List<float> usedTimes = new List<float>();
        foreach (float t in grid)
        {
            if (Random.value <= keepRatio) usedTimes.Add(t);
        }
        usedTimes.Sort();

        // 3) 生成音符：时刻已在拍上；左右完全相同。
        //    - 约 10% 为小圈点击 SmallTap，和普通 Tap 保留大小区分。
        //    - 约 12% 为单轨长按 Hold。
        //    - 约 18% 为连轨 Linked：固定占相邻两轨，包含双轨点击、直线长按和跨轨长按。
        //    - 其余为普通大圈 Tap。
        List<NoteData> notes = new List<NoteData>();
        float linkedBlockedUntil = -1f;
        foreach (float t in usedTimes)
        {
            // 连轨长按占用整个时间范围，范围内不再生成点击或其它音符。
            if (t <= linkedBlockedUntil + 1e-4f) continue;

            int lane = Random.Range(0, 4);
            float roll = Random.value;
            if (roll < 0.10f)
            {
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
            else if (roll < 0.40f)
            {
                int pairLane = Random.Range(0, 3); // 0=轨0+1，1=轨1+2，2=轨2+3
                if (Random.value < 0.45f)
                {
                    // 图 1/2：不区分大小圈，统一为双轨点击。
                    notes.Add(MakeLinked(t, pairLane, 0, null, null));
                    notes.Add(MakeLinked(t, pairLane, 1, null, null));
                }
                else
                {
                    // 图 3/4/5：双轨按住；节点的 pairLane 改变时自然形成跨轨。
                    int nodeCount = Random.value < 0.55f ? 2 : 3;
                    int[] pairLanes = new int[nodeCount];
                    float[] times = new float[nodeCount];
                    pairLanes[0] = pairLane;
                    times[0] = t;
                    for (int i = 1; i < nodeCount; i++)
                    {
                        pairLanes[i] = Random.Range(0, 3);
                        times[i] = times[i - 1] + Random.Range(1, 4) * beatDur;
                    }
                    notes.Add(MakeLinked(t, pairLane, 0, pairLanes, times));
                    notes.Add(MakeLinked(t, pairLane, 1, pairLanes, times));
                    linkedBlockedUntil = times[times.Length - 1];
                }
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

    private static NoteData MakeLinked(float time, int pairLane, int side, int[] lanes, float[] times)
    {
        return new NoteData
        {
            time = time,
            lane = pairLane,
            side = side,
            type = NoteData.NoteType.Linked,
            holdLanes = lanes != null ? (int[])lanes.Clone() : null,
            holdTimes = times != null ? (float[])times.Clone() : null
        };
    }

}
