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
    private List<Texture2D> nodeBaseTex = new List<Texture2D>();   // 节点基础贴图（tap / wide）
    private List<Texture2D> nodeSelectTex = new List<Texture2D>(); // 节点命中 Select 贴图（tapSelect / wideSelect）

    // ===== 链接带（ribbon）视觉：每段一个连续 mesh + 一张 select 覆盖带，替代原 16 圆柱细分 =====
    private List<GameObject> bandGOs = new List<GameObject>();
    private List<Mesh> bandMeshes = new List<Mesh>();
    private List<Material> bandMats = new List<Material>();
    private List<MeshRenderer> bandRends = new List<MeshRenderer>();
    private List<Mesh> bandOverlayMeshes = new List<Mesh>();   // select 覆盖带：跟随进度线从链头渐覆到进度点
    private List<Material> bandOverlayMats = new List<Material>();
    private List<MeshRenderer> bandOverlayRends = new List<MeshRenderer>();
    private Transform progressMarker;
    private Material progressMat;
    private Sprite slideLinkSprite, slideLinkSelectSprite, slideJudgmentSprite;
    private Texture2D slideLinkTex, slideLinkSelectTex, slideJudgmentTex;
    private float markerDist = 0f;     // 进度线位置=从判定线沿链体(朝 spawn 侧)已覆盖距离(世界单位)；<0=越判定线内→Miss
    private float normalSpeed = 1f;    // 音符移动速度(Setup 用 exitLeadTimes 推算，值同 NoteMover)

    [Tooltip("链接带端点内缩比例（相对节点贴图实际半宽）：1=带子刚好从节点贴图外沿开始/结束，0=贴到节点中心（被音符完全盖住，无缝）。")]
    public float bandEndInsetRatio = 0.0f;
    [Tooltip("③ 链接带端锚点向节点内埋入的深度（世界单位）：带子端边落在节点贴图下方被盖住（renderQueue 3000>2998），旋转/接缝时不再露缝、不扫过音符顶部。配合双侧固定侧边锚点用。")]
    public float nodeBandOverlap = 0.12f;
    [Tooltip("链接带整体宽度系数（乘到 BandWidth 上）：1=与所连节点同宽，<1=更细。带子粗细由几何决定，与贴图尺寸无关。")]
    public float bandWidthScale = 0.6f;
    [Tooltip("链拍带越过判定线后软边消失的过渡宽度（世界单位，smoothstep）。")]
    public float bandFadeWidth = 0.8f;
    [Tooltip("链接带每段纵向细分段数，越大曲线越平滑。")]
    public int bandSubdiv = 24;
    [Tooltip("链接带越过粉杠（中线）后逐顶点渐显的过渡宽度（世界单位，smoothstep）。替代旧的全局计时渐显。")]
    public float bandRevealWidth = 0.5f;
    [HideInInspector][Tooltip("（已弃用 v2）旧稳定位偏移，v2 稳定位=判定线(0)，仅保留字段防反序列化丢数据。")]
    public float markerHoldOffset = 0.5f;
    [Tooltip("Miss 判定距离（世界单位）：进度线衰退到判定线内该距离时触发断连 Miss（默认 0.5）。")]
    public float markerMissDist = 0.5f;
    [Tooltip("链接带越过判定线后带宽缩小下限（1=不缩，0.75=缩到 3/4）。与通用命中缩小一致。")]
    public float bandShrinkMin = 0.75f;
    [Tooltip("进度线 marker 缩放倍数（基于 slideJudgment 贴图原始尺寸 1:1）。1=完全按贴图尺寸。")]
    public float progressMarkerScale = 1.0f;
    [Tooltip("进度线 marker 绕 Y 轴偏航角（度）。slideJudgment 贴图为 9x69 竖窄条（长边在 quad 的 Z 边），默认 0 即长边沿 Z=垂直音轨线；90 会把竖条放倒成沿音轨的横条。")]
    public float progressMarkerYaw = 0f;
    [Tooltip("进度线相对 rideY 的抬升高度（世界单位）。0.075=运行期下调70%后实测值；遮挡主要靠材质渲染队列(3100)解决，高度只做微调。")]
    public float progressMarkerLift = 0.075f;
    [Tooltip("链接带相对节点下沉量（世界单位），防 z-fighting 闪。")]
    public float bandRideYDip = 0.02f;
    [Tooltip("链接带沿整条链长的连续顶点色渐变（subtle），长段也有一致渐变感，不依赖纹理拉伸。")]
    public bool bandGradient = true;
    [Tooltip("链长渐变强度（0=关，0.2=头端暗 20%）。")]
    public float bandGradientStrength = 0.2f;

    private Vector3 baseNodeScale;
    private Vector3[] nodeBaseScales;   // 每个节点基础缩放，MISS 逐段缩小时乘以收缩系数
    private float baseBarSegRadius;
    private Vector3[] exitPositions;
    private float[] exitLeadTimes;
    private float rideY;
    [Tooltip("起手宽限（秒）：StartHold 后头 0.08s 内视作按住，立刻建进度、不误断连。")]
    public float holdStartGrace = 0.08f;
    private float holdStartTime = -999f;
    private float fadeTimer = -1f;
    private bool missMode = false;        // 漏击后改为逐段越过判定线缩小消失（而非整条统一缩小）
    private bool missReported = false;    // MISS 反馈是否已上报（只报一次）
    private bool skillCleared = false;    // 清屏整条清除后置位：连接线应在原地逐段变大变白消失（覆盖漏击黑消失）
    private float missShrinkSpan = 0.5f;  // 越过判定线后多少距离内完成缩小消失
    [Tooltip("④ Miss 时节点（贴图 quad）的不透明度：改到 0.6，让链接音符漏击时变半透而非硬切消失。")]
    private float missNodeAlpha = 0.6f;    // [2026-09-18 ④由 1 改 0.6]

    [Tooltip("节点越过各自判定线后「按时间」缩小消失的时长（秒）。替代硬切隐藏，让音符是「变小」而非瞬间不见。[PLACEHOLDER 可微调]")]
    public float holdNodeShrinkDuration = 0.25f;

    // 命中放大（pop）表现：命中瞬间节点球体放大并变白，随后回落到基础缩放（与点击音符"放大变白"语义一致）
    private float[] nodePop;              // 每节点弹跳计时（>0 时放大），初始化于 BuildVisuals
    private float[] nodeShrinkStart;      // 每节点缩没动画起始时间（Time.time），-1 表示未开始，初始化于 BuildVisuals
    private float hitPopDuration = 0.30f; // 命中放大持续（秒）[2026-09-18 ②调长，避免"变大切图"一闪而过]
    private float hitPopScale = 1.55f;    // 命中峰值放大倍数 [2026-09-18 ②调大]

    // ② 命中"变大切图"反馈优先级高于缩没：反馈（放大 + 停留）未播完前，缩没动画不启动，确保看得见
    private float nodeCompleteLinger = 0.12f; // 命中放大结束后额外保留（不缩没）时长
    private float skillClearFeedbackGuard = 0f; // ① 技能清屏时，命中反馈(变大+停留)播完前整体 alpha 钉 1 的延迟时长
    private float[] nodeFeedbackEnd;           // 每节点反馈结束时刻（Time.time）；此前缩没不启动（默认 0 = 已过期，正常缩没）

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
        // 链接带透明度改由 UpdateBandAll 的逐顶点 alpha（含漏击/收尾）控制，不在这里统一设置
    }

    private void ResetAllToBlack()
    {
        // 链接带不发黑：漏击/断连时由 UpdateBandAll 整体降到 α=0.75 并随判定线软边消失。
        // 节点已改贴图 quad：重置为 base 贴图 + 白 tint（不再用黑色涂黑），保证漏击时仍是完整音符图。
        for (int i = 0; i < nodeMats.Count; i++)
        {
            if (nodeMats[i] == null) continue;
            if (i < nodeBaseTex.Count && nodeBaseTex[i] != null) nodeMats[i].mainTexture = nodeBaseTex[i];
            nodeMats[i].color = Color.white;
        }
    }

    /// <summary>触发某节点命中弹跳：放大 + 变白，随后由 UpdateHitPops 回落到基础缩放。</summary>
    private void PlayHitPop(int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= nodeCount) return;
        nodePop[nodeIndex] = 1f; // 触发弹跳（从峰值开始回落）
        // ② 记录反馈结束时刻：放大(hitPopDuration) + 停留(nodeCompleteLinger) 期间，缩没动画不启动
        nodeFeedbackEnd[nodeIndex] = Time.time + hitPopDuration + nodeCompleteLinger;
        if (nodeMats[nodeIndex] != null)
        {
            nodeMats[nodeIndex].color = Color.white; // 贴图 tint 白（命中切 Select 只换贴图，不改 tint）
            if (nodeSelectTex[nodeIndex] != null) nodeMats[nodeIndex].mainTexture = nodeSelectTex[nodeIndex];
        }
    }

    /// <summary>每帧推进各节点命中弹跳：放大倍数从峰值随时间回落到 1。漏击/断连时由 UpdateBandVisibility 接管连接带缩放/透明度，此处跳过节点缩放。</summary>
    private void UpdateHitPops()
    {
        if (missMode) return; // 漏击/断连时连接带缩放/透明度由 UpdateBandVisibility 接管
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

        bool revealed = IsBeyondLine(NodeRawPos(i).x, cx);  // ③ 与带子统一用外推坐标，避免粉杠遮罩与节点错位
        float nodeJudgeX = hitPositions[i].x;
        float glowLineX = nodeJudgeX + (litGlow ? glowBufferDist : 0f);
        bool stillBeforeJudge = !IsFullyBeyondLine(nodeTransforms[i].position.x, noteRadius, glowLineX);

        // 清屏(skillCleared)：整条长按原地变白放大消失，跳过"未显现隐藏"（解决断弦高压残留未显现音节）
        if (!revealed && !skillCleared)
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

        // ② 命中"变大切图"反馈优先级高于缩没：反馈（放大 + nodeCompleteLinger 停留）未播完前，
        // 缩没动画不启动，确保"变大切图"反馈清晰可见（否则节点越过判定线即被缩没吞掉）。
        if (!missMode && Time.time < nodeFeedbackEnd[i])
        {
            nodeRends[i].enabled = true;
            return;
        }

        // 清屏(skillCleared)：整条原地放大淡出，不进入缩没动画（由对象级 SetAlpha 整体淡出），避免"变小消失"
        if (skillCleared)
        {
            nodeRends[i].enabled = true;
            return;
        }

        // 已完全越过各自判定线：启动/推进「按时间」缩没动画（替代硬切隐藏）
        // ③ 缩没起点对齐反馈结束时刻：若已在反馈结束前写入过 nodeShrinkStart（陈旧起点），
        // 重设到 nodeFeedbackEnd 起算，确保"变大切图"完整播完后再缩没（修复"命中直接变小消失"）。
        if (nodeShrinkStart[i] < 0f || nodeShrinkStart[i] < nodeFeedbackEnd[i])
            nodeShrinkStart[i] = Mathf.Max(nodeFeedbackEnd[i], Time.time);
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
        // ④ Miss：节点缩没期间恒定半透（missNodeAlpha=0.6），让链接音符漏击"变透"而非硬切消失
        if (nodeMats[i] != null)
        {
            Color c = nodeMats[i].color;
            c.a = missMode ? missNodeAlpha : 1f;
            nodeMats[i].color = c;
        }
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
        nodeFeedbackEnd = new float[nodeCount];
        for (int i = 0; i < nodeCount; i++) nodeShrinkStart[i] = -1f; // -1 表示尚未开始缩没

        // 节点（平躺贴图 quad，复用普通/跨轨音符贴图）：普通节点 = Note_Tap/Select，跨轨节点 = Note_Wide/Select
        float r = noteRadius / 0.5f;
        var lib = NoteSpriteLibrary.Instance;

        for (int i = 0; i < nodeCount; i++)
        {
            int span = NodeSpan(i);
            float linkedWidth = laneSpacing * (span - 1) + noteRadius * 2f;
            Sprite baseSp = (span > 1) ? (lib != null ? lib.wide : null) : (lib != null ? lib.tap : null);
            Sprite selSp  = (span > 1) ? (lib != null ? lib.wideSelect : null) : (lib != null ? lib.tapSelect : null);
            Texture2D baseTex = baseSp != null ? baseSp.texture : null;
            Texture2D selTex  = selSp != null ? selSp.texture : null;
            float ppu = baseSp != null ? baseSp.pixelsPerUnit : 100f;
            // 贴图 1:1 尺寸（世界单位）：宽 = 贴图像素宽 / PPU，沿带方向；高 = 贴图像素高 / PPU，带宽方向
            float w = baseTex != null ? baseTex.width / ppu : (span > 1 ? linkedWidth : r);
            float h = baseTex != null ? baseTex.height / ppu : (span > 1 ? noteRadius * 2f : r);

            var go = new GameObject($"HoldNode_{i}");
            var t = go.transform;
            t.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = MakeQuadMesh(w, h); // 平躺 quad（XZ 平面），与带子同朝向
            var rend = go.AddComponent<MeshRenderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            var mat = CreateNodeTexMaterial(baseTex);
            rend.material = mat;
            t.localScale = Vector3.one; // mesh 已按 1:1 原生尺寸建好，不再额外缩放
            nodeBaseScales[i] = Vector3.one;

            nodeTransforms.Add(t);
            nodeRends.Add(rend);
            nodeMats.Add(mat);
            nodeBaseTex.Add(baseTex);
            nodeSelectTex.Add(selTex);
        }

        // 连接带（ribbon）：节点 i 与 i+1 之间一段连续 mesh（base）+ 一张 select 覆盖带（overlay）。
        // UV 沿带长按纵横比平铺（G5），越判定线 smoothstep 软边 + 逐顶点渐显（G1），越线带宽缩小（G2）。
        slideLinkSprite = (NoteSpriteLibrary.Instance != null) ? NoteSpriteLibrary.Instance.slideLink : null;
        slideLinkSelectSprite = (NoteSpriteLibrary.Instance != null) ? NoteSpriteLibrary.Instance.slideLinkSelect : null;
        slideJudgmentSprite = (NoteSpriteLibrary.Instance != null) ? NoteSpriteLibrary.Instance.slideJudgment : null;
        slideLinkTex = (slideLinkSprite != null) ? slideLinkSprite.texture : null;
        slideLinkSelectTex = (slideLinkSelectSprite != null) ? slideLinkSelectSprite.texture : null;
        slideJudgmentTex = (slideJudgmentSprite != null) ? slideJudgmentSprite.texture : null;
        if (slideLinkTex != null) slideLinkTex.wrapMode = TextureWrapMode.Repeat;       // G5 平铺
        if (slideLinkSelectTex != null) slideLinkSelectTex.wrapMode = TextureWrapMode.Repeat;

        for (int s = 0; s < nodeCount - 1; s++)
        {
            // base 带
            var go = new GameObject($"SlideBand_{s}");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mesh = new Mesh();
            mf.mesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            var mat = CreateBandMaterial(slideLinkTex, 2998);
            rend.material = mat;
            rend.enabled = false;
            bandGOs.Add(go); bandMeshes.Add(mesh); bandMats.Add(mat); bandRends.Add(rend);

            // select 覆盖带（跟随进度线渐覆；renderQueue 高于 base，低于节点 3000）
            var ogo = new GameObject($"SlideBandSelect_{s}");
            ogo.transform.SetParent(transform, false);
            var omf = ogo.AddComponent<MeshFilter>();
            var omesh = new Mesh();
            omf.mesh = omesh;
            var orend = ogo.AddComponent<MeshRenderer>();
            orend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            orend.receiveShadows = false;
            var omat = CreateBandMaterial(slideLinkSelectTex, 2999);
            orend.material = omat;
            orend.enabled = false;
            bandOverlayMeshes.Add(omesh); bandOverlayMats.Add(omat); bandOverlayRends.Add(orend);
        }

        // 进度线 marker（slideJudgment 贴图，按原始尺寸 1:1），仅按住期间显示，表达"已完成/未完成"分界
        if (slideJudgmentTex != null)
        {
            var pm = new GameObject("SlideProgressMarker");
            pm.transform.SetParent(transform, false);
            var pmf = pm.AddComponent<MeshFilter>();
            pmf.mesh = MakeQuadMesh(1f, 1f);
            var prend = pm.AddComponent<MeshRenderer>();
            prend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            prend.receiveShadows = false;
            progressMat = new Material(Shader.Find("Sprites/Default"));
            progressMat.mainTexture = slideJudgmentTex;
            progressMat.color = Color.white;
            progressMat.SetInt("_Cull", 0);
            // 渲染队列 3100 > 提示灯(3050) > 节点(3000) > 带子(2998/2999)：进度线永远最后画、压在判定线/提示灯之上。
            // 抬 y 只解决与地面的深度关系；与判定线/提示灯的遮挡关系必须靠队列（透明队列内按 queue+距离排序，3050 会盖住默认 3000 的本材质）。
            progressMat.renderQueue = 3100;
            prend.material = progressMat;
            progressMarker = pm.transform;
            // 尺寸在 UpdateProgressMarker 每帧按贴图原始尺寸设置（1:1），这里先给 1
            pm.transform.localScale = new Vector3(1f, 1f, 1f);
            pm.SetActive(false);
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

        // 音符移动速度（供进度线速度模型），与 NoteMover 推算一致
        if (spawnPositions.Length > 0 && exitPositions.Length > 0 && exitLeadTimes.Length > 0 && exitLeadTimes[0] > 1e-4f)
            normalSpeed = (exitPositions[0] - spawnPositions[0]).magnitude / exitLeadTimes[0];

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

        UpdateHitPops(); // 推进各节点命中放大弹跳（missMode 时内部跳过，连接带缩放/透明度由 UpdateBandVisibility 接管）

        if (fadeTimer < 0f)
        {
            // 正常移动：每个节点按各自时刻插值
            for (int i = 0; i < nodeCount; i++)
            {
                float t = Mathf.Clamp01((songTime - (times[i] - leadTime)) / exitLeadTimes[i]);
                Vector3 p = Vector3.Lerp(spawnPositions[i], exitPositions[i], t);
                nodeTransforms[i].position = new Vector3(p.x, rideY, p.z);
            }

            // 可见性：按元素"前沿"是否越过粉杠决定显示
            float cx = centerLine != null ? centerLine.currentX : 0f;
            // 修复（2026-08-26 Issue 4）：节点可见性 + 缩没动画统一交给 UpdateNodeVisual，
            // 逾越各自判定线后「按时间」缩小消失（holdNodeShrinkDuration），而非瞬间硬切隐藏。
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeRends[i] != null)
                    UpdateNodeVisual(i, cx, hasLit || nodePop[i] > 0f);
            }
            UpdateBandAll(cx, judgeLineX);   // 带子几何 + 完成覆盖 + 逐顶点渐显/软边
            UpdateProgressMarker();

            // 漏击消失：每个节点各自越过判定线后缩小并淡出；链接带透明度/软边由 UpdateBandAll 处理（α=0.75 + 越线消失）
            if (missMode)
            {
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
            // 完成态保留完成图：手动完成(!skillCleared)与清屏(skillCleared)的节点都保留 Select 白图 + 放大，
            // 随收尾 SetAlpha 淡出（修复"clear 不切完成图"）。
            // 仅漏击/断连(missMode)在收尾首帧重置为 base 图（保证漏击时是完整音符图而非残留完成白图）。
            if (fadeTimer <= 0f && !skillCleared && missMode)
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

            float judgeX = judgeLineX;
            // 修复（2026-08-26 Issue 4）：收尾阶段节点可见性 + 缩没动画同样交给 UpdateNodeVisual，
            // 逾越各自判定线后「按时间」缩小消失，避免刚越过判定线就被销毁而看不到缩没。
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeRends[i] != null)
                    UpdateNodeVisual(i, centerLine != null ? centerLine.currentX : 0f, hasLit || nodePop[i] > 0f);
            }
            UpdateBandAll(centerLine != null ? centerLine.currentX : 0f, judgeX);
            UpdateProgressMarker();

            // 完成：整体透明度淡出；断连/漏击：保持不透明，只通过判定线裁剪消失
            if (!broken)
            {
                // ① 技能清屏：延迟淡出保命中反馈（fadeTimer 在反馈窗口内视为 0，alpha 钉 1）
                float fd = skillCleared ? skillClearFeedbackGuard : 0f;
                float alpha = 1f - Mathf.Clamp01((fadeTimer - fd) / Mathf.Max(fadeDuration, 1e-4f));
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
            if (allGone || fadeTimer > maxFadeLife + (skillCleared ? skillClearFeedbackGuard : 0f))  // ① 技能清屏延长存活，等反馈播完再淡出销毁
            {
                finished = true;
                Destroy(gameObject);
            }
        }
    }

    // ============================================================
    // 链接带（ribbon）实现：单连续 mesh + 沿带长 UV 平铺 + 越线 smoothstep 软边
    // 替代原 16 圆柱细分，彻底消除分段 on/off 锯齿。
    // ============================================================

    private float BandWidth(int nodeIndex)
    {
        int span = NodeSpan(nodeIndex);
        return laneSpacing * (span - 1) + noteRadius * 2f;
    }

    private Material CreateBandMaterial(Texture2D tex, int renderQueue)
    {
        Material m = new Material(Shader.Find("Sprites/Default"));
        m.mainTexture = tex;
        m.color = Color.white;
        m.SetInt("_Cull", 0); // 双面，避免视角下背面被剔除
        m.renderQueue = renderQueue;  // base=2998 / overlay=2999，均低于节点（默认 3000）
        return m;
    }

    /// <summary>节点贴图材质：Sprites/Default 自带 alpha 混合，renderQueue 高于带子（3000），节点盖住带子接头。</summary>
    private Material CreateNodeTexMaterial(Texture2D tex)
    {
        Material m = new Material(Shader.Find("Sprites/Default"));
        m.mainTexture = tex;
        m.color = Color.white;
        m.SetInt("_Cull", 0);
        m.renderQueue = 3000;
        return m;
    }

    private static Mesh MakeQuadMesh(float w, float h)
    {
        var m = new Mesh();
        float hw = w * 0.5f, hh = h * 0.5f;
        m.vertices = new Vector3[] { new Vector3(-hw, 0, hh), new Vector3(hw, 0, hh), new Vector3(hw, 0, -hh), new Vector3(-hw, 0, -hh) };
        m.uv = new Vector2[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
        m.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        m.colors = new Color[] { Color.white, Color.white, Color.white, Color.white };
        return m;
    }

    private static float Smoothstep(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    private Vector3 NodeHalfVec(int i)
    {
        int span = NodeSpan(i);
        float hx = noteRadius;                                   // X 半宽（普通/跨轨节点 X 方向都窄）
        float hz = (span > 1) ? (laneSpacing * 0.5f + noteRadius) : noteRadius; // Z 半宽：跨轨胶囊沿 Z 长
        return new Vector3(hx, 0f, hz);
    }

    /// <summary>重建一段 ribbon 的子区间 [u0,u1]（u 为整段参数 0..1）：位置含端点内缩(G4)+越线带宽缩小(G2)，
    /// UV 按纵横比平铺(G5)，顶点色含链长渐变(G5)。yDip 控制相对 rideY 下沉（base/overlay 不同，防 z-fight）。</summary>
    private void RebuildRibbon(int s, float u0, float u1, Mesh mesh, float judgeX, float cumStart, float totalLen, float yDip, float edgeX0 = float.NaN, float edgeX1 = float.NaN)
    {
        int M = Mathf.Max(2, bandSubdiv);
        // 端点用"时间外推"位置（节点到终点后被 Clamp01 钉死，用 nodeTransforms 会让跨轨斜向段被压缩畸变）；
        // 外推后两端以 normalSpeed 持续流动，段方向恒定→保持原形状。
        Vector3 a = NodeRawPos(s);
        Vector3 b = NodeRawPos(s + 1);
        Vector3 dir = b - a;
        float segLen = dir.magnitude;
        dir = segLen > 0.0001f ? dir / segLen : Vector3.right;
        // ② 带宽边缘垂直于带方向偏移所需的法向量（详见下方 cL/cR）
        Vector3 perp = new Vector3(dir.z, 0f, -dir.x);
        if (perp.sqrMagnitude > 1e-6f) perp.Normalize(); else perp = Vector3.forward;
        // G4：带端内缩按所接节点贴图实际半宽沿带方向投影
        float halfA = (Mathf.Abs(dir.x) * NodeHalfVec(s).x + Mathf.Abs(dir.z) * NodeHalfVec(s).z) * bandEndInsetRatio;
        float halfB = (Mathf.Abs(dir.x) * NodeHalfVec(s + 1).x + Mathf.Abs(dir.z) * NodeHalfVec(s + 1).z) * bandEndInsetRatio;
        // ③ 双侧固定侧边锚点：跨轨段带端落在节点朝目标侧的 Z 侧边边缘（从侧边出线、不穿过中心顶部）；
        // 同轨(dz≈0)沿用中心锚(sideA=0)。带端再朝目标方向埋入 nodeBandOverlap，使端边落在节点贴图下方被盖住(3000>2998)，防旋转露缝。
        float dz = b.z - a.z;
        int sideA = (Mathf.Abs(dz) > 1e-4f) ? (dz > 0f ? 1 : -1) : 0;
        float hzA = NodeHalfVec(s).z, hzB = NodeHalfVec(s + 1).z;
        // 埋入方向沿 Z 轴朝音符中心（修正：原 -dir*overlap 沿带方向把带端推出音符外侧导致露头）。
        // 锚点落在朝目标侧 ±Z 侧边内侧 hz-overlap 处，端边藏在音符贴图下方(2998<3000)防露缝。
        Vector3 aEdge = a + new Vector3(0f, 0f, sideA * (hzA - nodeBandOverlap)) + dir * halfA;
        Vector3 bEdge = b + new Vector3(0f, 0f, -sideA * (hzB - nodeBandOverlap)) - dir * halfB;
        float wA = BandWidth(s) * bandWidthScale, wB = BandWidth(s + 1) * bandWidthScale;
        bool flip = b.x >= a.x;
        float tileWorld = Mathf.Max(0.0001f, (wA + wB) * 0.5f); // G5：每"一格带宽"平铺一次，保持纵横比不变形
        int vCount = (M + 1) * 2;
        Vector3[] verts = new Vector3[vCount];
        Vector2[] uvs = new Vector2[vCount];
        Color[] cols = new Color[vCount];
        for (int i = 0; i <= M; i++)
        {
            float u = u0 + (u1 - u0) * (i / (float)M);
            Vector3 p = Vector3.Lerp(aEdge, bEdge, u);
            float w = Mathf.Lerp(wA, wB, u);
            // G2：越判定线后带宽缩小（下限 bandShrinkMin）
            float fade = side == 0 ? Smoothstep(judgeX - bandFadeWidth, judgeX, p.x) : Smoothstep(judgeX + bandFadeWidth, judgeX, p.x);
            float widthScale = bandShrinkMin + (1f - bandShrinkMin) * fade;
            float ww = w * widthScale;
            Vector3 cL = p + perp * (ww * 0.5f);
            Vector3 cR = p - perp * (ww * 0.5f);
            verts[2 * i] = new Vector3((flip ? cR.x : cL.x), rideY - yDip, (flip ? cR.z : cL.z));
            verts[2 * i + 1] = new Vector3((flip ? cL.x : cR.x), rideY - yDip, (flip ? cL.z : cR.z));
            // G5：uv.x 沿带长按世界长度平铺（与带宽成比例），uv.y 跨宽 0..1
            float worldLenFromStart = u * segLen;
            uvs[2 * i] = new Vector2(worldLenFromStart / tileWorld, 1f);
            uvs[2 * i + 1] = new Vector2(worldLenFromStart / tileWorld, 0f);
            // G5：链长渐变（subtle），保留在 RGB，供 ApplyBandAlpha 只改 alpha
            float bf = 1f;
            if (bandGradient && totalLen > 0.0001f)
            {
                float grad = Mathf.Clamp01((cumStart + u * segLen) / totalLen);
                bf = 1f - bandGradientStrength * (1f - grad);
            }
            cols[2 * i] = new Color(bf, bf, bf, 1f);
            cols[2 * i + 1] = new Color(bf, bf, bf, 1f);
        }
        int[] tris = new int[M * 6];
        for (int i = 0; i < M; i++)
        {
            int o = i * 6;
            int i0 = 2 * i, i1 = 2 * i + 1, i2 = 2 * i + 2, i3 = 2 * i + 3;
            tris[o] = i0; tris[o + 1] = i1; tris[o + 2] = i2;
            tris[o + 3] = i2; tris[o + 4] = i1; tris[o + 5] = i3;
        }
        mesh.Clear();
        // ② 带子顶点在世界坐标构建，但 band GO 挂在根 transform 下、mesh 顶点是局部坐标，
        // 渲染时会被根再平移一次 => 带子相对节点/粉杠"恒偏"。此处统一转成根局部坐标，消除双重偏移。
        // 根在原点(identity)时为恒等变换，零副作用；根有平移时彻底对齐节点与粉杠。
        for (int k = 0; k < verts.Length; k++) verts[k] = transform.InverseTransformPoint(verts[k]);
        // ① 端点竖切（替代脆弱的 coverWorldX 容差补丁）：u=0 端强制 x=edgeX0、u=1 端强制 x=edgeX1（非 NaN 时），
        // 保证覆盖带（黄液）前沿与远端边界永远垂直音轨线、与进度线同 X；端点 x 与链方向无关，斜向/跨轨链接均不再出现斜边。
        if (!float.IsNaN(edgeX0)) { float lx0 = transform.InverseTransformPoint(new Vector3(edgeX0, 0f, 0f)).x; verts[0].x = lx0; verts[1].x = lx0; }
        if (!float.IsNaN(edgeX1)) { float lx1 = transform.InverseTransformPoint(new Vector3(edgeX1, 0f, 0f)).x; verts[2 * M].x = lx1; verts[2 * M + 1].x = lx1; }
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.colors = cols;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
    }

    private void SetBandTexture(Material mat, Texture2D tex)
    {
        if (mat != null && mat.mainTexture != (Texture)tex) mat.mainTexture = tex;
    }

    /// <summary>逐顶点 alpha：过粉杠 reveal（G1）+ 越判定线 fade（G2）+ 整体 g（漏击/收尾）。保留 RGB 渐变，只写 alpha。</summary>
    private void ApplyBandAlpha(Mesh mesh, float revealX, float judgeX, float g)
    {
        if (mesh == null || mesh.vertexCount < 4) return;
        Vector3[] vs = mesh.vertices;
        Color[] cols = mesh.colors;
        if (cols == null || cols.Length != vs.Length) cols = new Color[vs.Length];
        // ③ 顶点已转根局部坐标(改动②)，revealX/judgeX 为世界值，须同步转局部才能与 vp.x 同坐标系比较，
        // 否则粉杠移动时遮罩边界与节点/粉杠"恒偏"。根 identity 时两处恒等、零副作用。
        float localRevealX = transform.InverseTransformPoint(new Vector3(revealX, 0f, 0f)).x;
        float localJudgeX = transform.InverseTransformPoint(new Vector3(judgeX, 0f, 0f)).x;
        int M = vs.Length / 2 - 1;
        // fen gang reveal gradient to crossed side
        float odSign = (spawnPositions.Length > 0 && spawnPositions[0].x > judgeX) ? 1f : -1f;
        for (int i = 0; i <= M; i++)
        {
            Vector3 vp = vs[2 * i];
            float rev = Smoothstep(localRevealX, localRevealX - odSign * bandRevealWidth, vp.x);
            float fade = side == 0 ? Smoothstep(localJudgeX - bandFadeWidth, localJudgeX, vp.x) : Smoothstep(localJudgeX + bandFadeWidth, localJudgeX, vp.x);
            float alpha = rev * fade * g;
            cols[2 * i].a = alpha;
            cols[2 * i + 1].a = alpha;
        }
        mesh.colors = cols;
    }

    /// <summary>按时间线性插值节点位置，不钳制 t：节点越过终点后继续沿原方向(=normalSpeed)外推。
    /// 供链接带几何使用——节点 transform 在 MoveAndFade 里被 Clamp01 钉死后，跨轨斜向链接段会被压缩畸变；
    /// 外推后两端仍以 normalSpeed 持续流动，段方向恒定→保持原形状，越判定线部分由软边淡出。节点视觉仍用 nodeTransforms(钉死缩没)。</summary>
    private Vector3 NodeRawPos(int i)
    {
        float songTime = conductor != null ? conductor.songPosition : 0f;
        float t = (songTime - (times[i] - leadTime)) / Mathf.Max(exitLeadTimes[i], 1e-4f);
        t = Mathf.Max(0f, t);  // ③ 只钳生成侧：避免尾端未生成节点(t<0)时带子外推伸出节点之外，导致粉杠遮罩与节点错位；保留 t>1 头端外推防畸变
        return Vector3.Lerp(spawnPositions[i], exitPositions[i], t);
    }

    /// <summary>每帧统一驱动带子：几何(RebuildRibbon) + 完成覆盖(select overlay 跟随进度线) + 逐顶点 alpha。</summary>
    private void UpdateBandAll(float revealX, float judgeX)
    {
        float g = 1f;
        if (missMode) g = 0.6f;   // ④ Miss 时连接带整体降到 0.6 半透（原为 0.75，更明显"变透"）
        else if (fadeTimer >= 0f && !broken) { float fd = skillCleared ? skillClearFeedbackGuard : 0f; g = 1f - Mathf.Clamp01((fadeTimer - fd) / Mathf.Max(fadeDuration, 1e-4f)); }  // ① 技能清屏延迟淡出保命中反馈

        // 带子端点位置：用"时间外推"而非节点 transform（节点到终点被 Clamp01 钉死会导致跨轨斜向链接段被压缩畸变）。
        // 外推后两端都以 normalSpeed 持续流动，段方向恒定→保持原形状，越判定线部分由软边淡出。
        Vector3[] rawPos = new Vector3[nodeCount];
        for (int i = 0; i < nodeCount; i++) rawPos[i] = NodeRawPos(i);

        // 链长累计（渐变用）
        float[] cum = new float[bandMeshes.Count + 1];
        float total = 0f;
        for (int s = 0; s < bandMeshes.Count; s++)
        {
            cum[s] = total;
            total += Vector3.Distance(rawPos[s], rawPos[s + 1]);
        }

        // 液面区间模型 [coverLo, coverHi]（od = 朝 spawn 侧距判定线距离，od>0=spawn 侧未完成部分，od<0=已越过判定线的完成侧）：
        // 按住(Holding)：[-∞, markerDist] —— 黄液填"已完成侧"（判定线已越过的一侧，玩家视角左侧；镜像侧则右侧），
        //   前沿横杠钉在 markerDist(按住=0=判定线，始终垂直音轨线)；od>0 的 spawn 侧剩余链带保持 base 不充能；
        // 松手衰退：markerDist<0 → 黄液右边界随衰退退入判定线内（断连预警可见，Break 逻辑不变）；
        // 完成态(Done 未断连，含技能清屏)：[-∞,+∞] 整条覆盖（Select 完成图）；
        // 其他（未命中/Waiting/Miss）：空区间 → 无任何黄色（修复"没点也充能"）。
        bool holdingNow = (state == HoldState.Holding);
        bool completed = (state == HoldState.Done && !broken);
        float odSign = (spawnPositions.Length > 0 && spawnPositions[0].x > judgeLineX) ? 1f : -1f;
        float coverLo, coverHi;
        if (holdingNow)
        {
            coverLo = float.NegativeInfinity;   // 完成侧一直填到链最前端（越线淡出由 ApplyBandAlpha fade 负责）
            coverHi = markerDist;               // 前沿横杠：按住=0(判定线)，松手衰退为负(退入线内)
        }
        else if (completed) { coverLo = float.NegativeInfinity; coverHi = float.PositiveInfinity; }
        else { coverLo = 1f; coverHi = -1f; }  // 空区间 → 永不 hasCov

        for (int s = 0; s < bandMeshes.Count; s++)
        {
            Vector3 a = rawPos[s], b = rawPos[s + 1];
            // 完成态：整条（含未显现段）都 revealed，保证清屏/手动完成时连接带在判定线前也整条显示完成图（无残留暗段）
            bool revealed = completed || IsBeyondLine(a.x, revealX) || IsBeyondLine(b.x, revealX);
            bandRends[s].enabled = revealed;

            // base：整段 base 贴图
            SetBandTexture(bandMats[s], slideLinkTex);
            RebuildRibbon(s, 0f, 1f, bandMeshes[s], judgeX, cum[s], total, bandRideYDip);

            // overlay：覆盖段 = 该段 od 范围与 [coverLo, coverHi] 区间交；端点用 edgeX 钉竖（替代旧 coverWorldX 容差补丁）
            float od0 = odSign * (a.x - judgeLineX);
            float od1 = odSign * (b.x - judgeLineX);
            float odLo = Mathf.Min(od0, od1), odHi = Mathf.Max(od0, od1);
            float dA = Mathf.Max(odLo, coverLo);
            float dB = Mathf.Min(odHi, coverHi);
            bool isVert = Mathf.Abs(od1 - od0) < 1e-6f;  // 跨轨垂直段：od 几乎不变
            bool hasCov = isVert ? (od0 >= coverLo - 1e-4f && od0 <= coverHi + 1e-4f) : (dB - dA > 1e-4f);
            if (hasCov)
            {
                float u0c, u1c;
                if (isVert) { u0c = 0f; u1c = 1f; }   // 竖段整段覆盖（两端钉同 worldX → 也是竖边），修复"跨轨段无黄液"
                else { u0c = (dA - od0) / (od1 - od0); u1c = (dB - od0) / (od1 - od0); }
                float uMin = Mathf.Min(u0c, u1c), uMax = Mathf.Max(u0c, u1c);
                // 覆盖区两端边界 od 值（uMin/uMax 端），换算 worldX 钉竖：前沿(od=markerDist)竖贴判定线、远端(od=dA)竖在深处
                float odAtUMin = (od1 > od0) ? dA : dB;
                float odAtUMax = (od1 > od0) ? dB : dA;
                float edgeX0 = judgeLineX + odSign * odAtUMin;
                float edgeX1 = judgeLineX + odSign * odAtUMax;
                RebuildRibbon(s, uMin, uMax, bandOverlayMeshes[s], judgeX, cum[s], total, bandRideYDip + 0.005f, edgeX0, edgeX1);
            }
            bandOverlayRends[s].enabled = revealed && hasCov;
            ApplyBandAlpha(bandMeshes[s], revealX, judgeX, g);
            if (hasCov) ApplyBandAlpha(bandOverlayMeshes[s], revealX, judgeX, g);
        }
    }

    /// <summary>进度线 marker：按 slideJudgment 原始尺寸 1:1。Holding 时位置由 markerDist 推出（沿链体朝 spawn 侧距判定线 markerDist 的世界点），
    /// 不再随飞行节点越跑越远；仅 Holding 且未完成时显示，完成(State.Done)自动隐藏。</summary>
    private void UpdateProgressMarker()
    {
        if (progressMarker == null) return;
        bool show = (state == HoldState.Holding && !finished);
        progressMarker.gameObject.SetActive(show);
        if (!show) return;

        // 尺寸：slideJudgment 原始贴图 1:1（不重算纵横比，用户已定好），每帧强制 localScale 防父级缩放干扰
        float ww = (slideJudgmentSprite != null) ? slideJudgmentSprite.rect.width / slideJudgmentSprite.pixelsPerUnit : 1f;
        float hh = (slideJudgmentSprite != null) ? slideJudgmentSprite.rect.height / slideJudgmentSprite.pixelsPerUnit : 1f;
        progressMarker.localScale = new Vector3(ww * progressMarkerScale, 1f, hh * progressMarkerScale);
        // ① 进度线强制垂直音轨线（长边沿 Z）：slideJudgment 为竖窄条(9x69)，长边本就在 quad 的 Z 边，yaw=0 即正确；
        //   90 会把竖条放倒成沿音轨(沿 X)的横条。角度 Inspector 可调(progressMarkerYaw)
        progressMarker.rotation = Quaternion.Euler(0f, progressMarkerYaw, 0f);

        // 进度线位置由 markerDist 推出：沿"时间外推链"(NodeRawPos) 距判定线 markerDist 的世界点。
        // 与带子液面共用同一坐标源(NodeRawPos)，避免斜向链上进度线钉节点、液面外推导致的 z 分离空档。
        Vector3 mp = ChainPointAtRawDist(markerDist);
        // y 抬升：仅做视觉微调(0.25=约半音符高)；与判定线/提示灯的层级关系由材质渲染队列 3100 保证（永远最后画、不被遮挡）。Inspector 可调(progressMarkerLift)
        progressMarker.position = new Vector3(mp.x, rideY + progressMarkerLift, mp.z);
    }

    /// <summary>返回链体上"朝 spawn 侧距判定线 d"的世界点（进度线 marker 落点）；d 超出链范围时取最外侧(首)节点。</summary>
    private Vector3 ChainPointAtOutsideDist(float d)
    {
        if (nodeTransforms.Count == 0) return transform.position;
        float odSign = (spawnPositions.Length > 0 && spawnPositions[0].x > judgeLineX) ? 1f : -1f;
        for (int s = 0; s < nodeTransforms.Count - 1; s++)
        {
            float od0 = odSign * (nodeTransforms[s].position.x - judgeLineX);
            float od1 = odSign * (nodeTransforms[s + 1].position.x - judgeLineX);
            float lo = Mathf.Min(od0, od1), hi = Mathf.Max(od0, od1);
            if (d >= lo && d <= hi && Mathf.Abs(od1 - od0) > 1e-5f)
            {
                float u = (d - od0) / (od1 - od0);
                return Vector3.Lerp(nodeTransforms[s].position, nodeTransforms[s + 1].position, u);
            }
        }
        return nodeTransforms[0].position; // 超出链范围：取最外侧(首)节点
    }

    /// <summary>返回"时间外推链"(NodeRawPos)上"朝 spawn 侧距判定线 d"的世界点（进度线 marker 落点）。
    /// 与带子液面(UpdateBandAll 的 cover=markerDist)共用同一坐标源——两者都按 NodeRawPos 在 od=markerDist 处取交点，
    /// 因此进度线永远贴在黄色液面前沿(同 X 判定线平面、同 z 链上)。旧 ChainPointAtOutsideDist(走钉死的 nodeTransforms)保留可回退。
    /// d 超出链范围时取最外侧(首)节点。</summary>
    private Vector3 ChainPointAtRawDist(float d)
    {
        if (nodeCount <= 0) return transform.position;
        float odSign = (spawnPositions.Length > 0 && spawnPositions[0].x > judgeLineX) ? 1f : -1f;
        for (int s = 0; s < nodeCount - 1; s++)
        {
            float od0 = odSign * (NodeRawPos(s).x - judgeLineX);
            float od1 = odSign * (NodeRawPos(s + 1).x - judgeLineX);
            float lo = Mathf.Min(od0, od1), hi = Mathf.Max(od0, od1);
            if (d >= lo && d <= hi && Mathf.Abs(od1 - od0) > 1e-5f)
            {
                float u = (d - od0) / (od1 - od0);
                return Vector3.Lerp(NodeRawPos(s), NodeRawPos(s + 1), u);
            }
        }
        return NodeRawPos(0); // 超出链范围：取最外侧(首)节点
    }

    /// <summary>把一个被附魔节点染成黄色发光（连接带附魔着色后置，本轮先只染节点）。</summary>
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
        // 连接带附魔着色本轮后置（连接带改用贴图，不再逐段染材质色）
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
        for (int s = 0; s < bandRends.Count; s++)
        {
            if (bandRends[s] != null) bandRends[s].enabled = enabled;
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
        return IsBeyondLine(NodeRawPos(index).x, cx);  // ③ 与带子统一坐标源
    }

    /// <summary>清屏技能：把整条长按视为命中清除。
    /// 带区内已显现的节点逐节点变白弹跳 + 调用 caster.OnSkillClearedNode（按节点音轨计分/连击/充能 + 弹 PERFECT）；
    /// 随后进入完成淡出：连接线在判定线处逐段变大变白消失（复用既有"完成"表现）。
    /// 由 NoteSpawner.SkillClearBand 逐条 HoldNote 调用。</summary>
    public void SkillClearWhole(float xMin, float xMax, ActiveSkillRuntime caster)
    {
        if (caster == null || nodeTransforms == null) return;
        if (state == HoldState.Done && !missMode) return;   // ① 正常完成收尾中不再重复计分；漏击/断连(missMode)链接也应被技能整条清除（不留尾单体）

        // 断弦高压/清屏：整条长按只要存在任一节点的判定线位置落在带区内即清除（不再要求"已显现"）。
        // 链接音符"整条都要命中"——早链接（节点尚未过粉杠）也应被整条清除，避免残留未显现音节。
        bool anyInBand = false;
        for (int i = 0; i < NodeCount; i++)
        {
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
            if (!missMode) { caster.OnSkillClearedNode(this, 0, "PERFECT"); ResolveCharmedNode(0, true); }  // ① 漏击态已漏击不补分
        }
        for (int i = completedSegments + 1; i < NodeCount; i++)
        {
            bool vis = IsNodeRevealed(i) && (hitPositions == null || (hitPositions[i].x >= xMin && hitPositions[i].x <= xMax));
            if (vis) PlayHitPop(i);
            if (!missMode) { caster.OnSkillClearedNode(this, i, "CLEAR"); ResolveCharmedNode(i, true); }  // ① 漏击态已漏击不补分
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
    /// <summary>
    /// ① 按压位置判定精准到"链接线相交音轨"：以当前进度所在的链段（节点 seg → seg+1）为准，
    /// 该段链接线在 Z 方向实际经过的轨道集合即合法轨道；玩家按住集合中任一轨即视为跟随成功。
    /// 两轨相交（跨轨节点 / 斜向链接）两者皆可。普通单轨链接线只经过自身 1 轨，故只接受该轨
    /// （不再用 laneTolerance 放宽到相邻轨，修正"判定不够准"）。
    /// 注：本工程 lane 沿 Z 轴分布（laneOffsets[i].z），故"相交轨"由 fromLane/toLane 宽度的并集 +
    /// 路径扫描决定，与 X（判定线）无关。
    /// </summary>
    private bool AreRequiredLanesHeld(float songTime, float reqLaneF)
    {
        if (spawner == null) return false;

        // 以当前进度链段（按时间推进，与 completedSegments 同一基准）为准
        int seg = CurrentSegmentIndex(songTime);
        int fromLane = lanes[seg];
        int toLane = lanes[seg + 1];
        int fromSpan = NodeSpan(seg);     // 节点 seg 宽度：1=单轨，2=连轨覆盖相邻两轨
        int toSpan = NodeSpan(seg + 1);   // 节点 seg+1 宽度

        // 合法轨道集合 = 起点节点覆盖轨道 ∪ 终点节点覆盖轨道 ∪ 路径扫过的整数轨道
        // （即"链接线与哪条音轨相交"：跨轨节点/斜向链接会同时覆盖两轨，两者皆可）
        System.Collections.Generic.HashSet<int> validSet = new System.Collections.Generic.HashSet<int>();
        for (int x = fromLane; x < fromLane + fromSpan; x++) validSet.Add(x);
        for (int x = toLane; x < toLane + toSpan; x++) validSet.Add(x);
        int lo = Mathf.Min(fromLane, toLane);
        int hi = Mathf.Max(fromLane, toLane);
        for (int x = lo; x <= hi; x++) validSet.Add(x);

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

            // 进度线 markerDist 速度模型 v2（世界距判定线距离，+为线外/spawn 侧）：稳定位=判定线(0)。
            // 正确按住→1× 恢复并钉在判定线；松手→vDecay 固定衰退（负向，进度线退入判定线内）；衰退到线内 markerMissDist(0.5)→Break Miss。保底不因命中重置。
            bool held = isAI ? true : AreRequiredLanesHeld(songTime, reqLaneF);
            bool inGrace = (songTime - holdStartTime) < holdStartGrace;
            if (inGrace) held = true; // 起手宽限视作按住：立刻建进度、不误断
            float vDecay = 0.5f / Mathf.Max(breakThreshold, 0.0001f); // 衰减速率：0.5u（判定线→线内0.5）/ 保底时长
            float dt = Time.deltaTime;
            if (held) markerDist = Mathf.Min(markerDist + normalSpeed * dt, 0f); // 按住：1× 恢复，钉在判定线
            else      markerDist -= vDecay * dt;                                  // 松手：固定速率衰退
            if (markerDist < -markerMissDist) { Break(); return; } // 衰退到判定线内 0.5 → 断连 Miss

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
        holdStartTime = songTime;   // 记录起手时刻，供起手宽限
        markerDist = 0f;            // 进度线从判定线起（=稳定位 v2），按住即钉在线上；松手才衰退

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
        // 首节点漏击后整条链接立即失效。漏击/断连消失改由 UpdateBandVisibility（整体 α=0.75 + 越判定线软边）处理。
        missMode = true;

        ResolveCharmedNode(0, false);
    }

    private void Break()
    {
        state = HoldState.Done;
        broken = true;
        fadeTimer = -1f;     // 走漏击"整体 α=0.75 + 越判定线软边"路径（与 MissHead 一致）
        missMode = true;     // 同上：由 UpdateBandVisibility 处理滑走消失
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
        // ① 技能清屏时，命中反馈(变大+停留≈0.42s)播完前整体 alpha 钉 1，避免"直接消失"吞掉命中反馈
        skillClearFeedbackGuard = skillCleared ? (hitPopDuration + nodeCompleteLinger) : 0f;
        // 尾节点变白 + 放大弹跳；全部段白色
        PlayHitPop(nodeCount - 1);
        // 最后一段 CLEAR 已在 Judge 循环中于尾节点处上报，这里不再重复上报
        // 连接带完成态的"变白"由 UpdateBandAll 依据 completedSegments 切换 Select 贴图 + select 覆盖带实现

    }
}
