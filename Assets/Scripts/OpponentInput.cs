using UnityEngine;
using System;

/// <summary>
/// 对手（AI）输入模拟器。
/// 按谱面自动在 Perfect 时机触发对应轨道的输入，让对战看起来有来有回。
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

    private int index = 0;

    void Update()
    {
        if (spawner == null || conductor == null || beatmap == null || beatmap.notes == null) return;

        float songTime = conductor.songPosition;

        while (index < beatmap.notes.Length)
        {
            NoteData note = beatmap.notes[index];

            if (note.side != spawner.side)
            {
                index++;
                continue;
            }

            float targetTime = note.time + aimOffset;
            if (songTime < targetTime - 0.01f) break;

            // 在这个时间窗口内触发（按几何判定，给 0.2 秒容错）
            if (songTime <= targetTime + 0.2f)
            {
                if (UnityEngine.Random.value > missChance)
                {
                    spawner.TriggerLaneInput(note.lane);
                    if (showVisualFeedback)
                        ShowOpponentPress(note.lane);
                }
            }

            index++;
        }
    }

    private void ShowOpponentPress(int lane)
    {
        OnPressLane?.Invoke(lane);
    }

    public void ResetInput()
    {
        index = 0;
    }
}
