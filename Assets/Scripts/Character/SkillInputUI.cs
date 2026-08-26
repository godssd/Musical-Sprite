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
/// 能量满是「必要不充分」条件：未充满时按对序列只会亮闪，不会释放。
///
/// 挂载：场景任意 GO；ScoreManager/FeverManager 启动后会自动补建。
/// </summary>
public class SkillInputUI : MonoBehaviour
{
    [Header("节流")]
    public float inputWindow = 1.5f;        // 序列时间窗（秒）

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
    private Dictionary<string, int> progress = new Dictionary<string, int>();   // 运行时唯一键 -> 已按对步数
    private Dictionary<string, float> lastInput = new Dictionary<string, float>();

    /// <summary>每个 ActiveSkillRuntime 的唯一定位键（characterId + skillId + 实例），使同一角色多个主动技能互不干扰。</summary>
    private string Key(ActiveSkillRuntime rt)
    {
        string sid = (rt.SkillRef != null) ? rt.SkillRef.skillId : "_";
        return rt.owner.characterId + "_" + sid + "_" + rt.GetInstanceID();
    }
    private Coroutine[] popCo = new Coroutine[3];

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
        // 序列时间窗超时 → 重置进度
        var runtimes = FindObjectsByType<ActiveSkillRuntime>(FindObjectsSortMode.None);
        foreach (var rt in runtimes)
        {
            if (rt.ownerSide != 0 || rt.owner == null) continue;
            string key = Key(rt);
            if (progress.ContainsKey(key) && progress[key] > 0)
            {
                float last = lastInput.ContainsKey(key) ? lastInput[key] : -99f;
                if (Time.time - last > inputWindow) progress[key] = 0;
            }
        }

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

    /// <summary>按下一个技能输入步：对每个「可释放」的己方角色运行时匹配其 inputSequence。
    /// 能量技能需能量满才能释放（但序列可正常亮闪）；无能量技能（玩家主动）随时可输入。</summary>
    private void OnKeyPressed(SkillInputStep step)
    {
        var runtimes = FindObjectsByType<ActiveSkillRuntime>(FindObjectsSortMode.None);
        foreach (var rt in runtimes)
        {
            if (rt.ownerSide != 0 || rt.owner == null || !rt.owner.HasActiveSkill) continue;
            if (rt.NeedsEnergyGate && !rt.owner.isFullyCharged) continue;   // 仅能量技能卡能量门槛；无能量技能随时可输入
            var seq = rt.inputSequence;
            if (seq == null || seq.Length == 0) continue;

            string key = Key(rt);
            int p = progress.ContainsKey(key) ? progress[key] : 0;
            if (p < seq.Length && seq[p] == step)
            {
                p++;
                progress[key] = p;
                lastInput[key] = Time.time;
                if (rt.marker != null) rt.marker.Flash();   // 对应角色亮闪一次
                if (p >= seq.Length)
                {
                    rt.BeginCast();
                    progress[key] = 0;
                }
            }
            else
            {
                progress[key] = 0;   // 按错：重置该运行时进度
            }
        }
    }

    public void OnButtonPushed(int idx)
    {
        if (idx < 0 || idx > 2) return;
        OnKeyPressed(touchSteps[idx]);                 // 触摸/点击：直接对应 Left/Down/Right
        Flash(touchSteps[idx]);                          // 同组按钮一起高亮
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
