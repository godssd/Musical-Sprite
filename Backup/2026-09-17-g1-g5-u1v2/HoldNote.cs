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

    // ===== 链接带（ribbon）视觉：每段一个连续 mesh，替代原 16 圆柱细分 =====
    private List<GameObject> bandGOs = new List<GameObject>();
    private List<Mesh> bandMeshes = new List<Mesh>();
    private List<Material> bandMats = new List<Material>();
    private List<MeshRenderer> bandRends = new List<MeshRenderer>();
    private Transform progressMarker;
    private Material progressMat;
    private Texture2D slideLinkTex, slideLinkSelectTex, slideJudgmentTex;
    private float bandBreakDebt = 0f; // 保底债务：进度线相对判定线的回退量（0=正常超前 0.5u）
    private float bandRevealAlpha = 0f;  // 链接带过粉杠渐显系数（0→1），与越线消失系数相乘
    private float bandRevealStart = -1f; // 链接带首次越过粉杠的时刻（songTime），<0 表示尚未开始渐显

    [Tooltip("链接带端点内缩比例（相对 noteRadius）：1=带子刚好从节点贴图外沿开始/结束，0=贴到节点中心。")]
    public float bandEndInsetRatio = 1.0f;
    [Tooltip("链拍带越过判定线后软边消失的过渡宽度（世界单位，smoothstep）。")]
    public float bandFadeWidth = 0.3f;
    [Tooltip("链接带每段纵向细分段数，越大曲线越平滑。")]
    public int bandSubdiv = 24;
    [Tooltip("进度线 marker 缩放（XZ 平面，世界单位）。")]
    public float progressMarkerScale = 0.6f;
    [Tooltip("链接带越过粉杠（中线）后出现的不透明度渐显时长（秒，默认 0.2）。硬切改为渐显。")]
    public float bandRevealFadeDur = 0.2f;

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
        // 链接带透明度改由 UpdateBandVisibility 的逐顶点 alpha（含漏击/收尾）控制，不在这里统一设置
    }

    private void ResetAllToBlack()
    {
        // 链接带不发黑：漏击/断连时由 UpdateBandVisibility 整体降到 α=0.75 并随判定线软边消失。
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

        // 连接带（ribbon）：节点 i 与 i+1 之间一段连续 mesh，用 slideLink/slideLinkSelect 贴图，
        // 沿带长平铺 tile + 越判定线 smoothstep 软边（彻底去除原 16 圆柱细分的 on/off 锯齿）。
        slideLinkTex = (NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.slideLink != null) ? NoteSpriteLibrary.Instance.slideLink.texture : null;
        slideLinkSelectTex = (NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.slideLinkSelect != null) ? NoteSpriteLibrary.Instance.slideLinkSelect.texture : null;
        slideJudgmentTex = (NoteSpriteLibrary.Instance != null && NoteSpriteLibrary.Instance.slideJudgment != null) ? NoteSpriteLibrary.Instance.slideJudgment.texture : null;
        if (slideLinkTex != null) slideLinkTex.wrapMode = TextureWrapMode.Clamp;
        if (slideLinkSelectTex != null) slideLinkSelectTex.wrapMode = TextureWrapMode.Clamp;

        for (int s = 0; s < nodeCount - 1; s++)
        {
            var go = new GameObject($"SlideBand_{s}");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mesh = new Mesh();
            mf.mesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            var mat = CreateBandMaterial(slideLinkTex);
            rend.material = mat;
            rend.enabled = false;
            bandGOs.Add(go); bandMeshes.Add(mesh); bandMats.Add(mat); bandRends.Add(rend);
        }

        // 进度线 marker（slideJudgment 贴图），仅按住期间显示，表达"已完成/未完成"分界
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
            prend.material = progressMat;
            progressMarker = pm.transform;
            pm.transform.localScale = new Vector3(progressMarkerScale, 1f, progressMarkerScale);
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

            UpdateBandGeometry();
            UpdateBandFill();

            // 可见性：按元素"前沿"是否越过粉杠决定显示
            float cx = centerLine != null ? centerLine.currentX : 0f;
            // 修复（2026-08-26 Issue 4）：节点可见性 + 缩没动画统一交给 UpdateNodeVisual，
            // 逾越各自判定线后「按时间」缩小消失（holdNodeShrinkDuration），而非瞬间硬切隐藏。
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeRends[i] != null)
                    UpdateNodeVisual(i, cx, hasLit || nodePop[i] > 0f);
            }
            UpdateBandVisibility(cx, judgeLineX);
            UpdateProgressMarker();

            // 漏击消失：每个节点各自越过判定线后缩小并淡出；链接带透明度/软边由 UpdateBandVisibility 处理（α=0.75 + 越线消失）
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

            UpdateBandGeometry();
            UpdateBandFill();

            float judgeX = judgeLineX;
            // 修复（2026-08-26 Issue 4）：收尾阶段节点可见性 + 缩没动画同样交给 UpdateNodeVisual，
            // 逾越各自判定线后「按时间」缩小消失，避免刚越过判定线就被销毁而看不到缩没。
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeRends[i] != null)
                    UpdateNodeVisual(i, centerLine != null ? centerLine.currentX : 0f, hasLit || nodePop[i] > 0f);
            }
            UpdateBandVisibility(centerLine != null ? centerLine.currentX : 0f, judgeX);
            UpdateProgressMarker();

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

    // ============================================================
    // 链接带（ribbon）实现：单连续 mesh + 沿带长 UV 平铺 + 越线 smoothstep 软边
    // 替代原 16 圆柱细分，彻底消除分段 on/off 锯齿。
    // ============================================================

    private float BandWidth(int nodeIndex)
    {
        int span = NodeSpan(nodeIndex);
        return laneSpacing * (span - 1) + noteRadius * 2f;
    }

    private Material CreateBandMaterial(Texture2D tex)
    {
        Material m = new Material(Shader.Find("Sprites/Default"));
        m.mainTexture = tex;
        m.color = Color.white;
        m.SetInt("_Cull", 0); // 双面，避免视角下背面被剔除
        m.renderQueue = 2998;  // 低于节点（默认 3000），节点圆盘盖在带子接头之上
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

    /// <summary>每帧按节点实时位置重建每段 ribbon 的几何 + UV（每段 0→1 整段拉伸，端点内缩到节点外沿）。</summary>
    private void UpdateBandGeometry()
    {
        int M = Mathf.Max(2, bandSubdiv);
        float inset = noteRadius * bandEndInsetRatio; // 带端从节点贴图外沿开始（不再插进节点中心）
        for (int s = 0; s < bandMeshes.Count; s++)
        {
            Vector3 a = nodeTransforms[s].position;
            Vector3 b = nodeTransforms[s + 1].position;
            Vector3 dir = b - a;
            float segLen = dir.magnitude;
            dir = segLen > 0.0001f ? dir / segLen : Vector3.right;
            Vector3 aEdge = a + dir * inset;
            Vector3 bEdge = b - dir * inset;
            float wA = BandWidth(s), wB = BandWidth(s + 1);
            bool flip = b.x >= a.x; // 保证法线朝 +y（朝上），两侧视角都可见
            int vCount = (M + 1) * 2;
            Vector3[] verts = new Vector3[vCount];
            Vector2[] uvs = new Vector2[vCount];
            Color[] cols = new Color[vCount];
            for (int i = 0; i <= M; i++)
            {
                float u = i / (float)M;
                Vector3 p = Vector3.Lerp(aEdge, bEdge, u);
                float w = Mathf.Lerp(wA, wB, u);
                Vector3 cL = p + new Vector3(0f, 0f, w * 0.5f);
                Vector3 cR = p - new Vector3(0f, 0f, w * 0.5f);
                verts[2 * i] = flip ? cR : cL;
                verts[2 * i + 1] = flip ? cL : cR;
                uvs[2 * i] = new Vector2(u, 1f);       // 每段 0→1 整段拉伸（一整张贴图铺满一段）
                uvs[2 * i + 1] = new Vector2(u, 0f);
                cols[2 * i] = Color.white;
                cols[2 * i + 1] = Color.white;
            }
            int[] tris = new int[M * 6];
            for (int i = 0; i < M; i++)
            {
                int o = i * 6;
                int i0 = 2 * i, i1 = 2 * i + 1, i2 = 2 * i + 2, i3 = 2 * i + 3;
                tris[o] = i0; tris[o + 1] = i1; tris[o + 2] = i2;
                tris[o + 3] = i2; tris[o + 4] = i1; tris[o + 5] = i3;
            }
            Mesh mesh = bandMeshes[s];
            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
        }
    }

    /// <summary>已完成段用 Select 贴图，未到达段用未完成贴图（slideLink）。</summary>
    private void UpdateBandFill()
    {
        for (int s = 0; s < bandMats.Count; s++)
        {
            if (bandMats[s] == null) continue;
            Texture2D tex = (s < completedSegments) ? slideLinkSelectTex : slideLinkTex;
            if (bandMats[s].mainTexture != (Texture)tex) bandMats[s].mainTexture = tex;
        }
    }

    /// <summary>可见性 + 越判定线 smoothstep 软边消失 + 漏击/收尾整体透明度 + 过粉杠渐显。</summary>
    private void UpdateBandVisibility(float revealX, float judgeX)
    {
        float g = 1f;
        if (missMode) g = 0.75f;
        else if (fadeTimer >= 0f && !broken) g = 1f - Mathf.Clamp01(fadeTimer / fadeDuration);

        // 过粉杠渐显：任一节点越过粉杠即开始计时，bandRevealAlpha 0→1 渐显（与越线消失系数相乘）
        bool anyRevealed = false;
        for (int s = 0; s < bandRends.Count; s++)
        {
            if (bandRends[s] == null) continue;
            if (IsBeyondLine(nodeTransforms[s].position.x, revealX) || IsBeyondLine(nodeTransforms[s + 1].position.x, revealX))
            { anyRevealed = true; break; }
        }
        if (anyRevealed && bandRevealStart < 0f)
            bandRevealStart = conductor != null ? conductor.songPosition : 0f;
        float ra = bandRevealStart < 0f ? 0f
            : Mathf.Clamp01(((conductor != null ? conductor.songPosition : 0f) - bandRevealStart) / Mathf.Max(bandRevealFadeDur, 0.0001f));

        for (int s = 0; s < bandRends.Count; s++)
        {
            var rend = bandRends[s];
            if (rend == null) continue;
            bool revealed = IsBeyondLine(nodeTransforms[s].position.x, revealX) || IsBeyondLine(nodeTransforms[s + 1].position.x, revealX);
            if (!revealed) { rend.enabled = false; continue; }
            rend.enabled = true;

            Mesh mesh = bandMeshes[s];
            if (mesh == null || mesh.vertexCount < 4) continue;
            Vector3[] vs = mesh.vertices;
            Color[] cols = mesh.colors;
            if (cols == null || cols.Length != vs.Length) cols = new Color[vs.Length];
            int M = vs.Length / 2 - 1;
            for (int i = 0; i <= M; i++)
            {
                Vector3 vp = vs[2 * i];
                float alpha;
                if (side == 0) alpha = Smoothstep(judgeX - bandFadeWidth, judgeX, vp.x);
                else alpha = 1f - Smoothstep(judgeX, judgeX + bandFadeWidth, vp.x);
                alpha *= g * ra; // 渐显系数 × 消失系数（/漏击整体透明度）
                cols[2 * i] = new Color(1f, 1f, 1f, alpha);
                cols[2 * i + 1] = new Color(1f, 1f, 1f, alpha);
            }
            mesh.colors = cols;
        }
    }

    /// <summary>进度线 marker：置于当前进度点，保底债务时相对判定线回退；仅 Holding 时显示。</summary>
    private void UpdateProgressMarker()
    {
        if (progressMarker == null) return;
        bool show = (state == HoldState.Holding && !finished);
        progressMarker.gameObject.SetActive(show);
        if (!show) return;
        float st = conductor != null ? conductor.songPosition : 0f;
        int seg = CurrentSegmentIndex(st);
        float t0 = times[seg], t1 = times[seg + 1];
        float f = Mathf.Clamp01((st - t0) / Mathf.Max(t1 - t0, 0.0001f));
        Vector3 front = Vector3.Lerp(nodeTransforms[seg].position, nodeTransforms[seg + 1].position, f);
        float dir = side == 0 ? -1f : 1f;
        front.x += dir * bandBreakDebt; // 保底回退
        progressMarker.position = new Vector3(front.x, rideY, front.z);
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
            // 保底债务：断连计时增长时进度线相对判定线回退（0→0.5u），按住正确时回到 0（正常超前 0.5u）
            bandBreakDebt = Mathf.Clamp01(breakTimer / Mathf.Max(breakThreshold, 0.0001f)) * 0.5f;

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
        // 尾节点变白 + 放大弹跳；全部段白色
        PlayHitPop(nodeCount - 1);
        // 最后一段 CLEAR 已在 Judge 循环中于尾节点处上报，这里不再重复上报
        // 连接带完成态的"变白"由 UpdateBandFill 依据 completedSegments 切换 Select 贴图实现

    }
}
