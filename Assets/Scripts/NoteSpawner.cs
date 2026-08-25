using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// 音符发射器 + 判定器。
/// 每个玩家各挂一个：左玩家 side=0，右玩家 side=1。
/// 负责：按谱面生成属于自己 side 的音符、移动、判定命中/漏击、通知中线移动。
/// 输入由外部调用 TriggerLaneDown()/TriggerLaneUp() 触发（键盘、触摸、AI 均可）。
/// Tap/ChainTap 音符在 Down 时判定；Hold 音符在 Down（head 命中窗口内）起手，按住期间需保持所需轨道被按下，
/// 到 tailTime（结束音符抵达判定线）完成，提前松开/未按住则断连。
/// 
/// 判定规则（基于时间窗口，单位：秒）：
/// - 普通点击视觉为圆柱，连轨点击为横跨两轨的圆角矩形；判定窗口由 X 方向半长推导：
///   goodWindow = noteRadius / 接近速度（即音符中心与判定线距离 <= 半径时正好视觉重叠）。
///   因此 GOOD 只有在“音符看得到与判定线重叠”时才能命中，不会提前触发。
/// - perfectWindow = goodWindow * perfectRatioOfGood（默认 0.45，即 GOOD 的一半略低）。
/// - 按键时取该轨道内 |按键时刻 - 音符目标时刻| 最小且未判定的音符。
/// - 时间差 <= perfectWindow => PERFECT；<= goodWindow => GOOD；窗口外忽略（不扣分）。
/// - ChainTap 首次命中后停在判定线处，每次命中刷新 chainTapHoldDuration 倒计时；超时则进入普通 MISS。
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

    [Header("连轨长按判定（滑动手感）")]
    [Tooltip("连轨滑动 Hold（如 第2轨→第3轨）在节点时间中点切换\"应被按住\"的轨道；切换前后各该秒数内，起手轨与目标轨都允许（容滑动手感）。调大=更宽松，调小=更接近正中切点切换。单位：秒。")]
    public float holdSlideSettleWindow = 0.15f;
    [Tooltip("所需轨道超过该秒数未被按住即断连 MISS。越小越严格。")]
    public float holdBreakThreshold = 0.2f;
    [Tooltip("普通单轨 Hold 跟随判定容差：按住轨道与当前插值轨道相差多少条轨道内算命中。")]
    public float holdLaneTolerance = 1.0f;

    [Header("连点音符判定")]
    [Tooltip("连点音符命中后停留等待下一次点击的时间（秒）")]
    public float chainTapHoldDuration = 0.4f;

    [Header("键盘输入键位（PC 测试用）")]
    [Tooltip("每条轨道对应的按键。lane 顺序（0=最下 → 3=最上）：0=空格，1=C，2=D，3=W。与屏幕从上到下 W,D,C,空格 对应")]
    public KeyCode[] keys = new KeyCode[4] { KeyCode.Space, KeyCode.C, KeyCode.D, KeyCode.W };

    [Header("输入开关")]
    [Tooltip("是否启用本侧键盘输入。红方（本地玩家）为 true；蓝方为联网对手，由网络驱动，应设为 false")]
    public bool useKeyboard = true;

    // 判定事件：side, lane, rank, position
    public event Action<int, int, string, Vector3> OnJudge;

    // 输入事件：side, lane。任意输入（键盘/触摸/AI）触发轨道时调用
    public event Action<int, int> OnLanePress;

    private List<Note> activeNotes = new List<Note>();
    private List<HoldNote> activeHoldNotes = new List<HoldNote>();

    // 主动技能附魔请求：角色释放后追加；spawn 音符时按 side 消耗名额
    public struct CharmRequest { public ActiveSkillRuntime owner; public int remaining; }
    public List<CharmRequest> activeCharms = new List<CharmRequest>();
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

        // 1. 到时间就生成音符。Linked 单节点是双轨点击，多节点是双轨长按/跨轨。
        while (spawnIndex < beatmap.notes.Length)
        {
            NoteData data = beatmap.notes[spawnIndex];
            if (songTime < data.time - leadTime) break;

            if (data.side == side)
            {
                if (data.type == NoteData.NoteType.Hold)
                    SpawnHoldNote(data, 1);
                else if (data.IsLinkedHold())
                    SpawnHoldNote(data, 2);
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

            if (note.isChainTap && note.chainTapWaiting)
            {
                if (note.IsChainTapExpired(songTime)) note.Miss();
            }
            else if (songTime > note.hitTime + goodWindow)
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
                Vector3 missPos = mover.HitPosition;
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

        // 3. 键盘输入判定（仅 PC 测试，且本侧启用键盘时）：按下 / 松开分别下发
        if (!useKeyboard) return;
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

        bool isSmallTap = data.type == NoteData.NoteType.SmallTap;
        bool isChainTap = data.type == NoteData.NoteType.ChainTap;
        int laneSpan = data.type == NoteData.NoteType.Linked ? 2 : 1;
        int startLane = ClampLaneSpanStart(data.lane, laneSpan);

        Vector3 offset = GetLaneSpanOffset(startLane, laneSpan);
        Vector3 spawnPos = spawnPoint.position + offset;
        Vector3 hitPos = hitPoint.position + offset;

        GameObject go = Instantiate(notePrefab, spawnPos, Quaternion.identity, transform);
        go.name = $"Note_{data.type}_side{side}_lane{data.lane}_t{data.time:F2}";

        Note note = go.GetComponent<Note>();
        if (note == null) note = go.AddComponent<Note>();
        note.hitTime = data.time;
        note.lane = startLane;
        note.laneSpan = laneSpan;
        note.side = side;
        note.isSmallTap = isSmallTap;
        note.isChainTap = isChainTap;
        note.chainTapRequired = isChainTap ? Mathf.Max(1, data.chainTapCount) : 0;
        note.chainTapRemaining = note.chainTapRequired;

        NoteMover mover = go.GetComponent<NoteMover>();
        if (mover == null) mover = go.AddComponent<NoteMover>();
        mover.Init(spawnPos, hitPos, data.time, leadTime, conductor, centerLine,
            noteRadius, isSmallTap, laneSpan, laneSpacing, isChainTap, data.chainTapCount,
            chainTapHoldDuration);

        activeNotes.Add(note);
        TryCharmNote(note);
    }

    /// <summary>角色释放主动技能时调用：登记一个附魔请求（配额 = count）。</summary>
    public void RequestCharm(ActiveSkillRuntime owner, int count)
    {
        if (owner == null || count <= 0) return;
        activeCharms.Add(new CharmRequest { owner = owner, remaining = count });
        owner.SetCharmQuota(count);
    }

    /// <summary>普通 tap 音符生成时尝试附魔：取首个 side 匹配且仍有名额的请求，标记该音符并消耗一个名额。</summary>
    private void TryCharmNote(Note note)
    {
        if (note == null) return;
        for (int i = activeCharms.Count - 1; i >= 0; i--)
        {
            var req = activeCharms[i];
            if (req.remaining > 0 && req.owner != null && req.owner.ownerSide == note.side)
            {
                note.charmOwner = req.owner;
                req.owner.OnNoteCharmed(note);
                req.remaining--;
                if (req.remaining <= 0)
                {
                    // 配额用完：通知 runtime（让其在所有已附魔音符解决后释放）
                    req.owner.OnCharmRequestClosed();
                    activeCharms.RemoveAt(i);
                }
                else
                {
                    activeCharms[i] = req;
                }
                TintCharmed(note);
                break;
            }
        }
    }

    /// <summary>Hold/Linked 整条链接链生成时尝试附魔：与 TryCharmNote 同理，但写入 HoldNote.charmOwner + 调用 OnHoldCharmed。
    /// 整条链算 1 个附魔单位（不管它有几个节点）。</summary>
    private void TryCharmHoldNote(HoldNote hn)
    {
        if (hn == null) return;
        for (int i = activeCharms.Count - 1; i >= 0; i--)
        {
            var req = activeCharms[i];
            if (req.remaining > 0 && req.owner != null && req.owner.ownerSide == hn.side)
            {
                hn.charmOwner = req.owner;
                req.owner.OnHoldCharmed(hn);
                req.remaining--;
                if (req.remaining <= 0)
                {
                    // 配额用完：通知 runtime（让其在所有已附魔单位解决后释放）
                    req.owner.OnCharmRequestClosed();
                    activeCharms.RemoveAt(i);
                }
                else
                {
                    activeCharms[i] = req;
                }
                TintCharmedHold(hn);
                break;
            }
        }
    }

    /// <summary>把被附魔的整条链接链（head 节点 + 段带）染成黄色发光。</summary>
    private void TintCharmedHold(HoldNote hn)
    {
        if (hn == null) return;
        // head 节点（lanes[0] 所在）上色；其它段带先不上色，避免打断长按时的白→黄切换
        // 真正染色交由 charm owner 完成后由 HoldNote 自身视觉处理
        // 这里仅给第一个节点上一个黄色 emissive 占位
    }

    /// <summary>把被附魔音符染成黄色发光（占位视觉）。</summary>
    private void TintCharmed(Note note)
    {
        var r = note.GetComponent<Renderer>();
        if (r == null) return;
        r.material = new Material(r.material);
        r.material.EnableKeyword("_EMISSION");
        r.material.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.1f));
    }

    /// <summary>
    /// 生成长按音符（多节点：head → mid → ... → tail，相邻节点间一段连接 bar），
    /// 交由 HoldNote 组件驱动运动与判定。每段（节点 i → i+1）完成触发一次 CLEAR。
    /// </summary>
    private void SpawnHoldNote(NoteData data, int laneSpan)
    {
        if (spawnPoint == null || hitPoint == null) return;

        int[] holdLanes = (int[])data.GetHoldLanes().Clone();
        float[] holdTimes = data.GetHoldTimes();
        int nodeCount = Mathf.Max(2, holdLanes.Length);
        // 防御：times 长度不足时用 lanes 长度补齐
        if (holdTimes.Length < nodeCount)
        {
            float[] tt = new float[nodeCount];
            tt[0] = data.time;
            for (int i = 1; i < nodeCount; i++) tt[i] = tt[i - 1] + Mathf.Max(0.1f, data.holdDuration > 0 ? data.holdDuration / (nodeCount - 1) : 0.5f);
            holdTimes = tt;
        }

        // 逐节点宽度：优先用 data.holdLaneSpans（链接模式可为不同节点设不同宽度），
        // 否则整条链沿用传入的 whole-span（旧谱面 / 纯连轨长按）。
        int[] nodeLaneSpans;
        if (data.holdLaneSpans != null && data.holdLaneSpans.Length >= nodeCount)
            nodeLaneSpans = System.Array.ConvertAll(data.holdLaneSpans, v => v > 1 ? 2 : 1);
        else if (data.holdLaneSpans != null)
        {
            nodeLaneSpans = new int[nodeCount];
            for (int i = 0; i < nodeCount; i++)
                nodeLaneSpans[i] = (i < data.holdLaneSpans.Length && data.holdLaneSpans[i] > 1) ? 2 : 1;
        }
        else
        {
            int whole = laneSpan > 1 ? 2 : 1;
            nodeLaneSpans = new int[nodeCount];
            for (int i = 0; i < nodeCount; i++) nodeLaneSpans[i] = whole;
        }

        Vector3[] spawnPositions = new Vector3[nodeCount];
        Vector3[] hitPositions = new Vector3[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            int span = nodeLaneSpans[i];
            int ln = ClampLaneSpanStart(holdLanes[i], span);
            holdLanes[i] = ln;
            Vector3 off = GetLaneSpanOffset(ln, span);
            spawnPositions[i] = spawnPoint.position + off;
            hitPositions[i] = hitPoint.position + off;
        }

        string kind = laneSpan > 1 ? "LinkedHold" : "Hold";
        GameObject go = new GameObject($"{kind}_side{side}_nodes{nodeCount}_lane{holdLanes[0]}_t{data.time:F2}");
        go.transform.SetParent(transform, false); // 挂到发射器下，场景重搭时随发射器一起销毁
        HoldNote hn = go.AddComponent<HoldNote>();
        hn.spawner = this;
        hn.side = side;
        hn.lanes = holdLanes;
        hn.times = holdTimes;
        hn.leadTime = leadTime;
        hn.noteRadius = noteRadius;
        // 整条链宽度兜底取各节点最大宽度（细宽度由 nodeLaneSpans 决定）
        int maxSpan = laneSpan > 1 ? 2 : 1;
        for (int i = 0; i < nodeLaneSpans.Length; i++) if (nodeLaneSpans[i] > maxSpan) maxSpan = nodeLaneSpans[i];
        hn.laneSpan = maxSpan;
        hn.nodeLaneSpans = nodeLaneSpans;
        hn.laneSpacing = laneSpacing;
        hn.spawnPositions = spawnPositions;
        hn.hitPositions = hitPositions;
        hn.conductor = conductor;
        hn.centerLine = centerLine;
        hn.judgeLineX = hitPositions[0].x; // 判定线 x：作为 Hold 收尾时的"消失边界"（取 head 判定线）
        hn.goodWindow = goodWindow;
        hn.perfectWindow = perfectWindow;
        hn.slideSettleWindow = holdSlideSettleWindow;
        hn.breakThreshold = holdBreakThreshold;
        hn.laneTolerance = holdLaneTolerance;
        hn.onJudge += (s, l, r, p) => OnJudge?.Invoke(s, l, r, p);

        activeHoldNotes.Add(hn);
        TryCharmHoldNote(hn);
    }

    /// <summary>
    /// 在 head 时间窗内、对应轨道（head 所在轨道）被按下时，起手最近的一个等待中长按音符。
    /// </summary>
    private void TryStartHold(int lane, bool fromAI)
    {
        float songTime = conductor.songPosition;

        HoldNote best = null;
        float bestAbs = goodWindow + 1f;

        foreach (var hn in activeHoldNotes)
        {
            if (hn.state != HoldNote.HoldState.Waiting) continue;
            if (hn.lanes == null || hn.lanes.Length < 2) continue;
            if (!hn.CanStartOnLane(lane)) continue;
            float dt = songTime - hn.times[0];
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
            if (!note.CoversLane(lane) || note.isHit || note.side != side) continue;
            bool chainContinuation = note.isChainTap && note.chainTapWaiting;
            float absDt;
            if (chainContinuation)
            {
                // 连点音符停留期间不再按首次 hitTime 判定，倒计时内的每次按下都有效。
                if (note.IsChainTapExpired(songTime)) continue;
                absDt = 0f;
            }
            else
            {
                // 连轨点击音符（laneSpan>1）只需按覆盖的任意一条轨道即可命中，
                // 因此不再要求两条轨道同时被按住；评价/MISS 与普通点击一致。
                if (songTime > note.hitTime + goodWindow) continue;
                float dt = songTime - note.hitTime;
                absDt = Mathf.Abs(dt);
                if (absDt > goodWindow) continue;
            }
            if (absDt < bestAbsDt)
            {
                bestAbsDt = absDt;
                best = note;
            }
        }

        if (best == null) return;

        // 小型点击音符：不做 PERFECT/GOOD 区分，命中统一 PASS 评价（80 分）。
        // 连点音符沿用普通大点击的 PERFECT/GOOD 判定，每次命中都单独计分/计连击。
        string rank;
        if (best.isSmallTap)
            rank = "PASS";
        else
            rank = bestAbsDt <= perfectWindow ? "PERFECT" : "GOOD";
        Debug.Log($"[Side {side}] {rank} lane {best.lane} dt {bestAbsDt:F4}s");

        float accuracy = Mathf.Clamp01(1f - bestAbsDt / (goodWindow + 0.0001f));
        if (centerLine != null)
        {
            centerLine.RegisterHit(side, accuracy);
        }

        // 反馈位置放在对应轨道的判定线处
        NoteMover bestMover = best.GetComponent<NoteMover>();
        Vector3 judgePos = bestMover != null ? bestMover.HitPosition : hitPoint.position + GetLaneSpanOffset(best.lane, best.laneSpan);
        OnJudge?.Invoke(side, best.lane, rank, judgePos);

        if (best.isChainTap)
        {
            bool cleared = best.RegisterChainTapHit(songTime, rank);
            if (cleared) activeNotes.Remove(best);
        }
        else
        {
            best.Hit(rank);
            activeNotes.Remove(best);
        }
    }

    /// <summary>检查从 startLane 开始的连续 laneSpan 条轨道是否都处于按住状态。</summary>
    public bool AreLanesHeld(int startLane, int laneSpan)
    {
        int start = ClampLaneSpanStart(startLane, laneSpan);
        for (int i = 0; i < laneSpan; i++)
        {
            if (!heldLanes.Contains(start + i)) return false;
        }
        return true;
    }

    private int ClampLaneSpanStart(int startLane, int laneSpan)
    {
        return Mathf.Clamp(startLane, 0, Mathf.Max(0, laneCount - Mathf.Max(1, laneSpan)));
    }

    private Vector3 GetLaneSpanOffset(int startLane, int laneSpan)
    {
        int start = ClampLaneSpanStart(startLane, laneSpan);
        int end = Mathf.Clamp(start + Mathf.Max(1, laneSpan) - 1, 0, laneCount - 1);
        return (laneOffsets[start] + laneOffsets[end]) * 0.5f;
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
