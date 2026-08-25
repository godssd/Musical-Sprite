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
///   跨轨时玩家需沿连接线滑动跟随。所需音轨超过 0.2s 未被按住 -> 断连 MISS。
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

    [Tooltip("跨轨 Hold 判定时，允许按住轨道与\"当前所需轨道\"相差多少条轨道。1.0 表示允许相邻整数轨道，越大越宽松。")]
    public float laneTolerance = 1.0f;

    [Tooltip("收尾后透明度淡出时长（秒）。在此期间音符继续移动并被判定线裁剪。")]
    public float fadeDuration = 0.35f;

    [Tooltip("收尾后最大存活时长（秒），防止超长 Hold 断连时久久不消失。")]
    public float maxFadeLife = 0.6f;

    public enum HoldState { Waiting, Holding, Done }
    public HoldState state = HoldState.Waiting;
    public bool broken = false;
    public bool finished = false; // 供发射器从列表中清理

    public event System.Action<int, int, string, Vector3> onJudge;

    // ===== 视觉 =====
    private List<Transform> nodeTransforms = new List<Transform>();
    private List<MeshRenderer> nodeRends = new List<MeshRenderer>();
    private List<Material> nodeMats = new List<Material>();

    private List<List<Transform>> segmentGroups = new List<List<Transform>>();      // 每段含多段细分圆柱
    private List<List<MeshRenderer>> segmentRendGroups = new List<List<MeshRenderer>>();
    private List<List<Material>> segmentMatGroups = new List<List<Material>>();
    private const int BarSegmentCount = 16; // 每段细分圆柱数

    private Vector3 baseNodeScale;
    private float baseBarSegRadius;
    private Vector3[] exitPositions;
    private float[] exitLeadTimes;
    private float rideY;
    private float breakTimer = 0f;
    private float fadeTimer = -1f;
    private bool missShrinking = false;

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

            nodeTransforms.Add(t);
            nodeRends.Add(rend);
            nodeMats.Add(mat);
        }

        // 连接 bar：节点 i 与 i+1 之间一段，拆成多段便于逐段变白。
        // 连轨使用圆角扁平矩形片，片段首尾略微重叠，避免跨轨时出现断缝。
        baseBarSegRadius = r * 0.18f;
        for (int s = 0; s < nodeCount - 1; s++)
        {
            var segTs = new List<Transform>();
            var segRends = new List<MeshRenderer>();
            var segMats = new List<Material>();
            for (int k = 0; k < BarSegmentCount; k++)
            {
                var segGo = new GameObject($"HoldSeg_{s}_{k}");
                var segT = segGo.transform;
                segT.SetParent(transform, false);
                var smf = segGo.AddComponent<MeshFilter>();
                smf.sharedMesh = laneSpan > 1 ? NoteMover.RoundedRectMesh : CylinderMesh;
                var sRend = segGo.AddComponent<MeshRenderer>();
                sRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sRend.receiveShadows = false;
                var sMat = CreateColoredMaterial(Color.black);
                sRend.material = sMat;
                segT.localScale = laneSpan > 1
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
        if (finished || missShrinking) return;

        float songTime = conductor.songPosition;

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
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeRends[i] != null)
                {
                    bool revealed = IsBeyondLine(nodeTransforms[i].position.x, cx);
                    bool stillBeforeJudge = !IsFullyBeyondLine(nodeTransforms[i].position.x, noteRadius, judgeLineX);
                    nodeRends[i].enabled = revealed && stillBeforeJudge;
                }
            }
            UpdateSegmentVisibility(cx, judgeLineX);
        }
        else
        {
            // 收尾：音符继续按原速度移动，用判定线 judgeLineX 作为固定的消失边界
            if (fadeTimer <= 0f)
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
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeRends[i] != null)
                {
                    bool revealed = IsBeyondLine(nodeTransforms[i].position.x, centerLine != null ? centerLine.currentX : 0f);
                    bool stillBeforeJudge = !IsFullyBeyondLine(nodeTransforms[i].position.x, noteRadius, judgeX);
                    nodeRends[i].enabled = revealed && stillBeforeJudge;
                }
            }
            UpdateSegmentVisibility(centerLine != null ? centerLine.currentX : 0f, judgeX);

            // 完成：整体透明度淡出；断连/漏击：保持不透明，只通过判定线裁剪消失
            if (!broken)
            {
                float alpha = 1f - Mathf.Clamp01(fadeTimer / fadeDuration);
                SetAlpha(alpha);
            }

            // 结束条件：整根越过判定线（以尾节点或最前节点为准），或超过最大存活时间
            bool allGone = true;
            for (int i = 0; i < nodeCount; i++)
            {
                if (!IsFullyBeyondLine(nodeTransforms[i].position.x, noteRadius, judgeX)) { allGone = false; break; }
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

            Vector3 normDir = dir.normalized;
            float headEdgeOffset = noteRadius;
            float tailEdgeOffset = noteRadius;
            float usableLen = Mathf.Max(0f, totalLen - headEdgeOffset - tailEdgeOffset);
            Vector3 start = a + normDir * headEdgeOffset;
            Vector3 end = b - normDir * tailEdgeOffset;

            int n = segs.Count;
            float segLen = n > 1 ? usableLen / n * 1.05f : usableLen;
            for (int k = 0; k < n; k++)
            {
                float p = (k + 0.5f) / n;
                segs[k].position = Vector3.Lerp(start, end, p);
                if (laneSpan > 1)
                {
                    // 连轨使用“阶梯”宽带：每个小片都保持水平，
                    // 只逐段改变 Z，避免整条连接带被拉成斜四边形。
                    // 每段宽度取两端节点宽度的最大值（节点 i 与 i+1 可能一宽一窄）。
                    int spanA = NodeSpan(s);
                    int spanB = NodeSpan(s + 1);
                    int segSpan = Mathf.Max(spanA, spanB);
                    float width = laneSpacing * (segSpan - 1) + noteRadius * 2f;
                    float directionX = Mathf.Sign(b.x - a.x);
                    if (Mathf.Abs(b.x - a.x) < 0.001f) directionX = side == 0 ? -1f : 1f;
                    float startX = a.x + directionX * noteRadius;
                    float endX = b.x - directionX * noteRadius;
                    float usableX = Mathf.Abs(endX - startX);
                    float stairSegLen = usableX / n * 1.12f;
                    float stairP = (k + 0.5f) / n;
                    float x = Mathf.Lerp(startX, endX, stairP);
                    float z = Mathf.Lerp(a.z, b.z, stairP);
                    segs[k].position = new Vector3(x, a.y, z);
                    segs[k].rotation = Quaternion.identity;
                    segs[k].localScale = new Vector3(stairSegLen, 0.12f, width);
                }
                else
                {
                    segs[k].rotation = Quaternion.FromToRotation(Vector3.up, normDir);
                    segs[k].localScale = new Vector3(baseBarSegRadius, segLen, baseBarSegRadius);
                }
            }
        }
    }

    /// <summary>
    /// 按住期间：已完成段保持白色，当前进行中的段按进度逐段变白，未到达段保持黑色。
    /// </summary>
    private void UpdateSegmentColors(float songTime)
    {
        if (state != HoldState.Holding) return;

        for (int s = 0; s < segmentGroups.Count; s++)
        {
            // 该段整体进度：从 times[s] 到 times[s+1]
            float segStart = times[s];
            float segEnd = times[s + 1];
            float segProg = Mathf.Clamp01((songTime - segStart) / Mathf.Max(segEnd - segStart, 0.0001f));

            // 已完成的段（s < completedSegments）全白；当前段按 segProg 逐细分变白；未来段全黑
            int litCount;
            if (s < completedSegments) litCount = BarSegmentCount;
            else if (s == completedSegments) litCount = Mathf.FloorToInt(segProg * BarSegmentCount);
            else litCount = 0;

            var mats = segmentMatGroups[s];
            for (int k = 0; k < BarSegmentCount; k++)
            {
                if (mats[k] == null) continue;
                mats[k].color = k < litCount ? Color.white : Color.black;
            }
        }
    }

    private void UpdateSegmentVisibility(float revealLineX, float hideLineX)
    {
        for (int s = 0; s < segmentGroups.Count; s++)
        {
            var segs = segmentGroups[s];
            var rends = segmentRendGroups[s];
            Vector3 a = nodeTransforms[s].position;
            Vector3 b = nodeTransforms[s + 1].position;
            Vector3 dir = b - a;
            float totalLen = dir.magnitude;
            Vector3 normDir = totalLen < 0.001f ? Vector3.right : dir.normalized;

            for (int k = 0; k < segs.Count; k++)
            {
                if (rends[k] == null) continue;
                if (totalLen < 0.001f)
                {
                    bool collapsedRevealed = IsBeyondLine(segs[k].position.x, revealLineX);
                    bool collapsedBeforeJudge = !IsFullyBeyondLine(segs[k].position.x, noteRadius, hideLineX);
                    rends[k].enabled = collapsedRevealed && collapsedBeforeJudge;
                    continue;
                }
                float halfLenX;
                if (laneSpan > 1)
                {
                    float halfAlong = segs[k].localScale.x * 0.5f * Mathf.Abs(normDir.x);
                    float halfAcross = segs[k].localScale.z * 0.5f * Mathf.Abs(normDir.z);
                    halfLenX = halfAlong + halfAcross;
                }
                else
                {
                    halfLenX = segs[k].localScale.y * 0.5f * Mathf.Abs(normDir.x);
                }
                float minX = segs[k].position.x - halfLenX;
                float maxX = segs[k].position.x + halfLenX;
                bool revealed = IsBeyondLine(segs[k].position.x, revealLineX);
                bool stillBeforeJudge = !IsFullyBeyondLine(segs[k].position.x, halfLenX, hideLineX);
                rends[k].enabled = revealed && stillBeforeJudge;
            }
        }
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

    /// <summary>当前 songTime 所处“应被按住”的轨道判定（连轨 Hold 判定宽一个音轨）。</summary>
    /// <param name="reqLaneF">当前插值轨道（来自 SampleLaneAtTime，保留滑动过渡）。</param>
    private bool AreRequiredLanesHeld(float reqLaneF)
    {
        if (spawner == null) return false;
        if (laneSpan <= 1) return IsAnyHeldLaneClose(reqLaneF);
        // 连轨 Hold：判定宽一个音轨——按住覆盖的任意一条相邻轨即可。
        // 基于当前插值轨道 reqLaneF：直接判断按下的是否为覆盖的两条相邻轨之一，
        // 并保留容差（IsAnyHeldLaneClose）以兼容普通 Hold 的滑动过渡手感。
        int baseLane = Mathf.RoundToInt(reqLaneF);
        if (spawner.heldLanes.Contains(baseLane) || spawner.heldLanes.Contains(baseLane + 1)) return true;
        return IsAnyHeldLaneClose(reqLaneF);
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
                bool held = AreRequiredLanesHeld(reqLaneF);
                if (!held) breakTimer += Time.deltaTime;
                else breakTimer = 0f;

                if (breakTimer > 0.2f)
                {
                    Break();
                    return;
                }
            }

            // 每跨越一个节点时刻，完成一段（节点 i -> i+1）=> 触发 CLEAR
            while (completedSegments < nodeCount - 1 && songTime >= times[completedSegments + 1])
            {
                completedSegments++;
                // 节点变白
                if (nodeMats[completedSegments] != null) nodeMats[completedSegments].color = Color.white;
                // 上报该段 CLEAR：位置取该段"结束节点"的判定线处
                Vector3 segEndPos = hitPositions[completedSegments];
                onJudge?.Invoke(side, lanes[completedSegments], "CLEAR", segEndPos);
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

        // 命中反馈：head 变白
        if (nodeMats[0] != null) nodeMats[0].color = Color.white;

        onJudge?.Invoke(side, lanes[0], rank, hitPositions[0]);
    }

    private void MissHead()
    {
        state = HoldState.Done;
        broken = true;
        fadeTimer = -1f;
        ResetAllToBlack();
        // 首节点漏击后整条链接立即失效，但保留对象播放缩小消失动画。
        missShrinking = true;
        StartCoroutine(MissShrinkCoroutine());
    }

    private System.Collections.IEnumerator MissShrinkCoroutine()
    {
        const float duration = 0.18f;
        float timer = 0f;
        Vector3[] nodeScales = new Vector3[nodeTransforms.Count];
        for (int i = 0; i < nodeTransforms.Count; i++)
            nodeScales[i] = nodeTransforms[i].localScale;

        var segmentScales = new List<List<Vector3>>();
        for (int s = 0; s < segmentGroups.Count; s++)
        {
            var scales = new List<Vector3>();
            for (int k = 0; k < segmentGroups[s].Count; k++)
                scales.Add(segmentGroups[s][k].localScale);
            segmentScales.Add(scales);
        }

        SetAlpha(1f);
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            float scale = 1f - t;
            for (int i = 0; i < nodeTransforms.Count; i++)
                nodeTransforms[i].localScale = nodeScales[i] * scale;
            for (int s = 0; s < segmentGroups.Count; s++)
                for (int k = 0; k < segmentGroups[s].Count; k++)
                    segmentGroups[s][k].localScale = segmentScales[s][k] * scale;
            SetAlpha(scale);
            yield return null;
        }

        SetVisualsEnabled(false);
        missShrinking = false;
        finished = true;
        onJudge?.Invoke(side, lanes[0], "MISS", hitPositions[0]);
    }

    private void SetVisualsEnabled(bool enabled)
    {
        for (int i = 0; i < nodeRends.Count; i++)
        {
            if (nodeRends[i] != null) nodeRends[i].enabled = enabled;
        }
        SetBarEnabledAll(enabled);
    }

    private void Break()
    {
        state = HoldState.Done;
        broken = true;
        fadeTimer = 0f;
        ResetAllToBlack();
        // 断连在"最后成功节点"处报 MISS
        int lastNode = Mathf.Max(0, completedSegments);
        onJudge?.Invoke(side, lanes[lastNode], "MISS", hitPositions[lastNode]);
    }

    private void Complete()
    {
        state = HoldState.Done;
        broken = false;
        fadeTimer = 0f;
        // 尾节点变白 + 全部段白色
        if (nodeMats[nodeCount - 1] != null) nodeMats[nodeCount - 1].color = Color.white;
        for (int s = 0; s < segmentMatGroups.Count; s++)
            for (int k = 0; k < segmentMatGroups[s].Count; k++)
                if (segmentMatGroups[s][k] != null) segmentMatGroups[s][k].color = Color.white;
        // 最后一段 CLEAR 已在 Judge 循环中于尾节点处上报，这里不再重复上报
    }
}
