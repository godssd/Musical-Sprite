using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 世界空间能量槽（2026-09-15 改为「绑定音轨固定锚点 + Sprite 素材图」，替换旧的程序化生成版本）。
///
/// 【方案要点】
/// - 位置由 LaneAnchor 固定锚点决定（绑定音轨、不跟随角色）；无该音轨角色则不建条。
/// - 平躺 -90°X 面朝上方（俯视相机可见）；整体主缩放 barScaleX(宽)/barScaleY(高) 默认 1（=纯贴图大小，matchTextureSize 时由作画决定）。
/// - 外观使用 Sprite 素材图（barBgSprite / barFillSprite），方便美术替换；留空则从 Resources/EnergyBar/ 自动加载（无需手动拖拽）。
/// - 单主动槽(如各 Aibo)=单条 4 态，双主动槽(主角)=**1 条 2 段**（一个 BG + 两个 Fill，视觉上是一条槽显示两段）。
/// - 条从左向右填充；涨条用 SpriteMask 遮罩揭示（Fill 恒定满尺寸、零缩放零位移，避免纹理被横向压扁畸变）。
/// - 颜色全部 Inspector 可配：themeSide0=玩家（两侧统一用此主题），themeSide1=蓝方（当前停用保留）。
/// - 黑底(Track) 默认隐藏（showTrack 开关）；三层 + Glow 均同 sortingOrder、靠局部 Z 偏移分层，并在 Start 把相机设为 Custom 透明排序（axis=相机朝向），实现真实三维遮挡（spine 角色与条按相机距离前后遮挡）。
///
/// 【Aibo 单条 4 态】
///   ① 一般：按能量比例填充（黄条 + 黄框）。
///   ② 充盈：满 + 白↔黄闪烁 + **整个条+槽整体缩放脉冲**（barPulseScale=10%，快涨慢落；条与环一起变大缩小）。
///   ③ CD：条 + 外框都变暗黄静止。
///   ④ 释放：条 + 外框**变白**，1s **单次脉冲**白闪（releaseFlashDuration）+ **延迟扣除**（先快后慢 releaseDrainRate，1s 结束刚好清空）→ 进 CD（暗黄）。Glow 层默认关（useGlow）。
///
/// 【主角 1 条 2 段 3 态】
///   ① 可使用：对应段满 + 灰↔白闪烁（heroReadyA/B）。
///   ② CD：对应段**灰**从 0 涨到满（两段独立，由 ActiveSkillRuntime.CooldownLeft/Total 驱动），涨满即就绪；技能生效期(Grow/Charming/Releasing)保持空。
///   ③ 释放：对应段**闪白 + 单次脉冲** 1s（与 aibo 一致），结束立刻清空 → 立即进下一轮 CD（灰涨条）。无冷却技能：无 CD 表现，恒定满+闪烁。
///
/// 本组件由 FeverManager.EnsureEnergyBar() 兜底创建（旧 ScreenSpaceOverlay 版 EnergyBarUIController 已删除）。
/// </summary>
public class EnergyBarWorldSpace : MonoBehaviour
{
    /// <summary>由 FeverManager.EnsureEnergyBar() 在 Start 阶段主动调用一次，确保场景有且仅有一个。</summary>
    public static void EnsureExists()
    {
        if (FindFirstObjectByType<EnergyBarWorldSpace>() != null) return;
        var go = new GameObject("EnergyBarWorldSpace");
        go.AddComponent<EnergyBarWorldSpace>();
    }

    [System.Serializable]
    public struct EnergyTheme
    {
        [Header("Aibo（单主动）")]
        public Color aiboFrame, aiboTrack, aiboFill, aiboFullA, aiboFullB, aiboCd, aiboRelease, aiboOutline;
        [Header("主角（双主动）")]
        public Color heroFrame, heroTrack, heroFill, heroReadyA, heroReadyB, heroRelease, heroOutline;
    }

    [System.Serializable]
    public class LaneAnchor
    {
        public int side;       // 0=左/玩家侧, 1=右/AI侧
        public int lane;       // 0~3；-1=玩家自身
        public Vector3 position;
        public bool useThis = true;
    }

    [Header("布局")]
    public float barWorldWidth = 0.85f;
    public float barWorldHeight = 0.12f;
    public float slotGap = 0.12f;
    public int sortingOrder = 0;
    [Header("深度分层（同 sortingOrder，靠世界 Z 微偏移实现真实前后遮挡）")]
    [Tooltip("Track/填充/外环/Glow 在 barRoot 局部 Z 上的高度差（局部 Z 对应世界 Y）。数值仅用于拉开彼此距离，参与相机自定义透明排序。")]
    public float layerZTrack = 0.0000f;
    public float layerZFill = 0.0012f;
    public float layerZBG = 0.0024f;
    public float layerZGlow = -0.0030f;

    [Header("Sprite 素材（Inspector 可拖拽替换；推荐放在 Assets/Art/UI/，颜色由主题色 tint）")]
    [Tooltip("胶囊环 Sprite（纯白，中空；运行时 tint 成黄/银白框）。单主动用。留空自动加载 Resources/EnergyBar/EnergyBar_BG.png。")]
    public Sprite barBgSprite;
    [Tooltip("玩家双主动胶囊环 Sprite（纯白，含中间竖杠；运行时 tint 成银白框）。留空自动加载 Resources/EnergyBar/EnergyBar_BG_Dual.png。")]
    public Sprite barBgDualSprite;
    [Tooltip("内部底色 Sprite（中灰实心胶囊，运行时 tint 成深色衬底）。留空自动加载 Resources/EnergyBar/EnergyBar_Track.png。")]
    public Sprite barTrackSprite;
    [Tooltip("填充条 Sprite（纯白实心胶囊；运行时 tint 成黄/银白/暗灰）。留空自动加载 Resources/EnergyBar/EnergyBar_Fill.png。")]
    public Sprite barFillSprite;
    [Tooltip("玩家双主动填充条 Sprite（纯白，整条含左右两段；运行时从正中切两半、原尺寸显示）。留空自动加载 Resources/EnergyBar/EnergyBar_Fill_Dual.png。")]
    public Sprite barFillDualSprite;

    [Header("固定锚点（绑定音轨，不绑角色；无角色不显示）")]
    [Tooltip("每个 (side,lane) 一个固定世界坐标。lane=-1=玩家自身（自动取玩家标记位置）。")]
    public List<LaneAnchor> laneAnchors = new List<LaneAnchor>
    {
        // 左侧(玩家侧 side0) AI 4 轨（保持原坐标）
        new LaneAnchor { side = 0, lane = 0, position = new Vector3(-7.20f, 0.01f, -2.71f) }, // 大狗
        new LaneAnchor { side = 0, lane = 1, position = new Vector3(-6.51f, 0.06f, -1.30f) }, // 屎屎
        new LaneAnchor { side = 0, lane = 2, position = new Vector3(-6.42f, 0.17f, 0.14f) },  // 布姆
        new LaneAnchor { side = 0, lane = 3, position = new Vector3(-7.14f, 0.06f, 1.81f) },  // 小黑
        // 左侧玩家(小熊) 固定锚点
        new LaneAnchor { side = 0, lane = -1, position = new Vector3(-7.221f, 1.07f, -0.851f) },
        // 右侧(玩家侧 side1) —— 玩家 + AI 4 轨（按用户截图 Transform）
        new LaneAnchor { side = 1, lane = -1, position = new Vector3(7.23f, 1.07f, -0.851f) },  // 右玩家(小熊)
        new LaneAnchor { side = 1, lane = 0, position = new Vector3(6.9f, 0.45f, -2.73f) },     // 大狗
        new LaneAnchor { side = 1, lane = 1, position = new Vector3(6.29f, 0.36f, -1.37f) },    // 屎屎
        new LaneAnchor { side = 1, lane = 2, position = new Vector3(6.24f, 0.45f, 0.06f) },     // 布姆
        new LaneAnchor { side = 1, lane = 3, position = new Vector3(6.88f, 0.45f, 1.57f) },     // 小黑
    };
    [Header("尺寸模式")]
    [Tooltip("true=三层直接按 PNG 原生世界尺寸显示（1 unit = 100px），整体大小完全由你作画决定；false=按下方 barWorldWidth/Height 强制尺寸。")]
    public bool matchTextureSize = true;
    [Tooltip("整体主缩放 X（宽度方向）。默认 1=不缩放（纯贴图大小）；想整体压窄再调。")]
    public float barScaleX = 1f;
    [Tooltip("整体主缩放 Y（高度/厚度方向，旋转后对应世界 Z）。默认 1=不缩放；与 matchTextureSize 配合决定整体大小。")]
    public float barScaleY = 1f;
    [Tooltip("编辑器 Gizmo：在每个锚点画青色线框球。")]
    public bool showAnchorDebug = false;
    [Tooltip("是否显示黑底(Track)层。默认 false = 只显示槽环 + 条；需要黑底时勾回。")]
    public bool showTrack = false;

    [Header("Aibo 动画")]
    [Tooltip("充满发光层开关。默认关——它复用 BG 放大 1.12x 加法混合，和你的贴图容易重影出双环；需要时再勾。")]
    public bool useGlow = false;
    [Tooltip("脉冲幅度（整体缩放比例，0.10 = 放大/缩小 10%）。快涨慢落包络，作用于条+槽整个 barRoot。")]
    public float barPulseScale = 0.10f;     // 脉冲整体缩放幅度（10%）
    public float aiboPulseCycle = 0.6f;     // aibo 充满闪烁脉冲周期（秒）
    public float aiboGlowCycle = 0.6f;
    public float releaseFlashDuration = 0.5f; // 释放表现总时长（秒）：白闪 + 单次脉冲（aibo/玩家一致，0.5s 末正好清空、三者同收尾）
    [Tooltip("aibo 延迟扣除速率常数：越大开头掉得越猛、尾巴越短（先快后慢差异越明显）。仅 aibo 生效；玩家释放为满白闪 1s 后立刻清空。")]
    public float releaseDrainRate = 4.5f;
    [Tooltip("调试：Console 打印每个 aibo 条的能量比例变化（定位'命中不积累'）。运行后看日志。")]
    public bool debugEnergy = false;

    [Header("主角 动画")]
    [Tooltip("主角就绪闪烁脉冲周期（秒）。")]
    public float heroBlinkCycle = 0.8f;

    [Header("主题色 side 0（红方/玩家）")]
    public EnergyTheme themeSide0 = new EnergyTheme
    {
        aiboFrame = new Color(1.00f, 0.82f, 0.10f, 1f),
        aiboTrack = new Color(0.08f, 0.06f, 0.02f, 1f),
        aiboFill  = new Color(1.00f, 0.82f, 0.10f, 1f),
        aiboFullA = new Color(1.00f, 1.00f, 1.00f, 1f), // 充满闪烁亮端改为纯白（白↔黄忽闪）
        aiboFullB = new Color(1.00f, 0.78f, 0.05f, 1f),
        aiboCd    = new Color(0.55f, 0.42f, 0.05f, 1f),
        aiboRelease = new Color(1.00f, 1.00f, 1.00f, 1f), // 释放亮色改为纯白（与黄色差异明显）
        aiboOutline = new Color(0.04f, 0.04f, 0.04f, 1f),
        heroFrame = new Color(0.90f, 0.90f, 0.95f, 1f),
        heroTrack = new Color(0.08f, 0.08f, 0.10f, 1f),
        heroFill  = new Color(0.85f, 0.85f, 0.90f, 1f),
        heroReadyA = new Color(1.00f, 1.00f, 1.00f, 1f),
        heroReadyB = new Color(0.55f, 0.55f, 0.60f, 1f),
        heroRelease = new Color(1.00f, 1.00f, 1.00f, 1f), // 释放闪白（与 aibo 一致）
        heroOutline = new Color(0.04f, 0.04f, 0.04f, 1f),
    };

    [Header("主题色 side 1（蓝方/AI——当前已停用：两侧统一用 themeSide0，此字段保留备用）")]
    public EnergyTheme themeSide1 = new EnergyTheme
    {
        aiboFrame = new Color(0.30f, 0.65f, 1.00f, 1f),
        aiboTrack = new Color(0.05f, 0.16f, 0.34f, 1f),
        aiboFill  = new Color(0.30f, 0.65f, 1.00f, 1f),
        aiboFullA = new Color(0.80f, 0.95f, 1.00f, 1f),
        aiboFullB = new Color(0.30f, 0.65f, 1.00f, 1f),
        aiboCd    = new Color(0.16f, 0.32f, 0.55f, 1f),
        aiboRelease = new Color(0.60f, 0.85f, 1.00f, 1f),
        aiboOutline = new Color(0.04f, 0.04f, 0.04f, 1f),
        heroFrame = new Color(0.55f, 0.70f, 0.95f, 1f),
        heroTrack = new Color(0.10f, 0.14f, 0.22f, 1f),
        heroFill  = new Color(0.50f, 0.65f, 0.90f, 1f),
        heroReadyA = new Color(0.85f, 0.95f, 1.00f, 1f),
        heroReadyB = new Color(0.40f, 0.50f, 0.70f, 1f),
        heroRelease = new Color(0.85f, 0.95f, 1.00f, 1f), // 释放闪白（蓝方）
        heroOutline = new Color(0.04f, 0.04f, 0.04f, 1f),
    };

    private readonly List<WorldEnergyBar> bars = new List<WorldEnergyBar>();
    private bool pendingRebuild = false;

    // —— Sprite 加载缓存（避免每帧 Resources.Load） —— //
    private Sprite _cachedBgSprite, _cachedBgDualSprite, _cachedTrackSprite, _cachedFillSprite, _cachedFillDualSprite;
    private bool _spriteLoaded = false;

    private void EnsureSpritesLoaded()
    {
        if (_spriteLoaded) return;
        _cachedBgSprite = LoadBarSprite(barBgSprite, "EnergyBar_BG");
        _cachedBgDualSprite = LoadBarSprite(barBgDualSprite, "EnergyBar_BG_Dual");
        _cachedTrackSprite = LoadBarSprite(barTrackSprite, "EnergyBar_Track");
        _cachedFillSprite = LoadBarSprite(barFillSprite, "EnergyBar_Fill");
        _cachedFillDualSprite = LoadBarSprite(barFillDualSprite, "EnergyBar_Fill_Dual");
        _spriteLoaded = true;
    }

    private static Sprite LoadBarSprite(Sprite inspectorVal, string resName)
    {
        // Inspector 拖了就用拖的（美术可手动覆盖）；为空则自动从 Resources/EnergyBar/ 加载，无需手动拖。
        if (inspectorVal != null) return inspectorVal;
        var sp = Resources.Load<Sprite>("EnergyBar/" + resName);
        if (sp == null)
            Debug.LogWarning($"[EnergyBarWorldSpace] Sprite 自动加载失败：Resources/EnergyBar/{resName}。请确认 Assets/Art/UI/EnergyBar/Resources/EnergyBar/{resName}.png 存在。");
        return sp;
    }

    void Start()
    {
        SetupTransparencySort();
        BuildAll();
        CharacterRoster.OnRosterChanged += OnRosterChanged;
    }

    // 真实三维遮挡：让所有透明物体（spine 角色 + 能量槽）按相机距离排序，而非用 sortingOrder 硬压
    private void SetupTransparencySort()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = cam.transform.forward;
        }
    }

    void OnDestroy()
    {
        CharacterRoster.OnRosterChanged -= OnRosterChanged;
    }

    void Update()
    {
        for (int i = 0; i < bars.Count; i++)
            if (bars[i] != null) bars[i].Tick();
    }

    void LateUpdate()
    {
        for (int i = 0; i < bars.Count; i++)
            if (bars[i] != null) bars[i].Follow();
    }

    private void OnRosterChanged()
    {
        if (pendingRebuild) return;
        pendingRebuild = true;
        StartCoroutine(RebuildNextFrame());
    }

    private IEnumerator RebuildNextFrame()
    {
        yield return null;
        pendingRebuild = false;
        _spriteLoaded = false; // 强制重新加载 Sprite（可能被用户在 Inspector 替换了）
        BuildAll();
    }

    private void BuildAll()
    {
        foreach (var b in bars) if (b.root != null) Destroy(b.root);
        bars.Clear();
        EnsureSpritesLoaded();

        for (int side = 0; side < 2; side++)
        {
            var p = CharacterRoster.GetPlayer(side);
            if (p != null) BuildForCharacter(p, side, -1);
            for (int lane = 0; lane < 4; lane++)
            {
                var inst = CharacterRoster.GetTeam(side, lane);
                if (inst != null) BuildForCharacter(inst, side, lane);
            }
        }
    }

    private void BuildForCharacter(CharacterClass c, int side, int lane)
    {
        if (c == null) return;
        bool isPlayer = (lane == -1) || c.isPlayer;
        int n;
        if (isPlayer)
        {
            n = (c.activeSlots != null && c.activeSlots.Count > 0) ? c.activeSlots.Count : 1;
        }
        else
        {
            if (c.maxEnergies == null) return;
            n = 0;
            for (int i = 0; i < c.maxEnergies.Length; i++)
                if (c.maxEnergies[i] > 0f) n++;
            if (n == 0) return;
        }

        bool isDual = n >= 2;
        // 颜色统一（2026-09-16 用户拍板）：右侧(side1)与左侧同用 themeSide0（黄白主题）。
        // themeSide1（蓝方）字段保留不删，日后要区分两侧配色时把本行改回
        // `var theme = (side == 1) ? themeSide1 : themeSide0;` 即可整体回退。
        var theme = themeSide0;
        Vector3 anchorPos = GetAnchorPosition(side, lane);

        GameObject root = new GameObject($"EBW_S{side}_L{lane}_{c.displayName}");
        root.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        root.transform.localScale = Vector3.one; // 主缩放改放到 barRoot（含 X/Y），默认 1=纯贴图大小
        root.transform.position = anchorPos;

        // 玩家（isDual）：只创建 **1 个** WorldEnergyBar，内部管理 1 个 BG + 2 个 Fill
        // Aibo（非 isDual 或 isDual 但非 player）：每个有能量的 slot 创建 1 个独立条
        if (isPlayer && isDual)
        {
            // 主角：1 条 2 段
            bars.Add(new WorldEnergyBar(this, c, side, lane, isDual, theme, root, 0f, anchorPos, n));
        }
        else if (isPlayer)
        {
            bars.Add(new WorldEnergyBar(this, c, side, lane, false, theme, root, 0f, anchorPos, 1));
        }
        else
        {
            float totalW = isDual ? (n * barWorldWidth + (n - 1) * slotGap) : barWorldWidth;
            for (int s = 0; s < c.maxEnergies.Length; s++)
            {
                if (c.maxEnergies[s] <= 0f) continue;
                float xOff = isDual
                    ? (-totalW / 2f + barWorldWidth / 2f + s * (barWorldWidth + slotGap))
                    : 0f;
                bars.Add(new WorldEnergyBar(this, c, side, lane, s, isDual, theme, root, xOff, anchorPos));
            }
        }
    }

    private Vector3 GetAnchorPosition(int side, int lane)
    {
        foreach (var a in laneAnchors)
        {
            if (a != null && a.useThis && a.side == side && a.lane == lane)
                return a.position;
        }
        var m = FindMarker(side, lane);
        return m != null ? m.position : Vector3.zero;
    }

    private bool HasFixedAnchor(int side, int lane)
    {
        foreach (var a in laneAnchors)
            if (a != null && a.useThis && a.side == side && a.lane == lane) return true;
        return false;
    }

    public static Transform FindMarker(int side, int lane)
    {
        var markers = FindObjectsByType<CharacterCubeMarker>(FindObjectsSortMode.None);
        foreach (var m in markers)
        {
            if (m.side != side) continue;
            if (lane == -1) { if (m.IsPlayer) return m.transform; }
            else { if (!m.IsPlayer && m.laneIndex == lane) return m.transform; }
        }
        return null;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showAnchorDebug) return;
        foreach (var b in bars) if (b != null) b.DrawAnchorGizmo();
    }
#endif

    // ====================================================================== //
    //  WorldEnergyBar：单根条视图 + 状态机
    //  - Aibo 模式：1 BG + 1 Fill
    //  - 玩家 Dual 模式：1 BG + 2 Fill（1 条显示 2 段）
    // ====================================================================== //
    private class WorldEnergyBar
    {
        private readonly EnergyBarWorldSpace mgr;
        private CharacterClass owner; // 非 readonly：每帧向 CharacterRoster 重新解析活的实例，避免拿到被重建后的旧实例（能量/冷却不显示的根因）
        private readonly int slot;
        private readonly bool isDual;
        private readonly bool isPlayerDual; // true = 主角 1 条 2 段模式
        private readonly EnergyTheme theme;
        private readonly int side;
        private readonly int lane;
        private readonly int totalSegments; // 玩家 dual=2, 其他=1

        public GameObject root;
        private GameObject barRoot;
        private SpriteRenderer bgSR;
        private SpriteRenderer trackSR;
        private SpriteRenderer[] fillSRs;     // [0]=左段/单段, [1]=右段(仅玩家dual)
        private SpriteRenderer glowSR;
        // Fill 基准（贴图原生缩放，恒定不随能量变；涨条靠 SpriteMask 遮罩展宽揭示，零畸变零位移）
        private float fillScaleX, fillScaleY, fillUnitMinX;
        private float[] fillLeftX;            // 每段左缘的本地 X
        // 遮罩（SpriteMask）：每段独立，左缘钉 fillLeftX、按 ratios 展宽揭示
        private SpriteMask[] fillMasks;
        private float[] maskL;                // 每段 Fill 左缘本地 X（遮罩左缘）
        private float[] maskW;                // 每段 Fill 满宽（本地单位，遮罩总宽）
        private float maskH;                  // Fill 满高（本地单位，遮罩恒定高）
        private static Texture2D _maskTex;    // 共享 1x1 白底（仅用于 stencil，永不绘制，纯透明不可见）
        private static Sprite _maskSprite;
        private Transform playerMarker;
        private Vector3 anchorPos;
        private float baseScaleX, baseScaleY;

        // 状态（per-segment for player dual）
        private float[] ratios;
        private float pulseCurrent = 1f;
        private bool[] wasFull;
        private bool releasing = false;
        private int releaseSeg = 0;            // 当前正在释放的片段索引（Tick 据此跳过该段，其余段继续更新，避免玩家另一段静止）
        private Coroutine relCo;
        private float _dbgLastRatio = -1f;
        private bool _dbgLastFull = false;

        // 主题色
        private Color frame, track, fillNormal, fullA, fullB, cd, releaseCol, outlineCol;

        // 玩家 dual 专用构造
        public WorldEnergyBar(EnergyBarWorldSpace mgr, CharacterClass owner, int side, int lane,
                              bool isDual, EnergyTheme theme, GameObject rootGo, float xOff, Vector3 anchorPos, int segmentCount)
        {
            this.mgr = mgr; this.owner = owner; this.side = side; this.lane = lane;
            this.slot = 0; this.isDual = isDual;
            this.isPlayerDual = isDual && (lane == -1 || owner.isPlayer);
            this.theme = theme; this.root = rootGo;
            this.anchorPos = anchorPos;
            this.totalSegments = segmentCount;
            if (lane == -1) this.playerMarker = EnergyBarWorldSpace.FindMarker(0, -1);

            ratios = new float[segmentCount];
            wasFull = new bool[segmentCount];
            for (int i = 0; i < segmentCount; i++) { ratios[i] = 0f; wasFull[i] = false; }

            if (isDual)
            {
                frame = theme.heroFrame; track = theme.heroTrack; fillNormal = theme.heroFill;
                fullA = theme.heroReadyA; fullB = theme.heroReadyB; cd = theme.heroReadyB; releaseCol = theme.heroRelease;
                outlineCol = theme.heroOutline;
            }
            else
            {
                frame = theme.aiboFrame; track = theme.aiboTrack; fillNormal = theme.aiboFill;
                fullA = theme.aiboFullA; fullB = theme.aiboFullB; cd = theme.aiboCd; releaseCol = theme.aiboRelease;
                outlineCol = theme.aiboOutline;
            }

            BuildVisuals(xOff);
        }

        // 旧签名兼容（aibo 单条用）
        public WorldEnergyBar(EnergyBarWorldSpace mgr, CharacterClass owner, int side, int lane,
                              int slot, bool isDual, EnergyTheme theme, GameObject rootGo, float xOff, Vector3 anchorPos)
            : this(mgr, owner, side, lane, isDual, theme, rootGo, xOff, anchorPos, 1)
        {
            this.slot = slot;
        }

        // 共享遮罩贴图：1x1 纯白，仅写入 stencil，自身不绘制（SpriteMask 不画颜色）→ 纯透明不可见，只遮 Fill
        private static Sprite GetMaskSprite()
        {
            if (_maskSprite != null) return _maskSprite;
            _maskTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _maskTex.SetPixel(0, 0, Color.white);
            _maskTex.Apply();
            _maskSprite = Sprite.Create(_maskTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
            return _maskSprite;
        }

        private void BuildVisuals(float xOff)
        {
            barRoot = new GameObject("Bar");
            barRoot.transform.SetParent(root.transform, false);
            barRoot.transform.localPosition = new Vector3(xOff, 0f, 0f);
            // 整体主缩放（matchTextureSize 时=纯贴图大小；barScaleX/Y 默认 1=不缩放）。脉冲只作用于 Fill，不覆盖此值。
            barRoot.transform.localScale = new Vector3(mgr.barScaleX, mgr.barScaleY, 1f);

            // 玩家双主动用含竖杠的 BG，其余用普通环 BG
            Sprite bgSpr = (isPlayerDual && mgr._cachedBgDualSprite != null) ? mgr._cachedBgDualSprite : mgr._cachedBgSprite;
            Sprite trackSpr = mgr._cachedTrackSprite;
            Sprite fillSpr = mgr._cachedFillSprite;

            // ---- Track（内部底色：中灰实心胶囊，运行时 tint 成深色衬底） ---- //
            if (mgr.showTrack)
            {
                var trackGo = new GameObject("Track");
                trackGo.transform.SetParent(barRoot.transform, false);
                trackGo.transform.localPosition = new Vector3(0f, 0f, mgr.layerZTrack);
                trackSR = trackGo.AddComponent<SpriteRenderer>();
                trackSR.sprite = trackSpr;
                trackSR.sortingOrder = mgr.sortingOrder;   // 同层，靠 Z 偏移分层
                trackSR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trackSR.receiveShadows = false;
            }

            // ---- Fill(s)（纯白胶囊，运行时 tint；玩家双主动用 Fill_Dual 切左右半） ---- //
            fillSRs = new SpriteRenderer[totalSegments];
            fillLeftX = new float[totalSegments];
            fillMasks = new SpriteMask[totalSegments];
            maskL = new float[totalSegments];
            maskW = new float[totalSegments];
            Sprite[] fillSprites = new Sprite[totalSegments];
            if (isPlayerDual && mgr._cachedFillDualSprite != null)
            {
                // 运行时把整条贴图从正中切两半，左半管左段、右半管右段（原尺寸，不缩放）
                var dtex = mgr._cachedFillDualSprite.texture;
                float ppu = mgr._cachedFillDualSprite.pixelsPerUnit;
                float hw = dtex.width * 0.5f;
                fillSprites[0] = Sprite.Create(dtex, new Rect(0f, 0f, hw, dtex.height), new Vector2(0f, 0.5f), ppu);
                fillSprites[1] = Sprite.Create(dtex, new Rect(hw, 0f, hw, dtex.height), new Vector2(0f, 0.5f), ppu);
            }
            else
            {
                for (int s = 0; s < totalSegments; s++) fillSprites[s] = fillSpr;
            }
            Sprite msk = GetMaskSprite();
            for (int s = 0; s < totalSegments; s++)
            {
                var fillGo = new GameObject($"Fill_{s}");
                fillGo.transform.SetParent(barRoot.transform, false);
                fillSRs[s] = fillGo.AddComponent<SpriteRenderer>();
                fillSRs[s].sprite = fillSprites[s];
                fillSRs[s].sortingOrder = mgr.sortingOrder;
                fillSRs[s].maskInteraction = SpriteMaskInteraction.VisibleInsideMask;  // 仅 Fill 受遮罩裁剪，BG/Glow 不受影响
                fillSRs[s].transform.localPosition = new Vector3(0f, 0f, mgr.layerZFill);
                fillSRs[s].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                fillSRs[s].receiveShadows = false;

                // 每段独立遮罩（SpriteMask 写 stencil；自身不可见，只裁 Fill）
                var maskGo = new GameObject($"Mask_{s}");
                maskGo.transform.SetParent(barRoot.transform, false);
                fillMasks[s] = maskGo.AddComponent<SpriteMask>();
                fillMasks[s].sprite = msk;
                fillMasks[s].isCustomRangeActive = true;
                fillMasks[s].backSortingOrder = mgr.sortingOrder - 1;
                fillMasks[s].frontSortingOrder = mgr.sortingOrder + 1;
                fillMasks[s].transform.localPosition = new Vector3(0f, 0f, mgr.layerZFill);
            }

            // ---- BG 环（纯白胶囊环，运行时 tint 成框色） ---- //
            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(barRoot.transform, false);
            bgSR = bgGo.AddComponent<SpriteRenderer>();
            bgSR.sprite = bgSpr;
            bgSR.sortingOrder = mgr.sortingOrder;
            bgGo.transform.localPosition = new Vector3(0f, 0f, mgr.layerZBG);
            bgSR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bgSR.receiveShadows = false;
            bgSR.color = frame;                            // 外框基准色（aibo=黄框 / 主角=银白框；CD 时动态改暗黄）

            // ---- 设计宽/高：matchTextureSize=true 时取贴图原生尺寸（1 unit=100px），否则取强制尺寸 ---- //
            float designW = mgr.matchTextureSize
                ? (bgSpr != null ? bgSpr.bounds.size.x : mgr.barWorldWidth)
                : mgr.barWorldWidth;
            float designH = mgr.matchTextureSize
                ? (bgSpr != null ? bgSpr.bounds.size.y : mgr.barWorldHeight)
                : mgr.barWorldHeight;

            if (bgSpr != null)
            {
                baseScaleX = designW / bgSpr.bounds.size.x;
                baseScaleY = designH / bgSpr.bounds.size.y;
                if (trackSpr != null && trackSR != null)
                {
                    trackSR.transform.localScale = new Vector3(baseScaleX, baseScaleY, 1f);
                    trackSR.color = track;
                }
                bgSR.transform.localScale = new Vector3(baseScaleX, baseScaleY, 1f);

                for (int s = 0; s < totalSegments; s++)
                {
                    Sprite fseg = fillSRs[s].sprite;
                    if (fseg == null) continue;
                    float segW = isPlayerDual ? fseg.bounds.size.x : designW; // 双主动=半条原生宽；其余=整条设计宽
                    fillScaleX = segW / fseg.bounds.size.x;                    // 双主动=1（原尺寸），其余按尺寸模式
                    fillScaleY = designH / fseg.bounds.size.y;
                    fillUnitMinX = fseg.bounds.min.x;                          // pivot 到左缘的本地偏移
                    fillLeftX[s] = isPlayerDual ? (s == 0 ? -designW / 2f : 0f) : -designW / 2f;
                    // Fill 恒定满尺寸、固定左缘：零缩放零位移，涨条全靠遮罩揭示
                    fillSRs[s].transform.localScale = new Vector3(fillScaleX, fillScaleY, 1f);
                    fillSRs[s].transform.localPosition = new Vector3(
                        fillLeftX[s] - fillScaleX * fillUnitMinX, 0f, mgr.layerZFill);
                    // 记录遮罩几何：左缘 + 满宽（每段独立，双段不同宽）
                    maskL[s] = fillLeftX[s];
                    maskW[s] = fillScaleX * fseg.bounds.size.x;
                    maskH = designH;
                    ApplyMask(s);                                              // 初始按 ratios(=0) 收起遮罩
                }
            }

            // ---- Glow（复用 BG ring，additive，仅 useGlow 时创建；默认关避免与你的贴图重影出双环） ---- //
            if (mgr.useGlow)
            {
                var glowGo = new GameObject("Glow");
                glowGo.transform.SetParent(barRoot.transform, false);
                glowSR = glowGo.AddComponent<SpriteRenderer>();
                glowSR.sprite = bgSpr;
                var gMat = RuntimeSpriteUtility.AdditiveGlow;
                if (gMat != null) glowSR.material = gMat;
                glowSR.sortingOrder = mgr.sortingOrder;
                glowGo.transform.localPosition = new Vector3(0f, 0f, mgr.layerZGlow);
                glowSR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glowSR.color = new Color(fullA.r, fullA.g, fullA.b, 0f);
                if (bgSpr != null)
                    glowSR.transform.localScale = new Vector3(baseScaleX * 1.12f, baseScaleY * 1.12f, 1f);
            }
            else
            {
                glowSR = null;
            }
        }

        private void SetGlow(float a)
        {
            if (glowSR == null) return;
            Color c = glowSR.color;
            c.a = a;
            glowSR.color = c;
        }

        private void SetFill(int seg, Color c) { if (fillSRs != null && seg < fillSRs.Length && fillSRs[seg] != null) fillSRs[seg].color = c; }
        private void SetAllFills(Color c) { for (int s = 0; s < fillSRs.Length; s++) SetFill(s, c); }
        private void SetFrame(Color c) { if (bgSR != null) bgSR.color = c; } // 外框(BG)运行时动态 tint（平时黄框 / CD 暗黄 / 释放亮黄）

        // 脉冲包络：快涨慢落（前 ~25% 周期快速涨到顶，其余缓慢落回）。返回 0..1，叠加到 barRoot 整体缩放。
        private float PulseEnvelope(float cycle) { return PulseEnvelopeAt(cycle, Time.time); }
        private float PulseEnvelopeAt(float cycle, float t)
        {
            float cyc = Mathf.Max(0.05f, cycle);
            float k = (t % cyc) / cyc;          // 0..1
            const float attack = 0.25f;         // 快速涨的阶段占比（变大快）
            return (k < attack) ? (k / attack) : (1f - (k - attack) / (1f - attack));
        }

        public void Follow()
        {
            if (root == null) return;
            if (owner == null) { root.SetActive(false); return; }
            root.SetActive(true);
            // 固定锚点(side,lane 在 laneAnchors 中)则钉死，不跟随角色
            if (mgr.HasFixedAnchor(side, lane)) return;
            if (lane == -1)
            {
                if (playerMarker == null) playerMarker = EnergyBarWorldSpace.FindMarker(0, -1);
                if (playerMarker != null) root.transform.position = playerMarker.position;
            }
        }

#if UNITY_EDITOR
        public void DrawAnchorGizmo()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(anchorPos, 0.12f);
        }
#endif

        // 每帧解析活的角色实例：避免能量槽持有被重建后的旧 CharacterClass（命中/冷却数据加到活实例、旧实例不变 → 不显示）
        private void ResolveOwner()
        {
            if (lane == -1) return;                 // 玩家（lane=-1）：roster 无对应条目，保留构建时缓存的实例
            var live = CharacterRoster.GetTeam(side, lane);
            if (live != null) owner = live;
        }

        public void Tick()
        {
            if (owner == null || fillSRs == null || fillSRs[0] == null) return;
            ResolveOwner(); // 每帧向 CharacterRoster 重新解析活的实例（lane>=0 的 aibo；玩家 lane=-1 用缓存）

            int frozen = releasing ? releaseSeg : -1;   // 释放中：只冻结正在释放的那一段，其余段继续走正常逻辑（避免玩家另一段静止）
            if (isPlayerDual)
            {
                TickPlayerDual(frozen);
            }
            else
            {
                TickSingle(frozen);
            }
            ApplyRatios();
        }

        // ---- Aibo / 单条逻辑 ---- //
        private void TickSingle(int frozen)
        {
            if (frozen == 0) return;   // 该段正在释放：条宽/颜色由 ReleaseCo 驱动，这里整体跳过（wasFull 已在 TriggerRelease 置 false，不会重复触发）
            float maxE = (owner.maxEnergies != null && slot < owner.maxEnergies.Length) ? owner.maxEnergies[slot] : 0f;
            float curE = (owner.currentEnergies != null && slot < owner.currentEnergies.Length) ? owner.currentEnergies[slot] : 0f;
            float target = maxE > 0f ? Mathf.Clamp01(curE / maxE) : 0f;
            bool full = (owner.isFullyChargedArr != null && slot < owner.isFullyChargedArr.Length) && owner.isFullyChargedArr[slot];
            bool busy = (owner.skillBusyArr != null && slot < owner.skillBusyArr.Length) && owner.skillBusyArr[slot];

            if (wasFull[0] && !full && curE <= 0.0001f)
            {
                wasFull[0] = full;
                TriggerRelease(0);
                return;
            }
            wasFull[0] = full;

            if (mgr.debugEnergy && (Mathf.Abs(target - _dbgLastRatio) > 0.01f || full != _dbgLastFull))
            {
                Debug.Log($"[EBW][debug] {owner.displayName} S{side}L{lane} slot{slot} curE={curE:F1}/{maxE:F1} ratio={target:F2} full={full} busy={busy}");
                _dbgLastRatio = target; _dbgLastFull = full;
            }

            if (busy) { CooldownAibo(); ratios[0] = target; }
            else if (full) { FullAibo(); ratios[0] = 1f; }
            else { NormalAibo(); ratios[0] = target; }
        }

        // ---- 玩家 1 条 2 段逻辑 ---- //
        // 注意：owner.activeSlots 是 List<SkillSlot>（配置数据），运行时实例挂在角色 marker 上（ActiveSkillRuntime 组件）。
        // 这里按 owner 匹配全场扫描并缓存（懒加载；扫描为空不缓存，下一帧重试，规避 Start 时序）。
        private ActiveSkillRuntime[] cachedRuntimes;
        private ActiveSkillRuntime GetRuntimeForSlot(int s)
        {
            if (owner == null) return null;
            if (cachedRuntimes == null)
            {
                var all = UnityEngine.Object.FindObjectsByType<ActiveSkillRuntime>(FindObjectsSortMode.None);
                var list = new System.Collections.Generic.List<ActiveSkillRuntime>();
                foreach (var r in all) if (r != null && r.owner == owner) list.Add(r);
                if (list.Count > 0) cachedRuntimes = list.ToArray();
            }
            if (cachedRuntimes == null) return null;
            foreach (var r in cachedRuntimes)
                if (r != null && r.SlotIndex == s) return r;
            return null;
        }
        private void TickPlayerDual(int frozen)
        {
            for (int s = 0; s < totalSegments && s < 2; s++)
            {
                if (s == frozen) { wasFull[s] = false; continue; }   // 释放中的段：冻结，跳过其正常更新（不重复触发释放）
                bool busy = (owner.skillBusyArr != null && s < owner.skillBusyArr.Length) && owner.skillBusyArr[s];
                bool full = !busy; // 玩家满 = 可使用 = 不 busy
                // 释放检测（满 → 空：玩家开火触发）
                if (wasFull[s] && !full) { wasFull[s] = full; TriggerRelease(s); return; }
                wasFull[s] = full;

                if (!busy)
                {
                    ReadyHeroSegment(s); ratios[s] = 1f;                 // 可使用：满 + 灰白闪烁
                }
                else
                {
                    var rt = GetRuntimeForSlot(s);
                    if (rt != null && rt.CooldownTotal > 0.0001f && rt.CurrentPhase == ActiveSkillRuntime.Phase.Cooldown)
                    {
                        // CD 计时中：灰条从 0 涨到 1（两段独立），涨满即技能可用
                        ChargingHeroSegment(s);
                        ratios[s] = Mathf.Clamp01(1f - rt.CooldownLeft / rt.CooldownTotal);
                    }
                    else
                    {
                        // 技能生效中(Grow/Charming/Releasing)保持空；无冷却技能无 CD 表现，恒定满+闪烁
                        if (rt != null && rt.CooldownTotal > 0.0001f) { ChargingHeroSegment(s); ratios[s] = 0f; }
                        else { ReadyHeroSegment(s); ratios[s] = 1f; }
                    }
                }
            }
        }

        private void ApplyRatios()
        {
            // 脉冲：整体缩放（条+槽一起变大缩小），作用于 barRoot 主缩放；Fill 形状/位置不变，遮罩比例随之放大，缝隙不变
            if (barRoot != null)
                barRoot.transform.localScale = new Vector3(mgr.barScaleX * pulseCurrent, mgr.barScaleY * pulseCurrent, 1f);
            // Fill 恒定满尺寸、固定位置（BuildVisuals 已设）；涨条仅由遮罩揭示
            for (int s = 0; s < fillSRs.Length; s++)
                ApplyMask(s);
        }

        // 用 SpriteMask 按 reveal(ratios) 展宽揭示 Fill：遮罩左缘钉在 fillLeftX，宽度 = ratios × 满宽，高度恒满高。
        // r=0 遮罩收为 0 → Fill 全隐；r=1 满宽 → Fill 全露。Fill 永不缩放/位移，故全程零畸变。
        private void ApplyMask(int s)
        {
            if (fillMasks == null || s >= fillMasks.Length || fillMasks[s] == null) return;
            if (maskW == null || s >= maskW.Length) return;
            float r = Mathf.Clamp01(ratios[s]);
            float w = maskW[s] * r;
            const float unit = 0.01f;   // 1px 遮罩贴图 @ ppu100 的本地尺寸
            float cx = maskL[s] + w * 0.5f;
            fillMasks[s].transform.localPosition = new Vector3(cx, 0f, mgr.layerZFill);
            fillMasks[s].transform.localScale = new Vector3(Mathf.Max(w, 1e-4f) / unit, maskH / unit, 1f);
        }

        // ---- Aibo 态 ---- //
        private void NormalAibo() { pulseCurrent = 1f; SetAllFills(fillNormal); SetGlow(0f); SetFrame(frame); }
        private void FullAibo()
        {
            float cyc = Mathf.Max(0.05f, mgr.aiboGlowCycle);
            float k = (Time.time % cyc) / cyc;
            float w = Mathf.Sin(k * Mathf.PI * 2f) * 0.5f + 0.5f;
            SetAllFills(Color.Lerp(fullA, fullB, w));
            // 整体脉冲（条+槽一起）：快涨慢落包络
            pulseCurrent = 1f + mgr.barPulseScale * PulseEnvelope(mgr.aiboPulseCycle);
            SetGlow(0.45f * (0.6f + 0.4f * w));
            SetFrame(frame);
        }
        private void CooldownAibo() { pulseCurrent = 1f; SetAllFills(cd); SetGlow(0f); SetFrame(cd); } // CD：条+外框都变暗黄

        // ---- 主角态 ---- //
        private void ChargingHeroSegment(int s) { pulseCurrent = 1f; SetFill(s, cd); SetGlow(0f); }
        private void ReadyHeroSegment(int s)
        {
            // 释放中：整条脉冲由 ReleaseCo 驱动（白闪脉冲），这里不再覆盖 pulseCurrent，只更新另一段的颜色闪烁，避免脉冲被冲掉
            if (!releasing) pulseCurrent = 1f + mgr.barPulseScale * PulseEnvelope(mgr.heroBlinkCycle);
            float cyc = Mathf.Max(0.05f, mgr.heroBlinkCycle);
            float k = (Time.time % cyc) / cyc;
            float w = Mathf.Sin(k * Mathf.PI * 2f) * 0.5f + 0.5f;
            SetFill(s, Color.Lerp(fullA, fullB, w));
            SetGlow(0.45f * (0.6f + 0.4f * w));
        }

        // ---- 释放 ---- //
        private void TriggerRelease(int seg)
        {
            releaseSeg = seg;
            wasFull[seg] = false;   // 标记已释放，避免下一帧状态机重复触发
            releasing = true;
            if (relCo != null) mgr.StopCoroutine(relCo);
            relCo = mgr.StartCoroutine(ReleaseCo(seg));
        }

        private IEnumerator ReleaseCo(int seg)
        {
            releasing = true;
            float dur = Mathf.Max(0.01f, mgr.releaseFlashDuration);
            bool delayedDrain = !isPlayerDual;   // aibo：延迟扣除（先快后慢）；玩家：满白闪后立刻清空
            Color post = cd;                     // 释放后状态色：aibo=暗黄CD；玩家条宽将缩0、颜色淡到灰(heroReadyB=cd)
            // 起始帧：纯白闪一下
            SetFill(seg, releaseCol);
            if (!isPlayerDual) SetFrame(releaseCol);
            SetGlow(0f);

            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float flash = Mathf.Exp(-3f * k);           // 开头猛白、尾巴快没
                // 单次脉冲（整体缩放）：快涨慢落包络，起止=1、中间放大最多
                pulseCurrent = 1f + mgr.barPulseScale * PulseEnvelopeAt(dur, t);
                ratios[seg] = delayedDrain ? Mathf.Exp(-mgr.releaseDrainRate * k) : 1f;
                // 白闪颜色：从后状态色 向 纯白 按 flash 提亮；0.5s 末 flash→0 即回到后状态色，与脉冲/清空同收尾
                Color col = Color.Lerp(post, releaseCol, flash);
                SetFill(seg, col);
                if (!isPlayerDual) SetFrame(col);
                ApplyRatios();
                yield return null;
            }
            pulseCurrent = 1f;
            ratios[seg] = 0f;               // 0.5s 后立刻清空
            ApplyRatios();
            releasing = false;             // 下一帧进入 CD（aibo=暗黄；玩家=灰涨条）
            relCo = null;
        }
    }
}
