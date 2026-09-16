using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 世界空间能量槽（2026-09-15 改为「绑定音轨固定锚点」，替换旧的逐帧跟随角色版本）。
///
/// 【方案要点】
/// - 位置由 LaneAnchor 固定锚点决定（绑定音轨、不跟随角色）；无该音轨角色则不建条。
/// - 平躺 -90°X 面朝上方（俯视相机可见）；整体 X 缩放 barScaleX 收窄（用户运行时调好）。
/// - 每个「有能量的主动槽」建一根条；单主动槽(如各 Aibo)=单条 4 态，双主动槽(主角)=左右两半各独立 3 态。
/// - 条从左向右填充（左右两侧一致，用户 09-14 拍板）。
/// - 颜色全部 Inspector 可配：themeSide0=红方/玩家，themeSide1=蓝方/AI（后续启用，实现即走配置）。
///
/// 【Aibo 单条 4 态】
///   ① 一般：黄框/黄条按能量比例填充（无脉冲）。
///   ② 充盈：满 + 黄白闪烁 + 整体 ±10% 大小脉冲（脉冲是能量槽表现，与角色命中动画不冲突）。
///   ③ CD：技能进行中(含冷却)时显示暗黄满条静止（暗黄 = 冷却中）。
///   ④ 释放：OnEnergyDepleted 触发——瞬间暗黄、淡黄条延迟衰减到 0；结束后若仍在 CD 则进入③。
///
/// 【主角 双主动 左右两半各独立 3 态】
///   ① 可使用：满 + 银白↔暗灰闪烁（周期默认 0.8s，Inspector 可调）。
///   ② CD(充能)：暗灰、按能量比例左→右填充。
///   ③ 释放：瞬间暗灰、条瞬间清空（只清当前释放那半）。
///
/// 【与旧实现关系】旧 EnergyBarUIController 文件保留未删（可回退：把 FeverManager.EnsureEnergyBar 改回调它即可）；
/// 本组件由 FeverManager.EnsureEnergyBar() 兜底创建。
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
        public Color aiboFrame, aiboTrack, aiboFill, aiboFullA, aiboFullB, aiboCd, aiboRelease;
        [Header("主角（双主动）")]
        public Color heroFrame, heroTrack, heroFill, heroReadyA, heroReadyB, heroRelease;
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
    public int sortingOrder = 5;

    [Header("固定锚点（绑定音轨，不绑角色；无角色不显示）")]
    [Tooltip("每个 (side,lane) 一个固定世界坐标；位置不随角色移动/换人改变，只读取该音轨角色的能量/CD 数据。lane=-1=玩家自身（自动取玩家标记位置）。")]
    public List<LaneAnchor> laneAnchors = new List<LaneAnchor>
    {
        new LaneAnchor { side = 0, lane = 0, position = new Vector3(-7.14f, 0.06f, 1.81f) },
        new LaneAnchor { side = 0, lane = 1, position = new Vector3(-6.42f, 0.17f, 0.14f) },
        new LaneAnchor { side = 0, lane = 2, position = new Vector3(-6.51f, 0.06f, -1.3f) },
        new LaneAnchor { side = 0, lane = 3, position = new Vector3(-0.14f, 0.01f, 0f) },
    };
    [Tooltip("能量槽整体 X 缩放（用户 09-15 运行时把 scale.x 由 1 调到 0.7792，整体收窄）。")]
    public float barScaleX = 0.7792f;
    [Tooltip("编辑器 Gizmo：在每个锚点画青色线框球，便于确认是否压在脚下。")]
    public bool showAnchorDebug = false;

    [Header("Aibo 动画")]
    public float aiboPulseAmount = 0.10f;   // 充盈整体 ±10% 脉冲
    public float aiboPulseCycle = 0.6f;     // 脉冲周期（秒）
    public float aiboGlowCycle = 0.6f;      // 充盈黄白闪烁周期（秒）
    public float releaseDecayDuration = 0.9f;

    [Header("主角 动画")]
    public float heroBlinkCycle = 0.8f;      // 可使用态银白↔暗灰闪烁周期（秒，09-14 拍板默认 0.8）

    [Header("主题色 side 0（红方/玩家）")]
    public EnergyTheme themeSide0 = new EnergyTheme
    {
        aiboFrame = new Color(1.00f, 0.85f, 0.20f, 1f),
        aiboTrack = new Color(0.30f, 0.22f, 0.05f, 1f),
        aiboFill  = new Color(1.00f, 0.85f, 0.20f, 1f),
        aiboFullA = new Color(1.00f, 1.00f, 0.80f, 1f),
        aiboFullB = new Color(1.00f, 0.85f, 0.20f, 1f),
        aiboCd    = new Color(0.50f, 0.40f, 0.10f, 1f),
        aiboRelease = new Color(1.00f, 0.95f, 0.60f, 1f),
        heroFrame = new Color(0.40f, 0.40f, 0.45f, 1f),
        heroTrack = new Color(0.12f, 0.12f, 0.14f, 1f),
        heroFill  = new Color(0.50f, 0.50f, 0.55f, 1f),
        heroReadyA = new Color(0.95f, 0.95f, 1.00f, 1f),
        heroReadyB = new Color(0.45f, 0.45f, 0.50f, 1f),
        heroRelease = new Color(0.40f, 0.40f, 0.45f, 1f),
    };

    [Header("主题色 side 1（蓝方/AI，后续启用）")]
    public EnergyTheme themeSide1 = new EnergyTheme
    {
        aiboFrame = new Color(0.30f, 0.65f, 1.00f, 1f),
        aiboTrack = new Color(0.05f, 0.16f, 0.34f, 1f),
        aiboFill  = new Color(0.30f, 0.65f, 1.00f, 1f),
        aiboFullA = new Color(0.80f, 0.95f, 1.00f, 1f),
        aiboFullB = new Color(0.30f, 0.65f, 1.00f, 1f),
        aiboCd    = new Color(0.16f, 0.32f, 0.55f, 1f),
        aiboRelease = new Color(0.60f, 0.85f, 1.00f, 1f),
        heroFrame = new Color(0.40f, 0.45f, 0.55f, 1f),
        heroTrack = new Color(0.12f, 0.14f, 0.20f, 1f),
        heroFill  = new Color(0.45f, 0.55f, 0.65f, 1f),
        heroReadyA = new Color(0.90f, 0.95f, 1.00f, 1f),
        heroReadyB = new Color(0.40f, 0.48f, 0.58f, 1f),
        heroRelease = new Color(0.40f, 0.45f, 0.55f, 1f),
    };

    private readonly List<WorldEnergyBar> bars = new List<WorldEnergyBar>();
    private bool pendingRebuild = false;

    void Start()
    {
        BuildAll();
        CharacterRoster.OnRosterChanged += OnRosterChanged;
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
        BuildAll();
    }

    private void BuildAll()
    {
        foreach (var b in bars) if (b.root != null) Destroy(b.root);
        bars.Clear();

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
        if (c == null || c.maxEnergies == null) return;
        int n = 0;
        for (int i = 0; i < c.maxEnergies.Length; i++)
            if (c.maxEnergies[i] > 0f) n++;
        if (n == 0) return;                          // 该角色无能量槽（如主角当前 energyCost=0）→ 不建条

        bool isDual = n >= 2;
        var theme = (side == 1) ? themeSide1 : themeSide0;

        // 位置绑定音轨（固定锚点），不跟角色；lane=-1 自动取玩家标记位置。
        Vector3 anchorPos = GetAnchorPosition(side, lane);

        GameObject root = new GameObject($"EBW_S{side}_L{lane}_{c.displayName}");
        root.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // 平躺，面朝上方
        root.transform.localScale = new Vector3(barScaleX, 1f, 1f); // 整体收窄（用户运行时调好的值）
        root.transform.position = anchorPos;

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

    /// <summary>能量槽固定锚点：绑定音轨不绑角色。lane==-1=玩家自身，自动取玩家标记位置；否则查 laneAnchors 表，未配置退回角色标记位置。</summary>
    private Vector3 GetAnchorPosition(int side, int lane)
    {
        if (lane == -1)
        {
            var pm = FindMarker(0, -1);
            return pm != null ? pm.position : Vector3.zero;
        }
        foreach (var a in laneAnchors)
        {
            if (a != null && a.useThis && a.side == side && a.lane == lane)
                return a.position;
        }
        var m = FindMarker(side, lane);
        return m != null ? m.position : Vector3.zero;
    }

    /// <summary>按 (side, lane) 找 CharacterCubeMarker；lane==-1 表示玩家自身。</summary>
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

    // —— 运行时生成圆角长条 Sprite（参考 RuntimeSpriteUtility.CreateRoundedBarSprite，增加 pivot 参数） —— //
    private static Sprite GenBarSprite(int w, int h, Color fill, Color border, int borderT, int radius, Vector2 pivot)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[w * h];
        float halfW = w * 0.5f, halfH = h * 0.5f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - halfW);
                float dy = Mathf.Abs(y + 0.5f - halfH);
                float maxRx = halfW - radius;
                float maxRy = halfH - radius;
                float dist = (dx > maxRx && dy > maxRy)
                    ? Mathf.Sqrt((dx - maxRx) * (dx - maxRx) + (dy - maxRy) * (dy - maxRy))
                    : Mathf.Max(dx - maxRx, dy - maxRy);
                Color c;
                if (dist > radius) c = Color.clear;
                else if (borderT > 0 && dist > radius - borderT) c = border;
                else c = fill;
                px[x + y * w] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, h);
    }

    /// <summary>单根世界空间能量槽视图 + 状态机。</summary>
    private class WorldEnergyBar
    {
        private readonly EnergyBarWorldSpace mgr;
        private readonly CharacterClass owner;
        private readonly int slot;
        private readonly bool isDual;
        private readonly EnergyTheme theme;
        private readonly int side;
        private readonly int lane;

        public GameObject root;            // 跟随 marker 的父物体（平躺）
        private GameObject barRoot;        // 单根条（承载脉冲缩放）
        private SpriteRenderer bgSR, fillSR, glowSR;
        private Transform playerMarker;    // 仅 lane==-1（玩家自身）用于吸附到玩家标记；普通音轨不跟随角色
        private Vector3 anchorPos;          // 固定锚点（音轨位置，不随角色移动/换人变化）
        private float baseScaleX, baseScaleY;

        // 状态
        private float ratio = 0f;
        private float pulseCurrent = 1f;
        private bool wasFull = false;
        private bool releasing = false;
        private Coroutine relCo;

        // 解析后的主题色
        private Color frame, track, fillNormal, fullA, fullB, cd, releaseCol;

        public WorldEnergyBar(EnergyBarWorldSpace mgr, CharacterClass owner, int side, int lane,
                              int slot, bool isDual, EnergyTheme theme, GameObject rootGo, float xOff, Vector3 anchorPos)
        {
            this.mgr = mgr; this.owner = owner; this.side = side; this.lane = lane;
            this.slot = slot; this.isDual = isDual; this.theme = theme; this.root = rootGo;
            this.anchorPos = anchorPos;
            if (lane == -1) this.playerMarker = EnergyBarWorldSpace.FindMarker(0, -1);

            if (isDual)
            {
                frame = theme.heroFrame; track = theme.heroTrack; fillNormal = theme.heroFill;
                fullA = theme.heroReadyA; fullB = theme.heroReadyB; cd = theme.heroFill; releaseCol = theme.heroRelease;
            }
            else
            {
                frame = theme.aiboFrame; track = theme.aiboTrack; fillNormal = theme.aiboFill;
                fullA = theme.aiboFullA; fullB = theme.aiboFullB; cd = theme.aiboCd; releaseCol = theme.aiboRelease;
            }

            BuildVisuals(xOff);
        }

        private void BuildVisuals(float xOff)
        {
            barRoot = new GameObject("Bar" + slot);
            barRoot.transform.SetParent(root.transform, false);
            barRoot.transform.localPosition = new Vector3(xOff, 0f, 0f);

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(barRoot.transform, false);
            bgSR = bgGo.AddComponent<SpriteRenderer>();
            bgSR.sprite = GenBarSprite(128, 32, track, frame, 3, 16, new Vector2(0.5f, 0.5f));
            bgSR.sortingOrder = mgr.sortingOrder;
            bgSR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bgSR.receiveShadows = false;

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(barRoot.transform, false);
            fillGo.transform.localPosition = new Vector3(-mgr.barWorldWidth / 2f, 0f, 0f); // 左边缘对齐
            fillSR = fillGo.AddComponent<SpriteRenderer>();
            fillSR.sprite = GenBarSprite(128, 32, fillNormal, Color.clear, 0, 16, new Vector2(0f, 0.5f));
            fillSR.sortingOrder = mgr.sortingOrder + 1;
            fillSR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fillSR.receiveShadows = false;

            baseScaleX = mgr.barWorldWidth / bgSR.sprite.bounds.size.x;
            baseScaleY = mgr.barWorldHeight / bgSR.sprite.bounds.size.y;
            bgSR.transform.localScale = new Vector3(baseScaleX, baseScaleY, 1f);
            fillSR.transform.localScale = new Vector3(baseScaleX, baseScaleY, 1f);

            // 充盈 glow（加法光晕，仅在满/可使用态亮起）
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(barRoot.transform, false);
            glowGo.transform.localPosition = Vector3.zero;
            glowSR = glowGo.AddComponent<SpriteRenderer>();
            glowSR.sprite = bgSR.sprite;
            var gMat = RuntimeSpriteUtility.AdditiveGlow;
            if (gMat != null) glowSR.material = gMat;
            glowSR.sortingOrder = mgr.sortingOrder - 1;
            glowSR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glowSR.color = new Color(fullA.r, fullA.g, fullA.b, 0f);
            glowSR.transform.localScale = new Vector3(baseScaleX * 1.12f, baseScaleY * 1.12f, 1f);
        }

        private void SetGlow(float a)
        {
            if (glowSR == null) return;
            Color c = glowSR.color;
            c.a = a;
            glowSR.color = c;
        }

        public void Follow()
        {
            if (root == null) return;
            if (owner == null) { root.SetActive(false); return; }
            root.SetActive(true);
            // 位置已固定到音轨锚点（不随角色移动/换人变化）；仅玩家自身(lane==-1)在标记晚生成时吸附到位。
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

        public void Tick()
        {
            if (owner == null || fillSR == null) return;
            if (releasing) return;   // 释放协程独占

            float maxE = (owner.maxEnergies != null && slot < owner.maxEnergies.Length) ? owner.maxEnergies[slot] : 0f;
            float curE = (owner.currentEnergies != null && slot < owner.currentEnergies.Length) ? owner.currentEnergies[slot] : 0f;
            float target = maxE > 0f ? Mathf.Clamp01(curE / maxE) : 0f;
            bool full = (owner.isFullyChargedArr != null && slot < owner.isFullyChargedArr.Length) && owner.isFullyChargedArr[slot];
            bool busy = (owner.skillBusyArr != null && slot < owner.skillBusyArr.Length) && owner.skillBusyArr[slot];

            // 释放检测：满 -> 非满 且能量归零（被 ConsumeSlot 扣空）
            if (wasFull && !full && curE <= 0.0001f)
            {
                wasFull = full;
                TriggerRelease();
                return;
            }
            wasFull = full;

            if (isDual)
            {
                if (full && !busy) ReadyHero();
                else ChargingHero();
                ratio = full ? 1f : target;
            }
            else
            {
                if (busy) { CooldownAibo(); ratio = target; }    // ③ CD：暗黄、按真实比例充能（充能不受影响），CD 结束回黄
                else if (full) { FullAibo(); ratio = 1f; }        // ② 充盈：脉冲+闪烁
                else { NormalAibo(); ratio = target; }            // ① 一般：按比例填充
            }
            ApplyRatio();
        }

        private void ApplyRatio()
        {
            fillSR.transform.localScale = new Vector3(baseScaleX * Mathf.Clamp01(ratio), baseScaleY, 1f);
            barRoot.transform.localScale = Vector3.one * pulseCurrent;
        }

        private void SetFill(Color c) { if (fillSR != null) fillSR.color = c; }

        // —— Aibo —— //
        private void NormalAibo() { pulseCurrent = 1f; SetFill(fillNormal); SetGlow(0f); }
        private void FullAibo()
        {
            float cyc = Mathf.Max(0.05f, mgr.aiboGlowCycle);
            float k = (Time.time % cyc) / cyc;
            float w = Mathf.Sin(k * Mathf.PI * 2f) * 0.5f + 0.5f;
            SetFill(Color.Lerp(fullA, fullB, w));                 // 黄白闪烁
            float pc = Mathf.Max(0.05f, mgr.aiboPulseCycle);
            float p = Mathf.Sin((Time.time % pc) / pc * Mathf.PI * 2f) * 0.5f + 0.5f;
            pulseCurrent = 1f + mgr.aiboPulseAmount * (p * 2f - 1f); // ±10% 脉冲
            SetGlow(0.45f * (0.6f + 0.4f * w));
        }
        private void CooldownAibo() { pulseCurrent = 1f; SetFill(cd); SetGlow(0f); }

        // —— 主角 —— //
        private void ChargingHero() { pulseCurrent = 1f; SetFill(fillNormal); SetGlow(0f); }
        private void ReadyHero()
        {
            pulseCurrent = 1f;
            float cyc = Mathf.Max(0.05f, mgr.heroBlinkCycle);
            float k = (Time.time % cyc) / cyc;
            float w = Mathf.Sin(k * Mathf.PI * 2f) * 0.5f + 0.5f;
            SetFill(Color.Lerp(fullA, fullB, w));                 // 银白↔暗灰闪烁
            SetGlow(0.45f * (0.6f + 0.4f * w));
        }

        // —— 释放（两种形态共用入口） —— //
        private void TriggerRelease()
        {
            releasing = true;
            if (relCo != null) mgr.StopCoroutine(relCo);
            relCo = mgr.StartCoroutine(isDual ? ReleaseHeroCo() : ReleaseAiboCo());
        }

        private IEnumerator ReleaseAiboCo()
        {
            SetGlow(0f);
            float dur = Mathf.Max(0.05f, mgr.releaseDecayDuration);
            float t = 0f;
            ratio = 1f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                SetFill(Color.Lerp(releaseCol, frame, k));        // 暗黄 -> 淡黄
                ratio = 1f - k;                                   // 长度衰减到 0
                ApplyRatio();
                yield return null;
            }
            releasing = false;
            relCo = null;
            // 结束后若仍在 CD(busy) 由 Tick 进入 CooldownAibo
        }

        private IEnumerator ReleaseHeroCo()
        {
            SetGlow(0f);
            SetFill(releaseCol);                                  // 瞬间暗灰
            ratio = 1f;
            ApplyRatio();
            yield return new WaitForSeconds(0.05f);
            ratio = 0f;                                           // 只清当前半
            ApplyRatio();
            releasing = false;
            relCo = null;
        }
    }
}
