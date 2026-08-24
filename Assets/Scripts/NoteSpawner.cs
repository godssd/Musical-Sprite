using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// 音符发射器 + 判定器。
/// 每个玩家各挂一个：左玩家 side=0，右玩家 side=1。
/// 负责：按谱面生成属于自己 side 的音符、移动、判定命中/漏击、通知中线移动。
/// 输入由外部调用 TriggerLaneDown()/TriggerLaneUp() 触发（键盘、触摸、AI 均可）。
/// Tap 音符在 Down 时判定；Hold 音符在 Down（head 命中窗口内）起手，按住期间需保持所需轨道被按下，
/// 到 tailTime（结束音符抵达判定线）完成，提前松开/未按住则断连。
/// 
/// 判定规则（基于时间窗口，单位：秒）：
/// - 音符视觉为圆柱，半径 noteRadius。判定窗口由“视觉重叠”推导：
///   goodWindow = noteRadius / 接近速度（即音符中心与判定线距离 <= 半径时正好视觉重叠）。
///   因此 GOOD 只有在“音符看得到与判定线重叠”时才能命中，不会提前触发。
/// - perfectWindow = goodWindow * perfectRatioOfGood（默认 0.45，即 GOOD 的一半略低）。
/// - 按键时取该轨道内 |按键时刻 - 音符目标时刻| 最小且未判定的音符。
/// - 时间差 <= perfectWindow => PERFECT；<= goodWindow => GOOD；窗口外忽略（不扣分）。
/// - 超过 goodWindow 仍未命中 => 音符进入漏击状态，快速缩小后消失，缩小完成时出 MISS。
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

    [Header("判定参数（时间窗口，单位：秒）")]
    [Tooltip("音符圆柱半径（X 方向半长）。同时决定视觉大小与 GOOD 窗口：goodWindow = 半径 / 接近速度")]
    public float noteRadius = 0.45f;
    [Tooltip("PERFECT 窗口 = GOOD 窗口 × 该系数。默认 0.45（GOOD 的一半略低）。可调大变宽松、调小变严格")]
    public float perfectRatioOfGood = 0.45f;

    [Tooltip("GOOD 窗口：自动 = noteRadius / 接近速度（视觉重叠窗口）。只读参考")]
    public float goodWindow = 0.07f;
    [Tooltip("PERFECT 窗口：自动 = goodWindow × perfectRatioOfGood。只读参考")]
    public float perfectWindow = 0.03f;

    [Header("键盘输入键位（PC 测试用）")]
    [Tooltip("每条轨道对应的按键")]
    public KeyCode[] keys = new KeyCode[4] { KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F };

    // 判定事件：side, lane, rank, position
    public event Action<int, int, string, Vector3> OnJudge;

    // 输入事件：side, lane。任意输入（键盘/触摸/AI）触发轨道时调用
    public event Action<int, int> OnLanePress;

    private List<Note> activeNotes = new List<Note>();
    private List<HoldNote> activeHoldNotes = new List<HoldNote>();
    public HashSet<int> heldLanes = new HashSet<int>(); // 当前被按住的轨道（本侧）
    private int spawnIndex = 0;
    private Vector3[] laneOffsets;
    private float approachSpeed; // 音符接近判定线的速度（单位/秒），用于把半径换算成时间窗口

    /// <summary>
    /// 该侧谱面是否已全部生成且所有音符（含长按）已消失。
    /// </summary>
    public bool IsFinished => beatmap != null && spawnIndex >= beatmap.notes.Length
        && activeNotes.Count == 0 && activeHoldNotes.Count == 0;

    void Start()
    {
        laneOffsets = new Vector3[laneCount];
        for (int i = 0; i < laneCount; i++)
        {
            float z = (i - (laneCount - 1) * 0.5f) * laneSpacing;
            laneOffsets[i] = new Vector3(0f, 0f, z);
        }

        RecomputeWindows();
    }

    void OnValidate()
    {
        RecomputeWindows();
    }

    /// <summary>
    /// 根据当前 noteRadius / leadTime / perfectRatioOfGood 重新计算判定窗口。
    /// 调试工具或 Inspector 修改参数后调用。
    /// </summary>
    public void RecomputeWindows()
    {
        if (spawnPoint != null && hitPoint != null && leadTime > 0.0001f)
        {
            float hitDistance = Vector3.Distance(spawnPoint.position, hitPoint.position);
            approachSpeed = hitDistance / leadTime;
            goodWindow = noteRadius / approachSpeed;
            perfectWindow = goodWindow * perfectRatioOfGood;
        }
    }

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return;
        if (conductor == null || beatmap == null || beatmap.notes == null) return;

        float songTime = conductor.songPosition;

        // 1. 到时间就生成音符（区分 Tap / Hold）
        while (spawnIndex < beatmap.notes.Length)
        {
            NoteData data = beatmap.notes[spawnIndex];
            if (songTime < data.time - leadTime) break;

            if (data.side == side)
            {
                if (data.type == NoteData.NoteType.Hold)
                    SpawnHoldNote(data);
                else
                    SpawnNote(data);
            }
            spawnIndex++;
        }

        // 2. 漏击检测：超过 GOOD 窗口仍未命中 => 进入漏击缩小（不直接出 MISS，
        //    避免“一个音符同时出现两种评价”，也避免 MISS 与同帧其它命中同时弹出）
        for (int i = activeNotes.Count - 1; i >= 0; i--)
        {
            var note = activeNotes[i];
            if (note.isHit) continue;

            if (songTime > note.hitTime + goodWindow)
            {
                // 标记为漏击并播放“穿过后快速缩小消失”的反馈，
                // MISS 文字等缩小完成（步骤 2b）才出现
                note.Miss();
            }
        }

        // 2b. 漏击缩小动画完成 => 此刻才出 MISS 反馈并销毁音符
        for (int i = activeNotes.Count - 1; i >= 0; i--)
        {
            var note = activeNotes[i];
            NoteMover mover = note.GetComponent<NoteMover>();
            if (mover != null && mover.MissShrinkComplete)
            {
                Vector3 missPos = hitPoint.position + laneOffsets[note.lane];
                OnJudge?.Invoke(side, note.lane, "MISS", missPos);
                activeNotes.RemoveAt(i);
                Destroy(note.gameObject);
            }
        }

        // 2c. 清理已完成/已断开的长按音符
        for (int i = activeHoldNotes.Count - 1; i >= 0; i--)
        {
            if (activeHoldNotes[i] == null || activeHoldNotes[i].finished)
            {
                if (activeHoldNotes[i] != null) Destroy(activeHoldNotes[i].gameObject);
                activeHoldNotes.RemoveAt(i);
            }
        }

        // 3. 键盘输入判定（仅 PC 测试）：按下 / 松开分别下发
        for (int lane = 0; lane < laneCount; lane++)
        {
            if (lane >= keys.Length) break;
            if (Input.GetKeyDown(keys[lane]))
            {
                TriggerLaneDown(lane);
            }
            if (Input.GetKeyUp(keys[lane]))
            {
                TriggerLaneUp(lane);
            }
        }
    }

    /// <summary>
    /// 由触摸、AI、键盘等外部系统调用：轨道“按下”时触发。
    /// 同时处理：普通音符判定 + 长按音符起手。
    /// </summary>
    public void TriggerLaneDown(int lane, bool fromAI = false)
    {
        if (conductor == null) return;

        heldLanes.Add(lane);
        OnLanePress?.Invoke(side, lane);
        TryHitTap(lane);
        TryStartHold(lane, fromAI);
    }

    /// <summary>
    /// 轨道“松开”时触发。长按中松开由 HoldNote 的断连检测感知（所需轨道不再被按住即断连）。
    /// </summary>
    public void TriggerLaneUp(int lane)
    {
        heldLanes.Remove(lane);
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
        mover.Init(spawnPos, hitPos, data.time, leadTime, conductor, centerLine, noteRadius);

        activeNotes.Add(note);
    }

    /// <summary>
    /// 生成长按音符（head + tail 圆柱 + 连接 bar），交由 HoldNote 组件驱动运动与判定。
    /// </summary>
    private void SpawnHoldNote(NoteData data)
    {
        if (spawnPoint == null || hitPoint == null) return;

        int headLane = Mathf.Clamp(data.lane, 0, laneCount - 1);
        int tailLane = Mathf.Clamp(data.holdEndLane, 0, laneCount - 1);

        Vector3 headOff = laneOffsets[headLane];
        Vector3 tailOff = laneOffsets[tailLane];
        Vector3 spawnPosHead = spawnPoint.position + headOff;
        Vector3 hitPosHead = hitPoint.position + headOff;
        Vector3 spawnPosTail = spawnPoint.position + tailOff;
        Vector3 hitPosTail = hitPoint.position + tailOff;

        float tailTime = data.time + Mathf.Max(0.1f, data.holdDuration);

        GameObject go = new GameObject($"Hold_side{side}_lane{headLane}_t{data.time:F2}");
        go.transform.SetParent(transform, false); // 挂到发射器下，场景重搭时随发射器一起销毁
        HoldNote hn = go.AddComponent<HoldNote>();
        hn.spawner = this;
        hn.side = side;
        hn.headLane = headLane;
        hn.tailLane = tailLane;
        hn.headTime = data.time;
        hn.tailTime = tailTime;
        hn.leadTime = leadTime;
        hn.noteRadius = noteRadius;
        hn.spawnPosHead = spawnPosHead;
        hn.hitPosHead = hitPosHead;
        hn.spawnPosTail = spawnPosTail;
        hn.hitPosTail = hitPosTail;
        hn.conductor = conductor;
        hn.centerLine = centerLine;
        hn.judgeLineX = hitPosHead.x; // 判定线 x：作为 Hold 收尾时的"消失边界"
        hn.goodWindow = goodWindow;
        hn.perfectWindow = perfectWindow;
        hn.onJudge += (s, l, r, p) => OnJudge?.Invoke(s, l, r, p);

        activeHoldNotes.Add(hn);
    }

    /// <summary>
    /// 在 head 时间窗内、对应轨道被按下时，起手最近的一个等待中长按音符。
    /// </summary>
    private void TryStartHold(int lane, bool fromAI)
    {
        float songTime = conductor.songPosition;

        HoldNote best = null;
        float bestAbs = goodWindow + 1f;

        foreach (var hn in activeHoldNotes)
        {
            if (hn.state != HoldNote.HoldState.Waiting) continue;
            if (hn.headLane != lane) continue;
            float dt = songTime - hn.headTime;
            float adt = Mathf.Abs(dt);
            if (adt > goodWindow) continue;
            if (adt < bestAbs)
            {
                bestAbs = adt;
                best = hn;
            }
        }

        if (best != null) best.StartHold(songTime, fromAI);
    }

    private void TryHitTap(int lane)
    {
        float songTime = conductor.songPosition;

        Note best = null;
        float bestAbsDt = float.MaxValue;

        // 在 GOOD 窗口内、取离目标时刻最近的未命中音符
        foreach (var note in activeNotes)
        {
            if (note.lane != lane || note.isHit || note.side != side) continue;
            if (songTime > note.hitTime + goodWindow) continue; // 已超出窗口（将由 Update 判 MISS）

            float dt = songTime - note.hitTime;
            float absDt = Mathf.Abs(dt);
            if (absDt > goodWindow) continue; // 窗口外（按太早），忽略这次输入
            if (absDt < bestAbsDt)
            {
                bestAbsDt = absDt;
                best = note;
            }
        }

        if (best == null) return;

        string rank = bestAbsDt <= perfectWindow ? "PERFECT" : "GOOD";
        Debug.Log($"[Side {side}] {rank} lane {best.lane} dt {bestAbsDt:F4}s");

        float accuracy = Mathf.Clamp01(1f - bestAbsDt / (goodWindow + 0.0001f));
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

    /// <summary>
    /// 清理场上所有残留音符（游戏结束时调用，避免黑音符堆积）。
    /// </summary>
    public void ClearActiveNotes()
    {
        foreach (var note in activeNotes)
        {
            if (note != null) Destroy(note.gameObject);
        }
        activeNotes.Clear();

        foreach (var hn in activeHoldNotes)
        {
            if (hn != null) Destroy(hn.gameObject);
        }
        activeHoldNotes.Clear();
        heldLanes.Clear();
    }

    /// <summary>
    /// 重置本侧所有音符，用于重新开始。
    /// </summary>
    public void ResetSpawner()
    {
        ClearActiveNotes();
        spawnIndex = 0;
    }
}
