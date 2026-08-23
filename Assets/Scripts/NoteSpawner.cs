using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// 音符发射器 + 判定器。
/// 每个玩家各挂一个：左玩家 side=0，右玩家 side=1。
/// 负责：按谱面生成属于自己 side 的音符、移动、判定命中/漏击、通知中线移动。
/// 输入由外部调用 TriggerLaneInput() 触发（键盘、触摸、AI 均可）。
/// </summary>
public class NoteSpawner : MonoBehaviour
{
    [Header("核心引用")]
    public Conductor conductor;
    public BeatmapSO beatmap;
    public BattleCenterLine centerLine;

    [Header("发射器身份")]
    [Tooltip("0 = 左玩家要接的音符，1 = 右玩家要接的音符")]
    public int side = 0;

    [Header("生成与判定位置")]
    public Transform spawnPoint;    // 音符生成点（对方半场远端）
    public Transform hitPoint;      // 判定线（自己半场黄线）

    [Header("音符预制体")]
    public GameObject notePrefab;

    [Header("时间参数")]
    [Tooltip("音符从生成到抵达判定线需要多少秒")]
    public float leadTime = 2f;

    [Tooltip("判定窗口（±秒）。total window = hitWindow * 2")]
    public float hitWindow = 0.15f;

    [Header("轨道参数")]
    [Tooltip("轨道数量")]
    public int laneCount = 4;

    [Tooltip("相邻轨道在 Z 轴上的间距")]
    public float laneSpacing = 1f;

    [Header("键盘输入键位（PC 测试用）")]
    [Tooltip("每条轨道对应的按键")]
    public KeyCode[] keys = new KeyCode[4] { KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F };

    // 判定事件：side, lane, rank, position
    public event Action<int, int, string, Vector3> OnJudge;

    // 输入事件：side, lane。任意输入（键盘/触摸/AI）触发轨道时调用
    public event Action<int, int> OnLanePress;

    private List<Note> activeNotes = new List<Note>();
    private int spawnIndex = 0;
    private Vector3[] laneOffsets;

    void Start()
    {
        laneOffsets = new Vector3[laneCount];
        for (int i = 0; i < laneCount; i++)
        {
            float z = (i - (laneCount - 1) * 0.5f) * laneSpacing;
            laneOffsets[i] = new Vector3(0f, 0f, z);
        }
    }

    void Update()
    {
        if (conductor == null || beatmap == null || beatmap.notes == null) return;

        float songTime = conductor.songPosition;

        // 1. 到时间就生成音符
        while (spawnIndex < beatmap.notes.Length)
        {
            NoteData data = beatmap.notes[spawnIndex];
            if (songTime < data.time - leadTime) break;

            if (data.side == side)
            {
                SpawnNote(data);
            }
            spawnIndex++;
        }

        // 2. 漏击检测
        for (int i = activeNotes.Count - 1; i >= 0; i--)
        {
            var note = activeNotes[i];
            if (note.isHit) continue;

            if (songTime > note.hitTime + hitWindow)
            {
                Debug.Log($"[Side {side}] MISS lane {note.lane} time {note.hitTime:F2}");
                OnJudge?.Invoke(side, note.lane, "MISS", note.transform.position);
                note.Miss();
                activeNotes.RemoveAt(i);
            }
        }

        // 3. 键盘输入判定（仅 PC 测试）
        for (int lane = 0; lane < laneCount; lane++)
        {
            if (lane >= keys.Length) break;
            if (Input.GetKeyDown(keys[lane]))
            {
                TriggerLaneInput(lane);
            }
        }
    }

    /// <summary>
    /// 由触摸、AI、键盘等外部系统调用，触发指定轨道的判定。
    /// </summary>
    public void TriggerLaneInput(int lane)
    {
        if (conductor == null) return;

        OnLanePress?.Invoke(side, lane);
        TryHit(lane, conductor.songPosition);
    }

    private void SpawnNote(NoteData data)
    {
        if (spawnPoint == null || hitPoint == null) return;

        Vector3 offset = laneOffsets[Mathf.Clamp(data.lane, 0, laneCount - 1)];
        Vector3 spawnPos = spawnPoint.position + offset;
        Vector3 hitPos = hitPoint.position + offset;

        GameObject go = Instantiate(notePrefab, spawnPos, Quaternion.identity, transform);
        go.name = $"Note_side{side}_lane{data.lane}_t{data.time:F2}";

        Note note = go.GetComponent<Note>();
        if (note == null) note = go.AddComponent<Note>();
        note.hitTime = data.time;
        note.lane = data.lane;
        note.side = side;

        NoteMover mover = go.GetComponent<NoteMover>();
        if (mover == null) mover = go.AddComponent<NoteMover>();
        mover.Init(spawnPos, hitPos, data.time, leadTime, conductor, centerLine);

        activeNotes.Add(note);
    }

    private void TryHit(int lane, float songTime)
    {
        Note best = null;
        float bestDiff = float.MaxValue;

        foreach (var note in activeNotes)
        {
            if (note.lane != lane || note.isHit || note.side != side) continue;

            float diff = Mathf.Abs(songTime - note.hitTime);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = note;
            }
        }

        if (best != null && bestDiff <= hitWindow)
        {
            float accuracy = bestDiff / hitWindow; // 0 = 完美，1 = 边缘
            string rank = accuracy < 0.4f ? "PERFECT" : "GOOD";

            Debug.Log($"[Side {side}] {rank} lane {best.lane} diff {bestDiff:F4}s");

            if (centerLine != null)
            {
                centerLine.RegisterHit(side, accuracy);
            }

            OnJudge?.Invoke(side, best.lane, rank, best.transform.position);

            best.Hit(rank);
            activeNotes.Remove(best);
        }
    }

    /// <summary>
    /// 重置本侧所有音符，用于重新开始。
    /// </summary>
    public void ResetSpawner()
    {
        foreach (var note in activeNotes)
        {
            if (note != null) Destroy(note.gameObject);
        }
        activeNotes.Clear();
        spawnIndex = 0;
    }
}
