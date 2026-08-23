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
        const string path = "Assets/ScriptableObjects/DemoBeatmap.asset";
        BeatmapSO beatmap = AssetDatabase.LoadAssetAtPath<BeatmapSO>(path);
        if (beatmap == null)
        {
            beatmap = ScriptableObject.CreateInstance<BeatmapSO>();
            AssetDatabase.CreateAsset(beatmap, path);
        }

        beatmap.bpm = 128f;
        beatmap.notes = GenerateDemoNotes();

        EditorUtility.SetDirty(beatmap);
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

            // 基础节奏：同一时间、同一轨道，同时给左右玩家各生成一个音符。
            int lane = i % 4;
            AddMirroredNote(notes, t, lane);

            // 每 8 拍加一个双押
            if (i % 8 == 4)
            {
                AddMirroredNote(notes, t, (lane + 2) % 4);
            }

            // 每 16 拍加一个小连打
            if (i % 16 == 8)
            {
                for (int j = 0; j < 4; j++)
                {
                    AddMirroredNote(notes, t + j * (beat / 4f), j);
                }
            }
        }

        // 最后的大合唱：双方同时出现
        for (int i = 0; i < 16; i++)
        {
            float t = startTime + totalBeats * beat + i * beat;
            AddMirroredNote(notes, t, i % 4);
        }

        // 必须按时间排序
        notes.Sort((a, b) => a.time.CompareTo(b.time));
        return notes.ToArray();
    }

    private static void AddMirroredNote(List<NoteData> notes, float time, int lane)
    {
        notes.Add(new NoteData { time = time, lane = lane, side = 0 });
        notes.Add(new NoteData { time = time, lane = lane, side = 1 });
    }
}
