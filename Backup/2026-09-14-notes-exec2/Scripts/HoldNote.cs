using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 长按音符（按住并沿节点链滑动）的运行时组件。
/// 现在节点与链接线均使用 SpriteRenderer，接入 Note_Slide_Judgment / Note_Slide_Link 美术资源。
/// </summary>
public class HoldNote : MonoBehaviour
{
    public NoteSpawner spawner;
    public int side;
    public int[] lanes;
    public float[] times;
    public float leadTime;
    public float noteRadius = 0.45f;
    public int laneSpan = 1;
    public int[] nodeLaneSpans;
    public float laneSpacing = 1.5f;
    public float goodWindow = 0.07f;
    public float perfectWindow = 0.03f;
    public Vector3[] spawnPositions;
    public Vector3[] hitPositions;
    public Conductor conductor;
    public BattleCenterLine centerLine;
    public float judgeLineX;
    public bool isAI = false;

    public float laneTolerance = 1.0f;
    public float slideSettleWindow = 0.15f;
    public float breakThreshold = 0.2f;
    public float earlySlideGrace = 0.18f;
    public float fadeDuration = 0.35f;
    public float maxFadeLife = 0.6f;

    public enum HoldState { Waiting, Holding, Done }
    public HoldState state = HoldState.Waiting;
    public bool broken = false;
    public bool finished = false;

    public event System.Action<int, int, string, Vector3> onJudge;

    private ActiveSkillRuntime[] charmOwnersByNode;
    [HideInInspector] public bool wasCharmed = false;
    public int NodeCount => lanes != null ? lanes.Length : nodeCount;
    public float GetNodeTime(int index) => times[index];
    public int GetNodeLane(int index) => lanes[index];

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
        var owner = charmOwnersByNode[index];
        if (owner == null) return;
        charmOwnersByNode[index] = null;
        if (success) owner.OnCharmNodeSuccess(this);
        else owner.OnCharmNodeFail(this);
    }

    // 视觉
    private List<Transform> nodeTransforms = new List<Transform>();
    private List<SpriteRenderer> nodeRends = new List<SpriteRenderer>();
    private List<List<Transform>> segmentGroups = new List<List<Transform>>();
    private List<List<SpriteRenderer>> segmentRendGroups = new List<List<SpriteRenderer>>();
    private const int BarSegmentCount = 1;   // 方案 A：每段只用一个柔光 Sprite，无段感
    private static readonly Color SegNormalColor = new Color(1f, 0.82f, 0.40f, 0.5f);  // 普通连线暖黄柔光
    private static readonly Color SegLitColor = new Color(1f, 0.95f, 0.72f, 0.72f);    // 已划过/点亮更亮

    private Vector3[] nodeBaseScales;
    private Vector3[][] segmentBaseScales; // 每段每小片的基准缩放
    private Vector3[] exitPositions;
    private float[] exitLeadTimes;
    private float rideY;
    private float breakTimer = 0f;
    private float fadeTimer = -1f;
    private bool missMode = false;
    private bool missReported = false;
    private bool skillCleared = false;
    private float missShrinkSpan = 0.5f;
    public float holdNodeShrinkDuration = 0.25f;

    private float[] nodePop;
    private float[] nodeShrinkStart;
    private float hitPopDuration = 0.18f;
    private float hitPopScale = 1.35f;

    private bool hasLit = false;
    private float glowBufferDist = 0.6f;
    private bool[] segLitFlags;

    private int completedSegments = 0;
    private int nodeCount;

    // 保护竖杠
    private Transform protectionBarTr;
    private SpriteRenderer protectionBarRend;
    private float protectionMaxOffset = 0.25f;

    private Sprite DefaultLink => NoteSpriteLibrary.Instance?.slideLink;
    private Sprite SelectLink => NoteSpriteLibrary.Instance?.slideLinkSelect ?? DefaultLink;
    private Sprite DefaultNode => NoteSpriteLibrary.Instance?.slideJudgment;
    private Sprite SelectNode => NoteSpriteLibrary.Instance?.slideJudgmentSelect ?? DefaultNode;

    private Vector3 SpriteScaleForSize(Sprite s, float worldW, float worldH)
    {
        if (s == null) return new Vector3(worldW, worldH, 1f);
        Vector2 sz = s.bounds.size;
        float sx = sz.x > 0.0001f ? worldW / sz.x : 1f;
        float sy = sz.y > 0.0001f ? worldH / sz.y : 1f;
        return new Vector3(sx, sy, 1f);
    }

    void Start()
    {
        BuildVisuals();
    }

    private void BuildVisuals()
    {
        if (lanes == null || lanes.Length < 2 || times == null || times.Length < 2 || lanes.Length != times.Length)
        {
            Debug.LogError("[HoldNote] lanes/times 非法，已退化为 2 节点默认 Hold。");
            lanes = new int[] { 0, 0 };
            times = new float[] { 0f, 1f };
        }
        nodeCount = lanes.Length;
        nodeBaseScales = new Vector3[nodeCount];
        segmentBaseScales = new Vector3[nodeCount - 1][];
        nodePop = new float[nodeCount];
        nodeShrinkStart = new float[nodeCount];
        for (int i = 0; i < nodeCount; i++) nodeShrinkStart[i] = -1f;

        float r = noteRadius / 0.5f;

        for (int i = 0; i < nodeCount; i++)
        {
            int span = NodeSpan(i);
            float linkedWidth = laneSpacing * (span - 1) + noteRadius * 2f;
            float nodeWorldW = span > 1 ? linkedWidth : noteRadius * 2f;
            Sprite nodeSpr = DefaultNode ?? RuntimeSpriteUtility.CreateRoundedBarSprite(32, 32, Color.white, Color.clear, 0, 8);
            float nodeAspect = nodeSpr.bounds.size.y / Mathf.Max(nodeSpr.bounds.size.x, 0.0001f);
            Vector3 nodeScale = SpriteScaleForSize(nodeSpr, nodeWorldW, nodeWorldW * nodeAspect);  // 保持原图宽高比

            var go = new GameObject($"HoldNode_{i}");
            var t = go.transform;
            t.SetParent(transform, false);
            t.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = nodeSpr;
            sr.sortingOrder = 10;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.color = Color.white;
            t.localScale = nodeScale;
            nodeBaseScales[i] = nodeScale;

            // 节点光晕（加法柔光，营造发光果冻感）
            var ag = RuntimeSpriteUtility.AdditiveGlow;
            if (ag != null)
            {
                var ng = new GameObject($"HoldNodeGlow_{i}");
                ng.transform.SetParent(go.transform, false);
                ng.transform.localPosition = Vector3.zero;
                ng.transform.localRotation = Quaternion.identity;
                var ngsr = ng.AddComponent<SpriteRenderer>();
                ngsr.sprite = sr.sprite;
                ngsr.material = ag;
                ngsr.sortingOrder = 9;
                ngsr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ngsr.color = new Color(1f, 0.90f, 0.62f, 0.30f);   // 更淡
                ng.transform.localScale = Vector3.one * 1.15f;       // 相对父(父已带 nodeScale)
            }

            nodeTransforms.Add(t);
            nodeRends.Add(sr);
        }

        if (segLitFlags == null || segLitFlags.Length != nodeCount - 1)
            segLitFlags = new bool[nodeCount - 1];

        for (int s = 0; s < nodeCount - 1; s++)
        {
            int spanA = NodeSpan(s);
            int spanB = NodeSpan(s + 1);
            int segSpan = Mathf.Max(spanA, spanB);
            float linkedWidth = laneSpacing * (segSpan - 1) + noteRadius * 2f;

            // 方案 A：每段只用一个程序化柔光 Sprite（GlowBar），无平铺段感
            var segGo = new GameObject($"HoldSeg_{s}");
            var segT = segGo.transform;
            segT.SetParent(transform, false);
            segT.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var sr = segGo.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSpriteUtility.GlowBar;
            sr.sortingOrder = 8;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var segAg = RuntimeSpriteUtility.AdditiveGlow;
            if (segAg != null) sr.material = segAg;
            sr.color = SegNormalColor;                            // 暖色柔光带（加法）

            var segTs = new List<Transform> { segT };
            var segRends = new List<SpriteRenderer> { sr };
            segmentBaseScales[s] = new Vector3[BarSegmentCount];  // BarSegmentCount = 1
            segmentGroups.Add(segTs);
            segmentRendGroups.Add(segRends);
        }

        if (charmOwnersByNode != null)
            for (int i = 0; i < charmOwnersByNode.Length; i++)
                if (charmOwnersByNode[i] != null) TintCharmedNode(i);

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

        for (int i = 0; i < nodeRends.Count; i++) nodeRends[i].enabled = false;
        SetBarEnabledAll(false);

        CreateProtectionBar();
    }

    private void CreateProtectionBar()
    {
        var go = new GameObject("ProtectionBar");
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        protectionBarTr = go.transform;
        protectionBarRend = go.AddComponent<SpriteRenderer>();
        protectionBarRend.sprite = RuntimeSpriteUtility.CreateRoundedBarSprite(8, 64, Color.white, Color.clear, 0, 4);
        protectionBarRend.sortingOrder = 15;
        protectionBarRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var pAg = RuntimeSpriteUtility.AdditiveGlow;
        if (pAg != null) protectionBarRend.material = pAg;
        protectionBarRend.color = new Color(1f, 0.85f, 0.45f, 0.25f);   // 更淡
        protectionBarTr.localScale = new Vector3(0.03f, 0.18f, 1f);     // 更短
        protectionBarRend.enabled = false;
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
        UpdateHitPops();
        UpdateProtectionBar();

        if (fadeTimer < 0f)
        {
            for (int i = 0; i < nodeCount; i++)
            {
                float t = Mathf.Clamp01((songTime - (times[i] - leadTime)) / exitLeadTimes[i]);
                Vector3 p = Vector3.Lerp(spawnPositions[i], exitPositions[i], t);
                nodeTransforms[i].position = new Vector3(p.x, rideY, p.z);
            }

            UpdateAllSegments();
            UpdateSegmentColors(songTime);

            float cx = centerLine != null ? centerLine.currentX : 0f;
            for (int i = 0; i < nodeCount; i++)
                if (nodeRends[i] != null)
                    UpdateNodeVisual(i, cx, hasLit || nodePop[i] > 0f);
            UpdateSegmentVisibility(cx, judgeLineX);

            if (missMode)
            {
                ApplyMissDisappear(cx, judgeLineX);
                for (int i = 0; i < nodeCount; i++)
                    if (songTime >= times[i]) ResolveCharmedNode(i, false);

                float headPast = side == 0
                    ? (judgeLineX - nodeTransforms[0].position.x)
                    : (nodeTransforms[0].position.x - judgeLineX);
                if (!missReported && headPast >= missShrinkSpan)
                {
                    missReported = true;
                    onJudge?.Invoke(side, lanes[0], "MISS", hitPositions[0]);
                }

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
            if (fadeTimer <= 0f && !skillCleared)
            {
                ResetAllToDefault();
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
                if (nodeRends[i] != null)
                    UpdateNodeVisual(i, centerLine != null ? centerLine.currentX : 0f, hasLit || nodePop[i] > 0f);
            UpdateSegmentVisibility(centerLine != null ? centerLine.currentX : 0f, judgeX);

            if (!broken)
            {
                float alpha = 1f - Mathf.Clamp01(fadeTimer / fadeDuration);
                SetAlpha(alpha);
            }

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

    private void UpdateProtectionBar()
    {
        if (protectionBarTr == null || protectionBarRend == null) return;
        bool show = state == HoldState.Holding && !isAI;
        protectionBarRend.enabled = show;
        if (!show) return;

        // 竖杠位置：当前头节点判定线外偏移
        Vector3 headPos = nodeTransforms[0].position;
        float targetOffset = protectionMaxOffset * (1f - Mathf.Clamp01(breakTimer / Mathf.Max(0.001f, breakThreshold)));
        float currentOffset = side == 0 ? (protectionBarTr.position.x - headPos.x) : (headPos.x - protectionBarTr.position.x);
        currentOffset = Mathf.Lerp(currentOffset, targetOffset, Time.deltaTime * 8f);
        float x = side == 0 ? headPos.x + currentOffset : headPos.x - currentOffset;
        protectionBarTr.position = new Vector3(x, rideY, headPos.z);
    }

    private void UpdateHitPops()
    {
        if (missMode) return;
        for (int i = 0; i < nodeCount; i++)
        {
            if (nodePop[i] <= 0f) continue;
            nodePop[i] = Mathf.Max(0f, nodePop[i] - Time.deltaTime / Mathf.Max(hitPopDuration, 0.0001f));
            float sc = 1f + (hitPopScale - 1f) * nodePop[i];
            nodeTransforms[i].localScale = new Vector3(nodeBaseScales[i].x, nodeBaseScales[i].y * sc, nodeBaseScales[i].z);
            if (nodePop[i] > 0f && nodeRends[i] != null && SelectNode != null)
                nodeRends[i].sprite = SelectNode;
            else if (nodeRends[i] != null)
                nodeRends[i].sprite = DefaultNode;
        }
    }

    private void UpdateNodeVisual(int i, float cx, bool litGlow)
    {
        if (nodeRends[i] == null) return;

        bool revealed = IsBeyondLine(nodeTransforms[i].position.x, cx);
        float nodeJudgeX = hitPositions[i].x;
        float glowLineX = nodeJudgeX + (litGlow ? glowBufferDist : 0f);
        bool stillBeforeJudge = !IsFullyBeyondLine(nodeTransforms[i].position.x, noteRadius, glowLineX);

        if (!revealed)
        {
            nodeRends[i].enabled = false;
            return;
        }

        if (stillBeforeJudge)
        {
            nodeRends[i].enabled = true;
            return;
        }

        if (nodeShrinkStart[i] < 0f) nodeShrinkStart[i] = Time.time;
        float t = (Time.time - nodeShrinkStart[i]) / Mathf.Max(holdNodeShrinkDuration, 0.0001f);
        if (t >= 1f)
        {
            nodeRends[i].enabled = false;
            nodeTransforms[i].localScale = new Vector3(nodeBaseScales[i].x, 0f, nodeBaseScales[i].z);
            return;
        }
        nodeRends[i].enabled = true;
        Vector3 bs = nodeBaseScales[i];
        float yScale = bs.y * (1f - t);
        if (nodePop[i] > 0f) yScale *= 1f + (hitPopScale - 1f) * nodePop[i];
        nodeTransforms[i].localScale = new Vector3(bs.x, yScale, bs.z);
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
                    segs[k].localScale = new Vector3(0.001f, 0.001f, 1f);
                }
                continue;
            }

            int spanA = NodeSpan(s);
            int spanB = NodeSpan(s + 1);
            int segSpan = Mathf.Max(spanA, spanB);
            float width = laneSpacing * (segSpan - 1) + noteRadius * 2f;

            Vector3 normDir = dir.normalized;
            float headEdgeOffset = noteRadius;
            float tailEdgeOffset = noteRadius;
            float usableLen = Mathf.Max(0f, totalLen - headEdgeOffset - tailEdgeOffset);
            Vector3 start = a + normDir * headEdgeOffset;
            Vector3 end = b - normDir * tailEdgeOffset;

            int n = segs.Count;
            float segLen = n > 1 ? usableLen / n * 1.05f : usableLen;

            // 平躺，X 沿链接方向
            float angleY = Mathf.Atan2(normDir.z, normDir.x) * Mathf.Rad2Deg;

            Sprite linkSpr = RuntimeSpriteUtility.GlowBar;
            Vector2 linkBounds = linkSpr != null ? linkSpr.bounds.size : Vector2.one;
            float scaleY = width / Mathf.Max(linkBounds.y, 0.0001f);

            for (int k = 0; k < n; k++)
            {
                float t = (k + 0.5f) / n;
                segs[k].position = Vector3.Lerp(start, end, t);
                segs[k].rotation = Quaternion.Euler(-90f, angleY, 0f);
                segs[k].localScale = new Vector3(segLen / Mathf.Max(linkBounds.x, 0.0001f), scaleY, 1f);
                segmentBaseScales[s][k] = segs[k].localScale;
            }
        }
    }

    private void UpdateSegmentColors(float songTime)
    {
        if (state != HoldState.Holding) return;

        // 方案 A：不再切换贴图，仅标记「已划过的段」用于点亮配色
        for (int s = 0; s < segmentGroups.Count; s++)
            segLitFlags[s] = (s < completedSegments);
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
            Vector2 gb = RuntimeSpriteUtility.GlowBar.bounds.size;

            float glowLineX = hideLineX + (hasLit ? glowBufferDist : 0f);
            float disappearSpan = 1.2f;

            for (int k = 0; k < segs.Count; k++)
            {
                if (rends[k] == null) continue;
                if (skillCleared)
                {
                    rends[k].enabled = true;
                    segs[k].localScale = segmentBaseScales[s][k] * 1.6f;
                    float fa = 1f - Mathf.Clamp01(fadeTimer / fadeDuration);
                    rends[k].color = new Color(SegLitColor.r, SegLitColor.g, SegLitColor.b, SegLitColor.a * fa);
                    continue;
                }
                if (totalLen < 0.001f)
                {
                    bool collapsedRevealed = IsBeyondLine(segs[k].position.x, revealLineX);
                    bool collapsedBeforeJudge = !IsFullyBeyondLine(segs[k].position.x, noteRadius, glowLineX);
                    rends[k].enabled = collapsedRevealed && collapsedBeforeJudge;
                    continue;
                }
                // 用真实精灵 bounds 推算段世界半长投影（避免段过早进入消失动画）
                float halfLenX = segmentBaseScales[s][k].x * gb.x * 0.5f * Mathf.Abs(normDir.x)
                              + segmentBaseScales[s][k].y * gb.y * 0.5f * Mathf.Abs(normDir.z);
                bool revealed = IsBeyondLine(segs[k].position.x, revealLineX);
                if (!revealed) { rends[k].enabled = false; continue; }

                bool stillBeforeJudge = !IsFullyBeyondLine(segs[k].position.x, halfLenX, glowLineX);
                if (stillBeforeJudge)
                {
                    rends[k].enabled = true;
                    if (!missMode) rends[k].color = segLitFlags[s] ? SegLitColor : SegNormalColor;
                }
                else
                {
                    float past = side == 0 ? (glowLineX - segs[k].position.x) : (segs[k].position.x - glowLineX);
                    float f = Mathf.Clamp01(past / disappearSpan);
                    segs[k].localScale = segmentBaseScales[s][k] * Mathf.Lerp(1f, 1.6f, f);
                    Color baseCol = segLitFlags[s] ? SegLitColor : SegNormalColor;
                    if (!missMode) rends[k].color = new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a * (1f - f));
                    rends[k].enabled = f < 0.98f;
                }
            }
        }
    }

    private void ApplyMissDisappear(float revealLineX, float judgeX)
    {
        for (int s = 0; s < segmentGroups.Count; s++)
        {
            var segs = segmentGroups[s];
            var rends = segmentRendGroups[s];
            float segJudgeX = (hitPositions[s].x + hitPositions[s + 1].x) * 0.5f;
            for (int k = 0; k < segs.Count; k++)
            {
                if (rends[k] == null) continue;
                bool revealed = IsBeyondLine(segs[k].position.x, revealLineX);
                if (!revealed) { rends[k].enabled = false; continue; }

                float past = side == 0 ? (segJudgeX - segs[k].position.x) : (segs[k].position.x - segJudgeX);
                float shrink = past <= 0f ? 1f : 1f - Mathf.Clamp01(past / missShrinkSpan);
                segs[k].localScale = segmentBaseScales[s][k] * shrink;
                rends[k].color = new Color(0.3f, 0.3f, 0.3f, shrink);
                rends[k].enabled = shrink > 0.02f;
            }
        }
    }

    private void TintCharmedNode(int nodeIndex)
    {
        Color col = (charmOwnersByNode != null && nodeIndex >= 0 && nodeIndex < charmOwnersByNode.Length && charmOwnersByNode[nodeIndex] != null)
            ? charmOwnersByNode[nodeIndex].charmColor : new Color(1f, 0.85f, 0.1f);
        if (nodeIndex >= 0 && nodeIndex < nodeRends.Count && nodeRends[nodeIndex] != null)
            nodeRends[nodeIndex].color = col;
    }

    private void ResetAllToDefault()
    {
        for (int i = 0; i < nodeRends.Count; i++)
            if (nodeRends[i] != null) nodeRends[i].sprite = DefaultNode;
        // 连线 sprite 固定为 GlowBar（方案 A），无需重置；颜色在 Tick 中按 segLitFlags 还原
    }

    private void SetAlpha(float a)
    {
        for (int i = 0; i < nodeRends.Count; i++)
        {
            if (nodeRends[i] == null) continue;
            Color c = nodeRends[i].color; c.a = a; nodeRends[i].color = c;
        }
        for (int s = 0; s < segmentRendGroups.Count; s++)
            for (int k = 0; k < segmentRendGroups[s].Count; k++)
            {
                if (segmentRendGroups[s][k] == null) continue;
                Color c = segmentRendGroups[s][k].color; c.a = a; segmentRendGroups[s][k].color = c;
            }
    }

    private void SetBarEnabledAll(bool enabled)
    {
        for (int s = 0; s < segmentRendGroups.Count; s++)
            for (int k = 0; k < segmentRendGroups[s].Count; k++)
                if (segmentRendGroups[s][k] != null) segmentRendGroups[s][k].enabled = enabled;
    }

    private bool IsBeyondLine(float x, float lineX)
    {
        return side == 0 ? x < lineX : x > lineX;
    }

    public bool IsNodeRevealed(int index)
    {
        if (index < 0 || index >= NodeCount || nodeTransforms == null || index >= nodeTransforms.Count || nodeTransforms[index] == null) return false;
        float cx = centerLine != null ? centerLine.currentX : 0f;
        return IsBeyondLine(nodeTransforms[index].position.x, cx);
    }

    public void SkillClearWhole(float xMin, float xMax, ActiveSkillRuntime caster)
    {
        if (caster == null || nodeTransforms == null) return;
        if (state == HoldState.Done) return;

        bool anyInBand = false;
        for (int i = 0; i < NodeCount; i++)
        {
            if (!IsNodeRevealed(i)) continue;
            float x = (hitPositions != null && i < hitPositions.Length) ? hitPositions[i].x : 0f;
            if (x >= xMin && x <= xMax) { anyInBand = true; break; }
        }
        if (!anyInBand) return;

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

        for (int i = 0; i < nodeCount; i++) PlayHitPop(i);
        completedSegments = NodeCount - 1;
        hasLit = true;
        skillCleared = true;
        Complete();
    }

    private bool IsFullyBeyondLine(float x, float halfExtent, float lineX)
    {
        return side == 0 ? x + halfExtent < lineX : x - halfExtent > lineX;
    }

    private bool IsAnyHeldLaneClose(float targetLane)
    {
        if (spawner == null) return false;
        foreach (int held in spawner.heldLanes)
            if (Mathf.Abs(held - targetLane) <= laneTolerance) return true;
        return false;
    }

    private int NodeSpan(int i)
    {
        if (nodeLaneSpans != null && i >= 0 && i < nodeLaneSpans.Length)
            return nodeLaneSpans[i] > 1 ? 2 : 1;
        return laneSpan > 1 ? 2 : 1;
    }

    private bool AreRequiredLanesHeld(float songTime, float reqLaneF)
    {
        if (spawner == null) return false;
        if (laneSpan <= 1) return IsAnyHeldLaneClose(reqLaneF);

        int seg = CurrentSegmentIndex(songTime);
        int fromLane = lanes[seg];
        int toLane = lanes[seg + 1];
        int fromSpan = NodeSpan(seg);
        int toSpan = NodeSpan(seg + 1);

        HashSet<int> validSet = new HashSet<int>();
        for (int x = fromLane; x < fromLane + fromSpan; x++) validSet.Add(x);
        for (int x = toLane; x < toLane + toSpan; x++) validSet.Add(x);
        int lo = Mathf.Min(fromLane, toLane);
        int hi = Mathf.Max(fromLane, toLane);
        for (int x = lo; x <= hi; x++) validSet.Add(x);

        foreach (int held in spawner.heldLanes)
            if (validSet.Contains(held)) return true;
        return false;
    }

    private int CurrentSegmentIndex(float songTime)
    {
        for (int i = 0; i < nodeCount - 1; i++)
            if (songTime >= times[i] && songTime < times[i + 1]) return i;
        return Mathf.Max(0, nodeCount - 2);
    }

    public bool CanStartOnLane(int pressedLane)
    {
        if (lanes == null || lanes.Length == 0) return false;
        int span0 = NodeSpan(0);
        int start = Mathf.Clamp(lanes[0], 0, Mathf.Max(0, spawner != null ? spawner.laneCount - span0 : lanes[0]));
        return pressedLane >= start && pressedLane < start + span0;
    }

    private void Judge()
    {
        float songTime = conductor.songPosition;

        if (state == HoldState.Waiting)
        {
            if (songTime > times[0] + goodWindow)
                MissHead();
            return;
        }

        if (state == HoldState.Holding)
        {
            float reqLaneF = SampleLaneAtTime(songTime);
            if (!isAI)
            {
                bool held = AreRequiredLanesHeld(songTime, reqLaneF);
                if (!held) breakTimer += Time.deltaTime;
                else breakTimer = Mathf.Max(0f, breakTimer - Time.deltaTime * 2f); // 正确按住快速回弹

                if (breakTimer > breakThreshold)
                {
                    Break();
                    return;
                }
            }

            while (completedSegments < nodeCount - 1 && songTime >= times[completedSegments + 1])
            {
                completedSegments++;
                PlayHitPop(completedSegments);
                Vector3 segEndPos = hitPositions[completedSegments];
                onJudge?.Invoke(side, lanes[completedSegments], "CLEAR", segEndPos);
                ResolveCharmedNode(completedSegments, true);
            }

            if (completedSegments >= nodeCount - 1)
                Complete();
        }
    }

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

    public void StartHold(float songTime, bool fromAI)
    {
        if (state != HoldState.Waiting) return;
        state = HoldState.Holding;
        if (fromAI) isAI = true;

        float dt = songTime - times[0];
        float absDt = Mathf.Abs(dt);
        string rank = absDt <= perfectWindow ? "PERFECT" : "GOOD";

        hasLit = true;
        PlayHitPop(0);

        onJudge?.Invoke(side, lanes[0], rank, hitPositions[0]);
        ResolveCharmedNode(0, true);
    }

    private void PlayHitPop(int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= nodeCount) return;
        nodePop[nodeIndex] = 1f;
        if (nodeRends[nodeIndex] != null && SelectNode != null)
            nodeRends[nodeIndex].sprite = SelectNode;
    }

    private void MissHead()
    {
        state = HoldState.Done;
        broken = true;
        fadeTimer = -1f;
        ResetAllToDefault();
        missMode = true;
        ResolveCharmedNode(0, false);
    }

    private void Break()
    {
        state = HoldState.Done;
        broken = true;
        fadeTimer = -1f;
        missMode = true;
        missReported = true;
        int lastNode = Mathf.Max(0, completedSegments);
        onJudge?.Invoke(side, lanes[lastNode], "BREAK", hitPositions[lastNode]);
    }

    private void Complete()
    {
        state = HoldState.Done;
        broken = false;
        fadeTimer = 0f;
        PlayHitPop(nodeCount - 1);
        // 方案 A：完成后整条点亮（颜色在淡出 Tick 中按 segLitFlags 还原为 SegLitColor）
        for (int s = 0; s < segLitFlags.Length; s++) segLitFlags[s] = true;
    }
}
