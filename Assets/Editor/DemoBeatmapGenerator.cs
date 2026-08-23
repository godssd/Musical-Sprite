using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 编辑器工具：一键生成测试用谱面。
/// 菜单路径：Tools > Musical Sprite > Create Demo Beatmap
/// </summary>
public class DemoBeatmapGenerator : MonoBehaviour
{
    [MenuItem("Tools/Musical Sprite/Create Demo Beatmap")]
    public static void CreateDemoBeatmap()
    {
        BeatmapSO beatmap = ScriptableObject.CreateInstance<BeatmapSO>();
        beatmap.bpm = 128f;
        beatmap.notes = GenerateDemoNotes();

        string path = "Assets/ScriptableObjects/DemoBeatmap.asset";
        AssetDatabase.CreateAsset(beatmap, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = beatmap;

        Debug.Log($"测试谱面已生成：{path}");
    }

    private static NoteData[] GenerateDemoNotes()
    {
        List<NoteData> notes = new List<NoteData>();

        float bpm = 128f;
        float beat = 60f / bpm;       // 每拍时长
        float startTime = 2f;         // 前奏 2 秒
        int totalBeats = 120;         // 总共 120 拍

        for (int i = 0; i < totalBeats; i++)
        {
            float t = startTime + i * beat;

            // 基础节奏：每拍一个音符，轨道轮流；左右玩家谱面完全相同
            int lane = i % 4;
            notes.Add(new NoteData { time = t, lane = lane, side = 0 });
            notes.Add(new NoteData { time = t, lane = lane, side = 1 });

            // 每 8 拍加一个双押
            if (i % 8 == 4)
            {
                notes.Add(new NoteData { time = t, lane = (lane + 2) % 4, side = 0 });
                notes.Add(new NoteData { time = t, lane = (lane + 2) % 4, side = 1 });
            }

            // 每 16 拍加一个小连打
            if (i % 16 == 8)
            {
                for (int j = 0; j < 4; j++)
                {
                    notes.Add(new NoteData
                    {
                        time = t + j * (beat / 4f),
                        lane = j,
                        side = 0
                    });
                    notes.Add(new NoteData
                    {
                        time = t + j * (beat / 4f),
                        lane = j,
                        side = 1
                    });
                }
            }
        }

        // 最后的大合唱：双方同时出现，谱面相同
        for (int i = 0; i < 16; i++)
        {
            float t = startTime + totalBeats * beat + i * beat;
            notes.Add(new NoteData { time = t, lane = i % 4, side = 0 });
            notes.Add(new NoteData { time = t, lane = i % 4, side = 1 });
        }

        // 必须按时间排序
        notes.Sort((a, b) => a.time.CompareTo(b.time));
        return notes.ToArray();
    }
}
