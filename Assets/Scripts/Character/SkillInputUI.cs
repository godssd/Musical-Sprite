using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// P2 主动技能 3-键输入 UI（仅服务左侧玩家 / side=0）。
/// 屏幕右边缘竖排列三个透明方块（自上而下：←、↓、→），同时监听键盘 LeftArrow/DownArrow/RightArrow。
/// 按 ABC 顺序输入（按任意键 → 推进序列），时间窗 ≤ inputWindow 秒完成即触发对应队伍角色主动技能。
///
/// 挂载：场景任意 GO；ScoreManager/FeverManager 启动后会自动补建。
/// </summary>
public class SkillInputUI : MonoBehaviour
{
    [Header("节流")]
    public float inputWindow = 0.5f;        // 键序时间窗（秒）
    [Header("主题色")]
    public Color pressedColor = new Color(1f, 0.9f, 0.2f, 1f);
    public Color readyColor = new Color(1f, 1f, 1f, 0.18f);
    public Color idleColor = new Color(1f, 1f, 1f, 0.08f);

    [Header("系统引用")]
    public CharacterBattleSystem battle;

    private Image[] btnImages = new Image[3];
    private Text[] btnLabels = new Text[3];
    private readonly KeyCode[] keys = { KeyCode.LeftArrow, KeyCode.DownArrow, KeyCode.RightArrow };
    private readonly string[] arrows = { "←", "↓", "→" };
    private readonly int[] targetLanes = { 0, 1, 2 }; // 三个按钮对应三个队伍音轨（lane0/1/2）。第三个 → 对应 lane3 也可绑定，简化取 lane%3
    private int sequencePos = 0;
    private float lastInputTime;

    void Start()
    {
        if (battle == null) battle = FindFirstObjectByType<CharacterBattleSystem>();
        BuildButtons();
        ResetSequence();
    }

    void BuildButtons()
    {
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

        // 屏幕右边缘，竖排列 3 个按钮
        for (int i = 0; i < 3; i++)
        {
            GameObject go = new GameObject("SkillBtn_" + arrows[i]);
            go.transform.SetParent(canvasGo.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(160f, 160f);
            rt.anchoredPosition = new Vector2(-32f, (1 - i) * 180f); // 上中下
            Image img = go.AddComponent<Image>();
            img.color = idleColor;
            img.raycastTarget = true;
            btnImages[i] = img;

            // 文字
            GameObject txtGo = new GameObject("Label");
            txtGo.transform.SetParent(go.transform, false);
            var tr = txtGo.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;
            Text t = txtGo.AddComponent<Text>();
            t.text = arrows[i];
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.fontSize = 110;
            t.fontStyle = FontStyle.Bold;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            btnLabels[i] = t;

            // 点击 → 等价于键盘事件
            int idx = i;
            var trigger = go.AddComponent<SkillButtonProxy>();
            trigger.buttonIndex = idx;
            trigger.onClick = OnButtonPushed;
        }
    }

    void Update()
    {
        if (Time.time - lastInputTime > inputWindow && sequencePos != 0)
        {
            ResetSequence();
        }

        // 键盘监听
        for (int i = 0; i < 3; i++)
        {
            if (Input.GetKeyDown(keys[i]))
            {
                OnButtonPushed(i);
            }
        }
    }

    public void OnButtonPushed(int idx)
    {
        // 期望顺序：0 → 1 → 2（按一次只能推进 1；如果按对就换色 + 推进；不匹配就重置）
        if (idx == sequencePos)
        {
            sequencePos++;
            lastInputTime = Time.time;
            Flash(idx);
            if (sequencePos >= 3)
            {
                // 完整 ABC 序列完成
                int laneToCast = -1;
                if (battle != null)
                {
                    // 选择最近一次按的按钮对应的 lane（0/1/2），若 lane0~2 都已满，则仍取最近一次按的 lane（最后一次按下对应 lane index）
                    laneToCast = targetLanes[idx];
                    if (battle.TryCastActive(0, laneToCast))
                    {
                        Debug.Log($"[SkillInputUI] 序列完成 → 释放 lane {laneToCast} 主动技能");
                    }
                    else
                    {
                        Debug.Log($"[SkillInputUI] 序列完成但 lane {laneToCast} 能量不足/无可用技能");
                    }
                }
                ResetSequence();
            }
        }
        else
        {
            ResetSequence();
            sequencePos = 0;
        }
    }

    private void Flash(int idx)
    {
        if (btnImages[idx] != null) btnImages[idx].color = pressedColor;
        CancelInvoke(nameof(ClearFlash));
        Invoke(nameof(ClearFlash), 0.15f);
    }

    private void ClearFlash()
    {
        for (int i = 0; i < 3; i++)
            if (btnImages[i] != null) btnImages[i].color = idleColor;
    }

    private void ResetSequence()
    {
        sequencePos = 0;
    }
}

/// <summary>接收透明按钮点击事件的最小代理组件。</summary>
public class SkillButtonProxy : MonoBehaviour, IPointerClickHandler
{
    public int buttonIndex;
    public System.Action<int> onClick;
    public void OnPointerClick(PointerEventData e) { onClick?.Invoke(buttonIndex); }
}
