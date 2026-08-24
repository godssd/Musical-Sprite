using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 对手（AI）输入模拟器。
/// 按谱面自动在 Perfect 时机触发对应轨道的输入，让对战看起来有来有回。
/// 支持 Tap、Hold 与 Linked：
/// - Tap：在目标时刻按下并随即松开（fromAI 标记，避免误占 heldLanes）。
/// - Hold：在 head 时刻按下（fromAI，自动完成长按），在 tail 时刻松开。
/// - Linked：同时按下相邻两轨；多节点 Linked 由 AI 自动完成，到 tail 时同时松开两轨。
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
    /// 预排序所有长按音符的“每个节点（除 head 外）抵达判定线”松开事件。
    /// 多节点 Hold：每个中间/尾节点都需要松开（AI 在最后一个节点松开即可，
    /// 但这里记录所有节点时刻以确保任何中途断点也能释放按键状态）。
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
                float[] times = n.GetHoldTimes();
                int[] lanes = n.GetHoldLanes();
                // 头节点由按下事件处理，这里登记其余节点（含尾节点）的松开
                for (int i = 1; i < times.Length && i < lanes.Length; i++)
                {
                    releaseEvents.Add((times[i], lanes[i]));
                }
            }
            else if (n.IsLinkedHold())
            {
                float[] times = n.GetHoldTimes();
                int[] lanes = n.GetHoldLanes();
                int startLane = Mathf.Clamp(lanes[0], 0, Mathf.Max(0, spawner.laneCount - 2));
                float tailTime = times[Mathf.Min(times.Length, lanes.Length) - 1];
                releaseEvents.Add((tailTime, startLane));
                releaseEvents.Add((tailTime, startLane + 1));
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
                    bool linked = note.type == NoteData.NoteType.Linked;
                    bool heldNote = note.type == NoteData.NoteType.Hold || note.IsLinkedHold();
                    int startLane = linked
                        ? Mathf.Clamp(note.lane, 0, Mathf.Max(0, spawner.laneCount - 2))
                        : note.lane;

                    spawner.TriggerLaneDown(startLane, true);
                    if (linked)
                        spawner.TriggerLaneDown(startLane + 1, true);

                    if (!heldNote)
                    {
                        // Tap：按下后立刻松开，避免占用 heldLanes（AI 标记也会让长按跳过按住检测）
                        spawner.TriggerLaneUp(startLane);
                        if (linked) spawner.TriggerLaneUp(startLane + 1);
                    }
                    if (showVisualFeedback)
                    {
                        ShowOpponentPress(startLane);
                        if (linked) ShowOpponentPress(startLane + 1);
                    }
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
