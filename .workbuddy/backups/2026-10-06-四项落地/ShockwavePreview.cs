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

    [Header("缩放节奏：弹簧阻尼（推荐，替代下面固定时间缓动）")]
    [Tooltip("总开关：开启用弹簧阻尼（有惯性的肉体感）；关闭则回退到旧的固定时间缓动（用下面那组参数）")]
    public bool useSpringModel = true;
    [Tooltip("弹簧刚度：越大越硬、追得越快；越小越软、越有惯性")]
    public float springStiffness = 80f;
    [Tooltip("阻尼比：0=来回震荡，1=临界阻尼（最快且不过冲），>1=迟缓")]
    [Range(0f, 2f)] public float springDampingRatio = 0.9f;
    [Tooltip("速度上限（倍/秒），防止低帧率下弹簧过激")]
    public float springMaxVelocity = 8f;

    [Header("放大保持（移动中 2s 内不变小）")]
    [Tooltip("保持时长（秒）：只要该侧在前进/被推入，就刷新为这个值；期间目标倍率只维持或变大，绝不下调。\n用于修正『扣血补分把优势方推回 -> 它立刻缩小』的异常表现。")]
    public float enlargeHoldTime = 2f;
    [Tooltip("刷新阈值：目标倍率 >= 该值即视为『在前进』，刷新保持时间")]
    public float holdRefreshThreshold = 1.05f;

    [Header("放大/缩小时间模型（旧：固定时间缓动，useSpringModel 关闭时生效）")]
    [Tooltip("变大总时间（秒）：在该时间内从当前倍率涨到目标倍率（减速曲线：起步最快，越接近目标越慢）")]
    public float enlargeTime = 0.35f;
    [Tooltip("变大收尾速度（倍/秒）：结尾仅剩这么慢（起步速度由『时长』与『收尾速度』反推，恒为最快）")]
    public float enlargeMinSpeed = 0.6f;
    [Tooltip("变小总时间（秒）：在该时间内从当前倍率缩回 1.0（加速曲线：起步最慢，越接近静止越快）")]
    public float shrinkTime = 0.5f;
    [Tooltip("变小起步速度（倍/秒）：起步仅这么慢，之后越来越快（撞击回位瞬间最快）")]
    public float shrinkMinSpeed = 0.3f;

    [Header("补分闪烁（仅 ApplyCatchUp 给劣势方补分时触发，非命中加分）")]
    [Tooltip("闪白强度（写入材质 _Flash 的峰值，0~1）")]
    [Range(0f, 1f)] public float flashIntensity = 1f;
    [Tooltip("闪白一次的总时长（秒）：触发瞬间最亮，平方衰减到 0（单次白闪，不连续眨）")]
    public float flashDuration = 0.3f;

    [Header("对峙循环（P4 ④.1：僵持时的互顶开合呼吸）")]
    [Tooltip("总开关：开启后两墙内侧边做周期性开合，僵持不动时也有呼吸感；关闭则完全静止（等于改动前）")]
    public bool idleBreath = true;
    [Tooltip("开合幅度（世界单位）：缝隙在『centerGap』到『centerGap + 幅度』之间变化，两墙各退让一半")]
    public float breathGapAmplitude = 0.12f;
    [Tooltip("一个完整开合循环的时长（秒）")]
    public float breathPeriod = 2.4f;
    [Tooltip("远离（张开）阶段占整周期的比例；其余为接近（合拢）阶段。调大=慢慢拉开的时间更长")]
    [Range(0.1f, 0.9f)] public float breathOutRatio = 0.6f;
    [Tooltip("撞击加亮幅度：对峙循环合拢到最紧（撞击瞬间）最亮，张开时回落")]
    [Range(0f, 0.4f)] public float breathOpacityAmp = 0.05f;

    [Header("亮度：前进 / 撞击加亮（写材质 _Opacity）")]
    [Tooltip("前进加亮系数：优势方被放大时亮度随倍率提升（倍率 1.0→1.5 时亮度 +0.5×该值）")]
    public float glowPerEnlarge = 0.3f;
    [Tooltip("亮度上限：基础 Opacity + 前进加亮 + 撞击加亮 封顶值，防过曝")]
    [Range(0f, 1f)] public float opacityMax = 0.85f;

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

    // 弹簧阻尼状态（useSpringModel 时生效）
    private float _redVel = 0f, _blueVel = 0f;
    // 放大保持倒计时（秒）：>0 表示该侧处于"只维持/变大，不变小"的保护期
    private float _redHoldT = 0f, _blueHoldT = 0f;

    // 对峙循环（P4 ④.1）呼吸状态：_breathT 相位计时、_breathK 当前开合量(0=合拢最紧,1=张开最大)、
    // _breathFade 淡入淡出系数（开关切换 / 移动中抑制时平滑过渡，避免突跳）
    private float _breathT = 0f;
    private float _breathK = 0f;
    private float _breathFade = 0f;
    private float _redBaseOpacity = 0f, _blueBaseOpacity = 0f;

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
    void Awake()
    {
        EnsureWalls();
        ApplyToWalls();
        CaptureBase();
        EnsureVFX();
    }

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

        // ②b 放大保持（P3）：该侧目标倍率 >= 阈值 -> 刷新保持计时（不断前进就不断刷新）；
        //     保持期内目标只维持/变大，绝不下调 -> 扣血补分把优势方推回时，它仍维持变大，不会来回切换。
        ApplyEnlargeHold(ref _redHoldT, redTarget, ref redTarget, _redScale, dt);
        ApplyEnlargeHold(ref _blueHoldT, blueTarget, ref blueTarget, _blueScale, dt);
        // ③ 闪白：仅由扣血/补分机制（ScoreManager.ApplyCatchUp 直呼）触发，单次衰减到 0
        UpdateFlash(ref _redFlashing, ref _redFlashT, redWall, dt);
        UpdateFlash(ref _blueFlashing, ref _blueFlashT, blueWall, dt);

        // ③b 对峙循环呼吸（P4 ④.1）：僵持时两墙内侧边周期性开合，速度=远离减速 / 接近加速
        bool moving = dist > 1e-4f
            || Mathf.Abs(_redScale - 1f) > 1e-3f
            || Mathf.Abs(_blueScale - 1f) > 1e-3f;
        UpdateBreath(dt, moving);

        // ④ 放大/缩小：弹簧阻尼（默认）或旧的时间缓动（useSpringModel=false 回退）
        if (useSpringModel)
        {
            SpringScale(ref _redScale, ref _redVel, redTarget, dt);
            SpringScale(ref _blueScale, ref _blueVel, blueTarget, dt);
        }
        else
        {
            AnimateScale(ref _redScale, ref _redPhase, ref _redPT, ref _redPStart, ref _redPTarget, ref _redV0, ref _redAcc,
                         redTarget, enlargeTime, enlargeMinSpeed, shrinkTime, shrinkMinSpeed, dt);
            AnimateScale(ref _blueScale, ref _bluePhase, ref _bluePT, ref _bluePStart, ref _bluePTarget, ref _blueV0, ref _blueAcc,
                         blueTarget, enlargeTime, enlargeMinSpeed, shrinkTime, shrinkMinSpeed, dt);
        }

        // 仅在"正在过渡或放大中"或"呼吸中"才写墙 Transform，完全静止且无呼吸时不动，避免覆盖手动编辑
        if (!moving && _breathFade <= 1e-3f) return;

        // 呼吸位移：红墙内侧边向 -x 退、蓝墙向 +x 退 -> 缝隙变大（张开）；k=0 时两墙回到 base 位置（合拢最紧）
        float breathOff = breathGapAmplitude * _breathK * _breathFade * 0.5f;
        ApplyScale(redWall, _redBasePos, _redBaseRot, _redBaseScale, _redPivot, _redScale, new Vector3(-breathOff, 0f, 0f));
        ApplyScale(blueWall, _blueBasePos, _blueBaseRot, _blueBaseScale, _bluePivot, _blueScale, new Vector3(breathOff, 0f, 0f));

        // 呼吸亮度 + 前进加亮（优势方随放大倍率变亮）
        ApplyWallGlow();
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
    /// 单墙缩放缓动（红蓝各自独立调用）。与对峙循环同一条曲线：远离减速 / 接近加速。
    /// - 需要变大（远离基准，desiredTarget &gt; 当前）：减速曲线（起步最快，越接近目标越慢，收尾到 enlargeMinSpeed）；
    ///   期间若 desiredTarget 进一步增大（距离继续拉大跨过阈值），以当前大小为起点重新起跑续接。
    /// - 需要变小（接近基准，desiredTarget &lt; 当前）：加速曲线（起步最慢 = shrinkMinSpeed，越接近静止越快，回位瞬间最快）。
    /// - 相等：保持。
    /// </summary>
    private void AnimateScale(ref float scale, ref ScalePhase phase, ref float pt, ref float pStart, ref float pTarget,
        ref float v0, ref float acc, float desiredTarget,
        float enlTime, float enlMin, float shrTime, float shrMin, float dt)
    {
        if (desiredTarget > scale + 1e-4f)
        {
            // 进入/续接变大：以当前大小为起点重启减速 ease（远离减速）
            if (phase != ScalePhase.Enlarge || desiredTarget > pTarget + 1e-4f)
            {
                phase = ScalePhase.Enlarge;
                pt = 0f;
                pStart = scale;
                pTarget = desiredTarget;
                float D = Mathf.Max(1e-5f, desiredTarget - scale);
                float T = Mathf.Max(1e-3f, enlTime);
                // 收尾速度 = enlMin（越接近目标越慢）；位移过小则退化为匀速，恒不倒退
                float vEnd = Mathf.Min(enlMin, D / T);
                v0 = 2f * D / T - vEnd;   // 起步速度（最快）
                acc = (vEnd - v0) / T;    // 负 = 减速
            }
            pt += dt;
            float v = v0 + acc * pt;
            scale += v * dt;
            if (scale >= pTarget) { scale = pTarget; phase = ScalePhase.Idle; }
        }
        else if (desiredTarget < scale - 1e-4f)
        {
            // 进入变小：加速曲线（接近加速），从当前大小缩回 1.0
            if (phase != ScalePhase.Shrink)
            {
                phase = ScalePhase.Shrink;
                pt = 0f;
                pStart = scale;
                pTarget = 1f;
                float D = Mathf.Max(1e-5f, scale - 1f);
                float T = Mathf.Max(1e-3f, shrTime);
                v0 = shrMin;                                   // 起步最慢
                float vEnd = Mathf.Max(v0, 2f * D / T - v0);   // 收尾最快（位移过小则退化为匀速）
                acc = (vEnd - v0) / T;                         // 正 = 加速
            }
            pt += dt;
            float v = v0 + acc * pt;
            scale -= v * dt;
            if (scale <= 1f) { scale = 1f; phase = ScalePhase.Idle; }
        }
        else
        {
            phase = ScalePhase.Idle;
        }
    }

    /// <summary>
    /// 放大保持（P3）：修正"扣血补分把优势方推回 -> 它立刻缩小"的异常。
    /// - 目标倍率 >= holdRefreshThreshold（在前进/被推入）-> 刷新保持计时为 enlargeHoldTime（不断前进就不断刷新）；
    /// - 保持期内：目标倍率只维持或变大，绝不下调（即使方向已反转、rawTarget 已回到 1）；
    /// - 保持结束且 rawTarget 仍为 1 -> 目标恢复 1.0，由弹簧/缓动自然收回。
    /// </summary>
    private void ApplyEnlargeHold(ref float holdT, float rawTarget, ref float finalTarget, float curScale, float dt)
    {
        if (rawTarget >= holdRefreshThreshold)
        {
            holdT = enlargeHoldTime;                       // 刷新（不是累加，避免无限增长）
            if (rawTarget > finalTarget) finalTarget = rawTarget;
            return;
        }

        if (holdT > 0f)
        {
            holdT -= dt;
            // 保持期内：目标不低于"当前实际倍率" -> 维持现状（弹簧无外力，静止），不会缩小
            if (finalTarget < curScale) finalTarget = curScale;
        }
    }

    /// <summary>
    /// 弹簧阻尼缩放（P2 节奏方案 A）：把墙当成有惯性的肉体，被推时先抗拒再顺从。
    /// a = -k(scale - target) - c·v，c = 2·√k·dampingRatio（1 = 临界阻尼）。
    /// 用固定小步长子步进积分，保证低帧率下也不会发散。
    /// </summary>
    private void SpringScale(ref float scale, ref float vel, float target, float dt)
    {
        float k = Mathf.Max(1f, springStiffness);
        float c = 2f * Mathf.Sqrt(k) * Mathf.Max(0f, springDampingRatio);
        float maxS = Mathf.Max(1.01f, scaleAbove1_0);

        float remain = Mathf.Min(dt, 0.1f);   // 单帧最多推进 0.1s，防卡顿帧炸开
        while (remain > 0f)
        {
            float h = Mathf.Min(remain, 1f / 120f);
            float a = -k * (scale - target) - c * vel;
            vel += a * h;
            vel = Mathf.Clamp(vel, -springMaxVelocity, springMaxVelocity);
            scale += vel * h;
            remain -= h;
        }

        if (scale < 1f) { scale = 1f; if (vel < 0f) vel = 0f; }
        if (scale > maxS) { scale = maxS; if (vel > 0f) vel = 0f; }
    }

    /// <summary>
    /// 对峙循环呼吸（P4 ④.1）：算出当前开合量 _breathK ∈ [0,1]（0=合拢最紧，1=张开最大）。
    /// 速度曲线（用户指定）：远离=减速（越张开越慢，最开处速度归零）；接近=加速（越合拢越快，撞击瞬间最快）。
    /// 两段在接缝处速度连续：张开末尾 0 → 合拢起步 0；合拢末尾最快 → 张开起步最快，无速度突变。
    /// </summary>
    private void UpdateBreath(float dt, bool moving)
    {
        // 移动中（有位移 / 正在放大缩小）：收回并保持【最小缝隙】，相位归零；
        // 停下后从相位 0（最紧处）重新播放整段对峙循环。收回用 _breathFade 平滑，避免突跳。
        if (moving)
        {
            _breathFade = Mathf.MoveTowards(_breathFade, 0f, dt * 4f);
            if (_breathFade <= 1e-3f) { _breathFade = 0f; _breathT = 0f; _breathK = 0f; }
            return;   // 不推进相位
        }

        // 静止：淡入呼吸并推进相位
        _breathFade = Mathf.MoveTowards(_breathFade, idleBreath ? 1f : 0f, dt * 3f);
        if (!idleBreath && _breathFade <= 1e-3f) { _breathK = 0f; return; }

        _breathT += dt;
        float period = Mathf.Max(0.05f, breathPeriod);
        float p = Mathf.Repeat(_breathT, period) / period;      // 相位 0..1
        float outR = Mathf.Clamp(breathOutRatio, 0.05f, 0.95f);

        if (p <= outR)
        {
            // 远离（张开）：ease-out，k 由 0 涨到 1，速度 2→0（越张开越慢）
            float u = p / outR;
            _breathK = 1f - (1f - u) * (1f - u);
        }
        else
        {
            // 接近（合拢）：ease-in，k 由 1 落到 0，速度 0→2（越合拢越快）
            float u = (p - outR) / (1f - outR);
            _breathK = 1f - u * u;
        }
    }

    /// <summary>墙亮度：基础 Opacity + 撞击加亮（对峙循环合拢最紧时最亮）+ 前进加亮（优势方被放大时随倍率变亮），封顶 opacityMax。
    /// 注意：ShockwaveMeshGenerator.SyncMaterial 每帧会写回基础值，本方法在 DriveRuntime 末尾调用（在它之后），故本帧生效。</summary>
    private void ApplyWallGlow()
    {
        float breath = breathOpacityAmp * _breathFade * (1f - _breathK);   // k=0（合拢最紧）最亮
        float rGlow = breath + (_redScale - 1f) * glowPerEnlarge;           // 红墙：自己被放大才变亮
        float bGlow = breath + (_blueScale - 1f) * glowPerEnlarge;
        SetWallFloat(redWall, "_Opacity", Mathf.Clamp(_redBaseOpacity + rGlow, 0f, opacityMax));
        SetWallFloat(blueWall, "_Opacity", Mathf.Clamp(_blueBaseOpacity + bGlow, 0f, opacityMax));
    }

    /// <summary>VFX 读取用：当前红墙放大倍率（1.0 = 静止）。</summary>
    public float redScaleValue => _redScale;
    /// <summary>VFX 读取用：当前蓝墙放大倍率（1.0 = 静止）。</summary>
    public float blueScaleValue => _blueScale;

    /// <summary>VFX 读取用：根物体 base 位置的世界 X（curX=0 时的位置），用于推算判定线的固定世界坐标。</summary>
    public float baseRootWorldX => _baseRootPos.x;

    /// <summary>VFX 写入材质用（如 _Dissolve）。</summary>
    public void SetWallFloatPublic(Transform wall, string prop, float v) { SetWallFloat(wall, prop, v); }

    private void SetWallFloat(Transform wall, string prop, float v)
    {
        if (wall == null) return;
        var mr = wall.GetComponent<MeshRenderer>();
        if (mr != null && mr.sharedMaterial != null) mr.sharedMaterial.SetFloat(prop, v);
    }

    /// <summary>绕墙 mesh 局部内侧边 pivot 缩放：保持中缝侧边不动，向外扩，间隙不变。
    /// extra = 额外位移（对峙呼吸的开合偏移），与缩放补偿叠加，互不干扰。</summary>
    private void ApplyScale(Transform wall, Vector3 basePos, Quaternion baseRot, Vector3 baseScale, Vector3 meshPivot, float f, Vector3 extra)
    {
        if (wall == null) return;
        wall.localScale = new Vector3(baseScale.x * f, baseScale.y * f, baseScale.z * f);
        Vector3 offset = baseRot * new Vector3(baseScale.x * meshPivot.x, baseScale.y * meshPivot.y, baseScale.z * meshPivot.z);
        wall.localPosition = basePos + offset * (1f - f) + extra;
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
        // 缓存两墙的基础 Opacity（呼吸亮度在其上叠加；SyncMaterial 每帧会写回该值）
        var rg = redWall != null ? redWall.GetComponent<ShockwaveMeshGenerator>() : null;
        var bg = blueWall != null ? blueWall.GetComponent<ShockwaveMeshGenerator>() : null;
        _redBaseOpacity = rg != null ? rg.opacity : 0f;
        _blueBaseOpacity = bg != null ? bg.opacity : 0f;
        _redScale = 1f; _blueScale = 1f;
        _redVel = 0f; _blueVel = 0f;
        _redHoldT = 0f; _blueHoldT = 0f;
        _redPhase = ScalePhase.Idle; _bluePhase = ScalePhase.Idle;
        _redFlashing = false; _blueFlashing = false; _redFlashT = 0f; _blueFlashT = 0f;
        _redSuppress = false; _blueSuppress = false;
        _breathT = 0f; _breathK = 0f; _breathFade = 0f;   // 呼吸相位归零，从合拢最紧处平滑起步
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

    private ShockwaveVFX _vfx;

    /// <summary>确保附加特效（火花 / 能量注入 / 消散）存在：场景里没有就自动创建一个独立的 VFX 对象（挂在不随动的根下）。</summary>
    private void EnsureVFX()
    {
        if (_vfx != null) return;
        _vfx = FindFirstObjectByType<ShockwaveVFX>();
        if (_vfx == null)
        {
            var go = new GameObject("ShockwaveVFX");
            _vfx = go.AddComponent<ShockwaveVFX>();
            _vfx.preview = this;
        }
        else if (_vfx.preview == null) _vfx.preview = this;
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
