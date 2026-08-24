using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 长按音符（按住并沿连接线滑动）的运行时组件。
/// 由 NoteSpawner 在生成 Hold 音符时创建，并注入所有运动/判定参数。
///
/// 视觉：
/// - head / tail 两个黑色圆柱体，中间用一条黑色连接 bar 相连。
/// - 命中 head 后 head 变白；按住期间 bar 从 head 向 tail 一路变白。
/// - 完成（tail 抵达判定线）后 tail 变白并整体淡出。
/// - 断开 / 漏击时直接淡出。
///
/// 判定状态机（由自身 Update 驱动，结果通过 onJudge 回调上报给 NoteSpawner）：
/// - Waiting：等待起始音符(head)抵达判定线。若 head 时间窗内没有按下 -> MISS。
/// - Holding：head 命中后进入。按进度插值出“当前所需音轨”，要求该音轨被按住；
///   跨轨时玩家需沿连接线滑动跟随。所需音轨超过 0.2s 未被按住 -> 断连 MISS。
///   到达 tailTime（结束音符抵达判定线）-> 完成 CLEAR。
/// - Done：最终态，淡出后由发射器清理。
/// </summary>
public class HoldNote : MonoBehaviour
{
    // ===== 由 NoteSpawner 注入的配置 =====
    public NoteSpawner spawner;
    public int side;
    public int headLane;
    public int tailLane;
    public float headTime;
    public float tailTime;
    public float leadTime;
    public float noteRadius = 0.45f;
    public float goodWindow = 0.07f;
    public float perfectWindow = 0.03f;
    public Vector3 spawnPosHead;
    public Vector3 hitPosHead;
    public Vector3 spawnPosTail;
    public Vector3 hitPosTail;
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
    private Transform head, tail;
    private MeshRenderer headRend, tailRend;
    private Material headMat, tailMat;

    private List<Transform> barSegments = new List<Transform>();
    private List<MeshRenderer> barSegmentRends = new List<MeshRenderer>();
    private List<Material> barSegmentMats = new List<Material>();
    private const int BarSegmentCount = 16;

    private Vector3 baseHead, baseTail;
    private float baseBarSegmentRadius;
    private Vector3 exitPosHead, exitPosTail;
    private float exitLeadTimeHead, exitLeadTimeTail;
    private float rideY;
    private bool visible = false;
    private float breakTimer = 0f;
    private float fadeTimer = -1f;

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
        if (headMat != null)
        {
            Color c = headMat.color; c.a = a; headMat.color = c;
        }
        if (tailMat != null)
        {
            Color c = tailMat.color; c.a = a; tailMat.color = c;
        }
        for (int i = 0; i < barSegmentMats.Count; i++)
        {
            if (barSegmentMats[i] == null) continue;
            Color c = barSegmentMats[i].color; c.a = a; barSegmentMats[i].color = c;
        }
    }

    private void ResetBarToBlack()
    {
        for (int i = 0; i < barSegmentMats.Count; i++)
        {
            if (barSegmentMats[i] == null) continue;
            barSegmentMats[i].color = Color.black;
        }
    }

    void Start()
    {
        BuildVisuals();
    }

    private void BuildVisuals()
    {
        // head
        var headGo = new GameObject("HoldHead");
        head = headGo.transform;
        head.SetParent(transform, false);
        var hmf = headGo.AddComponent<MeshFilter>(); hmf.sharedMesh = CylinderMesh;
        headRend = headGo.AddComponent<MeshRenderer>();
        headRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        headRend.receiveShadows = false;
        headMat = CreateColoredMaterial(Color.black);
        headRend.material = headMat;

        // tail
        var tailGo = new GameObject("HoldTail");
        tail = tailGo.transform;
        tail.SetParent(transform, false);
        var tmf = tailGo.AddComponent<MeshFilter>(); tmf.sharedMesh = CylinderMesh;
        tailRend = tailGo.AddComponent<MeshRenderer>();
        tailRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tailRend.receiveShadows = false;
        tailMat = CreateColoredMaterial(Color.black);
        tailRend.material = tailMat;

        // 连接 bar：拆成多段圆柱，便于按住时从 head 向 tail 逐段变白
        float r = noteRadius / 0.5f;
        baseHead = new Vector3(r, 0.12f, r);
        baseTail = new Vector3(r, 0.12f, r);
        head.localScale = baseHead;
        tail.localScale = baseTail;

        baseBarSegmentRadius = r * 0.18f; // 细连接线：直径约为音符的 36%
        for (int i = 0; i < BarSegmentCount; i++)
        {
            var segGo = new GameObject($"HoldBarSeg_{i}");
            var segT = segGo.transform;
            segT.SetParent(transform, false);
            var smf = segGo.AddComponent<MeshFilter>(); smf.sharedMesh = CylinderMesh;
            var sRend = segGo.AddComponent<MeshRenderer>();
            sRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sRend.receiveShadows = false;
            var sMat = CreateColoredMaterial(Color.black);
            sRend.material = sMat;
            segT.localScale = new Vector3(baseBarSegmentRadius, 1f, baseBarSegmentRadius);
            barSegments.Add(segT);
            barSegmentRends.Add(sRend);
            barSegmentMats.Add(sMat);
        }

        // 预计算越过判定线的终点（再往前 1.5 单位），与 NoteMover 一致
        Vector3 dirHead = (hitPosHead - spawnPosHead).normalized;
        exitPosHead = hitPosHead + dirHead * 1.5f;
        float distHead = Vector3.Distance(spawnPosHead, hitPosHead);
        exitLeadTimeHead = leadTime * ((distHead + 1.5f) / Mathf.Max(distHead, 0.001f));

        Vector3 dirTail = (hitPosTail - spawnPosTail).normalized;
        exitPosTail = hitPosTail + dirTail * 1.5f;
        float distTail = Vector3.Distance(spawnPosTail, hitPosTail);
        exitLeadTimeTail = leadTime * ((distTail + 1.5f) / Mathf.Max(distTail, 0.001f));

        rideY = hitPosHead.y;

        headRend.enabled = false;
        tailRend.enabled = false;
        SetBarEnabled(false);
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
        float songTime = conductor.songPosition;

        if (fadeTimer < 0f)
        {
            // 正常移动
            float tHead = Mathf.Clamp01((songTime - (headTime - leadTime)) / exitLeadTimeHead);
            head.position = Vector3.Lerp(spawnPosHead, exitPosHead, tHead);
            head.position = new Vector3(head.position.x, rideY, head.position.z);

            float tTail = Mathf.Clamp01((songTime - (tailTime - leadTime)) / exitLeadTimeTail);
            tail.position = Vector3.Lerp(spawnPosTail, exitPosTail, tTail);
            tail.position = new Vector3(tail.position.x, rideY, tail.position.z);

            // 连接 bar：多段圆柱从 head 排到 tail
            UpdateBarSegments();

            // 长按进度：决定 bar 已经变白到哪一段
            UpdateBarColor(songTime);

            // 可见性：按元素"前沿"是否越过粉杠决定显示，避免长 segment 中心越过后
            // 后半段还戳在粉杠前面，导致长 Hold 看起来"隐身失效"。
            float cx = centerLine != null ? centerLine.currentX : 0f;
            visible = true;

            if (headRend != null) headRend.enabled = IsBeyondLine(head.position.x, cx);
            if (tailRend != null) tailRend.enabled = IsBeyondLine(tail.position.x, cx);

            for (int i = 0; i < barSegments.Count; i++)
            {
                if (barSegmentRends[i] == null) continue;
                barSegmentRends[i].enabled = IsSegmentLeadingBeyondLine(i, cx);
            }
        }
        else
        {
            // 收尾（完成 / 断连 / 漏击）：音符继续按原速度移动，
            // 用"判定线 judgeLineX"作为固定的消失边界——已越过判定线的部分（从头/最越线端）被裁掉，
            // 未越线的部分保持显示，于是自然形成"从头向尾逐渐消失"，且只发生在判定线那一侧。
            // 收尾第一帧把 bar 重置为黑色，避免白色命中反馈久久残留。
            if (fadeTimer <= 0f)
            {
                ResetBarToBlack();
            }
            fadeTimer += Time.deltaTime;

            float tHead = Mathf.Clamp01((songTime - (headTime - leadTime)) / exitLeadTimeHead);
            head.position = Vector3.Lerp(spawnPosHead, exitPosHead, tHead);
            head.position = new Vector3(head.position.x, rideY, head.position.z);

            float tTail = Mathf.Clamp01((songTime - (tailTime - leadTime)) / exitLeadTimeTail);
            tail.position = Vector3.Lerp(spawnPosTail, exitPosTail, tTail);
            tail.position = new Vector3(tail.position.x, rideY, tail.position.z);

            UpdateBarSegments();

            float judgeX = judgeLineX;
            if (headRend != null) headRend.enabled = !IsBeyondLine(head.position.x, judgeX);
            if (tailRend != null) tailRend.enabled = !IsBeyondLine(tail.position.x, judgeX);
            for (int i = 0; i < barSegments.Count; i++)
            {
                if (barSegmentRends[i] == null) continue;
                barSegmentRends[i].enabled = !IsSegmentLeadingBeyondLine(i, judgeX);
            }

            // 完成：整体透明度淡出，让 tail 白反馈自然消失；
            // 断连/漏击：保持不透明，只通过判定线裁剪让音符"正常飞过终点后逐渐消失"，避免留下地面阴影。
            if (!broken)
            {
                float alpha = 1f - Mathf.Clamp01(fadeTimer / fadeDuration);
                SetAlpha(alpha);
            }

            // 结束条件：整根越过判定线，或超过最大存活时间
            bool allGone = side == 0 ? tail.position.x < judgeX : tail.position.x > judgeX;
            if (allGone || fadeTimer > maxFadeLife)
            {
                finished = true;
                Destroy(gameObject);
            }
        }
    }

    private void UpdateBarSegments()
    {
        Vector3 dir = tail.position - head.position;
        float totalLen = dir.magnitude;
        if (totalLen < 0.001f)
        {
            for (int i = 0; i < barSegments.Count; i++)
            {
                barSegments[i].position = head.position;
                barSegments[i].localScale = new Vector3(barSegments[i].localScale.x, 0.001f, barSegments[i].localScale.z);
            }
            return;
        }

        Vector3 normDir = dir.normalized;
        // 让 bar 实际从 head 圆柱边缘开始、到 tail 圆柱边缘结束，不要穿过圆柱内部
        float headEdgeOffset = noteRadius;
        float tailEdgeOffset = noteRadius;
        float usableLen = Mathf.Max(0f, totalLen - headEdgeOffset - tailEdgeOffset);
        Vector3 start = head.position + normDir * headEdgeOffset;
        Vector3 end = tail.position - normDir * tailEdgeOffset;

        // 每段长度，留 5% 重叠避免裂缝
        int n = barSegments.Count;
        float segLen = n > 1 ? usableLen / n * 1.05f : usableLen;
        for (int i = 0; i < n; i++)
        {
            float p = (i + 0.5f) / n;
            barSegments[i].position = Vector3.Lerp(start, end, p);
            barSegments[i].rotation = Quaternion.FromToRotation(Vector3.up, normDir);
            barSegments[i].localScale = new Vector3(baseBarSegmentRadius, segLen, baseBarSegmentRadius);
        }
    }

    private void UpdateBarColor(float songTime)
    {
        if (state != HoldState.Holding) return;

        float prog = Mathf.Clamp01((songTime - headTime) / Mathf.Max(tailTime - headTime, 0.0001f));
        int litCount = Mathf.FloorToInt(prog * BarSegmentCount);
        for (int i = 0; i < BarSegmentCount; i++)
        {
            if (barSegmentMats[i] == null) continue;
            barSegmentMats[i].color = i < litCount ? Color.white : Color.black;
        }
    }

    private void SetBarEnabled(bool enabled)
    {
        for (int i = 0; i < barSegmentRends.Count; i++)
        {
            if (barSegmentRends[i] != null) barSegmentRends[i].enabled = enabled;
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

    /// <summary>
    /// 第 i 段 bar 的"前沿"是否已经越过指定竖直线。
    /// 使用 segment 在 x 轴上的实际范围（而非仅中心点），让裁剪边界更贴近粉杠/判定线。
    /// </summary>
    private bool IsSegmentLeadingBeyondLine(int i, float lineX)
    {
        if (i < 0 || i >= barSegments.Count || barSegmentRends[i] == null) return false;

        Vector3 dir = tail.position - head.position;
        float totalLen = dir.magnitude;
        if (totalLen < 0.001f)
        {
            return IsBeyondLine(barSegments[i].position.x, lineX);
        }

        Vector3 normDir = dir.normalized;
        float halfLenX = (barSegments[i].localScale.y * 0.5f) * Mathf.Abs(normDir.x);
        float minX = barSegments[i].position.x - halfLenX;
        float maxX = barSegments[i].position.x + halfLenX;

        // side==0 音符向左飞，前沿是 segment 的左端（minX）；
        // side==1 音符向右飞，前沿是 segment 的右端（maxX）。
        return side == 0 ? minX < lineX : maxX > lineX;
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

    /// <summary>
    /// 判定状态机。由自身 Update 每帧调用。
    /// </summary>
    private void Judge()
    {
        float songTime = conductor.songPosition;

        if (state == HoldState.Waiting)
        {
            if (songTime > headTime + goodWindow)
            {
                MissHead();
            }
            return;
        }

        if (state == HoldState.Holding)
        {
            float prog = Mathf.Clamp01((songTime - headTime) / Mathf.Max(tailTime - headTime, 0.0001f));
            float reqLaneF = Mathf.Lerp(headLane, tailLane, prog);

            // 按住检测：当前"所需轨道"是一个浮点插值；只要玩家按住的某个轨道在容差范围内即算命中。
            // 这样跨轨 Hold 不需要精确对准某个整数轨道，拖动时更宽容。
            if (!isAI)
            {
                bool held = spawner != null && IsAnyHeldLaneClose(reqLaneF);
                if (!held) breakTimer += Time.deltaTime;
                else breakTimer = 0f;

                if (breakTimer > 0.2f)
                {
                    Break();
                    return;
                }
            }

            // 结束音符抵达判定线 -> 完成
            if (songTime >= tailTime)
            {
                Complete();
            }
        }
    }

    /// <summary>
    /// 由 NoteSpawner 在对应轨道按下且处于 head 时间窗内时调用，起手长按。
    /// </summary>
    public void StartHold(float songTime, bool fromAI)
    {
        if (state != HoldState.Waiting) return;
        state = HoldState.Holding;
        if (fromAI) isAI = true;

        float dt = songTime - headTime;
        float absDt = Mathf.Abs(dt);
        string rank = absDt <= perfectWindow ? "PERFECT" : "GOOD";

        // 命中反馈：head 变白（与普通音符命中一致）
        if (headMat != null) headMat.color = Color.white;

        onJudge?.Invoke(side, headLane, rank, hitPosHead);
    }

    private void MissHead()
    {
        state = HoldState.Done;
        broken = true;
        fadeTimer = 0f;
        // 漏击/断连时立即把命中反馈（白色 head、已变白 bar）恢复黑色，避免残留白色残影
        if (headMat != null) headMat.color = Color.black;
        ResetBarToBlack();
        onJudge?.Invoke(side, headLane, "MISS", hitPosHead);
    }

    private void Break()
    {
        state = HoldState.Done;
        broken = true;
        fadeTimer = 0f;
        if (headMat != null) headMat.color = Color.black;
        ResetBarToBlack();
        onJudge?.Invoke(side, headLane, "MISS", hitPosHead);
    }

    private void Complete()
    {
        state = HoldState.Done;
        broken = false;
        fadeTimer = 0f;
        // 完成时 tail 也变白
        if (tailMat != null) tailMat.color = Color.white;
        onJudge?.Invoke(side, tailLane, "CLEAR", hitPosTail);
    }
}
