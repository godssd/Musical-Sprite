using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 过热占位 VFX（P1 临时表现）。
/// - 在运行时创建全屏 Overlay Canvas，左右半屏各一块 Image（轻 tint 提示）。
/// - 订阅 FeverManager.OnStateChanged：左侧玩家触发 → 左半屏，右侧（对手/AI）触发 → 右半屏。
/// - None=透明；Fever=红(左)/蓝(右) 脉冲；SuperFever=金色脉冲。
/// - 队伍方块在过热时向主题色混合 60%，保留角色身份色（避免分不清谁是谁）。
/// - 中央文字 banner 已被替换为世界空间的 FeverBanner 山峦涂鸦，详见 FeverBanner.cs。
///
/// 挂载：场景任意 GO（FeverManager 会自动补建）。无正式美术，后续整体替换。
/// </summary>
public class FeverVFXPlaceholder : MonoBehaviour
{
    [Header("主题色")]
    public Color leftTheme = new Color(1f, 0.3f, 0.3f);    // 红方
    public Color rightTheme = new Color(0.3f, 0.6f, 1f);   // 蓝方
    public Color superColor = new Color(1f, 0.85f, 0.1f);  // 超级过热（金）

    [Header("队伍方块（按 side 自动绑定 CharacterCubeMarker）")]
    public List<Renderer> leftTeamCubes = new List<Renderer>();
    public List<Renderer> rightTeamCubes = new List<Renderer>();

    // 角色身份色快照：进入过热前先抓一次，过热时只“向主题色混合”，不整体覆盖成白/红/蓝（否则谁是谁就分不清了）
    private List<Color> leftTeamCubeIdentity = new List<Color>();
    private List<Color> rightTeamCubeIdentity = new List<Color>();
    private bool identityCaptured = false;

    private Image[] overlays = new Image[2];     // 0=左, 1=右
    private readonly Color[] targetColor = new Color[2];
    private readonly float[] targetAlpha = new float[2];

    private FeverManager fever;

    void Start()
    {
        fever = FindFirstObjectByType<FeverManager>();
        if (fever == null)
        {
            Debug.LogWarning("[FeverVFXPlaceholder] 未找到 FeverManager");
            return;
        }
        if (leftTeamCubes.Count == 0 && rightTeamCubes.Count == 0)
            AutoFindCubes();
        BuildOverlays();
        fever.OnStateChanged += OnFeverChanged;
    }

    void OnDestroy()
    {
        if (fever != null) fever.OnStateChanged -= OnFeverChanged;
    }

    private void BuildOverlays()
    {
        GameObject canvasGo = new GameObject("FeverVFXCanvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        for (int side = 0; side < 2; side++)
        {
            // 左右半屏 tint
            GameObject img = new GameObject(side == 0 ? "FeverOverlayLeft" : "FeverOverlayRight");
            img.transform.SetParent(canvasGo.transform, false);
            RectTransform rt = img.AddComponent<RectTransform>();
            if (side == 0)
            {
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0.5f, 1f);
            }
            else
            {
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
            }
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image image = img.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = false;
            overlays[side] = image;
        }
    }

    private void AutoFindCubes()
    {
        var markers = FindObjectsByType<CharacterCubeMarker>(FindObjectsSortMode.None);
        if (markers.Length == 0)
            return; // 场景里没人手动补 marker，正常（CharacterBattleSystem 启动时也会再尝试一次）
        identityCaptured = false; // 列表可能变化，下次 OnFeverChanged 重新抓身份色
        foreach (var m in markers)
        {
            if (m == null) continue;
            if (m.IsPlayer) continue; // 玩家自身不参与队伍方块材质变色
            var r = m.GetComponent<Renderer>();
            if (r == null) continue;
            if (m.side == 0) leftTeamCubes.Add(r);
            else if (m.side == 1) rightTeamCubes.Add(r);
        }
    }

    /// <summary>抓一次队伍方块的当前颜色作为“身份色”快照（在第一次过热变化前调用）。</summary>
    private void CaptureIdentities()
    {
        leftTeamCubeIdentity.Clear();
        rightTeamCubeIdentity.Clear();
        foreach (var r in leftTeamCubes) leftTeamCubeIdentity.Add(r != null ? r.material.color : Color.white);
        foreach (var r in rightTeamCubes) rightTeamCubeIdentity.Add(r != null ? r.material.color : Color.white);
    }

    private void OnFeverChanged(FeverState oldState, FeverState cur, int side)
    {
        Color c = cur == FeverState.SuperFever ? superColor :
                  cur == FeverState.Fever ? (side == 0 ? leftTheme : rightTheme) :
                  Color.white;
        float a = cur == FeverState.None ? 0f : (cur == FeverState.SuperFever ? 0.45f : 0.30f);
        targetColor[side] = c;
        targetAlpha[side] = a;

        // 队伍方块材质变色：保留角色身份色，过热时仅“向主题色混合 60%”（不再整体覆盖成白/红/蓝，避免分不清谁是谁）
        var cubes = side == 0 ? leftTeamCubes : rightTeamCubes;
        var identities = side == 0 ? leftTeamCubeIdentity : rightTeamCubeIdentity;
        if (!identityCaptured)
        {
            CaptureIdentities();
            identityCaptured = true;
        }
        Color feverTint = cur == FeverState.None ? Color.clear :
                          cur == FeverState.SuperFever ? superColor : (side == 0 ? leftTheme : rightTheme);
        for (int i = 0; i < cubes.Count; i++)
        {
            var r = cubes[i];
            if (r == null) continue;
            Color baseCol = (i < identities.Count) ? identities[i] : r.material.color;
            // None：保持身份色；Fever/Super：身份色 → 主题色 混合 60%
            Color target = (cur == FeverState.None) ? baseCol : Color.Lerp(baseCol, feverTint, 0.6f);
            r.material.color = target;
        }
    }

    void Update()
    {
        if (overlays[0] == null) return;
        float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 6f);
        for (int s = 0; s < 2; s++)
        {
            if (overlays[s] == null) continue;
            float a = targetAlpha[s] * (targetAlpha[s] > 0f ? pulse : 1f);
            Color tgt = new Color(targetColor[s].r, targetColor[s].g, targetColor[s].b, a);
            overlays[s].color = Color.Lerp(overlays[s].color, tgt, Time.deltaTime * 8f);
        }
    }
}
