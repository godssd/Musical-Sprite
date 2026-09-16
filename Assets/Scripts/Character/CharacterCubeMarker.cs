using UnityEngine;
using System.Collections;

/// <summary>
/// 占位 cube 标记组件（P2）。挂在角色占位 cube 上：
/// - side 0 = 左侧玩家阵营 / 1 = 右侧对手阵营
/// - laneIndex -1 = 玩家自身角色（NPC）/ 非负数 = 该侧对应音轨上的队伍角色
///
/// FeverVFXPlaceholder 通过 FindObjectsByType&lt;CharacterCubeMarker&gt; 取代 Tag 查找。
/// 新场景在角色创建时直接挂 Marker；旧场景由 CharacterBattleSystem 按乐队层级迁移。
///
/// 简易按键反馈：Flash() 让 cube 临时放大（popScale=1.25）后回弹，颜色短暂提亮，
/// 由 BattleVisualsController.OnLanePress 触发，与 LaneIndicator.Flash() 并列。
///
/// 主动技能释放表现（大狗叫等）：GrowGlow() 让角色变大 + 持续发光；ShrinkUnglow() 缩回并熄灭。
/// </summary>
public class CharacterCubeMarker : MonoBehaviour
{
    [Tooltip("0 = 左侧玩家阵营 / 1 = 右侧对手阵营")]
    public int side = 0;

    [Tooltip("-1 = 玩家自身角色（不占音轨） / 非负数 = 队伍角色对应音轨")]
    public int laneIndex = -1;

    public bool IsPlayer => laneIndex < 0;

    [Header("按键反馈（P3）")]
    [Tooltip("Flash() 时缩放倍数（弹跳峰值 = base × popScale）")]
    public float popScale = 1.25f;
    [Tooltip("Flash() 时颜色提亮系数（base + identityColor × boostAmount）")]
    [Range(0f, 1f)] public float colorBoost = 0.6f;
    [Tooltip("Flash() 总时长（秒）")]
    public float flashDuration = 0.18f;

    [Header("释放表现（主动技能）")]
    [Tooltip("GrowGlow() 时放大的倍数")]
    public float growScale = 1.5f;
    [Tooltip("GrowGlow() 起手闪烁时的发光颜色")]
    public Color releaseGlow = new Color(1f, 0.85f, 0.2f);

    [Header("命中跳跃（P3）")]
    [Tooltip("Jump() 跳跃峰值高度（本地坐标单位）")]
    public float jumpHeight = 0.6f;
    [Tooltip("Jump() 跳跃总时长（秒）")]
    public float jumpDuration = 0.3f;

    private Vector3 baseScale;
    private Vector3 baseLocalPos;
    private Coroutine flashCo;
    private Coroutine glowCo;
    private Coroutine pulseCo;
    private Coroutine jumpCo;
    private Color[] sleepBaseColors;
    private bool sleepVisualOn = false;
    [Tooltip("沉睡时方块缩放（相对 baseScale）；睡眠异常(表现)时角色方块变小")]
    public float sleepShrinkScale = 0.6f;

    [Header("外观（美术接入）")]
    [Tooltip("角色外观预制体；非空时实例化并隐藏默认占位 cube。通常由 CharacterDataSO.modelPrefab 经 CharacterBattleSystem 注入")]
    public GameObject modelPrefab;

    private GameObject spawnedModel;

    [Header("朝向调试")]
    [Tooltip("true=右侧(side=1)角色自动水平镜像，面向左侧中心（仅翻转 Spine 子模型 localScale.x，不动根节点/占位方块）")]
    public bool flipFacing = true;

    /// <summary>按 (side, lane) 索引的全局角色标记表，供普通命中时按音轨查找对应角色跳跃。</summary>
    private static System.Collections.Generic.Dictionary<int, CharacterCubeMarker> Registry = new System.Collections.Generic.Dictionary<int, CharacterCubeMarker>();
    // 改为 side*100+lane：原 side*4+lane 会让 side1 玩家(laneIndex=-1 → key=3) 与 side0 lane3(key=3) 撞键，
    // 导致 GetAt(0,3) 误返回玩家 marker、且玩家 marker 覆盖队友 lane3 的登记。放大基数避免任意 lane(-1..3) 与 side(0..1) 碰撞。
    private static int RegKey(int side, int lane) => side * 100 + lane;
    /// <summary>按 side/lane 取得对应音轨角色标记（无则返回 null）。</summary>
    public static CharacterCubeMarker GetAt(int side, int lane)
    {
        Registry.TryGetValue(RegKey(side, lane), out var m);
        return m;
    }

    private int? registeredKey = null;

    /// <summary>把本 marker 登记进全局 (side,lane) Registry；若已登记过先移除旧键，
    /// 避免 side/laneIndex 后续被修改后残留旧键，导致 GetAt 按 lane 查不到本 marker（普通命中不跳、受击走 side 全搜不受影响）。</summary>
    public void Register()
    {
        if (registeredKey.HasValue) Registry.Remove(registeredKey.Value);
        int key = RegKey(side, laneIndex);
        Registry[key] = this;
        registeredKey = key;
    }

    void Awake()
    {
        baseScale = transform.localScale;
        baseLocalPos = transform.localPosition;
        Register();

        // 自动确保有通用贴地阴影组件（P2 占位 cube 技术验证；后续角色模型同样复用 BlobShadow）。
        var blob = GetComponent<BlobShadow>();
        if (blob == null) blob = gameObject.AddComponent<BlobShadow>();

        if (modelPrefab != null) SetModelPrefab(modelPrefab, null);
    }

    void OnDestroy()
    {
        if (registeredKey.HasValue) Registry.Remove(registeredKey.Value);
        registeredKey = null;
    }

    /// <summary>
    /// 由 BattleVisualsController 调用：按下该侧对应 lane 时，让这个 cube 弹跳并提亮一下。
    /// 也用于主动技能「每按对一个键，对应角色亮闪一次」。
    /// </summary>
    public void Flash()
    {
        if (!isActiveAndEnabled) return;
        if (flashCo != null) StopCoroutine(flashCo);
        flashCo = StartCoroutine(FlashCo());
    }

    private System.Collections.IEnumerator FlashCo()
    {
        float t = 0f;
        float half = flashDuration * 0.5f;
        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) { flashCo = null; yield break; }
        // 缓存每个渲染器的基准色（多网格角色每个子网格各自基准）
        Color[] baseColors = new Color[rends.Length];
        Color[] boostColors = new Color[rends.Length];
        for (int i = 0; i < rends.Length; i++)
        {
            baseColors[i] = (rends[i].material != null) ? rends[i].material.color : Color.white;
            boostColors[i] = baseColors[i] + baseColors[i] * colorBoost;
        }

        while (t < half)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / half);
            transform.localScale = baseScale * Mathf.Lerp(1f, popScale, k);
            for (int i = 0; i < rends.Length; i++)
                if (rends[i].material != null) SetMarkerColor(rends[i].material, Color.Lerp(baseColors[i], boostColors[i], k));
            yield return null;
        }
        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / half);
            transform.localScale = baseScale * Mathf.Lerp(popScale, 1f, k);
            for (int i = 0; i < rends.Length; i++)
                if (rends[i].material != null) SetMarkerColor(rends[i].material, Color.Lerp(boostColors[i], baseColors[i], k));
            yield return null;
        }

        transform.localScale = baseScale;
        for (int i = 0; i < rends.Length; i++)
            if (rends[i].material != null) SetMarkerColor(rends[i].material, baseColors[i]);
        flashCo = null;
    }

    /// <summary>主动技能释放开始：角色变大 + 持续发光（保持到 ShrinkUnglow）。</summary>
    public void GrowGlow()
    {
        if (!isActiveAndEnabled) return;
        if (flashCo != null) StopCoroutine(flashCo);
        if (glowCo != null) StopCoroutine(glowCo);
        glowCo = StartCoroutine(GrowCo());
    }

    /// <summary>主动技能释放结束：缩回原大小并熄灭发光。</summary>
    public void ShrinkUnglow()
    {
        if (!isActiveAndEnabled) return;
        if (glowCo != null) StopCoroutine(glowCo);
        glowCo = StartCoroutine(ShrinkCo());
    }

    private System.Collections.IEnumerator GrowCo()
    {
        // 起手先快速闪烁（提亮）一下，对应「闪烁变大」的"闪烁"阶段；闪完即熄灭。
        SetEmission(releaseGlow * 2.6f);
        float blink = 0.15f;
        float tb = 0f;
        while (tb < blink)
        {
            tb += Time.deltaTime;
            yield return null;
        }
        SetEmission(Color.black);  // 闪烁结束：进入"只变大、不持续发光"状态

        // 附魔期间平时不发光，只保持变大；命中闪烁由 PulseGlow 负责。
        float t = 0f;
        float dur = 0.15f;
        Vector3 from = transform.localScale;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            transform.localScale = Vector3.Lerp(from, baseScale * growScale, k);
            yield return null;
        }
        transform.localScale = baseScale * growScale;
        glowCo = null;
    }

    private System.Collections.IEnumerator ShrinkCo()
    {
        float t = 0f;
        float dur = 0.15f;
        Vector3 from = transform.localScale;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            transform.localScale = Vector3.Lerp(from, baseScale, k);
            yield return null;
        }
        transform.localScale = sleepVisualOn ? baseScale * sleepShrinkScale : baseScale;
        SetEmission(Color.black);
        glowCo = null;
    }

    private void SetEmission(Color c)
    {
        // 多网格角色：遍历所有子渲染器统一发光（单网格时等价于原行为）
        var rends = GetComponentsInChildren<Renderer>();
        foreach (var r in rends)
        {
            if (r == null || r.material == null) continue;
            r.material.EnableKeyword("_EMISSION");
            r.material.SetColor("_EmissionColor", c);
        }
    }

    /// <summary>统一着色：同时写 URP/Lit 的 _BaseColor 与旧 cube 的 .color，兼容两种材质管线。</summary>
    private void SetMarkerColor(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
    }

    /// <summary>给该 marker 下所有（含子物体模型）渲染器统一上色（身份色 / 过热 tint）。</summary>
    public void ColorAll(Color col)
    {
        var rends = GetComponentsInChildren<Renderer>();
        foreach (var r in rends)
            if (r.material != null) SetMarkerColor(r.material, col);
    }

    /// <summary>由 CharacterBattleSystem 在装配 marker 时调用：注入角色外观预制体（数据驱动，非破坏式）。
    /// 非空时实例化到自身子节点；Spine 有效时隐藏默认占位 cube，Spine 无效（SkeletonDataAsset 缺失/导入失败）时保留 cube 可见并打 Warning。
    /// 重复调用只实例化一次。同时幂等地挂载 CharacterAnimator 并把动画前缀（animationPrefix）注入，供 Spine 动画自动发现。</summary>
    public void SetModelPrefab(GameObject prefab, string animationPrefix = null)
    {
        if (prefab == null) return;
        if (spawnedModel == null)
        {
            spawnedModel = Instantiate(prefab, transform);
            // 保留 prefab 自身的本地 Transform，允许不同 Spine 角色在 prefab 里预先对位
            // （pivot 不在视觉中心的角色需要本地偏移/旋转/缩放）。
            spawnedModel.transform.SetLocalPositionAndRotation(prefab.transform.localPosition, prefab.transform.localRotation);
            spawnedModel.transform.localScale = prefab.transform.localScale;
            // 右侧(side=1)角色自动水平镜像，面向左侧中心（仅翻 Spine 子模型，不影响根节点/占位方块）
            if (side == 1 && flipFacing)
            {
                var fs = spawnedModel.transform.localScale;
                spawnedModel.transform.localScale = new Vector3(-Mathf.Abs(fs.x), fs.y, fs.z);
            }
        }

        // 自动挂载动画驱动器（幂等）：从 CharacterDataSO.animationPrefix 注入命名前缀。
        // 已实例化时仍刷新前缀并重建可用动画表（修复 Awake 用默认前缀 → ColorMarker 带正确前缀被 spawnedModel!=null 守卫跳过的隐患）。
        var anim = spawnedModel.GetComponent<CharacterAnimator>();
        if (anim == null) anim = spawnedModel.AddComponent<CharacterAnimator>();
        if (!string.IsNullOrEmpty(animationPrefix))
        {
            anim.animationPrefix = animationPrefix;
            anim.Rebuild();
        }

        // Spine 校验：没有有效 Skeleton/AnimationState 时保留占位 cube 可见，避免角色"凭空消失"且 Update 爆 NRE。
        bool validSpine = anim != null && anim.HasValidSkeleton();
        var selfRend = GetComponent<Renderer>();
        if (selfRend != null) selfRend.enabled = !validSpine;
        if (!validSpine)
        {
            Debug.LogWarning($"[CharacterCubeMarker] {gameObject.name}(side={side},lane={laneIndex}) 的模型 prefab={prefab.name} Spine 未就绪（SkeletonDataAsset 缺失/导入失败/AnimationState 为 null），已保留占位 cube 可见。animationPrefix={animationPrefix}", this);
        }
    }

    /// <summary>调试用：按当前 flipFacing 重新应用 / 撤销朝向翻转（运行时或编辑模式点右键菜单即可）。
    /// 先恢复为正向，再按 (side==1 &amp;&amp; flipFacing) 决定镜像，避免多次调用叠加负负得正。</summary>
    [ContextMenu("Musical-Sprite/刷新朝向 Flip Facing")]
    public void RefreshFlip()
    {
        if (spawnedModel == null) return;
        var s = spawnedModel.transform.localScale;
        float ax = Mathf.Abs(s.x);
        spawnedModel.transform.localScale = new Vector3((side == 1 && flipFacing) ? -ax : ax, s.y, s.z);
    }

    /// <summary>
    /// 主动技能"附魔音符命中"时的高亮脉冲：仅短暂提亮发光（emission），不动 scale。
    /// 用于变大/发光中的大狗被命中时闪一下，而不会把放大中的狗缩回原尺寸。
    /// </summary>
    public void PulseGlow()
    {
        if (!isActiveAndEnabled) return;
        if (pulseCo != null) StopCoroutine(pulseCo);
        pulseCo = StartCoroutine(PulseCo());
    }

    private System.Collections.IEnumerator PulseCo()
    {
        // 命中闪烁：在黑色（平时不亮）基础上短暂提亮，再回落到黑色。
        SetEmission(releaseGlow * 2.6f);
        yield return new WaitForSeconds(0.09f);
        SetEmission(Color.black);
        pulseCo = null;
    }

    /// <summary>
    /// 普通音符命中时，对应音轨角色向上跳一下（仅本地位移 hop，不影响 scale）。
    /// 释放主动技能期间由 BattleVisualsController 屏蔽调用。
    /// </summary>
    public void Jump()
    {
        if (!isActiveAndEnabled) return;
        if (jumpCo != null) StopCoroutine(jumpCo);
        jumpCo = StartCoroutine(JumpCo());
    }

    private System.Collections.IEnumerator JumpCo()
    {
        float t = 0f;
        while (t < jumpDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / jumpDuration);
            float h = Mathf.Sin(k * Mathf.PI) * jumpHeight;   // 0 -> 峰 -> 0 的抛物线 hop
            transform.localPosition = baseLocalPos + Vector3.up * h;
            yield return null;
        }
        transform.localPosition = baseLocalPos;
        jumpCo = null;
    }

    // ===== 统一动画 API（对战手柄）：转发给 Spine 的 CharacterAnimator；cube 角色走原有反馈（no-op 或兜底）=====
    // 战斗逻辑只调语义，不关心具体动画；换角/加角只需填 CharacterDataSO（modelPrefab + animationPrefix）。

    private CharacterAnimator GetAnimator()
    {
        if (spawnedModel == null) return null;
        return spawnedModel.GetComponent<CharacterAnimator>();
    }

    /// <summary>开场：Spine 角色播 Opening 一次，结束后自动接回当前 loop。</summary>
    public void PlayOpening() => GetAnimator()?.PlayOpening();

    /// <summary>普通/过热命中：Spine 角色播对应命中动画；若 Spine 命中动画名缺失（available 未注册）则回退 cube 跳跃兜底，
    /// 避免"既没 Spine 动画、也没 cube 反馈"的静默无反应（双轨：未接入 Spine 的角色 + Spine 动画未就位的过渡期都仍有反馈）。</summary>
    public void PlayTarget(bool fever)
    {
        var a = GetAnimator();
        if (a != null)
        {
            bool played = a.PlayOnce(fever ? CharacterAnimator.CharacterAnimationState.TargetFever : CharacterAnimator.CharacterAnimationState.TargetNormal);
            if (!played) Jump();   // Spine 命中动画缺失/被高优先级打断 → 回退 cube 跳跃
        }
        else Jump();   // 未接入 Spine 的 cube 角色保留命中跳跃
    }

    /// <summary>受击：全队播 Hit（Spine 角色）；cube 角色无受击动画，暂不做额外反馈（避免与命中跳跃混淆）。</summary>
    public void PlayHit()
    {
        var a = GetAnimator();
        if (a != null) a.PlayOnce(CharacterAnimator.CharacterAnimationState.Hit);
    }

    /// <summary>进入过热：Spine 角色播 Special 后切到过热 loop（PlayFever）。</summary>
    public void EnterFever()
    {
        var a = GetAnimator();
        if (a != null)
        {
            a.PlayOnce(CharacterAnimator.CharacterAnimationState.Special);
            a.SetLoopState(CharacterAnimator.CharacterAnimationState.PlayFever);
        }
    }

    /// <summary>退出过热（未断连，正常冷却结束）：切回普通 loop。</summary>
    public void ExitFever()
    {
        var a = GetAnimator();
        if (a != null) a.SetLoopState(CharacterAnimator.CharacterAnimationState.PlayNormal);
    }

    /// <summary>过热断连颓废：Spine 角色播 Decadent（当前已是普通 loop）。</summary>
    public void PlayDecadent()
    {
        var a = GetAnimator();
        if (a != null) a.PlayOnce(CharacterAnimator.CharacterAnimationState.Decadent);
    }

    /// <summary>技能段播放：Select / Start / Attak / End / Loop 等，按优先级路由（Spine 角色）；cube 角色无对应动画。
    /// 返回 true 表示「释放起点动画已播放」（调用方 BeginCast 据此判定是否撤销整次释放）。
    ///
    /// 占位 cube（无 Spine，GetAnimator() 为 null）分支：美术未接入期间，用 cube 自身已有的闪烁/变大发光反馈
    /// 作为「技能动画」等价物，并返回 true（视为已播放），使 BeginCast 的「SkillStart 未播放即撤销」判定
    /// 不会对占位角色误伤（否则所有占位方块永远放不出技能、也无呼号闪烁）。
    /// 一旦 CharacterDataSO.modelPrefab 接入 Spine，GetAnimator() 自动返回 Spine，此处走真实动画分支，
    /// 撤销逻辑对该角色照常生效（Spine 的 SkillStart 被高优先级动画挡住同样会撤销）——与「AI 与玩家一致」铁律兼容。
    /// 该分支在美术全接入后自然失效，无需专门清理。</summary>
    public bool PlaySkillStep(CharacterAnimator.CharacterAnimationState step)
    {
        var a = GetAnimator();
        if (a != null) return a.PlayOnce(step);

        // 占位 cube（无 Spine）：用自身反馈替代动画，并返回 true（视为已播放）。
        switch (step)
        {
            case CharacterAnimator.CharacterAnimationState.SkillSelect:
                Flash();            // 呼号：方块闪烁一下（对应「呼号响应反馈（方块闪烁）」）
                return true;
            case CharacterAnimator.CharacterAnimationState.SkillStart:
                GrowGlow();         // 释放起点：变大发光（BeginCast 后续第 173 行会再调一次 GrowGlow，幂等安全）
                return true;
            default:
                return true;        // Attak/End/Loop 等 cube 无对应动画，视为已播放，不阻塞释放流程
        }
    }

    /// <summary>胜利终态 loop（优先级 20，直接中断一切）。</summary>
    public void PlayVictory()
    {
        var a = GetAnimator();
        if (a != null) a.SetLoopState(CharacterAnimator.CharacterAnimationState.Victory);
    }

    /// <summary>失败终态 loop（优先级 20，直接中断一切）。</summary>
    public void PlayFail()
    {
        var a = GetAnimator();
        if (a != null) a.SetLoopState(CharacterAnimator.CharacterAnimationState.Fail);
    }

    /// <summary>技能期间锁定 loop 状态（转发给 Spine 动画驱动器；cube 角色无动画，no-op）。</summary>
    public void SetSkillLoopLock(bool on) => GetAnimator()?.SetSkillLoopLock(on);

    /// <summary>显式切换 loop 状态（供技能/过热退出等需要直接切 loop 的调用方使用）。</summary>
    public void SetLoopState(CharacterAnimator.CharacterAnimationState state) => GetAnimator()?.SetLoopState(state);

    /// <summary>沉睡视觉：on=true 时方块变灰 + 熄灯；on=false 时恢复身份色（沉睡解除）。</summary>
    public void ApplySleepVisual(bool on)
    {
        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        if (on)
        {
            if (glowCo != null) StopCoroutine(glowCo);
            sleepBaseColors = new Color[rends.Length];
            for (int i = 0; i < rends.Length; i++)
            {
                if (IsBlobShadowRenderer(rends[i])) { sleepBaseColors[i] = Color.white; continue; }
                if (rends[i].material == null) { sleepBaseColors[i] = Color.white; continue; }
                sleepBaseColors[i] = rends[i].material.color;
                SetMarkerColor(rends[i].material, new Color(0.35f, 0.35f, 0.35f)); // 灰：沉睡
                SetEmission(Color.black);                                       // 黑灯
            }
            transform.localScale = baseScale * sleepShrinkScale;   // 变小
            sleepVisualOn = true;
        }
        else if (sleepVisualOn)
        {
            if (glowCo != null) StopCoroutine(glowCo);
            if (sleepBaseColors != null)
                for (int i = 0; i < rends.Length && i < sleepBaseColors.Length; i++)
                    if (rends[i].material != null && !IsBlobShadowRenderer(rends[i]))
                        SetMarkerColor(rends[i].material, sleepBaseColors[i]);
            SetEmission(Color.black);
            transform.localScale = baseScale;                      // 恢复
            sleepVisualOn = false;
        }
    }

    private bool IsBlobShadowRenderer(Renderer r)
    {
        if (r == null) return false;
        // BlobShadow 组件会创建一个名为 "BlobShadow" 的子物体并挂上 MeshRenderer；跳过它避免访问无 _Color 属性的材质。
        return r.gameObject.name == "BlobShadow" || r.GetComponentInParent<BlobShadow>() != null;
    }
}
