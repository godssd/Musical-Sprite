using UnityEngine;

/// <summary>
/// 双色冲击波运行时驱动器（替代旧粉杠 BattleCenterLine 的视觉表现）。
///
/// 职责边界：
///   - 整组随 BattleCenterLine.currentX 左右平移（位移通道）。
///   - 墙的大小（localScale）【只由累积距离 accum 决定】（大小通道），两条通道互不干扰。
///   - 放大以"贴中缝的内侧边"为轴心向外扩，保证两墙间隙不变。
///
/// ⛔ 铁律（2026-10-07 确立）：呼吸循环 / 前进回弹 / 闪白 / 亮度 一律不得影响 scale；
///    accum 只在前进状态内上升、只增不减，退出后才按倍率档位衰退；
///    accum 的唯一来源是己方命中得分（ScoreManager.OnScoreGained），保底补分不计入。
///
/// 形状：复用手动摆好的 WALL.fbx（ShockwaveMeshGenerator.autoFit 必须关闭）。
///       CaptureBase() 捕获手动 Transform 作为 base，f=1 时与手动值完全一致。
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

    [Header("运行时驱动：接入 BattleCenterLine")]
    [Tooltip("赋值后整组随 currentX 平移；留空则墙停在原位")]
    public BattleCenterLine centerLine;
    // ⚠【2026-10-07 已废弃 · 不要再使用】原「未接 centerLine 时编辑器预览用的模拟终点 X」。
    //   它曾造成两次位置污染事故：旧守卫 previewing = (centerLine != null) || (previewTargetX != 0f)
    //   在本场景（centerLine 已连着）下 previewing 恒为 true -> 编辑器非运行态也完整执行 DriveRuntime，
    //   而编辑器环境与运行时不同步（deltaTime / 呼吸相位 / 弹簧状态），墙的 Transform 被写坏且无法自动恢复。
    //   现场记录：用户拨动后保存，pos.x 由 ±1.6486963 被污染成 ±2.6477916。
    //   现【已从守卫中移除】：非运行态一律不驱动，详见 DriveRuntime 开头注释。
    [HideInInspector] public float previewTargetX = 0f;   // 保留字段仅为不破坏旧场景序列化

    [Header("编辑器预览（关闭时编辑器绝不改墙的 Transform）")]
    [Tooltip("勾上后，编辑器（非 Play）里也会驱动冲击波，可以实时调参并【保存到场景】。\n"+
             "⚠ 关闭时编辑器完全不碰墙，你摆好的 Transform 原样保留 —— 推荐调参时打开、调完关闭。\n"+
             "⚠ 别在开着它的时候手动拖动两墙（会被脚本覆盖）")]
    public bool previewInEditMode = false;

    [Header("墙形状（同时作用于两堵墙；默认 1 = 完全保持你当前调好的粗细/高度/纵深）")]
    [Tooltip("沿推进方向（X）的粗细倍率。因为缩放是绕『贴中缝的内侧边』pivot 进行的，\n所以调大它，墙会往【两侧外扩】，中缝宽度保持不变。\n1 = 保持当前；1.5 = 比现在粗 50%；0.5 = 变细一半")]
    [Range(0.2f, 3f)] public float wallWidthScaleX = 1f;
    [Tooltip("墙高（Y）倍率。1 = 保持当前高度")]
    [Range(0.2f, 3f)] public float wallWidthScaleY = 1f;
    [Tooltip("纵深（Z，沿场地前后）倍率。1 = 保持当前纵深")]
    [Range(0.2f, 3f)] public float wallWidthScaleZ = 1f;

    [Header("P1 场景染色：让场景与植被被冲击波的光影响")]
    [Tooltip("总开关：把两堵墙的世界位置 + 颜色写成全局参数，\n场景 shader（ScenePropSprite / GrassFringe / GroundEdge）按距离衰减做加色。\n⛔ 本项目场景全部是 Unlit / 假光照，URP 灯光系统照不到它们，\n所以这是唯一能让『光洒到场景上』生效的通路（且零实时光源开销）")]
    public bool sceneGlowEnabled = true;
    [Tooltip("染色强度：场景被照亮的加色量。0 = 关闭染色。建议从 0.3~0.6 起步，过高会像加了浓雾")]
    [Range(0f, 1.5f)] public float sceneGlowStrength = 0.8f;
    [Tooltip("影响半径（世界单位）：距离墙中心多远处染色衰减到 0。\n墙的世界位置约在 z=-3.75、场地约在 z=0，所以半径至少要 4 以上才盖得到")]
    public float sceneGlowRange = 6f;
    [Tooltip("两侧同时开启：红墙与蓝墙都往场景投染色（近距离时叠加得更亮）")]
    public bool sceneGlowBothSides = true;

    [Header("P2 缩放：弹簧阻尼（惯性肉体感）")]
    [Tooltip("弹簧刚度：越大越硬、追得越快；越小越软、越有惯性")]
    public float springStiffness = 80f;
    [Tooltip("阻尼比：0=来回震荡，1=临界阻尼（最快且不过冲），>1=迟缓")]
    [Range(0f, 2f)] public float springDampingRatio = 0.9f;
    [Tooltip("速度上限（倍/秒），防止低帧率下弹簧过激")]
    public float springMaxVelocity = 8f;

    [Header("P2b 前进预备（得分前进时：先向后弹开蓄力，再向前冲）")]
    [Tooltip("总开关：开启后『得分前进』瞬间先给一个反向预备动作（弹开），再释放向前冲；关闭=直接冲")]
    public bool forwardAnticipationEnable = true;
    [Tooltip("预备时长（秒）：这段时间内墙向后弹开再回位，之后才真正向前冲。太短会看不出蓄力")]
    public float forwardAnticipationTime = 0.18f;
    [Tooltip("向后弹开距离（世界单位）：前进侧墙沿『前进的反方向』弹开该距离，拉开与对手的间距。\n这是『后扯』幅度主旋钮。\n【注意】弹开只改位置，不改大小 —— 大小只由累积距离决定")]
    public float forwardAnticipationPullback = 0.45f;

    [Header("P2c 前进状态（3 秒窗口：连续推进合并为一次持续放大）")]
    [Tooltip("前进窗口时长（秒）：第一次推进进入前进状态后，窗口内继续推进只累积距离、不重复后弹；\n每次推进都会把该计时刷新回该值，直到『没能维持住』（窗口内无推进）才退出")]
    public float advanceWindowTime = 3f;
    [Tooltip("起跑判定阈值（世界单位）：单帧释放量超过该值才算『开始推进』，用于 Idle/Decay 起步防抖。\n【注意】只在起跑那一下生效；进入 Advancing 后累积距离每帧无条件累加，不受此阈值限制。\n调大会漏掉小幅度得分，调小易被抖动误触发")]
    public float advanceAccumThreshold = 0.003f;
    [Tooltip("每降一个【倍率档位】所需时间（秒）。\n五个档位就是倍率表本身：1.5 / 1.3 / 1.2 / 1.1 / 1.05。\n每档耗时相同，但档位之间的倍率差越来越小（0.2 / 0.1 / 0.1 / 0.05 / 0.05）\n-> 倍率下降速度自然【先快后慢】，不需要额外曲线。\n由此：1.5->1.0 = 5s、1.3->1.0 = 4s、1.2->1.0 = 3s、1.1->1.0 = 2s、1.05->1.0 = 1s。\n【衰退快慢的唯一旋钮】调大=整体更慢，调小=更快")]
    public float advanceDecayStepTime = 1.0f;
    [Tooltip("扣血补分位移的忽略时长（秒）：保底触发后这段时间内，劣势方的得分【不被认定为前进】。\n它拿不到前进状态、也累积不到距离，因此根本不会变大，只保留闪白。\n必须 > ScoreManager.catchUpInterval（默认 1s），否则连续保底的间隙里残余位移仍会被计入。\n默认 1.2s：连续保底期间计时被不断刷新 -> 整段位移一滴不漏地忽略")]
    public float ignoreAdvanceDuration = 1.2f;

    [Header("累积来源：得分事件驱动（ScoreManager.OnScoreGained）")]
    [Tooltip("分摊速率（1/秒）：得分换算出的世界位移不会瞬间灌进 accum，而是按该速率指数分摊，与中线 Lerp 观感同步。\n建议与 BattleCenterLine.smoothSpeed（默认 5）一致；调小=变大更慢更绵，调大=更跟手")]
    public float scoreEventApplyRate = 5f;

    [Header("补分闪烁（仅 ApplyCatchUp 给劣势方补分时触发，非命中加分）")]
    [Tooltip("闪白强度（写入材质 _Flash 的峰值，0~1）")]
    [Range(0f, 1f)] public float flashIntensity = 1f;
    [Tooltip("闪白一次的总时长（秒）：触发瞬间最亮，平方衰减到 0（单次白闪，不连续眨）")]
    public float flashDuration = 0.3f;

    [Header("对峙循环（P4 ④.1：僵持时的互顶开合呼吸）")]
    [Tooltip("静止多久才开始播（秒）：双方都不再移动后需静止这么久才起播对峙呼吸。\n【判定只看中线是否在动】，与前进状态 / 衰退完全无关 —— 衰退期间中线静止照样播呼吸")]
    public float idleBreathDelay = 1.0f;
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
    [Tooltip("总开关：关闭后只保留基础亮度，【前进加亮】与【撞击加亮】都不生效。\nP1 自发光分层方案落地前先关闭，避免过曝与层次混乱。\n【注意】扣血闪白是机制反馈不是发光，不受此开关影响")]
    public bool enableBrightnessFx = false;
    [Tooltip("前进加亮系数：优势方被放大时亮度随倍率提升（倍率 1.0→1.5 时亮度 +0.5×该值）")]
    public float glowPerEnlarge = 0.3f;
    [Tooltip("亮度上限：基础 Opacity + 前进加亮 + 撞击加亮 封顶值，防过曝")]
    [Range(0f, 1f)] public float opacityMax = 0.85f;

    [Header("放大倍率表（按【累积距离 accum】分档；档间线性插值，平滑无硬边）")]
    [Tooltip("⚠ 唯一事实源：下面 6 个倍率就是『档位』本身。衰退时每降一档固定耗时 advanceDecayStepTime 秒。\n因为高档位之间的倍率差更大（1.5→1.3 是 0.2；1.1→1.05 只有 0.05），每档耗时相同 ⇒ 下降速度自然先快后慢")]
    public float scaleAtRest = 1.0f;     // accum = 0
    public float scaleTiny = 1.05f;      // accum >= accumTiny
    public float scaleAbove0_2 = 1.1f;   // accum >= accumAbove0_2
    public float scaleAbove0_3 = 1.2f;   // accum >= accumAbove0_3
    public float scaleAbove0_5 = 1.3f;   // accum >= accumAbove0_5
    public float scaleAbove1_0 = 1.5f;   // accum >= accumAbove1_0（封顶）

    [Header("累积距离档位（与上面倍率表一一对应的『每档下界』，世界单位）")]
    [Tooltip("【大小 = f(累积距离) 的另一半】倍率表给出每档的『倍率』，这里给出升到该档所需的『累积距离下界』。\n两者一起构成分段线性映射：accum -> 档位进度 p -> 倍率。\n⛔ accum 在 accumAbove1_0 处封顶（默认 1.0），保证衰退起点可控：1.5→1.0 恰好 5 档 = 5 秒")]
    public float accumTiny = 0.05f;      // 升到 1.05 倍
    public float accumAbove0_2 = 0.2f;   // 升到 1.1 倍
    public float accumAbove0_3 = 0.3f;   // 升到 1.2 倍
    public float accumAbove0_5 = 0.5f;   // 升到 1.3 倍
    public float accumAbove1_0 = 1.0f;   // 升到 1.5 倍（同时是 accum 上限）

    [Header("（只读/自动维护）真实墙引用")]
    public Transform redWall;
    public Transform blueWall;

    // 捕获的你手动 base（驱动在 f=1 / 不移动时完全等于这些值，零改动）
    private Vector3 _baseRootPos;
    private Vector3 _redBasePos, _blueBasePos;
    private Quaternion _redBaseRot, _blueBaseRot;
    private Vector3 _redBaseScale, _blueBaseScale;
    // 未乘「墙形状」系数的原始缩放（CaptureBase 时捕获），供 ApplyShapeScale 每帧实时换算
    private Vector3 _redRawScale, _blueRawScale;
    // 上一次已写入墙 Transform 的 baseScale（用于判断形状系数是否变化 -> 是否需要重写）
    private Vector3 _lastAppliedShapeScale;
    private Vector3 _redPivot, _bluePivot;   // 墙 mesh 局部空间里的"贴中缝内侧边"点
    private float _redScale = 1f, _blueScale = 1f;

    // 扣血/补分机制（ApplyCatchUp）触发时：劣势方冲击波"闪白一次"（由 ScoreManager 直呼 OnScoreAdjustPush 触发）。
    // 该侧"不放大"由【忽略前进】计时负责（见 _redIgnoreT）：这段时间的位移不被认定为前进，
    // 拿不到前进状态也累积不到距离 —— 不是"冻结"，而是压根不计入。
    private bool _redFlashing = false, _blueFlashing = false;
    private float _redFlashT = 0f, _blueFlashT = 0f;     // 单次闪白衰减计时

    // 忽略前进计时（秒）：>0 表示该侧当前位移是"扣血补分"造成的，不计入前进状态与累积距离
    private float _redIgnoreT = 0f, _blueIgnoreT = 0f;

    // P2 弹簧阻尼速度状态
    private float _redVel = 0f, _blueVel = 0f;

    // P2b 前进预备状态：anticipT=剩余预备时间；anticipOff=当前弹开偏移（世界单位，sin 曲线 0→max→0）
    private float _redAnticipT = 0f, _blueAnticipT = 0f;
    private float _redAnticipOff = 0f, _blueAnticipOff = 0f;

    // P4A：待分摊的「己方得分 → 世界位移」队列（红蓝各自独立）。
    // 只由 ScoreManager.OnScoreGained 灌入；每帧按 scoreEventApplyRate 指数释放进 accum。
    // ⛔ 保底补分（ApplyCatchUp）不会灌入；ignoreAdvance 期间会直接丢弃。
    private float _redPending = 0f, _bluePending = 0f;
    private ScoreManager _scoreManagerRef;

    // P2c 前进状态（红蓝各自独立，互不干扰）：
    //   state  = Idle（无前进）/ Advancing（3 秒窗口内，累积距离决定倍率）/ Decay（窗口结束，按档位衰退）
    //   timer  = 窗口剩余时间；accum = 累积的前进距离（【只增不减】：只累加正向增量，被推回不倒扣）
    private enum AdvanceState { Idle, Advancing, Decay }
    private AdvanceState _redAdvState = AdvanceState.Idle, _blueAdvState = AdvanceState.Idle;
    private float _redAdvTimer = 0f, _blueAdvTimer = 0f;
    private float _redAdvAccum = 0f, _blueAdvAccum = 0f;
    // 衰退状态：decayT=衰退已过时间；decayStartProgress=进入衰退瞬间的【档位进度】（由 accum 换算，不是由 scale 换算）。
    // 衰退按【倍率档位】计时（每档 advanceDecayStepTime 秒）：p 匀速下降，accum = AccumFromProgress(p)，
    // 倍率再由 accum 换算 —— 全程只有 accum 一个变量决定大小，不需要任何 shape 曲线：
    // 档位之间的倍率差越来越小，每档耗时相同 -> 下降速度自然先快后慢。
    private float _redDecayT = 0f, _blueDecayT = 0f;
    private float _redDecayStartProgress = 0f, _blueDecayStartProgress = 0f;

    // 对峙循环（P4 ④.1）呼吸状态：_breathT 相位计时、_breathK 当前开合量(0=合拢最紧,1=张开最大)、
    // _breathFade 淡入淡出系数（开关切换 / 移动中抑制时平滑过渡，避免突跳）
    private float _breathT = 0f;
    private float _breathK = 0f;
    private float _breathFade = 0f;
    private float _redBaseOpacity = 0f, _blueBaseOpacity = 0f;

    // 对峙循环判定：中线静止累计时长 + 上一帧中线位置（只看中线，与前进状态 / 衰退无关）
    private float _idleTimer = 0f;
    private float _lastCenterX = 0f;
    // 上一帧已写入 Transform 的倍率：用于判断"本帧视觉是否有变化"。
    // ⚠ 必须做 —— 呼吸判定与衰退解耦后，若沿用旧的 `if (!moving && _breathFade<=0) return;`，
    //   衰退期间（中线静止、呼吸关闭或淡出）会把缩放写操作整个跳过，墙会卡在原地不动。
    private float _lastAppliedRedScale = 1f, _lastAppliedBlueScale = 1f;
    // 倍率档位表缓存（= 倍率表本身，随 Inspector 值刷新；预分配避免每帧 GC）
    private readonly float[] _decayLevels = new float[6];
    // 累积距离档位表缓存（与 _decayLevels 一一对应的『每档下界』）
    private readonly float[] _accumLevels = new float[6];

#if UNITY_EDITOR
    [Header("运行时诊断（只读）")]
    [SerializeField] private float _dbgRedAccum, _dbgBlueAccum;
    [SerializeField] private float _dbgRedPending, _dbgBluePending;
    [SerializeField] private float _dbgRedTarget, _dbgBlueTarget;
    [SerializeField] private string _dbgRedState, _dbgBlueState;
    [SerializeField] private float _dbgRedIgnoreT, _dbgBlueIgnoreT;
#endif

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

    void OnValidate()
    {
        EnsureWalls();
        ApplyToWalls();
        // 非 Play 态：墙的 Transform 只由previewInEditMode 驱动，
        // 此刻捕获 base 是安全的 —— 这让「墙形状系数」在编辑态就能实时看到效果并保存。
        // ⛔ Play 态下【不】捕获：此时墙已被 ApplyScale 写入临时位移，捕获会污染 base。
        if (!Application.isPlaying) CaptureBase();
    }
    // ⛔ autoFit 与本品互斥：它每帧按 (frontX - backX) 覆盖墙的 localScale，而 frontX 跟着中缝走，
    //    会把被推入侧的墙腰斩（实测 156 → 76，≈0.49 倍）。因此必须在 CaptureBase 之前强制关闭。
    void Awake()
    {
        EnsureWalls();
        EnsureWallAutoFitDisabled();
        ApplyToWalls();
        CaptureBase();
    }

    void Start()
    {
        // 运行时兜底：centerLine / ScoreManager 若忘记拖拽或晚于本脚本创建，这里补上
        if (centerLine == null) centerLine = FindFirstObjectByType<BattleCenterLine>();
        BindScoreEvents();
    }

    /// <summary>墙的 ShockwaveMeshGenerator.autoFit 若开启，与本品 Transform 控制冲突，强制关闭并继续运行。</summary>
    private void EnsureWallAutoFitDisabled()
    {
        var rg = redWall  != null ? redWall.GetComponent<ShockwaveMeshGenerator>()  : null;
        var bg = blueWall != null ? blueWall.GetComponent<ShockwaveMeshGenerator>() : null;
        bool changed = false;
        if (rg != null && rg.autoFit) { rg.autoFit = false; changed = true; }
        if (bg != null && bg.autoFit) { bg.autoFit = false; changed = true; }
        if (changed)
        {
            Debug.LogWarning("[ShockwavePreview] 检测到墙的 ShockwaveMeshGenerator.autoFit 为开启状态，已自动关闭。"
                + "autoFit 与本品 Transform 控制互斥（它会按 frontX-backX 每帧覆盖 localScale，导致推进到底时冲击波被腰斩）。"
                + "如需要旧行为，请改由 ShockwavePreview 关闭或删除。", this);
        }
    }

    void OnDestroy()
    {
        if (_scoreManagerRef != null)
        {
            _scoreManagerRef.OnScoreGained -= HandleScoreGained;
            _scoreManagerRef = null;
        }
        // P1 场景染色：Shader.SetGlobal 是【跨对象的全局状态】，不随本组件销毁而清除。
        // 不清的话退出 Play 后场景物件会一直带着最后一次的染色（编辑器里也残留）。
        Shader.SetGlobalFloat(ShockGlowParams2Id, 0f);
        Shader.SetGlobalFloat(ShockGlowEnabledId, 0f);
    }

    /// <summary>P4A：绑定得分事件（运行时兜底查找，ScoreManager 后于本脚本创建也能接上）。</summary>
    private void BindScoreEvents()
    {
        if (_scoreManagerRef != null) return;
        _scoreManagerRef = FindFirstObjectByType<ScoreManager>();
        if (_scoreManagerRef != null) _scoreManagerRef.OnScoreGained += HandleScoreGained;
    }

    /// <summary>P4A：己方靠命中拿到加分 -> 换算成世界位移，灌进待分摊队列。
    /// 换算用 BattleCenterLine.pushPerHit（与中线移动同一把尺子）：分数 × pushPerHit = 这次得分把中线推进了多少世界单位。
    /// ⚠ 保底补分不会走到这里（ScoreManager.ApplyCatchUp 里已屏蔽派发）。</summary>
    private void HandleScoreGained(int side, int deltaScore)
    {
        if (deltaScore <= 0) return;
        float per = (centerLine != null) ? centerLine.pushPerHit : 0.001f;
        float world = deltaScore * per;
        if (side == 0) _redPending += world;
        else _bluePending += world;
    }

    void Update()
    {
        // 运行时兜底：centerLine / ScoreManager 若此前未解析到（动态生成场景），每帧尝试一次
        if (centerLine == null) centerLine = FindFirstObjectByType<BattleCenterLine>();
        if (_scoreManagerRef == null) BindScoreEvents();

        EnsureWalls();
        ApplyToWalls();
        DriveRuntime();
    }

    // ---- 运行时驱动：平移 + 累积距离驱动放大 ----

    private void DriveRuntime()
    {
        // ⛔⛔【2026-10-07 事故祸根 · 已修正】原守卫是
        //     bool previewing = (centerLine != null) || (previewTargetX != 0f);
        //     if (!Application.isPlaying && !previewing) return;
        //   本类带 [ExecuteAlways]，Update() 在编辑器非运行态也会每帧执行；
        //   而 previewing 依赖「场景里连着的引用」，在真实场景里几乎恒为 true -> 守卫形同虚设
        //   -> 编辑器里完整驱动 DriveRuntime，墙的 Transform 被写坏且无法自动恢复。
        //
        //
        //   编辑器预览：改由【显式开关】previewInEditMode 控制（默认关）。
        //   这样玩家可以主动打开预览、实时调参并【保存到场景】；关闭时编辑器绝不碰墙的 Transform。
        if (!Application.isPlaying && !previewInEditMode) return;
        float curX = SeamX;
        float dt = Time.deltaTime;
        RefreshDecayLevels();   // 档位表每帧刷新一次；下面的映射函数都依赖它

        // 1) 整组随 currentX 平移（仅运行时接管根的 X，编辑器里保留手动根位置）
        if (centerLine != null && Application.isPlaying)
            transform.position = new Vector3(_baseRootPos.x + curX, _baseRootPos.y, _baseRootPos.z);

        // 2) 前进状态：目标倍率只由 accum 决定。
        //    accum 的唯一来源是己方得分事件，与 currentX 无关 —— 不受共享中线 / 对手得分 / 保底回推污染。
        UpdateAdvanceState(ref _redAdvState, ref _redAdvTimer, ref _redAdvAccum, ref _redIgnoreT,
                           ref _redAnticipT, ref _redDecayT, ref _redDecayStartProgress, ref _redPending, dt);
        UpdateAdvanceState(ref _blueAdvState, ref _blueAdvTimer, ref _blueAdvAccum, ref _blueIgnoreT,
                           ref _blueAnticipT, ref _blueDecayT, ref _blueDecayStartProgress, ref _bluePending, dt);

        float redTarget  = GetAdvanceTarget(_redAdvState, _redAdvAccum);
        float blueTarget = GetAdvanceTarget(_blueAdvState, _blueAdvAccum);

        // 3) 衰退：先推进档位进度 p，再由 p 反算 accum 与目标倍率。
        //    ⛔ 不直接给 scale 赋值、也不清零弹簧速度（旧实现的"瞬间被切小 / 被冻结锁住"根因）。
        if (_redAdvState == AdvanceState.Decay)
        {
            float p = AdvanceDecay(ref _redAdvState, ref _redAdvAccum, ref _redDecayT, _redDecayStartProgress, dt);
            redTarget = (p >= 0f) ? ScaleFromProgress(p) : scaleAtRest;
        }
        if (_blueAdvState == AdvanceState.Decay)
        {
            float p = AdvanceDecay(ref _blueAdvState, ref _blueAdvAccum, ref _blueDecayT, _blueDecayStartProgress, dt);
            blueTarget = (p >= 0f) ? ScaleFromProgress(p) : scaleAtRest;
        }

        // 4) P2b 弹开预备：只在 Idle/Decay → Advancing 转换时弹一次，窗口内连续推进不重复弹
        //    （合并高频低数值推进、消除抽搐感）。⛔ 只改位移，绝不改大小。
        UpdateAnticip(ref _redAnticipT, ref _redAnticipOff, dt);
        UpdateAnticip(ref _blueAnticipT, ref _blueAnticipOff, dt);

        // 5) 闪白：仅由扣血补分触发（ScoreManager 直呼 OnScoreAdjustPush），与"变大"完全解耦
        UpdateFlash(ref _redFlashing, ref _redFlashT, redWall, dt);
        UpdateFlash(ref _blueFlashing, ref _blueFlashT, blueWall, dt);

        // 6) 对峙呼吸：判定【只看中线是否在动】（currentX 红蓝共享，中线不动 == 双方都没移动），
        //    与前进状态 / 衰退 / 后弹完全无关 —— 衰退期间中线静止照样播呼吸。
        bool noMove = Mathf.Abs(curX - _lastCenterX) < 1e-4f;
        _idleTimer = noMove ? _idleTimer + dt : 0f;
        _lastCenterX = curX;
        UpdateBreath(dt, _idleTimer < idleBreathDelay);

        // 7) 弹簧跟随目标倍率：所有状态（含 Decay）统一走这一条通道，保证不存在硬切
        SpringScale(ref _redScale, ref _redVel, redTarget, dt);
        SpringScale(ref _blueScale, ref _blueVel, blueTarget, dt);

        // 运行时诊断：把内部状态同步到 Inspector，方便用户验证大小/累积/状态是否匹配。
#if UNITY_EDITOR
        _dbgRedAccum = _redAdvAccum; _dbgBlueAccum = _blueAdvAccum;
        _dbgRedPending = _redPending; _dbgBluePending = _bluePending;
        _dbgRedTarget = redTarget; _dbgBlueTarget = blueTarget;
        _dbgRedState = _redAdvState.ToString(); _dbgBlueState = _blueAdvState.ToString();
        _dbgRedIgnoreT = _redIgnoreT; _dbgBlueIgnoreT = _blueIgnoreT;
#endif

        // 仅在"本帧视觉确实有变化"时才写墙 Transform，完全静止时不动，避免覆盖手动编辑。
        // ⚠ 不能再沿用旧的 `if (!moving && _breathFade <= 1e-3f) return;` —— 呼吸判定已与衰退解耦，
        //   那样会在"中线静止 + 呼吸关闭/已淡出"时把【衰退的缩放】整个跳过，墙会卡在原地不动。
        bool needWrite = _breathFade > 1e-3f
            || Mathf.Abs(_redScale  - _lastAppliedRedScale)  > 1e-5f
            || Mathf.Abs(_blueScale - _lastAppliedBlueScale) > 1e-5f
            || _redAnticipOff != 0f || _blueAnticipOff != 0f
            // 形状系数是「静态基准的改变」，改完必须重写一次墙Transform，否则看起来没反应
            || Mathf.Abs(_redBaseScale.x - _lastAppliedShapeScale.x) > 1e-4f
            || Mathf.Abs(_redBaseScale.y - _lastAppliedShapeScale.y) > 1e-4f
            || Mathf.Abs(_redBaseScale.z - _lastAppliedShapeScale.z) > 1e-4f;

        // ⚠ 场景染色必须放在 needWrite 提前 return【之前】。
        //   needWrite 为假 = 墙 Transform 本帧不动，但全局染色参数是【状态量】而非增量——
        //   漏写会让场景一直停在上一次的染色值（墙动了、场景没跟着变，且退出 Play 后残留）。
        ApplySceneGlow();

        if (!needWrite) return;

        // 墙形状系数实时应用（必须在 ApplyScale 之前 —— 它改的是 baseScale）
        ApplyShapeScale();

        // 呼吸位移：红墙内侧边向 -x 退、蓝墙向 +x 退 -> 缝隙变大（张开）；k=0 时两墙回到 base 位置（合拢最紧）
        float breathOff = breathGapAmplitude * _breathK * _breathFade * 0.5f;
        // P2b 预备偏移：红墙向 -x 弹开 / 蓝墙向 +x 弹开 —— 都是"远离对手"，视觉上先拉开中缝间距再前冲
        ApplyScale(redWall, _redBasePos, _redBaseRot, _redBaseScale, _redPivot, _redScale,
                   new Vector3(-breathOff - _redAnticipOff, 0f, 0f), wallWidthScaleX);
        ApplyScale(blueWall, _blueBasePos, _blueBaseRot, _blueBaseScale, _bluePivot, _blueScale,
                   new Vector3(breathOff + _blueAnticipOff, 0f, 0f), wallWidthScaleX);
        _lastAppliedRedScale = _redScale;
        _lastAppliedBlueScale = _blueScale;
        _lastAppliedShapeScale = _redBaseScale;   // 记录已应用的形状基准，供 needWrite 判定

        // 呼吸亮度 + 前进加亮（优势方随放大倍率变亮）—— 受 enableBrightnessFx 总开关控制
        ApplyWallGlow();
    }

    /// <summary>扣血/补分机制触发（阈值态）。由 ScoreManager.ApplyCatchUp 直呼；side 0=红墙，1=蓝墙（=被补分/扣血的劣势方）。
    /// 阈值态规则（用户明确）：
    ///   1) 扣血方【不变大、只闪白】—— 给劣势方挂 `ignoreAdvanceDuration` 的"忽略前进"计时：
    ///      这段时间内它的得分【不被认定为前进】，拿不到前进状态、也累积不到距离，
    ///      因此根本不可能变大。注意这不是"冻结"（不会把状态卡住），计时结束后一切恢复正常判定。
    ///   2) 进攻方【不做任何特殊处理】—— 只要"被推回会倒扣累积距离"已修（`accum` 只增不减），
    ///      优势方的累积值就不会被这次补分影响；它若还在推进，窗口计时照常被自己的推进刷新。</summary>
    public void OnScoreAdjustPush(int side)
    {
        if (side == 0)
        {
            if (!_redFlashing) _redFlashT = 0f;   // 上一次闪完后再触发才重置，避免中途打断重来
            _redFlashing = true;                  // 劣势方（红）：只闪白
            _redIgnoreT = ignoreAdvanceDuration;  // 这段位移不算前进 -> 不进前进状态、不累积 -> 不会变大
            _redPending = 0f;                     // P4A：丢弃待分摊队列（双保险）
        }
        else
        {
            if (!_blueFlashing) _blueFlashT = 0f;
            _blueFlashing = true;
            _blueIgnoreT = ignoreAdvanceDuration;
            _bluePending = 0f;
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
        if (mr == null) return;
        var m = Application.isPlaying ? mr.material : mr.sharedMaterial;   // 同 SetWallFloat：运行时用实例，防串台/落盘
        if (m != null) m.SetFloat("_Flash", v);
    }


    /// <summary>
    /// 前进状态机（红蓝各自独立、互不干扰）。
    ///   1) 起跑（Idle / Decay → Advancing）：触发一次 P2b 弹开，并从本次位移开始累积；
    ///   2) Advancing 内继续推进：accum 每帧无条件累加，计时刷新回 advanceWindowTime —— 不重复弹开；
    ///   3) 窗口内无推进：计时递减，归零才转 Decay（期间保持当前累积值，小抖动不会缩回）；
    ///   4) Decay 中途重新推进：立刻结束衰退、以【衰退后的当前 accum】为起点继续累积。
    ///
    /// ⚠ accum 只会因得分事件增长，且【只增不减】（被推回不倒扣）；唯一让它下降的是 AdvanceDecay。
    /// ignoreT：扣血补分后的"忽略前进"剩余时间。>0 时本侧不计累积、不刷新计时、不触发弹开、不起跑。
    /// </summary>
    private void UpdateAdvanceState(ref AdvanceState state, ref float timer, ref float accum,
                                    ref float ignoreT, ref float anticipT,
                                    ref float decayT, ref float decayStartProgress, ref float pending, float dt)
    {
        if (ignoreT > 0f) ignoreT = Mathf.Max(0f, ignoreT - dt);
        bool ignoring = ignoreT > 0f;

        // 本帧前进量：得分换算出的世界位移按指数分摊释放（与中线 Lerp 观感同步）。
        // 扣血补分期间直接丢弃待分摊量（双保险，事件侧已屏蔽派发）。
        if (ignoring) pending = 0f;
        float rate = Mathf.Max(0.01f, scoreEventApplyRate);
        float stepFwd = pending * (1f - Mathf.Exp(-rate * dt));
        pending = Mathf.Max(0f, pending - stepFwd);

        // 累积上限：到 accumAbove1_0 封顶（对应最大倍率）。
        // ⛔ 必须封顶，否则衰退起点 progress 会远超 5，导致"先空转很久才开始变小"。
        float maxAccum = Mathf.Max(1e-4f, accumAbove1_0);

        switch (state)
        {
            case AdvanceState.Idle:
                if (!ignoring && stepFwd > advanceAccumThreshold)
                {
                    accum = Mathf.Min(stepFwd, maxAccum);
                    EnterAdvancing(ref state, ref timer, ref decayT, ref anticipT);
                }
                break;

            case AdvanceState.Advancing:
                if (!ignoring && stepFwd > 1e-6f)
                {
                    accum = Mathf.Min(accum + stepFwd, maxAccum);   // 只增不减
                    timer = advanceWindowTime;                      // 维持住就一直续期
                }
                else
                {
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        // 转衰退：记录当前档位进度作为起点（由 accum 换算，不由 scale 换算），
                        // accum 随后随档位同步下降，不瞬间清零。
                        state = AdvanceState.Decay;
                        timer = 0f;
                        decayT = 0f;
                        decayStartProgress = ProgressFromAccum(accum);
                    }
                }
                break;

            case AdvanceState.Decay:
                if (!ignoring && stepFwd > advanceAccumThreshold)
                {
                    accum = Mathf.Min(accum + stepFwd, maxAccum);   // 以衰退后的当前值为起点续接
                    EnterAdvancing(ref state, ref timer, ref decayT, ref anticipT);
                }
                break;
        }
    }

    /// <summary>进入前进状态：刷新窗口计时、清零衰退计时、触发一次弹开预备。</summary>
    private void EnterAdvancing(ref AdvanceState state, ref float timer, ref float decayT, ref float anticipT)
    {
        state = AdvanceState.Advancing;
        timer = advanceWindowTime;
        decayT = 0f;
        if (forwardAnticipationEnable) anticipT = Mathf.Max(0.01f, forwardAnticipationTime);
    }

    /// <summary>
    /// 衰退：按【倍率档位】计时。五个档位就是倍率表本身 1.5 / 1.3 / 1.2 / 1.1 / 1.05，
    /// 每降一档固定耗时 advanceDecayStepTime 秒；档位间倍率差越来越小 ⇒ 下降速度自然先快后慢。
    /// 由此：1.5→1.0 = 5s、1.3→1.0 = 4s、1.2→1.0 = 3s、1.1→1.0 = 2s、1.05→1.0 = 1s。
    ///
    /// ⛔ 不直接写 scale：只推进档位进度 p 并同步 accum，返回 p 由调用方换算目标倍率，
    ///    再交给弹簧平滑跟随（旧实现直接赋值 + 清零弹簧速度，会造成"瞬间被切小 / 被冻结"）。
    /// 返回：当前档位进度 p；-1 表示衰退已结束（已转 Idle，accum 归零）。
    /// </summary>
    private float AdvanceDecay(ref AdvanceState state, ref float accum,
                               ref float decayT, float decayStartProgress, float dt)
    {
        decayT += dt;
        float step = Mathf.Max(0.01f, advanceDecayStepTime);
        float p = decayStartProgress - decayT / step;      // 档位进度匀速下降（每档 step 秒）

        if (p <= 0f)
        {
            accum = 0f;
            state = AdvanceState.Idle;                     // 衰退结束 -> 回到 Idle
            decayT = 0f;
            return -1f;
        }

        accum = AccumFromProgress(p);                      // accum 随档位进度同步下降（不瞬间清零）
        return p;
    }

    /// <summary>刷新档位表缓存（倍率表 + 对应的累积距离下界）。
    /// ⚠ 调用方须在每帧驱动前调用一次（DriveRuntime 开头），映射函数本身不再各自刷新。</summary>
    private void RefreshDecayLevels()
    {
        _decayLevels[0] = scaleAtRest;
        _decayLevels[1] = scaleTiny;
        _decayLevels[2] = scaleAbove0_2;
        _decayLevels[3] = scaleAbove0_3;
        _decayLevels[4] = scaleAbove0_5;
        _decayLevels[5] = scaleAbove1_0;

        _accumLevels[0] = 0f;
        _accumLevels[1] = accumTiny;
        _accumLevels[2] = accumAbove0_2;
        _accumLevels[3] = accumAbove0_3;
        _accumLevels[4] = accumAbove0_5;
        _accumLevels[5] = accumAbove1_0;
    }

    /// <summary>累积距离 -> 档位进度（accum=0→0, accumTiny→1, 0.2→2, 0.3→3, 0.5→4, 1.0→5；档间线性插值）。
    /// ⛔ P4A 起，衰退起点由【accum】换算，不再由 scale 换算 —— 保证"大小只由累积距离决定"单向成立。</summary>
    private float ProgressFromAccum(float a)
    {
        if (a <= _accumLevels[0]) return 0f;
        for (int i = 1; i < _accumLevels.Length; i++)
        {
            if (a <= _accumLevels[i])
            {
                float lo = _accumLevels[i - 1], hi = _accumLevels[i];
                float f = Mathf.Clamp01((a - lo) / Mathf.Max(1e-6f, hi - lo));
                return (i - 1) + f;
            }
        }
        return _accumLevels.Length - 1;
    }

    /// <summary>档位进度 -> 累积距离（ProgressFromAccum 的逆运算）。衰退时用它让 accum 随档位同步下降。</summary>
    private float AccumFromProgress(float p)
    {
        int last = _accumLevels.Length - 1;
        if (p <= 0f) return _accumLevels[0];
        if (p >= last) return _accumLevels[last];
        int i = Mathf.Clamp(Mathf.FloorToInt(p), 0, last - 1);
        return Mathf.Lerp(_accumLevels[i], _accumLevels[i + 1], p - i);
    }

    /// <summary>档位进度 -> 倍率。</summary>
    private float ScaleFromProgress(float p)
    {
        int last = _decayLevels.Length - 1;
        if (p <= 0f) return _decayLevels[0];
        if (p >= last) return _decayLevels[last];
        int i = Mathf.Clamp(Mathf.FloorToInt(p), 0, last - 1);
        return Mathf.Lerp(_decayLevels[i], _decayLevels[i + 1], p - i);
    }

    /// <summary>【大小 = f(累积距离)】累积距离 -> 倍率（档间线性插值，平滑无硬边）。这是决定大小的唯一入口。</summary>
    private float ScaleFromAccum(float a) => ScaleFromProgress(ProgressFromAccum(a));

    /// <summary>P2c 目标倍率：除 Idle 外都按累积距离放大。
    /// ⛔ Decay 状态也必须返回 ScaleFromAccum(accum)，因为 accum 由 AdvanceDecay 同步维护；
    ///    若返回 scaleAtRest，会在 Decay 分支未覆盖的代码路径里把目标压回 1.0，造成"被冻结在 1.0 倍"的观感。</summary>
    private float GetAdvanceTarget(AdvanceState state, float accum)
    {
        if (state == AdvanceState.Idle) return scaleAtRest;
        return ScaleFromAccum(accum);
    }


    /// <summary>
    /// P2b 预备推进：推进剩余时间，并按 sin(π·phase) 算出当前弹开偏移。
    /// 曲线含义：phase 0→1 对应偏移 0→最大→0，即"平滑弹开到最远，再平滑回位"，起步与收尾都无突跳。
    /// 弹开方向由 ApplyScale 处决定（红墙 -x / 蓝墙 +x，都是远离对手）。
    /// </summary>
    private void UpdateAnticip(ref float anticipT, ref float off, float dt)
    {
        float T = Mathf.Max(0.01f, forwardAnticipationTime);
        if (anticipT <= 0f) { off = 0f; return; }
        anticipT -= dt;
        if (anticipT <= 0f) { anticipT = 0f; off = 0f; return; }
        float phase = Mathf.Clamp01(1f - anticipT / T);
        off = forwardAnticipationPullback * Mathf.Sin(Mathf.PI * phase);
    }


    /// <summary>
    /// P2 弹簧阻尼缩放：把墙当成有惯性的肉体，被推时先抗拒再顺从。
    /// a = -k(scale - target) - c·v，c = 2·√k·dampingRatio（1 = 临界阻尼）。
    /// 用固定小步长子步进积分，保证低帧率下也不发散。
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
        // 总开关关闭（P1 自发光分层方案落地前）：只保留基础亮度，前进加亮与撞击加亮都不生效
        if (!enableBrightnessFx)
        {
            SetWallFloat(redWall, "_Opacity", _redBaseOpacity);
            SetWallFloat(blueWall, "_Opacity", _blueBaseOpacity);
            return;
        }
        float breath = breathOpacityAmp * _breathFade * (1f - _breathK);   // k=0（合拢最紧）最亮
        float rGlow = breath + (_redScale - 1f) * glowPerEnlarge;           // 红墙：自己被放大才变亮
        float bGlow = breath + (_blueScale - 1f) * glowPerEnlarge;
        SetWallFloat(redWall, "_Opacity", Mathf.Clamp(_redBaseOpacity + rGlow, 0f, opacityMax));
        SetWallFloat(blueWall, "_Opacity", Mathf.Clamp(_blueBaseOpacity + bGlow, 0f, opacityMax));
    }

    /// <summary>写墙材质浮点。
    /// ⚠ 运行时必须用 `.material`（自动取本渲染器的实例），不能用 `.sharedMaterial`：
    ///   红蓝若共用同一个 .mat 资产会互相串台，且写入会落盘到资源文件、退出 Play 后不恢复。
    ///   编辑模式仍用 sharedMaterial，避免材质实例泄漏进场景。</summary>
    private void SetWallFloat(Transform wall, string prop, float v)
    {
        if (wall == null) return;
        var mr = wall.GetComponent<MeshRenderer>();
        if (mr == null) return;
        var m = Application.isPlaying ? mr.material : mr.sharedMaterial;
        if (m != null) m.SetFloat(prop, v);
    }

    // ==================================================================
    // P1 场景染色：让场景与植被被冲击波的光影响
    //
    // ⛔ 为什么不用 URP 灯光：本项目场景的 shader（ScenePropSprite / GrassFringe / GroundEdge）
    //   全部是 Unlit 或「假光照」（手写常量 lightDir），URP 光照系统照不到它们——
    //   挂真实灯是纯浪费。所以改走全局参数 + shader 内距离衰减：
    //     - 不产生任何实时光源开销（移动端友好）
    //     - 形状完全可控（比点光源的球形衰减更适合长条拱形）
    //     - 墙移动时光效自动跟随（每帧写全局）
    //
    // 两墙都在场景里，各自用自己的位置算距离；取【较亮的那一侧】写入全局。
    // ==================================================================
    private static readonly int ShockGlowColorId = Shader.PropertyToID("_ShockGlowColor");
    private static readonly int ShockGlowParamsId = Shader.PropertyToID("_ShockGlowParams");
    private static readonly int ShockGlowParams2Id = Shader.PropertyToID("_ShockGlowParams2");
    private static readonly int ShockGlowEnabledId = Shader.PropertyToID("_ShockGlowEnabled");

    // 墙的发光颜色（取自 ShockwaveMeshGenerator.glowColor），在 CaptureBase 时缓存
    private Color _redGlowColor = new Color(1f, 0.5f, 0.5f, 1f);
    private Color _blueGlowColor = new Color(0.4f, 0.6f, 1f, 1f);

    private void ApplySceneGlow()
    {
        if (!sceneGlowEnabled || sceneGlowStrength <= 0.0001f)
        {
            // 关闭时把强度写 0（而不是恢复 Enabled=0）—— shader 侧会整段短路，画面完全回到原样
            Shader.SetGlobalFloat(ShockGlowParams2Id, 0f);
            Shader.SetGlobalFloat(ShockGlowEnabledId, 0f);
            return;
        }
        Shader.SetGlobalFloat(ShockGlowEnabledId, 1f);

        // ---- 选贡献更强的一侧：放大倍率越大 => 能量越强 => 染色越亮 ----
        Transform src = redWall;
        float srcScale = _redScale;
        Color srcColor = _redGlowColor;
        if (blueWall != null && (!sceneGlowBothSides || _blueScale > _redScale))
        {
            src = blueWall; srcScale = _blueScale; srcColor = _blueGlowColor;
        }
        if (src == null) return;

        // 强度随放大倍率上升：1.0 倍时约 1.0×，1.5 倍时约 1.4×。
        // ⛔ 不改变墙自身亮度，只改「照到场景上的量」。
        float k = Mathf.Clamp(0.6f + 0.8f * srcScale, 0.4f, 1.8f);
        float strength = sceneGlowStrength * k;

        Vector3 p = src.position;
        Shader.SetGlobalVector(ShockGlowParamsId,
            new Vector4(p.x, p.y, p.z, Mathf.Max(0.0001f, sceneGlowRange)));
        Shader.SetGlobalVector(ShockGlowParams2Id, new Vector4(strength, 2f, 0f, 0f));
        Shader.SetGlobalColor(ShockGlowColorId, srcColor);
    }

    /// <summary>绕墙 mesh 局部内侧边 pivot 缩放：保持中缝侧边不动，向外扩，间隙不变。
    /// ⛔【通道分离铁律】f 只来自「累积距离 accum」，extra 只来自「呼吸开合 + 回弹弹开」：
    ///    - f     -> 只写 localScale（大小通道）
    ///    - extra -> 只写 localPosition（位移通道）
    /// 两者互不影响、互不叠加 —— 任何把 scale 塞进 extra 来源、或把位移塞进 f 的改动都违反铁律。</summary>
    /// <summary>把「墙形状」系数实时乘进两墙的 baseScale。
    /// ⚠【为什么不能只在 CaptureBase 里乘】用户会在 Play 模式里调 Inspector，
    ///   而 CaptureBase 只在 Awake / 非Play 的 OnValidate 里跑 —— Play 中改参数不会重新捕获，
    ///   参数看起来"完全没效果"。所以这里改成每帧应用，改完立刻见效。
    ///   它只改 baseScale（大小通道的基准），不碰 extra（位移通道），也不改中缝位置。</summary>
    private void ApplyShapeScale()
    {
        if (wallWidthScaleX == 1f && wallWidthScaleY == 1f && wallWidthScaleZ == 1f) return;
        if (_redRawScale == Vector3.zero) return;   // 还没捕获 base，跳过
        _redBaseScale = Vector3.Scale(_redRawScale, WallShapeScale);
        if (_blueRawScale != Vector3.zero)
            _blueBaseScale = Vector3.Scale(_blueRawScale, WallShapeScale);
    }
    /// <summary>把「墙形状」系数实时乘进两墙的 baseScale。
    /// ⚠【为什么不能只在 CaptureBase 里乘】用户会在 Play 模式里调 Inspector，
    ///   而 CaptureBase 只在 Awake / 非 Play 的 OnValidate 里跑 —— Play 中改参数不会重新捕获，
    ///   参数看起来"完全没效果"。所以这里改成每帧应用，改完立刻见效。</summary>
    private void ApplyShapeScale()
    {
        if (wallWidthScaleX == 1f && wallWidthScaleY == 1f && wallWidthScaleZ == 1f) return;
        if (_redRawScale == Vector3.zero) return;   // 还没捕获 base，跳过
        _redBaseScale = Vector3.Scale(_redRawScale, WallShapeScale);
        if (_blueRawScale != Vector3.zero)
            _blueBaseScale = Vector3.Scale(_blueRawScale, WallShapeScale);
    }

    /// <summary>绕墙 mesh 局部内侧边 pivot 缩放：保持中缝侧边不动，向外扩，间隙不变。
    /// ⛔【通道分离铁律】f 只来自「累积距离 accum」，extra 只来自「呼吸开合 + 回弹弹开」：
    ///    - f-> 只写 localScale（大小通道）
    ///    - extra -> 只写 localPosition（位移通道）
    /// 两者互不影响、互不叠加 —— 任何把 scale 塞进 extra 来源、或把位移塞进 f 的改动都违反铁律。
    ///
    /// ⚠【2026-10-08 修正·用户实测】补偿量必须是 <c>offset * (1 - totalF)</c>，
    ///   其中 <c>totalF = shapeX * f</c> —— <b>形状系数必须参与补偿</b>。
    ///   原来只写 (1 - f)：调大形状系数后补偿量为 0，
    ///   墙的中缝侧跟着变宽、直接压到对面墙（用户截图证实）。
    /// </summary>
    private void ApplyScale(Transform wall, Vector3 basePos, Quaternion baseRot, Vector3 baseScale,
                            Vector3 meshPivot, float f, Vector3 extra, float shapeX)
    {
        if (wall == null) return;
        float totalF = f * shapeX;   // 形状系数 × 累积倍率
        wall.localScale = new Vector3(baseScale.x * f, baseScale.y * f, baseScale.z * f);
        // offset 用 baseScale（含形状系数）算，与 localScale 保持同一套基准
        Vector3 offset = baseRot * new Vector3(baseScale.x * meshPivot.x,
                                               baseScale.y * meshPivot.y,
                                               baseScale.z * meshPivot.z);
        wall.localPosition = basePos + offset * (1f - totalF) + extra;
    }


    /// <summary>捕获你手动调好的 Transform 作为 base；并自动算出每堵墙"贴中缝内侧边"在 mesh 局部空间的坐标。</summary>
    /// <summary>墙形状倍率（Inspector 的 wallWidthScaleX/Y/Z）。
    /// ⚠ 只在 CaptureBase 里乘到 baseScale，<b>不改动 ApplyScale 的任何逻辑</b>——
    ///   ApplyScale 仍绕内侧边 pivot 缩放，所以 X 变大时墙往两侧外扩、中缝不变。
    ///   默认 (1,1,1) 时行为与改动前完全一致。</summary>
    private Vector3 WallShapeScale => new Vector3(wallWidthScaleX, wallWidthScaleY, wallWidthScaleZ);

    private void CaptureBase()
    {
        var rg = redWall != null ? redWall.GetComponent<ShockwaveMeshGenerator>() : null;
        var bg = blueWall != null ? blueWall.GetComponent<ShockwaveMeshGenerator>() : null;
        // ⛔ autoFit 与本品互斥：若仍有开启，强制关闭后再捕获（避免 base 被污染）。
        if ((rg != null && rg.autoFit) || (bg != null && bg.autoFit))
        {
            if (rg != null) rg.autoFit = false;
            if (bg != null) bg.autoFit = false;
            Debug.LogWarning("[ShockwavePreview] CaptureBase 检测到 autoFit 仍为开启，已自动关闭并继续捕获 base。"
                + "autoFit 会每帧覆盖墙的 localScale，base 被污染后推进到底会表现为『被切成 1.0 倍』。", this);
        }

        _baseRootPos = transform.position;
        if (redWall != null)
        {
            var mf = redWall.GetComponent<MeshFilter>();
            _redBasePos = redWall.localPosition;
            _redBaseRot = redWall.localRotation;
            // 应用「墙形状」倍率（只在捕获 base 时乘一次，运行时 ApplyScale 的逻辑完全不动）。
            // ⚠ 必须用 Vector3.Scale（逐分量），不能用 `*` —— Unity C# 不支持 Vector3 * Vector3（CS0019）。
            _redRawScale = redWall.localScale;                       // 原始缩放（不含形状系数）
            _redBaseScale = Vector3.Scale(_redRawScale, WallShapeScale);
            _redPivot = (mf != null && mf.sharedMesh != null) ? GetInnerEdgeMeshLocal(mf, true) : Vector3.zero;
        }
        if (blueWall != null)
        {
            var mf = blueWall.GetComponent<MeshFilter>();
            _blueBasePos = blueWall.localPosition;
            _blueBaseRot = blueWall.localRotation;
            _blueRawScale = blueWall.localScale;
            _blueBaseScale = Vector3.Scale(_blueRawScale, WallShapeScale);
            _bluePivot = (mf != null && mf.sharedMesh != null) ? GetInnerEdgeMeshLocal(mf, false) : Vector3.zero;
        }
        // 缓存两墙的基础 Opacity（呼吸亮度在其上叠加；SyncMaterial 每帧会写回该值）
        _redBaseOpacity = rg != null ? rg.opacity : 0f;
        _blueBaseOpacity = bg != null ? bg.opacity : 0f;
        // P1 场景染色：缓存两墙的发光色（用户在 Inspector 改 glowColor 后立即生效）
        _redGlowColor = rg != null ? rg.glowColor : _redGlowColor;
        _blueGlowColor = bg != null ? bg.glowColor : _blueGlowColor;
        _redScale = 1f; _blueScale = 1f;
        _redVel = 0f; _blueVel = 0f;
        // P2b 前进预备状态归零（避免 Recapture 后残留半个预备相位导致墙歪着）
        _redAnticipT = 0f; _blueAnticipT = 0f;
        _redAnticipOff = 0f; _blueAnticipOff = 0f;
        // P2c 前进状态归零
        _redAdvState = AdvanceState.Idle; _blueAdvState = AdvanceState.Idle;
        _redAdvTimer = 0f; _blueAdvTimer = 0f;
        _redAdvAccum = 0f; _blueAdvAccum = 0f;
        // 忽略前进计时归零（扣血补分用）
        _redIgnoreT = 0f; _blueIgnoreT = 0f;
        // 衰退状态归零（accum 由档位进度反算，不需要额外缓存起始 accum）
        _redDecayT = 0f; _blueDecayT = 0f;
        _redDecayStartProgress = 0f; _blueDecayStartProgress = 0f;
        // 待分摊队列归零（得分 -> 世界位移）
        _redPending = 0f; _bluePending = 0f;
        _redFlashing = false; _blueFlashing = false; _redFlashT = 0f; _blueFlashT = 0f;
        _breathT = 0f; _breathK = 0f; _breathFade = 0f;   // 呼吸相位归零，从合拢最紧处平滑起步
        // 对峙静止判定 / 已应用倍率 归零
        _idleTimer = 0f; _lastCenterX = (centerLine != null) ? centerLine.currentX : centerX;
        _lastAppliedRedScale = 1f; _lastAppliedBlueScale = 1f;
        _lastAppliedShapeScale = _redBaseScale;   // 避免首次因"没变化"而跳过写入
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
            // ⚠ 不用「插值 + 内嵌三元/字符串字面量」：Unity 的 C# 在 "$...{(x ? "a" : "b")}..."
            //   这种写法下会报 CS8076 / CS0361（插值里嵌字符串字面量会被当成结束插值）。
            string which = (withMesh != null) ? "带 Mesh" : "第一个";
            Debug.LogWarning("[ShockwavePreview] 发现 " + count + " 个名为 '" + name + "' 的子物体。"
                + "已优先使用 " + which + "。"
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

    /// <summary>中缝 X：接了 centerLine 用 currentX，否则用静态预览的 centerX。</summary>
    private float SeamX => (centerLine != null) ? centerLine.currentX : centerX;

    private void GetFronts(out float rFront, out float bFront)
    {
        float half = centerGap * 0.5f;
        rFront = SeamX - half;
        bFront = SeamX + half;
    }

    private void ApplyToWalls()
    {
        EnsureWalls();
        GetFronts(out float rFront, out float bFront);

        var rm = redWall != null ? redWall.GetComponent<ShockwaveMeshGenerator>() : null;
        if (rm != null) { rm.backX = leftEdge; rm.frontX = rFront; rm.Refresh(); }

        var bm = blueWall != null ? blueWall.GetComponent<ShockwaveMeshGenerator>() : null;
        if (bm != null) { bm.backX = rightEdge; bm.frontX = bFront; bm.Refresh(); }
    }

    void OnDrawGizmos()
    {
        GetFronts(out float rFront, out float bFront);
        Gizmos.color = Color.white;
        Gizmos.DrawLine(new Vector3((rFront + bFront) * 0.5f, 0f, -3.75f),
                        new Vector3((rFront + bFront) * 0.5f, 1.5f, 3.75f));
    }
}
