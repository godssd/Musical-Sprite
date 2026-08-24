using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 对手（AI）输入模拟器。
/// 按谱面自动在 Perfect 时机触发对应轨道的输入，让对战看起来有来有回。
/// 支持 Tap 与 Hold：
/// - Tap：在目标时刻按下并随即松开（fromAI 标记，避免误占 heldLanes）。
/// - Hold：在 head 时刻按下（fromAI，自动完成长按），在 tail 时刻松开。
/// 挂载在任意物体上，GameManager 会自动把它关联到 rightSpawner。
/// </summary>
public class OpponentInput : MonoBehaviour
{
    [Header("引用")]
    public NoteSpawner spawner;
    public Conductor conductor;
    public BeatmapSO beatmap;

    [Header("AI 参数")]
    [Tooltip("AI 判定精度偏移（秒）。0 表示每次都 Perfect；0.02 表示略有人味。")]
    public float aimOffset = 0f;

    [Tooltip("AI 偶尔会 Miss 的概率。0 = 全中，0.1 = 10% Miss。")]
    [Range(0f, 1f)]
    public float missChance = 0.05f;

    [Header("视觉表现")]
    [Tooltip("AI 按下轨道时是否显示轨道高亮")]
    public bool showVisualFeedback = true;

    /// <summary>
    /// AI 按下轨道时触发。参数：lane
    /// </summary>
    public event Action<int> OnPressLane;

    private int pressIndex = 0;                                  // 遍历 head / tap 事件
    private List<(float time, int lane)> releaseEvents = new List<(float, int)>();
    private int releaseIndex = 0;                                // 遍历 hold 的 tail 松开事件

    void Start()
    {
        BuildSchedule();
    }

    /// <summary>
    /// 预排序所有长按音符的“结束音符(tail)抵达判定线”松开事件。
    /// </summary>
    private void BuildSchedule()
    {
        releaseEvents.Clear();
        releaseIndex = 0;
        pressIndex = 0;

        if (beatmap == null || beatmap.notes == null) return;

        foreach (var n in beatmap.notes)
        {
            if (n.side != spawner.side) continue;
            if (n.type == NoteData.NoteType.Hold)
            {
                releaseEvents.Add((n.time + Mathf.Max(0.1f, n.holdDuration), n.holdEndLane));
            }
        }
        releaseEvents.Sort((a, b) => a.time.CompareTo(b.time));
    }

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return;
        if (spawner == null || conductor == null || beatmap == null || beatmap.notes == null) return;

        float songTime = conductor.songPosition;

        // 1) 按下事件（Tap 与 Hold 的 head），按 time 升序处理
        while (pressIndex < beatmap.notes.Length)
        {
            NoteData note = beatmap.notes[pressIndex];

            if (note.side != spawner.side)
            {
                pressIndex++;
                continue;
            }

            float targetTime = note.time + aimOffset;
            if (songTime < targetTime - 0.01f) break;

            if (songTime <= targetTime + 0.2f)
            {
                if (UnityEngine.Random.value > missChance)
                {
                    spawner.TriggerLaneDown(note.lane, true);
                    if (note.type != NoteData.NoteType.Hold)
                    {
                        // Tap：按下后立刻松开，避免占用 heldLanes（AI 标记也会让长按跳过按住检测）
                        spawner.TriggerLaneUp(note.lane);
                    }
                    if (showVisualFeedback)
                        ShowOpponentPress(note.lane);
                }
            }

            pressIndex++;
        }

        // 2) 松开事件（仅 Hold 的 tail）
        while (releaseIndex < releaseEvents.Count)
        {
            var ev = releaseEvents[releaseIndex];
            if (songTime < ev.time - 0.01f) break;

            if (songTime <= ev.time + 0.2f)
            {
                spawner.TriggerLaneUp(ev.lane);
            }

            releaseIndex++;
        }
    }

    private void ShowOpponentPress(int lane)
    {
        OnPressLane?.Invoke(lane);
    }

    public void ResetInput()
    {
        BuildSchedule();
    }
}
