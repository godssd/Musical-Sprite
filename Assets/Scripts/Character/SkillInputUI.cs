using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 主动技能「完全手动」输入控制器（大狗叫等）。
/// 每个队伍角色（side=0）能量充满后，玩家按其 SkillSO.inputSequence 按键即可释放：
///   - 每按对一个键 → 对应角色 cube 亮闪一次（CharacterCubeMarker.Flash）；
///   - 完整序列按对 → 该角色立刻进入释放（ActiveSkillRuntime.BeginCast：变大发光 → 附魔 6 音符 → 音波削减对方连击）。
/// 右侧三个触摸/点击按键 ← / ↓ / → 是释放输入口（功能需求表里 A/B/C 的语义映射为 ←/↓/→）；
/// 键盘也支持 ←/↓/→ 与 A/B/C（S/D）互通，共用同一组 SkillInputStep。
///
/// 输入规则（防 bug，Issue 2 修复）：
///   - 全局输入缓冲 buffer：按下的步骤按顺序累积；只有「buffer 恰好等于某角色技能完整序列」才释放。
///   - ① 时间间隔：相邻两次输入超过 inputInterval → 立刻清空缓冲，回到无任何输入状态。
///   - ② 无匹配：把当前步追加后仍不是任何角色技能序列的有效前缀（即没有任何人物应闪烁）→ 立刻清空，回到无输入状态（并红色错误闪烁提示）。
///   - ③ 成功释放：buffer 恰好等于某（能量已满的）角色技能完整序列 → 触发释放并立刻清空缓冲。
/// 能量未满的技能不参与匹配（与「能量满才响应」一致）；只有「当前缓冲是该角色序列的有效前缀」时对应角色才闪烁，避免按错乱闪。
///
/// 挂载：场景任意 GO；ScoreManager/FeverManager 启动后会自动补建。
/// </summary>
public class SkillInputUI : MonoBehaviour
{
    [Header("输入缓冲")]
    [Tooltip("序列最大时间间隔（秒）：相邻两次输入超过该间隔即视为超时，立刻重置为无输入状态")]
    public float inputInterval = 1.2f;

    [Header("主题色（右侧按键反馈）")]
    public Color pressedColor = new Color(1f, 0.9f, 0.2f, 1f);
    public Color readyColor   = new Color(1f, 1f, 1f, 0.18f);
    public Color idleColor    = new Color(1f, 1f, 1f, 0.08f);
    public Color errorColor   = new Color(1f, 0.35f, 0.35f, 1f);

    [Header("反馈动画")]
    public float flashDuration = 0.18f;
    public float popScale = 1.35f;

    [Header("系统引用")]
    public CharacterBattleSystem battle;

    private Image[] btnImages = new Image[3];
    private RectTransform[] btnRects = new RectTransform[3];
    private Text[] btnLabels = new Text[3];
    // 右侧三个触摸/点击按键对应技能释放输入 ←/↓/→（键盘 A/B/C 也会映射到同一组，作为肌肉记忆备用）。
    private readonly KeyCode[] skillKeys = { KeyCode.LeftArrow, KeyCode.DownArrow, KeyCode.RightArrow };
    private readonly string[] skillLabels = { "←", "↓", "→" };
    // 触摸按钮：按下 idx 0/1/2 → 直接对应 Left/Down/Right（不需要再走 KeyToStep 二次映射）
    private readonly SkillInputStep[] touchSteps = { SkillInputStep.Left, SkillInputStep.Down, SkillInputStep.Right };
    private Coroutine[] popCo = new Coroutine[3];

    // 全局输入缓冲：按顺序累积玩家按下的技能步骤；匹配某角色完整序列才释放。
    private List<SkillInputStep> buffer = new List<SkillInputStep>();
    private float lastInputTime = -999f;

    void Start()
    {
        if (battle == null) battle = FindFirstObjectByType<CharacterBattleSystem>();
        BuildButtons();
        RefreshButtonColors();
    }

    void BuildButtons()
    {
        // 没有 EventSystem 时 IPointerClickHandler 不会触发（移动/触屏表现得"按钮没反应"）
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        GameObject canvasGo = new GameObject("SkillInputCanvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        for (int i = 0; i < 3; i++)
        {
            GameObject go = new GameObject("SkillBtn_" + skillLabels[i]);
            go.transform.SetParent(canvasGo.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(160f, 160f);
            rt.anchoredPosition = new Vector2(-32f, (1 - i) * 180f);
            rt.localScale = Vector3.one;
            Image img = go.AddComponent<Image>();
            img.color = idleColor;
            img.raycastTarget = true;
            btnImages[i] = img;
            btnRects[i] = rt;

            GameObject txtGo = new GameObject("Label");
            txtGo.transform.SetParent(go.transform, false);
            var tr = txtGo.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;
            Text t = txtGo.AddComponent<Text>();
            t.text = skillLabels[i];
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.fontSize = 110;
            t.fontStyle = FontStyle.Bold;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            btnLabels[i] = t;

            int idx = i;
            var trigger = go.AddComponent<SkillButtonProxy>();
            trigger.buttonIndex = idx;
            trigger.onClick = OnButtonPushed;
        }
    }

    void Update()
    {
        // 键盘监听：←/↓/→ 直接对应，键盘 A/B/C/W/S/D 复用同一组（玩家既可键入箭头，也可沿用 A/S/D 的左下右肌肉记忆）
        foreach (KeyCode k in new[] {
                     KeyCode.LeftArrow, KeyCode.DownArrow, KeyCode.RightArrow,
                     KeyCode.A, KeyCode.B, KeyCode.C })
        {
            if (Input.GetKeyDown(k))
            {
                OnKeyPressed(SkillSO.KeyToStep(k));
                FlashButtonByKey(k);
            }
        }
    }

    /// <summary>按下一个技能输入步：维护全局有序缓冲，按「限时 + 有效前缀 + 完整匹配」规则处理，避免输入错误引发 bug。</summary>
    private void OnKeyPressed(SkillInputStep step)
    {
        float now = Time.time;

        // ① 超时：与上次输入间隔超过 inputInterval → 立刻重置为无输入状态（当前这步作为新起点）。
        if (buffer.Count > 0 && (now - lastInputTime) > inputInterval)
            buffer.Clear();

        buffer.Add(step);
        lastInputTime = now;

        // 收集「当前缓冲是有效前缀」且可响应（Standby、能量门槛满足）的运行时
        var runtimes = FindObjectsByType<ActiveSkillRuntime>(FindObjectsSortMode.None);
        List<ActiveSkillRuntime> matching = new List<ActiveSkillRuntime>();
        foreach (var rt in runtimes)
        {
            if (rt.ownerSide != 0 || rt.owner == null || !rt.owner.HasActiveSkill) continue;
            if (rt.phase != ActiveSkillRuntime.Phase.Standby) continue;            // 进行中/冷却中不响应
            if (rt.NeedsEnergyGate && !rt.owner.isFullyCharged) continue;         // 仅能量技能卡能量门槛
            var seq = rt.inputSequence;
            if (seq == null || seq.Length == 0) continue;
            if (IsPrefix(seq, buffer)) matching.Add(rt);
        }

        if (matching.Count == 0)
        {
            // ② 没有任何角色符合当前输入（无人闪烁）→ 立刻重置为无输入状态（并红色错误闪烁提示）。
            buffer.Clear();
            lastInputTime = -999f;
            FlashError();
            return;
        }

        // 删除了角色 cube 闪烁反馈（用户 2026-08-26 反馈：按右边按键不应触发地面变大闪动）。
        // 按钮本身有 pressedColor+popScale，无匹配时有红色错误闪烁，三档反馈已足够定位。
        // 完整匹配某技能 → 触发释放并立刻重置为无输入状态。
        foreach (var rt in matching)
        {
            if (buffer.Count == rt.inputSequence.Length)
            {
                rt.BeginCast();
                buffer.Clear();
                lastInputTime = -999f;
                return;
            }
        }
    }

    /// <summary>buf 是否为 seq 的前缀（长度可小于 seq，但逐项必须相等）。</summary>
    private bool IsPrefix(SkillInputStep[] seq, List<SkillInputStep> buf)
    {
        if (buf.Count > seq.Length) return false;
        for (int i = 0; i < buf.Count; i++)
            if (seq[i] != buf[i]) return false;
        return true;
    }

    public void OnButtonPushed(int idx)
    {
        if (idx < 0 || idx > 2) return;
        OnKeyPressed(touchSteps[idx]);                 // 触摸/点击：直接对应 Left/Down/Right
        Flash(touchSteps[idx]);                         // 同组按钮一起高亮
    }

    private void FlashButtonByKey(KeyCode k)
    {
        int idx = System.Array.IndexOf(skillKeys, k);
        if (idx < 0) return;
        Flash(idx, pressedColor);
    }

    private void Flash(SkillInputStep step)
    {
        int idx = -1;
        for (int i = 0; i < touchSteps.Length; i++)
            if (touchSteps[i] == step) { idx = i; break; }
        if (idx < 0) return;
        Flash(idx, pressedColor);
    }

    private void Flash(int idx, Color flashColor)
    {
        if (btnImages[idx] == null) return;
        btnImages[idx].color = flashColor;
        Pop(idx);
        CancelInvoke(nameof(ClearFlash));
        Invoke(nameof(ClearFlash), flashDuration);
    }

    /// <summary>错误反馈：三个按键一起红色闪烁一下，提示「这一步不被接受、已重置」。</summary>
    private void FlashError()
    {
        for (int i = 0; i < 3; i++) Flash(i, errorColor);
    }

    private void Pop(int idx)
    {
        if (popCo[idx] != null) StopCoroutine(popCo[idx]);
        popCo[idx] = StartCoroutine(PopCo(idx));
    }

    private System.Collections.IEnumerator PopCo(int idx)
    {
        float t = 0f;
        while (t < flashDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / flashDuration);
            float s = 1f + (popScale - 1f) * Mathf.Sin(k * Mathf.PI);
            if (btnRects[idx] != null) btnRects[idx].localScale = Vector3.one * s;
            yield return null;
        }
        if (btnRects[idx] != null) btnRects[idx].localScale = Vector3.one;
        popCo[idx] = null;
    }

    private void ClearFlash() { RefreshButtonColors(); }

    private void RefreshButtonColors()
    {
        for (int i = 0; i < 3; i++)
            if (btnImages[i] != null) btnImages[i].color = idleColor;
    }
}

/// <summary>接收透明按钮点击事件的最小代理组件。</summary>
public class SkillButtonProxy : MonoBehaviour, IPointerClickHandler
{
    public int buttonIndex;
    public System.Action<int> onClick;
    public void OnPointerClick(PointerEventData e) { onClick?.Invoke(buttonIndex); }
}
