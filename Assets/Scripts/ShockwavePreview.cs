using UnityEngine;

/// <summary>
/// 双色冲击波预览 / 运行时驱动器。
///
/// 功能（替代旧粉杠 BattleCenterLine 的视觉表现）：
///   - 红墙(RedWall) + 蓝墙(BlueWall) 始终贴合在一起（中缝留 centerGap）。
///   - 整组随 BattleCenterLine.currentX 左右平移（与旧粉杠一致）。
///   - 当分差推动中线移动时，"被推入"一侧的冲击波放大：
///        targetX > currentX（向蓝/+x 移动）-> 红墙放大
///        targetX < currentX（向红/-x 移动）-> 蓝墙放大
///   - 放大倍率由 剩余距离 dist = |currentX - targetX| 决定（查表）。
///   - 放大以"贴中缝的内侧边"为轴心向外扩，保证两墙间隙不变。
///   - 速度随距离变化（距离大快、距离小慢），到终点 dist=0 时速度归最小、平滑回到 1.0 倍。
///   - 停下后若不再次触发移动，逐渐缩小回静止 1.0 倍。
///
/// 形状：直接复用你手动调好的 WALL.fbx 摆放（autoFit=false，脚本不覆盖手动 Transform）。
///       捕获 base 后，所有驱动在 f=1 / 不移动 时完全等于你的手动值，零改动。
/// </summary>
[ExecuteAlways]
public class ShockwavePreview : MonoBehaviour
{
    [Header("场地边界（判定线）")]
    public float leftEdge = -6f;
    public float rightEdge = 6f;

    [Header("稳态中缝 centerX（模拟 BattleCenterLine.currentX；未接 centerLine 时的静态预览）")]
    [Range(-6f, 6f)]
    public float centerX = 0f;

    [Header("中缝对撞缝隙（两墙不贴合，留出空间）")]
    [Tooltip("两墙前沿各从 centerX 向己方退让该值的一半，中间形成缝隙")]
    public float centerGap = 0.2f;

    [Header("开场对撞演示（两波相向从 ±edge 推到 0）")]
    public bool autoClash = false;
    public float clashDuration = 1.5f;
    [Range(0f, 1f)] public float clashProgress = 0f;

    [Header("运行时驱动：接入 BattleCenterLine")]
    [Tooltip("赋值后整组随 currentX 平移、并按 |currentX-targetX| 放大被推入一侧；留空则只用下面的静态/预览参数")]
    public BattleCenterLine centerLine;
    [Tooltip("未接 centerLine 时，用于编辑器内测试放大效果的模拟终点 X（运行时无需管）")]
    public float previewTargetX = 0f;

    [Header("放大/缩小时间模型（按时间缓动，不再按距离算速度）")]
    [Tooltip("变大总时间（秒）：在该时间内从当前倍率涨到目标倍率（加速曲线，越来越快）")]
    public float enlargeTime = 0.35f;
    [Tooltip("变大起始速度（倍/秒）：起步速度，之后越来越快")]
    public float enlargeMinSpeed = 0.6f;
    [Tooltip("变小总时间（秒）：在该时间内从当前倍率缩回 1.0（减速曲线，越来越慢）")]
    public float shrinkTime = 0.5f;
    [Tooltip("变小收尾速度（倍/秒）：结尾仅剩这么快")]
    public float shrinkMinSpeed = 0.3f;

    [Header("补分闪烁（仅 ApplyCatchUp 给劣势方补分时触发，非命中加分）")]
    [Tooltip("闪白强度（写入材质 _Flash 的峰值，0~1）")]
    [Range(0f, 1f)] public float flashIntensity = 1f;
    [Tooltip("闪白一次的总时长（秒）：触发瞬间最亮，平方衰减到 0（单次白闪，不连续眨）")]
    public float flashDuration = 0.3f;

    [Header("越界禁放大（扣血/反弹位移，用户方案）")]
    [Tooltip("自动模式（推荐）：禁放大边界 = ScoreManager.catchUpDiffThreshold × BattleCenterLine.pushPerHit，\n即『触发扣血机制时中线所在位置』。改分差阈值或推进系数后自动跟随，永远不会滞后。")]
    public bool autoSuppressEdge = true;
    [Tooltip("手动兜底边界：仅在自动模式取不到 ScoreManager / BattleCenterLine 时使用（默认 1.5 = 1500 分差 × 0.001）")]
    public float enlargeSuppressEdge = 1.5f;
    [Tooltip("边界判定容差：中线位置 >= 边界-容差 即视为越界。避免中心线正好停在边界值（1.5）时严格大于判定不命中。")]
    public float edgeEpsilon = 1e-3f;

    [Header("放大倍率表（按剩余距离 dist = |currentX - targetX|）")]
    public float scaleAtRest = 1.0f;     // 不动
    public float scaleTiny = 1.05f;      // 0 < dist < 0.2
    public float scaleAbove0_2 = 1.1f;   // >= 0.2
    public float scaleAbove0_3 = 1.2f;   // >= 0.3
    public float scaleAbove0_5 = 1.3f;   // >= 0.5
    public float scaleAbove1_0 = 1.5f;   // >= 1.0

    [Header("（只读/自动维护）真实墙引用")]
    public Transform redWall;
    public Transform blueWall;

    private float _elapsed;

    // 捕获的你手动 base（驱动在 f=1 / 不移动时完全等于这些值，零改动）
    private Vector3 _baseRootPos;
    private Vector3 _redBasePos, _blueBasePos;
    private Quaternion _redBaseRot, _blueBaseRot;
    private Vector3 _redBaseScale, _blueBaseScale;
    private Vector3 _redPivot, _bluePivot;   // 墙 mesh 局部空间里的"贴中缝内侧边"点
    private float _redScale = 1f, _blueScale = 1f;

    // 缩放缓动相位状态（红蓝各自独立）
    private enum ScalePhase { Idle, Enlarge, Shrink }
    private ScalePhase _redPhase = ScalePhase.Idle, _bluePhase = ScalePhase.Idle;
    private float _redPT, _bluePT;            // 相位计时
    private float _redPStart, _bluePStart;    // 相位起点倍率（保留，便于调试）
    private float _redPTarget, _bluePTarget;  // 相位目标倍率
    private float _redV0, _blueV0;            // 相位初速度
    private float _redAcc, _blueAcc;          // 相位加速度（enlarge 为正；shrink 为减速度大小）

    // 扣血/补分机制（ApplyCatchUp）触发时：劣势方冲击波"闪白一次"（由 ScoreManager 直呼 OnScoreAdjustPush 触发）。
    // 该侧"不放大"由下面的【越界禁放大】规则负责（enlargeSuppressEdge），不再用计时冻结。
    private bool _redFlashing = false, _blueFlashing = false;
    private float _redFlashT = 0f, _blueFlashT = 0f;     // 单次闪白衰减计时

    private ScoreManager _scoreManager;                     // 自动边界用（读 catchUpDiffThreshold）

    // 越界禁放大：按"本次位移"（起点=旧targetX，终点=新targetX）判定。只要起点或终点越过边界，
    // 本次位移全程直接压回 1.0；下一帧若 targetX 变化产生新位移，重新判定。无 latch、无超时、不残留。
    private bool _redSuppress = false, _blueSuppress = false;

    // 位移起点跟踪：targetX 只在分数事件时变化；一旦变化就记录本次位移起点 curX。
    // 用途：越界判定（起点或终点任一越界 = 扣血/反弹位移 -> 整段禁放大），起点一锤定音贯穿整段位移。
    private float _prevTargetX;
    private float _dispStartX;

#if UNITY_EDITOR
    [ContextMenu("Reset & Force Refresh")]
    void ResetAndForceRefresh()
    {
        // 不销毁墙（保留你手动调好的 Transform）；仅重新捕获 base 并刷新表现
        EnsureWalls();
        ApplyToWalls();
        CaptureBase();
        Debug.Log("[ShockwavePreview] 已按当前手动 Transform 重新捕获 base 并刷新（非破坏性）", this);
    }

    [ContextMenu("Recapture Base Transforms")]
    void RecaptureBaseMenu() { CaptureBase(); Debug.Log("[ShockwavePreview] 已重新捕获 RedWall/BlueWall 与根节点的手动 Transform 作为 base", this); }
#endif

    void OnValidate() { EnsureWalls(); ApplyToWalls(); }
    void Awake() { EnsureWalls(); ApplyToWalls(); CaptureBase(); }

    void Start()
    {
        // 自动查找 BattleCenterLine（驱动平移/放大用；用户忘记拖拽时兜底）
        if (centerLine == null)
            centerLine = FindFirstObjectByType<BattleCenterLine>();

        EnsureWalls();
        ApplyToWalls();
        CaptureBase();

        // 初始化位移起点跟踪：起点 = 位移前的目标位置；初始无位移，起点与终点同为当前 targetX
        float t0 = (centerLine != null) ? centerLine.targetX : previewTargetX;
        _prevTargetX = t0;
        _dispStartX = t0;
    }

    void OnDestroy() { }

    void Update()
    {
        if (autoClash)
        {
            _elapsed += Time.deltaTime;
            clashProgress = Mathf.PingPong(_elapsed / clashDuration, 1f);
        }
        EnsureWalls();
        ApplyToWalls();
        DriveRuntime();
    }

    // ---- 运行时驱动：平移 + 距离驱动放大 ----

    private void DriveRuntime()
    {
        // 运行时兜底：centerLine 若此前未解析到，这里持续尝试
        if (centerLine == null) centerLine = FindFirstObjectByType<BattleCenterLine>();

        // 编辑器静止（未接 centerLine 且未设 previewTargetX）时，保留你手动 Transform，不驱动。
        bool previewing = (centerLine != null) || (previewTargetX != 0f);
        if (!Application.isPlaying && !previewing) return;

        float curX = (centerLine != null) ? centerLine.currentX : centerX;
        float tgtX = (centerLine != null) ? centerLine.targetX : previewTargetX;

        // ① 检测新位移：targetX 只在分数事件时变化；一旦变化就记录本次位移的起点。
        //    起点定义（用户明确）：= 本次位移开始前的【目标位置】（分数决定的位置），不是屏幕上 lerp 滞后的视觉位置 curX。
        //    例：扣血触发时中线在 -3.3（界外）-> 加分后目标变 -3.0 -> 本次位移起点即 -3.3（越界，判定命中）。
        if (Mathf.Abs(tgtX - _prevTargetX) > 1e-4f)
        {
            _dispStartX = _prevTargetX;   // 起点 = 位移前的目标位置（旧 targetX）
            _prevTargetX = tgtX;
        }

        // 1) 整组随 currentX 平移（仅运行时接管根的 X，编辑器里保留你手动根位置）
        if (centerLine != null && Application.isPlaying)
        {
            transform.position = new Vector3(_baseRootPos.x + curX, _baseRootPos.y, _baseRootPos.z);
        }

        float dt = Time.deltaTime;

        // 2) 距离决定目标倍率与方向；放大/缩小按时间缓动（加速变大 / 减速变小）
        float dist = Mathf.Abs(curX - tgtX);
        float targetMag = LookupScale(dist);

        // 方向：向蓝(+x) -> 红放大；向红(-x) -> 蓝放大；不动 -> 都 1.0
        float redTarget = (tgtX > curX + 1e-4f) ? targetMag : 1f;
        float blueTarget = (tgtX < curX - 1e-4f) ? targetMag : 1f;

        // ② 越界禁放大（用户方案）：本次位移的【起点】或【终点】越过 ±edge
        //    -> 该位移必是扣血/反弹位移（正常博弈位移都在范围内，因为超过就触发扣血被弹回）-> 禁止放大。
        //    边界自动跟随扣血触发位置：edge = 分差阈值 × 中线推进系数（改阈值/系数不再滞后）。
        //    判定按"本次位移"实时覆盖（无 latch、无超时）：targetX 每变化一次即刷新 _dispStartX，
        //    全程只取决于当前位移的起点/终点是否越界；一旦产生新位移就重新判定，不会被旧 latch 拖住。
        //    被推入侧 = 移动方向指向的那一侧：向 +x 推 -> 红；向 -x 推 -> 蓝。
        float edge = GetSuppressEdge();
        bool startOut = BeyondEdge(_dispStartX, edge);
        bool endOut   = BeyondEdge(tgtX, edge);
        bool dispOutOfRange = startOut || endOut;

        _redSuppress  = dispOutOfRange && (tgtX > curX + 1e-4f);
        _blueSuppress = dispOutOfRange && (tgtX < curX - 1e-4f);

        if (_redSuppress) redTarget = 1f;
        if (_blueSuppress) blueTarget = 1f;


        // ③ 闪白：仅由扣血/补分机制（ScoreManager.ApplyCatchUp 直呼）触发，单次衰减到 0
        UpdateFlash(ref _redFlashing, ref _redFlashT, redWall, dt);
        UpdateFlash(ref _blueFlashing, ref _blueFlashT, blueWall, dt);

        // ④ 放大/缩小：正常博弈照常；越界位移因 redTarget/blueTarget 已压回 1.0 而不会放大
        AnimateScale(ref _redScale, ref _redPhase, ref _redPT, ref _redPStart, ref _redPTarget, ref _redV0, ref _redAcc,
                     redTarget, enlargeTime, enlargeMinSpeed, shrinkTime, shrinkMinSpeed, dt);
        AnimateScale(ref _blueScale, ref _bluePhase, ref _bluePT, ref _bluePStart, ref _bluePTarget, ref _blueV0, ref _blueAcc,
                     blueTarget, enlargeTime, enlargeMinSpeed, shrinkTime, shrinkMinSpeed, dt);

        // 仅在"正在过渡或放大中"才写墙 Transform，静止（f=1 且 dist=0）时不动，避免覆盖手动编辑
        bool active = dist > 1e-4f
            || Mathf.Abs(_redScale - 1f) > 1e-3f
            || Mathf.Abs(_blueScale - 1f) > 1e-3f;
        if (!active) return;

        ApplyScale(redWall, _redBasePos, _redBaseRot, _redBaseScale, _redPivot, _redScale);
        ApplyScale(blueWall, _blueBasePos, _blueBaseRot, _blueBaseScale, _bluePivot, _blueScale);
    }

    /// <summary>扣血/补分机制触发：让被补分那一侧（side 0=红墙，1=蓝墙）冲击波"闪白一次"。
    /// 由 ScoreManager.ApplyCatchUp 直呼。该侧"不放大"由 DriveRuntime 的越界禁放大规则负责（按位移起点/终点判定）。</summary>
    public void OnScoreAdjustPush(int side)
    {
        if (side == 0)
        {
            if (!_redFlashing) _redFlashT = 0f;   // 上一次闪完后再触发才重置，避免中途打断重来
            _redFlashing = true;
        }
        else
        {
            if (!_blueFlashing) _blueFlashT = 0f;
            _blueFlashing = true;
        }
    }

    /// <summary>单次闪白：触发瞬间最亮(flashIntensity)，在 flashDuration 内平方衰减到 0，写进材质 _Flash；结束后自动关闭并清零。</summary>
    private void UpdateFlash(ref bool flashing, ref float ft, Transform wall, float dt)
    {
        if (!flashing) return;
        ft += dt;
        float k = 1f - Mathf.Clamp01(ft / flashDuration);
        float v = flashIntensity * k * k;   // 平方衰减，收尾更柔（避免硬切）
        SetWallFlash(wall, v);
        if (ft >= flashDuration) { flashing = false; SetWallFlash(wall, 0f); }
    }

    private void SetWallFlash(Transform wall, float v)
    {
        if (wall == null) return;
        var mr = wall.GetComponent<MeshRenderer>();
        if (mr != null && mr.sharedMaterial != null) mr.sharedMaterial.SetFloat("_Flash", v);
    }

    /// <summary>禁放大边界：自动模式 = 分差阈值 × 中线推进系数（= 触发扣血机制时中线所在位置），改阈值/系数自动跟随。</summary>
    private float GetSuppressEdge()
    {
        if (!autoSuppressEdge) return enlargeSuppressEdge;
        if (_scoreManager == null) _scoreManager = FindFirstObjectByType<ScoreManager>();
        if (centerLine == null) centerLine = FindFirstObjectByType<BattleCenterLine>();
        if (_scoreManager != null && centerLine != null)
            return Mathf.Max(0.01f, _scoreManager.catchUpDiffThreshold * centerLine.pushPerHit);
        return enlargeSuppressEdge;
    }

    /// <summary>是否达到/越过边界（含容差，避免正好停在边界值时不命中）。</summary>
    private bool BeyondEdge(float x, float edge) { return Mathf.Abs(x) >= edge - edgeEpsilon; }

    /// <summary>
    /// 单墙缩放缓动（红蓝各自独立调用）。
    /// - 需要变大（desiredTarget &gt; 当前）：加速曲线（起步 enlargeMinSpeed，越来越快），约 enlargeTime 内涨到目标；
    ///   期间若 desiredTarget 进一步增大（距离继续拉大跨过阈值），以当前大小为起点重新加速续接。
    /// - 需要变小（desiredTarget &lt; 当前）：减速曲线（起步快，收尾到 shrinkMinSpeed），约 shrinkTime 内缩回 1.0。
    /// - 相等：保持。
    /// </summary>
    private void AnimateScale(ref float scale, ref ScalePhase phase, ref float pt, ref float pStart, ref float pTarget,
        ref float v0, ref float acc, float desiredTarget,
        float enlTime, float enlMin, float shrTime, float shrMin, float dt)
    {
        if (desiredTarget > scale + 1e-4f)
        {
            // 进入/续接变大：以当前大小为起点重启加速 ease
            if (phase != ScalePhase.Enlarge || desiredTarget > pTarget + 1e-4f)
            {
                phase = ScalePhase.Enlarge;
                pt = 0f;
                pStart = scale;
                pTarget = desiredTarget;
                float D = Mathf.Max(1e-5f, desiredTarget - scale);
                float a = 2f * (D - enlMin * enlTime) / (enlTime * enlTime);
                acc = Mathf.Max(0f, a);   // 起步即 enlMin，之后越来越快；若 enlMin 已够快则夹 0（恒速）
                v0 = enlMin;
            }
            pt += dt;
            float v = v0 + acc * pt;
            scale += v * dt;
            if (scale >= pTarget) { scale = pTarget; phase = ScalePhase.Idle; }
        }
        else if (desiredTarget < scale - 1e-4f)
        {
            // 进入变小：减速曲线，从当前大小缩回 1.0
            if (phase != ScalePhase.Shrink)
            {
                phase = ScalePhase.Shrink;
                pt = 0f;
                pStart = scale;
                pTarget = 1f;
                float D = Mathf.Max(1e-5f, scale - 1f);
                float d = 2f * (D - shrMin * shrTime) / (shrTime * shrTime);
                acc = Mathf.Max(0f, d);             // 减速度大小
                v0 = shrMin + acc * shrTime;        // 起步快，结尾收到 shrMin
            }
            pt += dt;
            float v = Mathf.Max(0f, v0 - acc * pt);
            scale -= v * dt;
            if (scale <= 1f) { scale = 1f; phase = ScalePhase.Idle; }
        }
        else
        {
            phase = ScalePhase.Idle;
        }
    }

    /// <summary>绕墙 mesh 局部内侧边 pivot 缩放：保持中缝侧边不动，向外扩，间隙不变。</summary>
    private void ApplyScale(Transform wall, Vector3 basePos, Quaternion baseRot, Vector3 baseScale, Vector3 meshPivot, float f)
    {
        if (wall == null) return;
        wall.localScale = new Vector3(baseScale.x * f, baseScale.y * f, baseScale.z * f);
        Vector3 offset = baseRot * new Vector3(baseScale.x * meshPivot.x, baseScale.y * meshPivot.y, baseScale.z * meshPivot.z);
        wall.localPosition = basePos + offset * (1f - f);
    }

    private float LookupScale(float d)
    {
        if (d <= 1e-4f) return scaleAtRest;
        if (d < 0.2f) return scaleTiny;
        if (d < 0.3f) return scaleAbove0_2;
        if (d < 0.5f) return scaleAbove0_3;
        if (d < 1.0f) return scaleAbove0_5;
        return scaleAbove1_0;
    }

    /// <summary>捕获你手动调好的 Transform 作为 base；并自动算出每堵墙"贴中缝内侧边"在 mesh 局部空间的坐标。</summary>
    private void CaptureBase()
    {
        _baseRootPos = transform.position;
        if (redWall != null)
        {
            var mf = redWall.GetComponent<MeshFilter>();
            _redBasePos = redWall.localPosition;
            _redBaseRot = redWall.localRotation;
            _redBaseScale = redWall.localScale;
            _redPivot = (mf != null && mf.sharedMesh != null) ? GetInnerEdgeMeshLocal(mf, true) : Vector3.zero;
        }
        if (blueWall != null)
        {
            var mf = blueWall.GetComponent<MeshFilter>();
            _blueBasePos = blueWall.localPosition;
            _blueBaseRot = blueWall.localRotation;
            _blueBaseScale = blueWall.localScale;
            _bluePivot = (mf != null && mf.sharedMesh != null) ? GetInnerEdgeMeshLocal(mf, false) : Vector3.zero;
        }
        _redScale = 1f; _blueScale = 1f;
        _redPhase = ScalePhase.Idle; _bluePhase = ScalePhase.Idle;
        _redFlashing = false; _blueFlashing = false; _redFlashT = 0f; _blueFlashT = 0f;
        _redSuppress = false; _blueSuppress = false;
        float t0 = (centerLine != null) ? centerLine.targetX : previewTargetX;
        _prevTargetX = t0; _dispStartX = t0;
    }

    /// <summary>自动找墙 mesh 局部坐标里、落在"朝中缝一侧"极值处的顶点（红墙=世界 +X 极值，蓝墙=世界 -X 极值）。</summary>
    private Vector3 GetInnerEdgeMeshLocal(MeshFilter mf, bool isRed)
    {
        var b = mf.sharedMesh.bounds;
        Vector3[] corners =
        {
            new Vector3(b.min.x, b.min.y, b.min.z),
            new Vector3(b.min.x, b.min.y, b.max.z),
            new Vector3(b.min.x, b.max.y, b.min.z),
            new Vector3(b.min.x, b.max.y, b.max.z),
            new Vector3(b.max.x, b.min.y, b.min.z),
            new Vector3(b.max.x, b.min.y, b.max.z),
            new Vector3(b.max.x, b.max.y, b.min.z),
            new Vector3(b.max.x, b.max.y, b.max.z),
        };
        Transform t = mf.transform;
        float bestX = isRed ? float.MinValue : float.MaxValue;
        Vector3 best = corners[0];
        foreach (var c in corners)
        {
            float wx = t.TransformPoint(c).x;
            if (isRed ? wx > bestX : wx < bestX) { bestX = wx; best = c; }
        }
        // 只用 X 极值作为锚点（y/z 归零），放大时只沿 X 向外扩，不产生上下/前后漂移
        return new Vector3(best.x, 0f, 0f);
    }

    private void EnsureWalls()
    {
        if (redWall == null)
            redWall = FindBestWallChild("RedWall", ShockwaveMeshGenerator.Side.Red);
        if (blueWall == null)
            blueWall = FindBestWallChild("BlueWall", ShockwaveMeshGenerator.Side.Blue);
    }

    /// <summary>
    /// 按名字查找现有子物体作为墙；若找不到则创建一个默认的。
    /// 若存在多个同名子物体，优先返回带 MeshFilter 且 sharedMesh 非空的那个（即你手动摆的 WALL.fbx），
    /// 避免之前脚本误创建的空对象抢走引用。
    /// </summary>
    private Transform FindBestWallChild(string name, ShockwaveMeshGenerator.Side side)
    {
        Transform fallback = null;
        Transform withMesh = null;
        int count = 0;
        for (int i = 0; i < transform.childCount; i++)
        {
            var c = transform.GetChild(i);
            if (c.name != name) continue;
            count++;
            if (fallback == null) fallback = c;
            var mf = c.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                if (withMesh == null) withMesh = c;
            }
        }

        if (count > 1)
        {
            Debug.LogWarning($"[ShockwavePreview] 发现 {count} 个名为 '{name}' 的子物体。"
                + $"已优先使用 {(withMesh != null ? "带 Mesh" : "第一个")}。"
                + "建议 Hierarchy 里只保留一个手动调好的墙，删除脚本误创建的空对象。", this);
        }

        if (withMesh != null) return withMesh;
        if (fallback != null) return fallback;

        // 没有就创建一个默认的
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var mg = go.AddComponent<ShockwaveMeshGenerator>();
        mg.side = side;
        if (side == ShockwaveMeshGenerator.Side.Red)
        {
            mg.colorDeep = new Color(0.55f, 0.08f, 0.15f);
            mg.colorTip  = new Color(1.0f, 0.38f, 0.42f);
        }
        else
        {
            mg.colorDeep = new Color(0.25f, 0.55f, 1.0f);
            mg.colorTip  = new Color(0.90f, 1.0f, 1.0f);
        }
        return go.transform;
    }

    private float GetSeamX()
    {
        if (centerLine != null) return centerLine.currentX;
        return centerX;
    }

    private void GetFronts(out float rFront, out float bFront)
    {
        float seam = GetSeamX();
        float half = centerGap * 0.5f;
        if (autoClash)
        {
            rFront = Mathf.Lerp(leftEdge, -half, clashProgress);
            bFront = Mathf.Lerp(rightEdge, half, clashProgress);
        }
        else
        {
            rFront = seam - half;
            bFront = seam + half;
        }
    }

    private void ApplyToWalls()
    {
        EnsureWalls();
        GetFronts(out float rFront, out float bFront);

        var rm = redWall.GetComponent<ShockwaveMeshGenerator>();
        rm.backX = leftEdge;
        rm.frontX = rFront;
        rm.Refresh();

        var bm = blueWall.GetComponent<ShockwaveMeshGenerator>();
        bm.backX = rightEdge;
        bm.frontX = bFront;
        bm.Refresh();
    }

    void OnDrawGizmos()
    {
        GetFronts(out float rFront, out float bFront);
        Gizmos.color = Color.white;
        Gizmos.DrawLine(new Vector3((rFront + bFront) * 0.5f, 0f, -3.75f),
                        new Vector3((rFront + bFront) * 0.5f, 1.5f, 3.75f));
    }
}
