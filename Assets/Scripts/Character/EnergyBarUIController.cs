using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 主动技能能量槽 UI（2026-08-26 用户新增需求）。
///
/// 【样式要求】
/// - 边框棕黄色、能量条黄色；
/// - 能量累积时 Fill 从一侧平铺到另一侧，长度随 currentEnergy/maxEnergy 平滑变化；
/// - 满能量（OnEnergyFull）时整条能量条发光持续闪动（Fill 颜色在 fillColor 与 glowColor 间正弦振荡）；
/// - 释放瞬间（OnEnergyDepleted）整条能量条闪白并持续衰减长度到 0（ConsumeShrinkCo）；
/// - 衰减结束后重新显示空 bar，等待下一次充能。
///
/// 【结构】
/// 单文件两个类：
/// - EnergyBarUI          : 单根能量槽的视图/动画组件（含 Fill 锚点控制、闪动/衰减协程）；
/// - EnergyBarUIController: 全局管理器，启动时为左/右玩家 4 个队伍角色（共 8 个）各建一根能量槽，
///                          自动跟随 CharacterRoster（OnRosterChanged 时清掉旧 bar 并重建），
///                          与 EnergyVFXPlaceholder 一样的「运行时兜底创建」模式。
///
/// 挂载：场景任意 GameObject（场景无实例时由 RuntimeInitializeOnLoadMethod 兜底自建）。
/// </summary>
public class EnergyBarUIController : MonoBehaviour
{
    /// <summary>场景加载后若无 EnergyBarUIController 实例则自动创建（与 FeverManager.EnsureEnergyVFX 同套路）。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (FindFirstObjectByType<EnergyBarUIController>() != null) return;
        var go = new GameObject("EnergyBarUIController");
        go.AddComponent<EnergyBarUIController>();
    }

    [Header("整体配置（运行时创建 Canvas）")]
    [Tooltip("Canvas sortingOrder，建议在 HPBarCanvas 之上一层（避免被血条遮挡）")]
    public int canvasSortingOrder = 55;
    [Tooltip("距离屏幕底部的偏移（像素，正值向上）")]
    public float bottomOffset = 36f;
    [Tooltip("左右玩家 4 个能量槽的总横向跨度（像素）")]
    public float rowWidth = 880f;
    [Tooltip("每根能量槽宽度 / 高度（像素）")]
    public Vector2 barSize = new Vector2(208f, 36f);
    [Tooltip("单侧 4 根能量槽之间的水平间距（像素）")]
    public float barSpacing = 16f;

    [Header("主题色（用户指定：边框棕黄、能量条黄色）")]
    [Tooltip("未填充区颜色（同时充当「边框」视觉）—— 棕黄色")]
    public Color borderColor = new Color(0.55f, 0.40f, 0.18f, 1f);
    [Tooltip("能量条填充主色 —— 黄色")]
    public Color fillColor = new Color(1f, 0.85f, 0.20f, 1f);
    [Tooltip("满能量闪动 / 释放闪白时的叠加色 —— 纯白")]
    public Color glowColor = Color.white;
    [Tooltip("释放闪白衰减阶段穿插的过渡色 —— 暗黄")]
    public Color releaseFadeColor = new Color(0.90f, 0.78f, 0.20f, 1f);

    [Header("动画参数")]
    [Tooltip("Fill 长度每秒变化速度；过小会看起来延迟，过大则瞬变")]
    public float fillSpeed = 6f;
    [Tooltip("满能量发光闪动正弦周期（秒）")]
    public float glowCycleSeconds = 0.6f;
    [Tooltip("释放瞬间闪白衰减总时长（秒）")]
    public float consumeShrinkDuration = 0.9f;

    // —— 运行时状态 —— //
    private GameObject canvasGo;
    private RectTransform rowLeft;   // 屏幕底部左侧容器（4 个能量槽）
    private RectTransform rowRight;  // 屏幕底部右侧容器（4 个能量槽）
    private readonly List<EnergyBarUI> bars = new List<EnergyBarUI>();
    private readonly HashSet<CharacterClass> subscribed = new HashSet<CharacterClass>();

    void Start()
    {
        BuildCanvas();
        Rebuild();
        CharacterRoster.OnRosterChanged += OnRosterChanged;
    }

    void OnDestroy()
    {
        CharacterRoster.OnRosterChanged -= OnRosterChanged;
        UnsubscribeAll();
    }

    /// <summary>花名册变化时清掉所有旧 bar 并重新为在场角色建立（支持重启用）。</summary>
    private void OnRosterChanged()
    {
        // 延迟一帧重建 —— CharacterBattleSystem 在同一帧内多次触发（RegisterXxx 各调用一次）
        if (!isPendingRebuild)
        {
            isPendingRebuild = true;
            StartCoroutine(RebuildNextFrame());
        }
    }

    private bool isPendingRebuild = false;
    private IEnumerator RebuildNextFrame()
    {
        yield return null;
        isPendingRebuild = false;
        Rebuild();
    }

    /// <summary>为当前花名册里所有有主动技能的 4+4 = 8 个队伍角色建立能量槽，并订阅事件。</summary>
    private void Rebuild()
    {
        UnsubscribeAll();
        DestroyAllBars();

        for (int side = 0; side < 2; side++)
        {
            for (int lane = 0; lane < 4; lane++)
            {
                var inst = CharacterRoster.GetTeam(side, lane);
                if (inst == null) continue;
                if (!inst.HasActiveSkill) continue;                  // 没有主动技能 = 不显示能量槽
                if (inst.maxEnergy <= 0f) continue;                  // 能量门=0 的主动技能也不需要能量槽

                CreateBar(side, lane, inst);
            }
        }
    }

    private void CreateBar(int side, int lane, CharacterClass owner)
    {
        RectTransform row = side == 0 ? rowLeft : rowRight;
        if (row == null) return;                                   // Canvas 还没建好（边界）

        // —— 创建 Box（棕黄色背景当作「边框/未填充区」）—— //
        GameObject boxGo = new GameObject($"EnergyBar_S{side}_L{lane}_{owner.displayName}");
        boxGo.transform.SetParent(row, false);
        RectTransform boxRect = boxGo.AddComponent<RectTransform>();
        boxRect.anchorMin = new Vector2(0f, 0f);
        boxRect.anchorMax = new Vector2(0f, 0f);
        boxRect.pivot = new Vector2(0f, 1f);
        boxRect.sizeDelta = barSize;
        // 横排位置：单侧 4 个，从 row 锚点开始向右逐个排列
        float perBarStep = barSize.x + barSpacing;
        boxRect.anchoredPosition = new Vector2(lane * perBarStep, 0f);

        // 背景（棕黄色 = "边框/未填充区" 视觉）
        GameObject bgGo = new GameObject("Background");
        bgGo.transform.SetParent(boxGo.transform, false);
        RectTransform bgRect = bgGo.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        Image bgImage = bgGo.AddComponent<Image>();
        bgImage.color = borderColor;
        bgImage.raycastTarget = false;

        // Fill（黄色 = 能量条本体；按 currentEnergy/maxEnergy 调整锚点宽度）
        GameObject fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(boxGo.transform, false);
        RectTransform fillRect = fillGo.AddComponent<RectTransform>();
        Image fillImage = fillGo.AddComponent<Image>();
        fillImage.color = fillColor;
        fillImage.raycastTarget = false;

        // 在 Background 之上绘制 Fill
        fillGo.transform.SetSiblingIndex(bgGo.transform.GetSiblingIndex() + 1);

        // —— 安装 UI 行为组件 —— //
        EnergyBarUI bar = boxGo.AddComponent<EnergyBarUI>();
        bar.Setup(owner, side, lane, fillImage, fillRect, this);
        bars.Add(bar);

        // —— 订阅事件 —— //
        owner.OnEnergyFull += bar.OnEnergyFull;
        owner.OnEnergyDepleted += bar.OnEnergyDepleted;
        subscribed.Add(owner);
    }

    private void DestroyAllBars()
    {
        foreach (var bar in bars)
        {
            if (bar != null && bar.gameObject != null)
                Destroy(bar.gameObject);
        }
        bars.Clear();
    }

    private void UnsubscribeAll()
    {
        foreach (var bar in bars)
        {
            if (bar == null || bar.owner == null) continue;
            bar.owner.OnEnergyFull -= bar.OnEnergyFull;
            bar.owner.OnEnergyDepleted -= bar.OnEnergyDepleted;
        }
        subscribed.Clear();
    }

    private void BuildCanvas()
    {
        canvasGo = new GameObject("EnergyBarCanvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasSortingOrder;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // 屏幕底部居中的一条横向容器，分左右镜像
        float rowH = barSize.y + 24f;
        GameObject rowLeftGo = new GameObject("RowLeft");
        rowLeftGo.transform.SetParent(canvasGo.transform, false);
        rowLeft = rowLeftGo.AddComponent<RectTransform>();
        rowLeft.anchorMin = new Vector2(0.5f, 0f);
        rowLeft.anchorMax = new Vector2(0.5f, 0f);
        rowLeft.pivot = new Vector2(1f, 1f);                                       // 左行：pivot 在右，便于靠拢中央
        rowLeft.sizeDelta = new Vector2(rowWidth, rowH);
        // 整行向左平移 (rowWidth/2 + 中线间距)
        rowLeft.anchoredPosition = new Vector2(-12f, bottomOffset);                // 中线左侧 12px

        GameObject rowRightGo = new GameObject("RowRight");
        rowRightGo.transform.SetParent(canvasGo.transform, false);
        rowRight = rowRightGo.AddComponent<RectTransform>();
        rowRight.anchorMin = new Vector2(0.5f, 0f);
        rowRight.anchorMax = new Vector2(0.5f, 0f);
        rowRight.pivot = new Vector2(0f, 1f);                                       // 右行：pivot 在左
        rowRight.sizeDelta = new Vector2(rowWidth, rowH);
        rowRight.anchoredPosition = new Vector2(12f, bottomOffset);                  // 中线右侧 12px
    }

    // —— 公开给 EnergyBarUI 访问主题色的快捷接口 —— //
    public Color FillColor   => fillColor;
    public Color BorderColor => borderColor;
    public Color GlowColor   => glowColor;
    public Color ReleaseFadeColor => releaseFadeColor;
    public float  FillSpeed  => fillSpeed;
    public float  GlowCycleSeconds  => glowCycleSeconds;
    public float  ConsumeShrinkDuration => consumeShrinkDuration;
}

/// <summary>单根能量槽的视图/动画行为；由 EnergyBarUIController.Rebuild() 创建并驱动。</summary>
public class EnergyBarUI : MonoBehaviour
{
    [HideInInspector] public CharacterClass owner;
    [HideInInspector] public int side;
    [HideInInspector] public int laneIndex;

    private Image fillImage;
    private RectTransform fillRect;
    private EnergyBarUIController controller;

    private float currentFillAmount = 0f;     // 当前显示长度 (0..1)
    private float targetFillAmount  = 0f;     // 目标长度（随 currentEnergy/maxEnergy 变化）
    private bool  isConsuming = false;        // 释放衰减协程进行中
    private Coroutine fullGlowCo;
    private Coroutine consumeCo;
    private float lastConsumeSnapRatio = 0f;  // ConsumeShrinkCo 起始长度

    /// <summary>由 EnergyBarUIController.CreateBar 调用：绑定数据和主题。</summary>
    public void Setup(CharacterClass owner, int side, int lane,
                      Image fillImage, RectTransform fillRect,
                      EnergyBarUIController controller)
    {
        this.owner = owner;
        this.side = side;
        this.laneIndex = lane;
        this.fillImage = fillImage;
        this.fillRect = fillRect;
        this.controller = controller;

        // 初始 Fill 锚点 = 0（左侧玩家从左向右长；右侧玩家从右向左长）
        ApplyFillAnchors(0f);
        // 颜色初始化为主题 fillColor
        if (fillImage != null) fillImage.color = controller.FillColor;
    }

    void Update()
    {
        if (owner == null || controller == null) return;
        if (isConsuming) return;                                  // 释放衰减阶段由协程独占控制，不被追逐 owner

        if (owner.maxEnergy <= 0f)
        {
            targetFillAmount = 0f;
        }
        else
        {
            // 让 bar 立即追上 energy 实际比例（重启用或新玩家加入时补正）
            targetFillAmount = Mathf.Clamp01(owner.currentEnergy / owner.maxEnergy);
        }

        // MoveTowards 平滑
        currentFillAmount = Mathf.MoveTowards(currentFillAmount, targetFillAmount,
                                              controller.FillSpeed * Time.deltaTime);
        ApplyFillAnchors(currentFillAmount);

        // 满能闪动阶段：维持 Fill 颜色振荡（不重启协程，节省 GC）
        if (owner.isFullyCharged && !isConsuming)
            UpdateFullGlowFillColor();
        else if (fillImage != null && owner.currentEnergy < owner.maxEnergy)
            fillImage.color = controller.FillColor;
    }

    /// <summary>调整 Fill 的 anchor 比例，模拟「变长」效果。</summary>
    private void ApplyFillAnchors(float ratio)
    {
        if (fillRect == null) return;
        ratio = Mathf.Clamp01(ratio);
        if (side == 0)
        {
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(ratio, 1f);
        }
        else
        {
            fillRect.anchorMin = new Vector2(1f - ratio, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
        }
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fillRect.pivot = new Vector2(0.5f, 0.5f);
    }

    /// <summary>满能量发光闪动：根据正弦周期插值 fillColor↔glowColor（黄↔白）。</summary>
    private void UpdateFullGlowFillColor()
    {
        if (fillImage == null || controller == null) return;
        float cycle = Mathf.Max(0.05f, controller.GlowCycleSeconds);
        float k = (Time.time % cycle) / cycle;                  // 0..1
        float wave = Mathf.Sin(k * Mathf.PI * 2f) * 0.5f + 0.5f; // 0..1..0
        fillImage.color = Color.Lerp(controller.FillColor, controller.GlowColor, wave);
    }

    /// <summary>事件回调：能量充满 → 启动持续发光闪动（实际每帧在 Update 中更新）。</summary>
    public void OnEnergyFull(int characterId)
    {
        if (owner == null || !owner.isFullyCharged) return;
        if (isConsuming) return;                                  // 衰减阶段不接受「满能量」事件覆盖
        // 直接让下一帧 Update 检测到 isFullyCharged 并切到 UpdateFullGlowFillColor
    }

    /// <summary>事件回调：能量耗尽（大招释放）→ 启动闪白 + 长度衰减协程。</summary>
    public void OnEnergyDepleted(int characterId)
    {
        if (controller == null) return;
        if (consumeCo != null) StopCoroutine(consumeCo);
        consumeCo = StartCoroutine(ConsumeShrinkCo(currentFillAmount));
    }

    /// <summary>释放瞬间：Fill 闪白 + 持续衰减长度到 0，期间拒绝 owner 字段追逐。</summary>
    private IEnumerator ConsumeShrinkCo(float startRatio)
    {
        isConsuming = true;
        // 关键：第一帧立刻把 Fill 显示长度锁成 startRatio（防止 Update 与协程打架）
        currentFillAmount = Mathf.Max(currentFillAmount, startRatio);
        ApplyFillAnchors(currentFillAmount);

        float dur = Mathf.Max(0.05f, controller.ConsumeShrinkDuration);
        float t = 0f;
        while (t < dur && currentFillAmount > 0.001f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            // 长度：从 startRatio 平滑到 0（用 smoothstep 让头部/尾部更柔和）
            float len = Mathf.Lerp(startRatio, 0f, Mathf.SmoothStep(0f, 1f, k));
            ApplyFillAnchors(len);
            currentFillAmount = len;
            // 颜色：起始 flash 到白色 → 渐变到 releaseFadeColor → 到 borderColor
            if (fillImage != null)
            {
                if (k < 0.20f)
                {
                    // 前 20%：闪白（白 → 释放 fade 色）
                    float kk = k / 0.20f;
                    fillImage.color = Color.Lerp(controller.GlowColor, controller.ReleaseFadeColor, kk);
                }
                else
                {
                    // 后 80%：渐变到 borderColor（让消耗后视觉回归空槽）
                    float kk = (k - 0.20f) / 0.80f;
                    fillImage.color = Color.Lerp(controller.ReleaseFadeColor, controller.BorderColor, kk);
                }
            }
            yield return null;
        }

        // 收尾
        currentFillAmount = 0f;
        targetFillAmount = 0f;
        ApplyFillAnchors(0f);
        if (fillImage != null) fillImage.color = controller.FillColor;
        isConsuming = false;
        consumeCo = null;
    }
}
