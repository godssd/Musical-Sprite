using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 主动技能能量槽 UI（2026-08-26 用户需求）。
///
/// 【样式要求】
/// - 边框棕黄色、能量条黄色；
/// - 能量累积时 Fill 从一侧平铺到另一侧，长度随 currentEnergy/maxEnergy 平滑变化；
/// - 满能量（OnEnergyFull）时整条能量条发光持续闪动；
/// - 释放瞬间（OnEnergyDepleted）整条能量条闪白并持续衰减长度到 0；
/// - 衰减结束后重新显示空 bar，等待下一次充能。
///
/// 【结构】
/// 单文件两个类：
/// - EnergyBarUI          : 单根能量槽的视图/动画组件（Fill 锚点控制、闪动/衰减协程）；
/// - EnergyBarUIController: 全局管理器，启动时为左/右玩家 4 个队伍角色（共 8 个）各建一根能量槽，
///                          自动跟随 CharacterRoster（OnRosterChanged 时清掉旧 bar 并重建），
///                          与 EnergyVFXPlaceholder 一样的「运行时兜底创建」模式。
///
/// 挂载：场景任意 GameObject（场景无实例时由 FeverManager.EnsureEnergyBar() 主动兜底创建）。
///
/// 【布局：方案 B（2026-08-26 用户确认）】
/// 不固定在屏幕底部一条带，而是「跟随每个队伍角色 cube」：
/// 每根能量槽在 ScreenSpaceOverlay Canvas 下，每帧把其 owner 角色 cube 的世界坐标
/// 投影到屏幕，锚定在 cube 投影点正下方 verticalOffset 像素处，并随角色移动。
/// 尺寸固定、永远可见、不被场地几何遮挡。
/// </summary>
public class EnergyBarUIController : MonoBehaviour
{
    /// <summary>由 FeverManager.EnsureEnergyBar() 在 Start 阶段主动调用一次，确保场景里有且仅有一个 EnergyBarUIController。
    /// 设计原因：Unity 6 默认 Domain Reload Off 模式下 [RuntimeInitializeOnLoadMethod] 不会在每次 EnterPlayMode 重跑，
    /// 故不使用该套路，改用 Manager.Start 主动兜底的方式（与 EnsureFeverVFX / EnsureFeverBanner 一致）。</summary>
    public static void EnsureExists()
    {
        if (FindFirstObjectByType<EnergyBarUIController>() != null) return;
        var go = new GameObject("EnergyBarUIController");
        go.AddComponent<EnergyBarUIController>();
    }

    [Header("整体配置（运行时创建 Canvas）")]
    [Tooltip("Canvas sortingOrder，建议在 HPBarCanvas 之上一层（避免被血条遮挡）")]
    public int canvasSortingOrder = 55;
    [Tooltip("每根能量槽宽度 / 高度（像素）")]
    public Vector2 barSize = new Vector2(69.333f, 16.2f);
    [Tooltip("能量槽中心相对角色 cube 屏幕投影点的向下偏移（像素，正值向下）")]
    public float verticalOffset = 60f;

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
    private RectTransform canvasRect;
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

    void LateUpdate()
    {
        // 方案 B：每帧把每根 bar 跟随到其角色 cube 的屏幕投影位置（正下方 verticalOffset）。
        for (int i = 0; i < bars.Count; i++)
            if (bars[i] != null) bars[i].FollowOwner();
    }

    /// <summary>花名册变化时清掉所有旧 bar 并重新为在场角色建立（支持重启用）。</summary>
    private void OnRosterChanged()
    {
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
        Transform cubeTr = FindCubeMarker(side, lane);
        if (cubeTr == null)
            Debug.LogWarning($"[EnergyBarUI] 未找到 side={side} lane={lane} 的 cube marker，能量槽将等待其出现后再跟随。");

        GameObject boxGo = new GameObject($"EnergyBar_S{side}_L{lane}_{owner.displayName}");
        boxGo.transform.SetParent(canvasGo.transform, false);
        RectTransform boxRect = boxGo.AddComponent<RectTransform>();
        boxRect.anchorMin = new Vector2(0.5f, 0.5f);
        boxRect.anchorMax = new Vector2(0.5f, 0.5f);
        boxRect.pivot = new Vector2(0.5f, 0.5f);
        boxRect.sizeDelta = barSize;
        boxRect.anchoredPosition = Vector2.zero; // 初始在中心，LateUpdate 会跟随到 cube 位置

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

        fillGo.transform.SetSiblingIndex(bgGo.transform.GetSiblingIndex() + 1);

        EnergyBarUI bar = boxGo.AddComponent<EnergyBarUI>();
        bar.Setup(owner, side, lane, fillImage, fillRect, this, cubeTr);
        bars.Add(bar);

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

        canvasRect = canvasGo.GetComponent<RectTransform>();
    }

    /// <summary>按 (side, lane) 查找对应的角色 cube（CharacterCubeMarker），用于能量槽跟随。</summary>
    public static Transform FindCubeMarker(int side, int lane)
    {
        var markers = FindObjectsByType<CharacterCubeMarker>(FindObjectsSortMode.None);
        foreach (var m in markers)
        {
            if (m.side == side && m.laneIndex == lane) return m.transform;
        }
        return null;
    }

    // —— 公开给 EnergyBarUI 访问主题色的快捷接口 —— //
    public RectTransform CanvasRect => canvasRect;
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
    private RectTransform boxRect;
    private EnergyBarUIController controller;
    private Transform ownerCube;

    private float currentFillAmount = 0f;     // 当前显示长度 (0..1)
    private float targetFillAmount  = 0f;     // 目标长度（随 currentEnergy/maxEnergy 变化）
    private bool  isConsuming = false;        // 释放衰减协程进行中
    private Coroutine consumeCo;

    /// <summary>由 EnergyBarUIController.CreateBar 调用：绑定数据和主题。</summary>
    public void Setup(CharacterClass owner, int side, int lane,
                      Image fillImage, RectTransform fillRect,
                      EnergyBarUIController controller, Transform ownerCube)
    {
        this.owner = owner;
        this.side = side;
        this.laneIndex = lane;
        this.fillImage = fillImage;
        this.fillRect = fillRect;
        this.controller = controller;
        this.ownerCube = ownerCube;
        this.boxRect = GetComponent<RectTransform>();

        ApplyFillAnchors(0f);
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
            targetFillAmount = Mathf.Clamp01(owner.currentEnergy / owner.maxEnergy);
        }

        currentFillAmount = Mathf.MoveTowards(currentFillAmount, targetFillAmount,
                                              controller.FillSpeed * Time.deltaTime);
        ApplyFillAnchors(currentFillAmount);

        if (owner.isFullyCharged && !isConsuming)
            UpdateFullGlowFillColor();
        else if (fillImage != null && owner.currentEnergy < owner.maxEnergy)
            fillImage.color = controller.FillColor;
    }

    /// <summary>方案 B：每帧把本 bar 跟随到其角色 cube 的屏幕投影位置（正下方 verticalOffset）。</summary>
    public void FollowOwner()
    {
        if (boxRect == null || controller == null) return;
        if (ownerCube == null) ownerCube = EnergyBarUIController.FindCubeMarker(side, laneIndex);
        if (ownerCube == null) { boxRect.gameObject.SetActive(false); return; }

        Camera cam = Camera.main;
        if (cam == null) { boxRect.gameObject.SetActive(false); return; }

        Vector3 screen = cam.WorldToScreenPoint(ownerCube.position);
        if (screen.z < 0f) { boxRect.gameObject.SetActive(false); return; }  // cube 在相机背后

        boxRect.gameObject.SetActive(true);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(controller.CanvasRect, screen, null, out Vector2 local);
        boxRect.anchoredPosition = local + new Vector2(0f, -controller.verticalOffset);
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
        float k = (Time.time % cycle) / cycle;
        float wave = Mathf.Sin(k * Mathf.PI * 2f) * 0.5f + 0.5f;
        fillImage.color = Color.Lerp(controller.FillColor, controller.GlowColor, wave);
    }

    public void OnEnergyFull(int characterId)
    {
        if (owner == null || !owner.isFullyCharged) return;
        if (isConsuming) return;                                  // 衰减阶段不接受「满能量」事件覆盖
    }

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
        currentFillAmount = Mathf.Max(currentFillAmount, startRatio);
        ApplyFillAnchors(currentFillAmount);

        float dur = Mathf.Max(0.05f, controller.ConsumeShrinkDuration);
        float t = 0f;
        while (t < dur && currentFillAmount > 0.001f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float len = Mathf.Lerp(startRatio, 0f, Mathf.SmoothStep(0f, 1f, k));
            ApplyFillAnchors(len);
            currentFillAmount = len;
            if (fillImage != null)
            {
                if (k < 0.20f)
                {
                    float kk = k / 0.20f;
                    fillImage.color = Color.Lerp(controller.GlowColor, controller.ReleaseFadeColor, kk);
                }
                else
                {
                    float kk = (k - 0.20f) / 0.80f;
                    fillImage.color = Color.Lerp(controller.ReleaseFadeColor, controller.BorderColor, kk);
                }
            }
            yield return null;
        }

        currentFillAmount = 0f;
        targetFillAmount = 0f;
        ApplyFillAnchors(0f);
        if (fillImage != null) fillImage.color = controller.FillColor;
        isConsuming = false;
        consumeCo = null;
    }
}
