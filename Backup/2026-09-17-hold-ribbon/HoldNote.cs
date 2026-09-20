using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 长按音符（按住并沿节点链滑动）的运行时组件。
/// 由 NoteSpawner 在生成 Hold 音符时创建，并注入所有运动/判定参数。
///
/// 多节点链接：
/// - 支持 2 个或更多节点（head → mid → ... → tail）。
/// - lanes[i] / times[i] 一一对应：节点 i 的圆心的抵达判定线时刻 = times[i]，所在轨道 = lanes[i]。
/// - 按住后沿连接线从头节点滑到尾节点；每完成「节点 i → 节点 i+1」一段链接，触发一次 CLEAR。
/// - 全部节点完成后整体淡出（最后一段 CLEAR 在尾节点处上报）。
///
/// 视觉：
/// - 普通 Hold 的节点是黑色圆柱体，节点间用细连接 bar 相连。
/// - 连轨 Hold 的节点与连接带都会横跨相邻两轨；节点轨道变化时形成跨轨宽带。
/// - 命中 head 后 head 变白；按住期间 bar 从 head 向 tail 逐段变白，节点抵达判定线时变白。
/// - 完成（尾节点抵达判定线）后整体淡出；断开 / 漏击时直接淡出。
///
/// 判定状态机（由自身 Update 驱动，结果通过 onJudge 回调上报给 NoteSpawner）：
/// - Waiting：等待起始节点(head)抵达判定线。若 head 时间窗内没有按下 -> MISS。
    /// - Holding：head 命中后进入。按进度插值出"当前所需音轨"，要求该音轨被按住；
    ///   连轨滑动段在节点时间中点"强制切换"应被按住的轨道（前半段按起手轨、后半段按目标轨），
    ///   中点前后各 slideSettleWindow 秒内两条轨都允许（容滑动手感）；窗口之外必须严格跟随。
    ///   所需轨道超过 breakThreshold 秒未被按住 -> 断连 MISS。
    ///   每跨越一个节点时刻 -> 该段完成，触发 CLEAR。
    ///   到达最后一个节点时刻 -> 完成。
/// - Done：最终态，淡出后由发射器清理。
/// </summary>
public class HoldNote : MonoBehaviour
{
    // ===== 由 NoteSpawner 注入的配置 =====
    public NoteSpawner spawner;
    public int side;
    public int[] lanes;     // 节点轨道（已解析，length >= 2）
    public float[] times;   // 节点时刻（已解析，升序，length == lanes.Length）
    public float leadTime;
    public float noteRadius = 0.45f;
    public int laneSpan = 1;       // 整条链宽度兜底（取各节点最大宽度）；逐节点细宽度见 nodeLaneSpans
    public int[] nodeLaneSpans;   // 逐节点宽度：1=普通单轨节点，2=连轨节点（覆盖相邻两轨）。与 lanes 一一对应
    public float laneSpacing = 1.5f;
    public float goodWindow = 0.07f;
    public float perfectWindow = 0.03f;
    public Vector3[] spawnPositions; // 每个节点的生成点（对方半场远端）
    public Vector3[] hitPositions;   // 每个节点的判定线处位置
    public Conductor conductor;
    public BattleCenterLine centerLine;
    public float judgeLineX; // 判定线（hitPoint）的 x，作为"消失边界"
    public bool isAI = false;

    [Tooltip("普通单轨 Hold 跟随判定容差：按住轨道与\"当前插值轨道\"相差多少条轨道内算命中。1.0 表示允许相邻一轨，越小越严格。")]
    public float laneTolerance = 1.0f;

    [Tooltip("连轨滑动 Hold（如 第2轨→第3轨）在中点切换\"应被按住\"的轨道：切换前后各 slideSettleWindow 秒内，起手轨与目标轨都允许（容滑动手感）；窗口之外必须严格跟随当前阶段轨。单位：秒。")]
    public float slideSettleWindow = 0.15f;

    [Tooltip("断连判定：所需轨道超过该秒数未被按住即断连 MISS。越小越严格。")]
    public float breakThreshold = 0.2f;

    [Tooltip("连轨滑动「提前完成滑动」容错：在节点时间中点之前 earlySlideGrace 秒内提前滑到 toLane（下一轨），视为已完成滑动、不报警（容\"过早\"手感）。与 slideSettleWindow（过晚/停滞）对称。")]
    public float earlySlideGrace = 0.18f;

    [Tooltip("收尾后透明度淡出时长（秒）。在此期间音符继续移动并被判定线裁剪。")]
    public float fadeDuration = 0.35f;

    [Tooltip("收尾后最大存活时长（秒），防止超长 Hold 断连时久久不消失。")]
    public float maxFadeLife = 0.6f;

    public enum HoldState { Waiting, Holding, Done }
    public HoldState state = HoldState.Waiting;
    public bool broken = false;
    public bool finished = false; // 供发射器从列表中清理

    public event System.Action<int, int, string, Vector3> onJudge;

    /// <summary>逐节点记录附魔来源；同一条链可以只占“接下来六个音符”中的部分名额。</summary>
    private ActiveSkillRuntime[] charmOwnersByNode;

    /// <summary>本链接链是否至少有一个节点曾被附魔。</summary>
    [HideInInspector] public bool wasCharmed = false;

    /// <summary>节点总数（头 + 中间 + 尾）。被附魔时一个节点算 1 个附魔单位。</summary>
    public int NodeCount => lanes != null ? lanes.Length : nodeCount;

    public float GetNodeTime(int index) => times[index];
    public int GetNodeLane(int index) => lanes[index];

    /// <summary>取节点 index 当前的实时世界坐标（随音符移动）。
    /// 用于清屏反馈在"每个节点自身所在位置"弹出评价，而非统一堆在判定线（头部区域）。
    /// 兜底：节点变换尚未就绪时回退到判定线处 hitPositions。</summary>
    public Vector3 GetNodePosition(int index)
    {
        if (nodeTransforms != null && index >= 0 && index < nodeTransforms.Count && nodeTransforms[index] != null)
            return nodeTransforms[index].position;
        return (hitPositions != null && index >= 0 && index < hitPositions.Length) ? hitPositions[index] : Vector3.zero;
    }

    public bool CanCharmNode(int index, float songTime)
    {
        if (index < 0 || index >= NodeCount || state == HoldState.Done) return false;
        if (charmOwnersByNode != null && charmOwnersByNode[index] != null) return false;
        if (state == HoldState.Holding && index <= completedSegments) return false;
        return times != null && index < times.Length && songTime <= times[index] + goodWindow;
    }

    public bool TryCharmNode(int index, ActiveSkillRuntime owner)
    {
        if (owner == null || index < 0 || index >= NodeCount) return false;
        if (charmOwnersByNode == null || charmOwnersByNode.Length != NodeCount)
            charmOwnersByNode = new ActiveSkillRuntime[NodeCount];
        if (charmOwnersByNode[index] != null) return false;

        charmOwnersByNode[index] = owner;
        wasCharmed = true;
        owner.OnHoldCharmed(this, 1);
        TintCharmedNode(index);
        return true;
    }

    private void ResolveCharmedNode(int index, bool success)
    {
        if (charmOwnersByNode == null || index < 0 || index >= charmOwnersByNode.Length) return;
        ActiveSkillRuntime charmOwner = charmOwnersByNode[index];
        if (charmOwner == null) return;
        charmOwnersByNode[index] = null;
        if (success) charmOwner.OnCharmNodeSuccess(this);
        else charmOwner.OnCharmNodeFail(this);
    }

    // ===== 视觉 =====
    private List<Transform> nodeTransforms = new List<Transform>();
    private List<MeshRenderer> nodeRends = new List<MeshRenderer>();
    private List<Material> nodeMats = new List<Material>();

    private List<List<Transform>> segmentGroups = new List<List<Transform>>();      // 每段含多段细分圆柱
    private List<List<MeshRenderer>> segmentRendGroups = new List<List<MeshRenderer>>();
    private List<List<Material>> segmentMatGroups = new List<List<Material>>();
    private const int BarSegmentCount = 16; // 每段细分圆柱数

    private Vector3 baseNodeScale;
    private Vector3[] nodeBaseScales;   // 每个节点基础缩放，MISS 逐段缩小时乘以收缩系数
    private float baseBarSegRadius;
    private Vector3[] exitPositions;
    private float[] exitLeadTimes;
    private float rideY;
    private float breakTimer = 0f;
    private float fadeTimer = -1f;
    private bool missMode = false;        // 漏击后改为逐段越过判定线缩小消失（而非整条统一缩小）
    private bool missReported = false;    // MISS 反馈是否已上报（只报一次）
    private bool skillCleared = false;    // 清屏整条清除后置位：连接线应在原地逐段变大变白消失（覆盖漏击黑消失）
    private float missShrinkSpan = 0.5f;  // 越过判定线后多少距离内完成缩小消失

    [Tooltip("节点越过各自判定线后「按时间」缩小消失的时长（秒）。替代硬切隐藏，让音符是「变小」而非瞬间不见。[PLACEHOLDER 可微调]")]
    public float holdNodeShrinkDuration = 0.25f;

    // 命中放大（pop）表现：命中瞬间节点球体放大并变白，随后回落到基础缩放（与点击音符"放大变白"语义一致）
    private float[] nodePop;              // 每节点弹跳计时（>0 时放大），初始化于 BuildVisuals
    private float[] nodeShrinkStart;      // 每节点缩没动画起始时间（Time.time），-1 表示未开始，初始化于 BuildVisuals
    private float hitPopDuration = 0.18f; // 命中放大持续（秒）[PLACEHOLDER 可微调]
    private float hitPopScale = 1.35f;    // 命中峰值放大倍数 [PLACEHOLDER 可微调]

    // 命中后可见性缓冲：已命中（hasLit）或正在放大弹跳（nodePop>0）的元素，
    // 越过判定线后保留 glowBufferDist 一小段才隐藏，让"发白+放大"命中反馈能被看到（2b 修复）。
    private bool hasLit = false;          // 头节点命中后置 true，整条链命中期间保持
    private float glowBufferDist = 0.6f;  // 判定线后方保留距离（单位）[PLACEHOLDER 可微调]

    // 已完成的段数（段 i 表示 node[i] -> node[i+1]）
    private int completedSegments = 0;
    private int nodeCount;

    private static Mesh _cylMesh;
    private static Mesh CylinderMesh
    {
        get
        {
            if (_cylMesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _cylMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(tmp);
            }
            return _cylMesh;
        }
    }

    private static Shader _litShader;
    private static Shader LitShader
    {
        get
        {
            if (_litShader == null)
            {
                _litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (_litShader == null) _litShader = Shader.Find("Standard");
            }
            return _litShader;
        }
    }

    /// <summary>
    /// 创建 URP/Lit 材质并写入颜色。Standard fallback 仅用于极端情况。
    /// 默认开启透明混合，以便收尾时可以整体淡出。
    /// </summary>
    private Material CreateColoredMaterial(Color c)
    {
        Material mat = new Material(LitShader);
        mat.SetColor("_BaseColor", c);
        mat.color = c;
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0f);

        // 让 URP/Lit 支持 Alpha 淡出（透明渲染）
        mat.SetFloat("_Surface", 1f); // 1 = Transparent
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        return mat;
    }

    private void SetAlpha(float a)
    {
        for (int i = 0; i < nodeMats.Count; i++)
        {
            if (nodeMats[i] == null) continue;
            Color c = nodeMats[i].color; c.a = a; nodeMats[i].color = c;
        }
        for (int s = 0; s < segmentMatGroups.Count; s++)
        {
            for (int i = 0; i < segmentMatGroups[s].Count; i++)
            {
                if (segmentMatGroups[s][i] == null) continue;
                Color c = segmentMatGroups[s][i].color; c.a = a; segmentMatGroups[s][i].color = c;
            }
        }
    }

    private void ResetAllToBlack()
    {
        for (int i = 0; i < nodeMats.Count; i++)
        {
            if (nodeMats[i] == null) continue;
            nodeMats[i].color = Color.black;
        }
        for (int s = 0; s < segmentMatGroups.Count; s++)
        {
            for (int i = 0; i < segmentMatGroups[s].Count; i++)
            {
                if (segmentMatGroups[s][i] == null) continue;
                segmentMatGroups[s][i].color = Color.black;
            }
        }
    }

    /// <summary>触发某节点命中弹跳：放大 + 变白，随后由 UpdateHitPops 回落到基础缩放。</summary>
    private void PlayHitPop(int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= nodeCount) return;
        nodePop[nodeIndex] = 1f; // 触发弹跳（从峰值开始回落）
        if (nodeMats[nodeIndex] != null) nodeMats[nodeIndex].color = Color.white;
    }

    /// <summary>每帧推进各节点命中弹跳：放大倍数从峰值随时间回落到 1。漏击/断连时由 ApplyMissDisappear 接管缩放，此处跳过。</summary>
    private void UpdateHitPops()
    {
        if (missMode) return; // 漏击/断连时由 ApplyMissDisappear 接管缩放
        for (int i = 0; i < nodeCount; i++)
        {
            if (nodePop[i] <= 0f) continue;
            nodePop[i] = Mathf.Max(0f, nodePop[i] - Time.deltaTime / Mathf.Max(hitPopDuration, 0.0001f));
            float sc = 1f + (hitPopScale - 1f) * nodePop[i]; // 从峰值回落到基础
            nodeTransforms[i].localScale = nodeBaseScales[i] * sc;
        }
    }

    /// <summary>
    /// 单节点可见性 + 缩没动画（2026-08-26 Issue 4 核心修复）。
    /// 节点尚未抵达粉杠（reveal line）：保持隐藏，绝不启动缩没。
    /// 已抵达粉杠、但尚未完全越过各自判定线（hitPositions[i].x，含命中缓冲 glowBufferDist）：
    ///   正常显隐，scale 交给 UpdateHitPops（命中小弹跳）处理，此处不动 scale。
    /// 已完全越过各自判定线：不再瞬间硬切隐藏，而是「按时间」把 scale 从基准缩到 0，
    ///   时长 holdNodeShrinkDuration，缩没完成后才 disabled —— 让长按节点是「变小」而非「突然不见」。
    /// 用各自节点的判定线（而非统一的 head judgeLineX），保证节点 i 在到达“自己”的判定线之前不被裁掉。
    /// </summary>
    private void UpdateNodeVisual(int i, float cx, bool litGlow)
    {
        if (nodeRends[i] == null) return;

        bool revealed = IsBeyondLine(nodeTransforms[i].position.x, cx);
        float nodeJudgeX = hitPositions[i].x;
        float glowLineX = nodeJudgeX + (litGlow ? glowBufferDist : 0f);
        bool stillBeforeJudge = !IsFullyBeyondLine(nodeTransforms[i].position.x, noteRadius, glowLineX);

        // 尚未抵达粉杠：保持隐藏，不启动缩没
        if (!revealed)
        {
            nodeRends[i].enabled = false;
            return;
        }

        // 已抵达粉杠、但还没完全越过各自判定线：正常显隐，scale 留给 UpdateHitPops
        if (stillBeforeJudge)
        {
            nodeRends[i].enabled = true;
            return;
        }

        // 已完全越过各自判定线：启动/推进「按时间」缩没动画（替代硬切隐藏）
        if (nodeShrinkStart[i] < 0f) nodeShrinkStart[i] = Time.time;
        float t = (Time.time - nodeShrinkStart[i]) / Mathf.Max(holdNodeShrinkDuration, 0.0001f);
        if (t >= 1f)
        {
            nodeRends[i].enabled = false;
            nodeTransforms[i].localScale = Vector3.zero;
            return;
        }
        nodeRends[i].enabled = true;
        Vector3 baseScale = nodeBaseScales[i] * (1f - t);
        if (nodePop[i] > 0f)
        {
            float sc = 1f + (hitPopScale - 1f) * nodePop[i];
            baseScale = baseScale * sc;
        }
        nodeTransforms[i].localScale = baseScale;
    }

    void Start()
    {
        BuildVisuals();
    }

    private void BuildVisuals()
    {
        // 防御：确保 lanes/times 存在且至少 2 个节点、长度一致
        if (lanes == null || lanes.Length < 2 || times == null || times.Length < 2 || lanes.Length != times.Length)
        {
            Debug.LogError("[HoldNote] lanes/times 非法（需 ≥2 节点且等长），已退化为 2 节点默认 Hold。");
            lanes = new int[] { 0, 0 };
            times = new float[] { 0f, 1f };
        }
        nodeCount = lanes.Length;
        nodeBaseScales = new Vector3[nodeCount];
        nodePop = new float[nodeCount];
        nodeShrinkStart = new float[nodeCount];
        for (int i = 0; i < nodeCount; i++) nodeShrinkStart[i] = -1f; // -1 表示尚未开始缩没

        // 节点圆柱（逐节点宽度：连轨节点覆盖相邻两轨，普通节点单轨）
        float r = noteRadius / 0.5f;

        for (int i = 0; i < nodeCount; i++)
        {
            int span = NodeSpan(i);
            float linkedWidth = laneSpacing * (span - 1) + noteRadius * 2f;
            Vector3 nodeScale = new Vector3(span > 1 ? noteRadius * 2f : r, 0.12f, span > 1 ? linkedWidth : r);

            var go = new GameObject($"HoldNode_{i}");
            var t = go.transform;
            t.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            // 连轨 Hold 的头尾节点与连接带保持同样的圆角长方形外观，
            // 普通单轨 Hold 仍使用圆柱体，避免误改原有单轨视觉。
            mf.sharedMesh = span > 1 ? NoteMover.RoundedRectMesh : CylinderMesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            var mat = CreateColoredMaterial(Color.black);
            rend.material = mat;
            t.localScale = nodeScale;
            nodeBaseScales[i] = nodeScale;

            nodeTransforms.Add(t);
            nodeRends.Add(rend);
            nodeMats.Add(mat);
        }

        // 连接 bar：节点 i 与 i+1 之间一段，拆成多段便于逐段变白。
        // 段宽度取两端节点宽度的最大值（节点一宽一窄时也按宽的那端走），
        // mesh 与 localScale 都按段独立决定，避免 chain 级"全 Linked 化"。
        baseBarSegRadius = r * 0.18f;
        for (int s = 0; s < nodeCount - 1; s++)
        {
            int spanA = NodeSpan(s);
            int spanB = NodeSpan(s + 1);
            int segSpan = Mathf.Max(spanA, spanB);
            float linkedWidth = laneSpacing * (segSpan - 1) + noteRadius * 2f;

            var segTs = new List<Transform>();
            var segRends = new List<MeshRenderer>();
            var segMats = new List<Material>();
            for (int k = 0; k < BarSegmentCount; k++)
            {
                var segGo = new GameObject($"HoldSeg_{s}_{k}");
                var segT = segGo.transform;
                segT.SetParent(transform, false);
                var smf = segGo.AddComponent<MeshFilter>();
                smf.sharedMesh = segSpan > 1 ? NoteMover.RoundedRectMesh : CylinderMesh;
                var sRend = segGo.AddComponent<MeshRenderer>();
                sRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sRend.receiveShadows = false;
                var sMat = CreateColoredMaterial(Color.black);
                sRend.material = sMat;
                segT.localScale = segSpan > 1
                    ? new Vector3(0.001f, 0.12f, linkedWidth)
                    : new Vector3(baseBarSegRadius, 1f, baseBarSegRadius);
                segTs.Add(segT);
                segRends.Add(sRend);
                segMats.Add(sMat);
            }
            segmentGroups.Add(segTs);
            segmentRendGroups.Add(segRends);
            segmentMatGroups.Add(segMats);
        }

        if (charmOwnersByNode != null)
            for (int i = 0; i < charmOwnersByNode.Length; i++)
                if (charmOwnersByNode[i] != null) TintCharmedNode(i);

        // 预计算越过判定线的终点（再往前 1.5 单位），与 NoteMover 一致
        exitPositions = new Vector3[nodeCount];
        exitLeadTimes = new float[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            Vector3 spawn = spawnPositions[i];
            Vector3 hit = hitPositions[i];
            Vector3 dir = (hit - spawn).normalized;
            exitPositions[i] = hit + dir * 1.5f;
            float dist = Vector3.Distance(spawn, hit);
            exitLeadTimes[i] = leadTime * ((dist + 1.5f) / Mathf.Max(dist, 0.001f));
        }

        rideY = hitPositions[0].y;

        // 初始隐藏
        for (int i = 0; i < nodeRends.Count; i++) nodeRends[i].enabled = false;
        SetBarEnabledAll(false);
    }

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return;
        if (conductor == null) return;

        MoveAndFade();
        if (state != HoldState.Done) Judge();
    }

    private void MoveAndFade()
    {
        if (finished) return;

        float songTime = conductor.songPosition;

        UpdateHitPops(); // 推进各节点命中放大弹跳（missMode 时内部跳过，由 ApplyMissDisappear 接管缩放）

        if (fadeTimer < 0f)
        {
            // 正常移动：每个节点按各自时刻插值
            for (int i = 0; i < nodeCount; i++)
            {
                float t = Mathf.Clamp01((songTime - (times[i] - leadTime)) / exitLeadTimes[i]);
                Vector3 p = Vector3.Lerp(spawnPositions[i], exitPositions[i], t);
                nodeTransforms[i].position = new Vector3(p.x, rideY, p.z);
            }

            UpdateAllSegments();
            UpdateSegmentColors(songTime);

            // 可见性：按元素"前沿"是否越过粉杠决定显示
            float cx = centerLine != null ? centerLine.currentX : 0f;
            // 修复（2026-08-26 Issue 4）：节点可见性 + 缩没动画统一交给 UpdateNodeVisual，
            // 逾越各自判定线后「按时间」缩小消失（holdNodeShrinkDuration），而非瞬间硬切隐藏。
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeRends[i] != null)
                    UpdateNodeVisual(i, cx, hasLit || nodePop[i] > 0f);
            }
            UpdateSegmentVisibility(cx, judgeLineX);

            // 漏击消失：每个节点 / 段小片各自越过判定线后缩小并淡出（不再整条统一缩小）
            if (missMode)
            {
                ApplyMissDisappear(cx, judgeLineX);

                // 断连/漏击后，附魔节点仍要等到各自越过判定线才算“经过一个音符”，不能在断连瞬间提前结算。
                for (int i = 0; i < nodeCount; i++)
                    if (songTime >= times[i]) ResolveCharmedNode(i, false);

                // 漏击的首节点越过判定线并缩没后，立即上报一次 MISS（不等待整条链）
                float headPast = side == 0
                    ? (judgeLineX - nodeTransforms[0].position.x)
                    : (nodeTransforms[0].position.x - judgeLineX);
                if (!missReported && headPast >= missShrinkSpan)
                {
                    missReported = true;
                    onJudge?.Invoke(side, lanes[0], "MISS", hitPositions[0]);
                }

// 所有节点都缩没后才销毁对象（期间其余节点继续逐片消失）
            // 修复（2026-08-26 Issue 4）：逐节点用各自判定线 + 缩没动画完成作为销毁条件，
            // 节点越过判定线后必须等 holdNodeShrinkDuration 缩没动画跑完才视为消失，避免硬切 / 过早销毁。
            bool allGone = true;
            for (int i = 0; i < nodeCount; i++)
            {
                float nodeJudgeX = hitPositions[i].x;
                bool fullyPast = IsFullyBeyondLine(nodeTransforms[i].position.x, noteRadius, nodeJudgeX);
                if (!fullyPast) { allGone = false; break; }
                if (nodeShrinkStart[i] < 0f || (Time.time - nodeShrinkStart[i]) < holdNodeShrinkDuration) { allGone = false; break; }
            }
                if (allGone)
                {
                    for (int i = 0; i < nodeCount; i++) ResolveCharmedNode(i, false);
                    finished = true;
                    Destroy(gameObject);
                    return;
                }
            }
        }
        else
        {
            // 收尾：音符继续按原速度移动，用判定线 judgeLineX 作为固定的消失边界。
            // 清屏消灭（skillCleared）走命中反馈（白 + 放大 + 淡出），不要再重置成黑色。
            if (fadeTimer <= 0f && !skillCleared)
            {
                ResetAllToBlack();
            }
            fadeTimer += Time.deltaTime;

            for (int i = 0; i < nodeCount; i++)
            {
                float t = Mathf.Clamp01((songTime - (times[i] - leadTime)) / exitLeadTimes[i]);
                Vector3 p = Vector3.Lerp(spawnPositions[i], exitPositions[i], t);
                nodeTransforms[i].position = new Vector3(p.x, rideY, p.z);
            }

            UpdateAllSegments();

            float judgeX = judgeLineX;
            // 修复（2026-08-26 Issue 4）：收尾阶段节点可见性 + 缩没动画同样交给 UpdateNodeVisual，
            // 逾越各自判定线后「按时间」缩小消失，避免刚越过判定线就被销毁而看不到缩没。
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeRends[i] != null)
                    UpdateNodeVisual(i, centerLine != null ? centerLine.currentX : 0f, hasLit || nodePop[i] > 0f);
            }
            UpdateSegmentVisibility(centerLine != null ? centerLine.currentX : 0f, judgeX);

            // 完成：整体透明度淡出；断连/漏击：保持不透明，只通过判定线裁剪消失
            if (!broken)
            {
                float alpha = 1f - Mathf.Clamp01(fadeTimer / fadeDuration);
                SetAlpha(alpha);
            }

            // 结束条件：整根越过判定线（以尾节点或最前节点为准），或超过最大存活时间
            // 修复（2026-08-26 Issue 4）：收尾阶段节点越过各自判定线后，同样要等缩没动画跑完才算消失，
            // 否则节点刚越过判定线就被销毁、缩没动画看不到（"突然不见"）。
            bool allGone = true;
            for (int i = 0; i < nodeCount; i++)
            {
                float nodeJudgeX = hitPositions[i].x;
                if (!IsFullyBeyondLine(nodeTransforms[i].position.x, noteRadius, nodeJudgeX)) { allGone = false; break; }
                if (nodeShrinkStart[i] < 0f || (Time.time - nodeShrinkStart[i]) < holdNodeShrinkDuration) { allGone = false; break; }
            }
            if (allGone || fadeTimer > maxFadeLife)
            {
                finished = true;
                Destroy(gameObject);
            }
        }
    }

    private void UpdateAllSegments()
    {
        for (int s = 0; s < segmentGroups.Count; s++)
        {
            Vector3 a = nodeTransforms[s].position;
            Vector3 b = nodeTransforms[s + 1].position;
            Vector3 dir = b - a;
            float totalLen = dir.magnitude;
            var segs = segmentGroups[s];
            var rends = segmentRendGroups[s];

            if (totalLen < 0.001f)
            {
                for (int k = 0; k < segs.Count; k++)
                {
                    segs[k].position = a;
                    segs[k].localScale = new Vector3(segs[k].localScale.x, 0.001f, segs[k].localScale.z);
                }
                continue;
            }

            // 逐段宽度：取两端节点各自的宽度（1=普通单轨，2=连轨），
            // 不再用链级 laneSpan，从而避免“链内有一个连轨节点就把所有段都画粗”。
            int spanA = NodeSpan(s);
            int spanB = NodeSpan(s + 1);
            bool hasWide = spanA > 1 || spanB > 1;

            Vector3 normDir = dir.normalized;
            float headEdgeOffset = noteRadius;
            float tailEdgeOffset = noteRadius;
            float usableLen = Mathf.Max(0f, totalLen - headEdgeOffset - tailEdgeOffset);
            Vector3 start = a + normDir * headEdgeOffset;
            Vector3 end = b - normDir * tailEdgeOffset;

            int n = segs.Count;
            float segLen = n > 1 ? usableLen / n * 1.05f : usableLen;

            // 连轨/混合段的阶梯参数（沿 X 步进、Z 插值，避免斜四边形）
            float directionX = Mathf.Sign(b.x - a.x);
            if (Mathf.Abs(b.x - a.x) < 0.001f) directionX = side == 0 ? -1f : 1f;
            float startX = a.x + directionX * noteRadius;
            float endX = b.x - directionX * noteRadius;
            float usableX = Mathf.Abs(endX - startX);

            for (int k = 0; k < n; k++)
            {
                float t = (k + 0.5f) / n;
                segs[k].position = Vector3.Lerp(start, end, t);

                if (!hasWide)
                {
                    // 普通 ↔ 普通：细连接线（圆柱）
                    segs[k].rotation = Quaternion.FromToRotation(Vector3.up, normDir);
                    segs[k].localScale = new Vector3(baseBarSegRadius, segLen, baseBarSegRadius);
                }
                else
                {
                    // 含连轨节点：阶梯宽带。混合段（一端连轨一端普通）按位置插值宽度
                    // => 粗到细的渐变链接，而不是整段统一粗。
                    float kSpan = Mathf.Lerp(spanA, spanB, t);
                    float width = laneSpacing * (kSpan - 1) + noteRadius * 2f;
                    float stairSegLen = usableX / n * 1.12f;
                    float x = Mathf.Lerp(startX, endX, t);
                    float z = Mathf.Lerp(a.z, b.z, t);
                    segs[k].position = new Vector3(x, a.y, z);
                    segs[k].rotation = Quaternion.identity;
                    segs[k].localScale = new Vector3(stairSegLen, 0.12f, width);
                }
            }
        }
    }

    /// <summary>
    /// 按住期间：已完成段保持白色，当前进行中的段按"子片世界坐标是否已越过判定线"逐片点亮（发白前沿严格落在判定线上），未到达段保持黑色。
    /// </summary>
    private void UpdateSegmentColors(float songTime)
    {
        if (state != HoldState.Holding) return;

        for (int s = 0; s < segmentGroups.Count; s++)
        {
            if (s < completedSegments)        // 已完成段：全白
            {
                var mc = segmentMatGroups[s];
                for (int k = 0; k < mc.Count; k++) if (mc[k] != null) mc[k].color = Color.white;
                continue;
            }
            if (s > completedSegments)        // 未来段：全黑
            {
                var mf = segmentMatGroups[s];
                for (int k = 0; k < mf.Count; k++) if (mf[k] != null) mf[k].color = Color.black;
                continue;
            }
            // 当前段：按子片世界坐标是否已越过判定线逐片点亮，白边严格落在判定线上
            // （复用与可见性一致的 IsBeyondLine，方向语义正确；不再用时间比例导致超前发白）
            var segs = segmentGroups[s];
            var mats = segmentMatGroups[s];
            for (int k = 0; k < segs.Count; k++)
            {
                if (mats[k] == null) continue;
                bool passed = IsBeyondLine(segs[k].position.x, judgeLineX);
                mats[k].color = passed ? Color.white : Color.black;
            }
        }
    }

    private void UpdateSegmentVisibility(float revealLineX, float hideLineX)
    {
        // 链接线小片的消失边界统一用"玩家判定线 hideLineX"（与 UpdateSegmentColors 的变白线一致），
        // 不再用段中点 segJudgeX，从而链接线能保持到判定线位置才"变白 → 放大 → 淡出"消失。
        for (int s = 0; s < segmentGroups.Count; s++)
        {
            int spanA = NodeSpan(s);
            int spanB = NodeSpan(s + 1);
            bool hasWide = spanA > 1 || spanB > 1;

            var segs = segmentGroups[s];
            var rends = segmentRendGroups[s];
            var mats = segmentMatGroups[s];
            Vector3 a = nodeTransforms[s].position;
            Vector3 b = nodeTransforms[s + 1].position;
            Vector3 dir = b - a;
            float totalLen = dir.magnitude;
            Vector3 normDir = totalLen < 0.001f ? Vector3.right : dir.normalized;

            // 已命中的整条链：段带过判定线后保留一小段可见，逐片发白反馈被看到（2b）。
            float glowLineX = hideLineX + (hasLit ? glowBufferDist : 0f);
            float disappearSpan = 1.2f;   // 越线后放大淡出的行程（本地坐标单位）[PLACEHOLDER 可微调]

            for (int k = 0; k < segs.Count; k++)
            {
                if (rends[k] == null) continue;
                // 清屏整条清除：连接线应在原地逐段变大变白消失（与玩家手动完成一致），
                // 覆盖正常的"漏击黑消失"路径——技能清掉的长按节点还在带区内、未越判定线，否则会发黑消失。
                if (skillCleared)
                {
                    rends[k].enabled = true;
                    segs[k].localScale = segs[k].localScale * 1.6f;
                    if (mats[k] != null) mats[k].color = Color.white;
                    float fa = 1f - Mathf.Clamp01(fadeTimer / fadeDuration);
                    SetMatAlpha(mats[k], fa);
                    continue;
                }
                if (totalLen < 0.001f)
                {
                    bool collapsedRevealed = IsBeyondLine(segs[k].position.x, revealLineX);
                    bool collapsedBeforeJudge = !IsFullyBeyondLine(segs[k].position.x, noteRadius, glowLineX);
                    rends[k].enabled = collapsedRevealed && collapsedBeforeJudge;
                    continue;
                }
                float halfLenX;
                if (hasWide)
                {
                    float halfAlong = segs[k].localScale.x * 0.5f * Mathf.Abs(normDir.x);
                    float halfAcross = segs[k].localScale.z * 0.5f * Mathf.Abs(normDir.z);
                    halfLenX = halfAlong + halfAcross;
                }
                else
                {
                    halfLenX = segs[k].localScale.y * 0.5f * Mathf.Abs(normDir.x);
                }
                bool revealed = IsBeyondLine(segs[k].position.x, revealLineX);
                if (!revealed) { rends[k].enabled = false; continue; }

                bool stillBeforeJudge = !IsFullyBeyondLine(segs[k].position.x, halfLenX, glowLineX);
                if (stillBeforeJudge)
                {
                    rends[k].enabled = true;   // 判定线之前：正常显示（颜色由 UpdateSegmentColors 控制）
                }
                else
                {
                    // 已越过判定线：放大 ×1.6 + 变白 + alpha 淡出，最后才禁用（完成态连接线"逐段变大变白消失"）
                    float past = side == 0 ? (glowLineX - segs[k].position.x) : (segs[k].position.x - glowLineX);
                    float f = Mathf.Clamp01(past / disappearSpan);
                    segs[k].localScale = segs[k].localScale * Mathf.Lerp(1f, 1.6f, f);
                    if (!missMode && mats[k] != null) mats[k].color = Color.white;   // 完成态确保变白（放大淡出前先变白）
                    SetMatAlpha(mats[k], 1f - f);
                    rends[k].enabled = f < 0.98f;
                }
            }
        }
    }

    /// <summary>
    /// 漏击消失：每个节点与每个段小片在“越过判定线后”各自缩小并淡出，
    /// 表现与普通点击音符一致（先变小后消失），而不是整条链统一缩小。
    /// 越过判定线的距离越远（最多 missShrinkSpan），收缩越彻底，缩没即隐藏。
    /// </summary>
    private void ApplyMissDisappear(float revealLineX, float judgeX)
    {
        // 节点缩没已由 MoveAndFade 的 UpdateNodeVisual 统一处理（按时间逐节点缩小消失），
        // 这里不再对节点做距离缩放，避免与缩没动画冲突或硬切。

        // 段带：每个细分小片在“越过各自段中点的判定线后”缩小消失（漏击/断连时逐段滑走消失）
        for (int s = 0; s < segmentGroups.Count; s++)
        {
            var segs = segmentGroups[s];
            var rends = segmentRendGroups[s];
            var mats = segmentMatGroups[s];
            // 修复（2026-08-26 Issue 4）：每段用自己的中点判定线（两端节点判定线中点），
            // 而非统一的 head 判定线，避免整条段带过早/过晚被裁切。
            float segJudgeX = (hitPositions[s].x + hitPositions[s + 1].x) * 0.5f;
            for (int k = 0; k < segs.Count; k++)
            {
                if (rends[k] == null) continue;
                bool revealed = IsBeyondLine(segs[k].position.x, revealLineX);
                if (!revealed) { rends[k].enabled = false; continue; }

                float past = side == 0
                    ? (segJudgeX - segs[k].position.x)
                    : (segs[k].position.x - segJudgeX);
                float shrink = past <= 0f ? 1f : 1f - Mathf.Clamp01(past / missShrinkSpan);

                // 段缩放每帧由 UpdateAllSegments 重新写回基准值，这里直接乘以收缩系数即可
                segs[k].localScale = segs[k].localScale * shrink;
                SetMatAlpha(mats[k], shrink);
                rends[k].enabled = shrink > 0.02f;
            }
        }
    }

    /// <summary>把一个被附魔节点及通向它的连接段染成黄色发光。</summary>
    private void TintCharmedNode(int nodeIndex)
    {
        // 附魔颜色取释放方自身颜色（charmOwnersByNode 已登记 owner）；无 owner 时回退黄
        Color col = (charmOwnersByNode != null && nodeIndex >= 0 && nodeIndex < charmOwnersByNode.Length && charmOwnersByNode[nodeIndex] != null)
            ? charmOwnersByNode[nodeIndex].charmColor : new Color(1f, 0.85f, 0.1f);
        if (nodeIndex >= 0 && nodeIndex < nodeMats.Count && nodeMats[nodeIndex] != null)
        {
            nodeMats[nodeIndex].EnableKeyword("_EMISSION");
            nodeMats[nodeIndex].SetColor("_EmissionColor", col);
        }

        int segmentIndex = nodeIndex - 1;
        if (segmentIndex >= 0 && segmentIndex < segmentMatGroups.Count)
        {
            for (int k = 0; k < segmentMatGroups[segmentIndex].Count; k++)
            {
                var m = segmentMatGroups[segmentIndex][k];
                if (m == null) continue;
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", col);
            }
        }
    }

    private void SetMatAlpha(Material mat, float a)
    {
        if (mat == null) return;
        Color c = mat.color;
        c.a = Mathf.Clamp01(a);
        mat.color = c;
    }

    private void SetBarEnabledAll(bool enabled)
    {
        for (int s = 0; s < segmentRendGroups.Count; s++)
        {
            for (int k = 0; k < segmentRendGroups[s].Count; k++)
            {
                if (segmentRendGroups[s][k] != null) segmentRendGroups[s][k].enabled = enabled;
            }
        }
    }

    /// <summary>
    /// 某点是否已经越过指定竖直线（向判定线/粉杠方向前进）。
    /// side==0（左玩家，音符从右向左飞）：x < lineX 表示已越过。
    /// side==1（右玩家，音符从左向右飞）：x > lineX 表示已越过。
    /// </summary>
    private bool IsBeyondLine(float x, float lineX)
    {
        return side == 0 ? x < lineX : x > lineX;
    }

    /// <summary>该长按节点是否已越过中间粉杠（显现）。供附魔候选判定：只有已显现的节点才可被附魔。</summary>
    public bool IsNodeRevealed(int index)
    {
        if (index < 0 || index >= NodeCount || nodeTransforms == null || index >= nodeTransforms.Count || nodeTransforms[index] == null) return false;
        float cx = centerLine != null ? centerLine.currentX : 0f;
        return IsBeyondLine(nodeTransforms[index].position.x, cx);
    }

    /// <summary>清屏技能：把整条长按视为命中清除。
    /// 带区内已显现的节点逐节点变白弹跳 + 调用 caster.OnSkillClearedNode（按节点音轨计分/连击/充能 + 弹 PERFECT）；
    /// 随后进入完成淡出：连接线在判定线处逐段变大变白消失（复用既有"完成"表现）。
    /// 由 NoteSpawner.SkillClearBand 逐条 HoldNote 调用。</summary>
    public void SkillClearWhole(float xMin, float xMax, ActiveSkillRuntime caster)
    {
        if (caster == null || nodeTransforms == null) return;
        if (state == HoldState.Done) return;   // 已完成/已漏击的长按不再重复计分（避免与正常完成重复记账）

        // 是否至少有一个已显现且在带区内的节点
        bool anyInBand = false;
        for (int i = 0; i < NodeCount; i++)
        {
            if (!IsNodeRevealed(i)) continue;
            float x = (hitPositions != null && i < hitPositions.Length) ? hitPositions[i].x : 0f;
            if (x >= xMin && x <= xMax) { anyInBand = true; break; }
        }
        if (!anyInBand) return;

        // 整条长按被清屏消灭：必须按"整条音符的全部段"计分/连击/充能（与玩家手动完成一致），
        // 而非仅带区内已显现的节点——否则整条被消灭却只拿到部分分数/能量/连击。
        // 头节点视为最佳命中 PERFECT（仅当尚未被手动起手命中）；其后各段 CLEAR；
        // 已手动完成的段（completedSegments 之前）不再重复记账。
        if (completedSegments == 0)
        {
            bool headVis = IsNodeRevealed(0) && (hitPositions == null || (hitPositions[0].x >= xMin && hitPositions[0].x <= xMax));
            if (headVis) PlayHitPop(0);
            caster.OnSkillClearedNode(this, 0, "PERFECT");
            ResolveCharmedNode(0, true);
        }
        for (int i = completedSegments + 1; i < NodeCount; i++)
        {
            bool vis = IsNodeRevealed(i) && (hitPositions == null || (hitPositions[i].x >= xMin && hitPositions[i].x <= xMax));
            if (vis) PlayHitPop(i);
            caster.OnSkillClearedNode(this, i, "CLEAR");
            ResolveCharmedNode(i, true);
        }

        // 进入完成淡出：整条连接线 + 全部节点原地变白放大消失（命中反馈，与玩家手动完成一致）。
        // 整条所有节点都做命中弹跳（变白 + 放大），完成态段全白。
        for (int i = 0; i < nodeCount; i++) PlayHitPop(i);
        completedSegments = NodeCount - 1;
        hasLit = true;
        skillCleared = true;   // 标记：连接线/节点应原地变白放大消失，而非漏击黑消失
        Complete();
    }

    /// <summary>判断一个带有 X 半宽的视觉元素是否已经完整越过指定判定线。</summary>
    private bool IsFullyBeyondLine(float x, float halfExtent, float lineX)
    {
        return side == 0 ? x + halfExtent < lineX : x - halfExtent > lineX;
    }

    /// <summary>
    /// 检查当前是否有按住的轨道与目标浮点轨道相差在 laneTolerance 以内。
    /// </summary>
    private bool IsAnyHeldLaneClose(float targetLane)
    {
        if (spawner == null) return false;
        foreach (int held in spawner.heldLanes)
        {
            if (Mathf.Abs(held - targetLane) <= laneTolerance) return true;
        }
        return false;
    }

    /// <summary>节点 i 的宽度（1=普通单轨，2=连轨覆盖相邻两轨）。无逐节点数据时用整条链 laneSpan 兜底。</summary>
    private int NodeSpan(int i)
    {
        if (nodeLaneSpans != null && i >= 0 && i < nodeLaneSpans.Length)
            return nodeLaneSpans[i] > 1 ? 2 : 1;
        return laneSpan > 1 ? 2 : 1;
    }

    /// <summary>当前 songTime 所处"应被按住"的轨道判定。</summary>
    /// <param name="songTime">当前歌曲时间（秒），用于判断连轨滑动段所处的阶段与中点窗口。</param>
    /// <param name="reqLaneF">当前插值轨道（来自 SampleLaneAtTime，保留滑动过渡，仅普通单轨 Hold 使用）。</param>
    private bool AreRequiredLanesHeld(float songTime, float reqLaneF)
    {
        if (spawner == null) return false;

        // 普通单轨 Hold：沿用浮点容差跟随（laneTolerance）
        if (laneSpan <= 1) return IsAnyHeldLaneClose(reqLaneF);

        // 连轨 Hold：以"合法轨道集合"判定——玩家按住集合中任一轨道即视为跟随成功。
        // 集合 = 起点节点覆盖轨道 ∪ 终点节点覆盖轨道 ∪ 路径扫过的整数轨道。
        // 与静止宽音符语义一致（按覆盖的任一条轨都合法），消除"必须按时序从 from 滑到 to"的反直觉约束。
        int seg = CurrentSegmentIndex(songTime);
        int fromLane = lanes[seg];
        int toLane = lanes[seg + 1];
        int fromSpan = NodeSpan(seg);     // 节点 seg 宽度：1=单轨，2=连轨覆盖相邻两轨
        int toSpan = NodeSpan(seg + 1);   // 节点 seg+1 宽度

        System.Collections.Generic.HashSet<int> validSet = new System.Collections.Generic.HashSet<int>();
        // 起点节点覆盖：[fromLane, fromLane + fromSpan - 1]
        for (int x = fromLane; x < fromLane + fromSpan; x++) validSet.Add(x);
        // 终点节点覆盖：[toLane, toLane + toSpan - 1]
        for (int x = toLane; x < toLane + toSpan; x++) validSet.Add(x);
        // 路径上扫过的整数轨道（含两端 + 中间跨越）
        int lo = Mathf.Min(fromLane, toLane);
        int hi = Mathf.Max(fromLane, toLane);
        for (int x = lo; x <= hi; x++) validSet.Add(x);

        // 玩家按住的任一轨道在集合内即合法
        foreach (int held in spawner.heldLanes)
            if (validSet.Contains(held)) return true;
        return false;
    }

    /// <summary>返回 songTime 当前所处的段索引（段 i 表示 node[i] -> node[i+1]）。</summary>
    private int CurrentSegmentIndex(float songTime)
    {
        for (int i = 0; i < nodeCount - 1; i++)
        {
            if (songTime >= times[i] && songTime < times[i + 1]) return i;
        }
        return Mathf.Max(0, nodeCount - 2);
    }

    /// <summary>
    /// 普通 Hold 从 head 轨起手；连轨 Hold 按下覆盖范围的任意一条轨道即可起手（判定宽一个音轨）。
    /// 头节点宽度用自身 NodeSpan(0)，避免“后续连轨节点”把头节点也当成宽轨。
    /// </summary>
    public bool CanStartOnLane(int pressedLane)
    {
        if (lanes == null || lanes.Length == 0) return false;
        int span0 = NodeSpan(0);
        int start = Mathf.Clamp(lanes[0], 0, Mathf.Max(0, spawner != null ? spawner.laneCount - span0 : lanes[0]));
        if (pressedLane < start || pressedLane >= start + span0) return false;
        // 按下时该轨已在 heldLanes 中：普通头节点即该轨；连轨头节点为覆盖的任一条轨
        return true;
    }

    /// <summary>
    /// 判定状态机。由自身 Update 每帧调用。
    /// </summary>
    private void Judge()
    {
        float songTime = conductor.songPosition;

        if (state == HoldState.Waiting)
        {
            if (songTime > times[0] + goodWindow)
            {
                MissHead();
            }
            return;
        }

        if (state == HoldState.Holding)
        {
            // 当前进度位置：在节点链上插值出的浮点轨道（保留滑动过渡手感）
            float reqLaneF = SampleLaneAtTime(songTime);

            // 按住检测（AI 跳过，由 isAI 标志控制）
            if (!isAI)
            {
                bool held = AreRequiredLanesHeld(songTime, reqLaneF);
                if (!held) breakTimer += Time.deltaTime;
                else breakTimer = 0f;

                if (breakTimer > breakThreshold)
                {
                    Break();
                    return;
                }
            }

            // 每跨越一个节点时刻，完成一段（节点 i -> i+1）=> 触发 CLEAR
            while (completedSegments < nodeCount - 1 && songTime >= times[completedSegments + 1])
            {
                completedSegments++;
                // 节点变白 + 放大弹跳（命中反馈）
                PlayHitPop(completedSegments);
                // 上报该段 CLEAR：位置取该段"结束节点"的判定线处
                Vector3 segEndPos = hitPositions[completedSegments];
                onJudge?.Invoke(side, lanes[completedSegments], "CLEAR", segEndPos);
                // 附魔：该段完成 = 1 个附魔单位（节点）成功
                ResolveCharmedNode(completedSegments, true);
            }

            // 全部节点完成 -> Done
            if (completedSegments >= nodeCount - 1)
            {
                Complete();
            }
        }
    }

    /// <summary>
    /// 在节点链上按时间插值出当前"所需轨道"（浮点）。
    /// 仅用于按住检测，不影响视觉位置。
    /// </summary>
    private float SampleLaneAtTime(float songTime)
    {
        if (songTime <= times[0]) return lanes[0];
        if (songTime >= times[nodeCount - 1]) return lanes[nodeCount - 1];

        for (int i = 0; i < nodeCount - 1; i++)
        {
            float t0 = times[i], t1 = times[i + 1];
            if (songTime >= t0 && songTime <= t1)
            {
                float f = Mathf.Clamp01((songTime - t0) / Mathf.Max(t1 - t0, 0.0001f));
                return Mathf.Lerp(lanes[i], lanes[i + 1], f);
            }
        }
        return lanes[nodeCount - 1];
    }

    /// <summary>
    /// 由 NoteSpawner 在对应轨道按下且处于 head 时间窗内时调用，起手长按。
    /// </summary>
    public void StartHold(float songTime, bool fromAI)
    {
        if (state != HoldState.Waiting) return;
        state = HoldState.Holding;
        if (fromAI) isAI = true;

        float dt = songTime - times[0];
        float absDt = Mathf.Abs(dt);
        string rank = absDt <= perfectWindow ? "PERFECT" : "GOOD";

        // 命中反馈：head 变白 + 放大弹跳
        hasLit = true;
        PlayHitPop(0);

        onJudge?.Invoke(side, lanes[0], rank, hitPositions[0]);
        // 附魔：头节点达成 = 1 个附魔单位成功
        ResolveCharmedNode(0, true);
    }

    private void MissHead()
    {
        state = HoldState.Done;
        broken = true;
        fadeTimer = -1f;
        ResetAllToBlack();
        // 首节点漏击后整条链接立即失效。改为“逐段越过判定线缩小消失”，
        // 由 MoveAndFade 每帧调用 ApplyMissDisappear 处理，而不是整条统一缩小。
        missMode = true;

        ResolveCharmedNode(0, false);
    }

    private void Break()
    {
        state = HoldState.Done;
        broken = true;
        fadeTimer = -1f;     // 走漏击"逐段滑过判定线缩小"路径（与 MissHead 一致）
        missMode = true;     // 同上：由 MoveAndFade 的 ApplyMissDisappear 处理滑走消失
        missReported = true; // 关键：阻止 MoveAndFade 的 missMode 分支重复上报 MISS（避免一次断连两个 MISS）
        // 唯一一次评价：在最后成功节点处报 BREAK（非 MISS）。
        // BREAK 与 MISS 的区别：MISS 会清零连击（普通漏击），BREAK 表示「已命中若干节点后中途断连」，
        // 不应把前面已完成的节点的连击清零（Issue 3）。计分/过热判定均按「失败」处理（与 MISS 同效）。
        int lastNode = Mathf.Max(0, completedSegments);
        onJudge?.Invoke(side, lanes[lastNode], "BREAK", hitPositions[lastNode]);

    }

    private void Complete()
    {
        state = HoldState.Done;
        broken = false;
        fadeTimer = 0f;
        // 尾节点变白 + 放大弹跳；全部段白色
        PlayHitPop(nodeCount - 1);
        for (int s = 0; s < segmentMatGroups.Count; s++)
            for (int k = 0; k < segmentMatGroups[s].Count; k++)
                if (segmentMatGroups[s][k] != null) segmentMatGroups[s][k].color = Color.white;
        // 最后一段 CLEAR 已在 Judge 循环中于尾节点处上报，这里不再重复上报

    }
}
