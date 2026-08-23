using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// 音符发射器 + 判定器。
/// 每个玩家各挂一个：左玩家 side=0，右玩家 side=1。
/// 负责：按谱面生成属于自己 side 的音符、移动、判定命中/漏击、通知中线移动。
/// 输入由外部调用 TriggerLaneInput() 触发（键盘、触摸、AI 均可）。
/// 
/// 判定规则（几何重叠）：
/// - 音符完全穿过判定线（leftband）且未被命中 => MISS
/// - 命中时，音符中心与判定线中心的距离 <= 边长的一半 => PERFECT
/// - 命中时，距离 > 边长的一半 但在有效命中范围内 => GOOD
/// - 反馈文字生成在对应轨道判定线处。
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

    [Header("轨道参数")]
    [Tooltip("轨道数量")]
    public int laneCount = 4;

    [Tooltip("相邻轨道在 Z 轴上的间距")]
    public float laneSpacing = 1f;

    [Header("判定参数")]
    [Tooltip("PERFECT 距离阈值 = 音符边长 × 该系数。命中时中心距离 ≤ 该值为 PERFECT。默认 1.2")]
    public float perfectRatio = 1.2f;

    [Tooltip("GOOD 最大距离阈值 = 音符边长 × 该系数。1.2 < 距离 ≤ 该值时为 GOOD，再远未命中。默认 2.0")]
    public float goodRatio = 2.0f;

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

    /// <summary>
    /// 该侧谱面是否已全部生成且所有音符已消失。
    /// </summary>
    public bool IsFinished => beatmap != null && spawnIndex >= beatmap.notes.Length && activeNotes.Count == 0;

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

        // 2. 漏击检测：音符完全穿过判定线且未被命中
        for (int i = activeNotes.Count - 1; i >= 0; i--)
        {
            var note = activeNotes[i];
            if (note.isHit) continue;

            NoteMover mover = note.GetComponent<NoteMover>();
            if (mover != null && mover.hasFullyPassed)
            {
                Debug.Log($"[Side {side}] MISS lane {note.lane} time {note.hitTime:F2}");
                // 反馈位置放在对应轨道的判定线处
                Vector3 missPos = hitPoint.position + laneOffsets[note.lane];
                OnJudge?.Invoke(side, note.lane, "MISS", missPos);
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
        TryHit(lane);
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

    private void TryHit(int lane)
    {
        Note best = null;
        float bestDist = float.MaxValue;

        foreach (var note in activeNotes)
        {
            if (note.lane != lane || note.isHit || note.side != side) continue;

            NoteMover mover = note.GetComponent<NoteMover>();
            if (mover == null) continue;

            float dist = mover.DistanceToHitCenter();
            if (dist < bestDist)
            {
                bestDist = dist;
                best = note;
            }
        }

        if (best == null) return;

        // 读取音符边长，作为判定基准
        float noteEdge = 0.6f;
        NoteMover bestMover = best.GetComponent<NoteMover>();
        if (bestMover != null) noteEdge = bestMover.NoteEdgeLength;
        float perfectThresh = noteEdge * perfectRatio;
        float goodThresh = noteEdge * goodRatio;

        // 判定线发光与黑方块发生重叠即视为击中：
        // distance ≤ 1.2×边长 → PERFECT；1.2×边长 < distance ≤ 2.0×边长 → GOOD
        if (bestDist <= goodThresh)
        {
            string rank = bestDist <= perfectThresh ? "PERFECT" : "GOOD";

            Debug.Log($"[Side {side}] {rank} lane {best.lane} distance {bestDist:F4} edge {noteEdge:F2} thresh {goodThresh:F2}");

            float accuracy = Mathf.Clamp01(bestDist / goodThresh);
            if (centerLine != null)
            {
                centerLine.RegisterHit(side, accuracy);
            }

            // 反馈位置放在对应轨道的判定线处
            Vector3 judgePos = hitPoint.position + laneOffsets[best.lane];
            OnJudge?.Invoke(side, best.lane, rank, judgePos);

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
