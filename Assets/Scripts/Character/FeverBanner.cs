using System.Collections;
using UnityEngine;

/// <summary>
/// 过热（世界空间）山峦涂鸦表现。
/// - 每侧 2 个 LineRenderer：蓝色 Fever / 红色 Super（在 Fever 后方更远处 z+offset）。
/// - 7 个预定义顶点画一个"山峦涂鸦"形状：4 个山峰 + 2 端点（贴地）。
/// - Fever 进入：蓝色山峦从地面下（hiddenY）升起到 targetY。
/// - SuperFever 进入：红色山峦在蓝色后景（z+offset）升起，比蓝色更高。
/// - 解除（回到 None）：两座山峦向下掉落消失。
/// - 镜像 X：side=1 整体翻转到对侧场地中线上方。
///
/// 挂载：场景任意 GameObject（FeverManager.EnsureFeverBanner 会自动补建）。
/// </summary>
public class FeverBanner : MonoBehaviour
{
    [Header("侧别（0=玩家侧, 1=AI 侧）")]
    public int side = 0;

    [Header("颜色（与 FeverVFXPlaceholder 主题色一致）")]
    public Color feverColor = new Color(0.3f, 0.7f, 1f);     // 蓝
    public Color superColor = new Color(1f, 0.35f, 0.25f);  // 红

    [Header("世界坐标（山峦在场地中线 X=0 后方上空的相对位置）")]
    [Tooltip("X 偏移：side=0 取负、side=1 取正（场地中线左右各一座）")]
    public float sideOffsetX = -2.4f;
    [Tooltip("Z 距离：相机在 z=-2.8 看 +Z，5~6 在粉杠远景处")]
    public float baseZ = 5.2f;
    [Tooltip("SuperFever 红色山峦在 Fever 蓝色山峦后方的 z 偏移")]
    public float superZOffset = 0.6f;

    [Header("Y 动画")]
    [Tooltip("升起后相对 Y 高度（相对 bannerGo 的本地原点）")]
    public float targetY = 0f;
    [Tooltip("完全隐藏的相对 Y 高度（地面下）")]
    public float hiddenY = -3.5f;
    [Tooltip("升起/落下动画时长（秒）")]
    public float animDuration = 0.5f;

    [Header("线段宽度")]
    public float feverLineWidth = 0.18f;
    public float superLineWidth = 0.22f;

    /// <summary>7 顶点山峦（Fever 4 峰版本）。z=0 全部为本地坐标。</summary>
    private static readonly Vector3[] feverPeaks = new Vector3[]
    {
        new Vector3(-3.0f, 0.0f, 0f),
        new Vector3(-1.9f, 2.0f, 0f),
        new Vector3(-0.8f, 1.1f, 0f),
        new Vector3( 0.0f, 2.5f, 0f), // 中央最高
        new Vector3( 0.8f, 1.2f, 0f),
        new Vector3( 1.9f, 2.0f, 0f),
        new Vector3( 3.0f, 0.0f, 0f),
    };

    /// <summary>7 顶点山峦（Super 6 峰版本，峰更高）。</summary>
    private static readonly Vector3[] superPeaks = new Vector3[]
    {
        new Vector3(-3.0f, 0.0f, 0f),
        new Vector3(-1.9f, 3.0f, 0f),
        new Vector3(-0.8f, 1.8f, 0f),
        new Vector3( 0.0f, 3.6f, 0f), // 中央最高
        new Vector3( 0.8f, 1.9f, 0f),
        new Vector3( 1.9f, 3.0f, 0f),
        new Vector3( 3.0f, 0.0f, 0f),
    };

    private LineRenderer feverLine;
    private LineRenderer superLine;
    private Transform feverGo;
    private Transform superGo;
    private Coroutine animRoutine;

    void Awake()
    {
        BuildLines();
    }

    void Start()
    {
        var fever = FindFirstObjectByType<FeverManager>();
        if (fever == null) return;
        fever.OnStateChanged += OnFeverStateChanged;
        // 立即对齐一次当前状态
        OnFeverStateChanged(FeverState.None, fever.GetState(side), side);
    }

    void OnDestroy()
    {
        var fever = FindFirstObjectByType<FeverManager>();
        if (fever != null) fever.OnStateChanged -= OnFeverStateChanged;
    }

    private void OnFeverStateChanged(FeverState oldState, FeverState cur, int eventSide)
    {
        if (eventSide != side) return;
        ApplyState(cur);
    }

    private void BuildLines()
    {
        // Fever 蓝色线
        GameObject feverObj = new GameObject("FeverLine_Blue");
        feverObj.transform.SetParent(transform, false);
        feverObj.transform.localPosition = new Vector3(sideOffsetX, hiddenY, baseZ);
        feverGo = feverObj.transform;
        feverLine = feverObj.AddComponent<LineRenderer>();
        ConfigureLine(feverLine, feverColor, feverLineWidth);
        feverLine.positionCount = feverPeaks.Length;
        ApplyPeaks(feverLine, feverPeaks, hiddenY);

        // Super 红色线（在 Fever 蓝色线后方）
        GameObject superObj = new GameObject("SuperLine_Red");
        superObj.transform.SetParent(transform, false);
        superObj.transform.localPosition = new Vector3(sideOffsetX, hiddenY, baseZ + superZOffset);
        superGo = superObj.transform;
        superLine = superObj.AddComponent<LineRenderer>();
        ConfigureLine(superLine, superColor, superLineWidth);
        superLine.positionCount = superPeaks.Length;
        ApplyPeaks(superLine, superPeaks, hiddenY);
    }

    private static void ConfigureLine(LineRenderer lr, Color c, float width)
    {
        lr.useWorldSpace = false;
        lr.startWidth = lr.endWidth = width;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 2;
        lr.alignment = LineAlignment.View; // 始终面向相机，让线条粗细不随距离变
        lr.startColor = lr.endColor = c;
        // 找可用 shader：Sprites/Default > Unlit/Color > UI/Default
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("UI/Default");
        if (sh != null)
        {
            var mat = new Material(sh);
            mat.color = c;
            lr.material = mat;
        }
    }

    private void ApplyPeaks(LineRenderer lr, Vector3[] peaks, float yOffset)
    {
        // 把山峦平移：所有顶点的 y 加上当前 yOffset（hiddenY 或 targetY）
        Vector3[] world = new Vector3[peaks.Length];
        for (int i = 0; i < peaks.Length; i++)
            world[i] = new Vector3(peaks[i].x, peaks[i].y + yOffset, peaks[i].z);
        lr.SetPositions(world);
    }

    /// <summary>由 FeverVFXPlaceholder 在 OnStateChanged 中调用。</summary>
    public void ApplyState(FeverState cur)
    {
        if (animRoutine != null) StopCoroutine(animRoutine);
        animRoutine = StartCoroutine(AnimateTo(cur));
    }

    private IEnumerator AnimateTo(FeverState cur)
    {
        // 目标 y：fever=targetY，super=targetY（也升起，z 自然更靠后形成前后关系）
        float feverStart = feverGo.localPosition.y;
        float superStart = superGo.localPosition.y;
        float feverEnd, superEnd;
        bool feverVisible = false, superVisible = false;
        if (cur == FeverState.Fever)
        {
            feverEnd = targetY; feverVisible = true;
            superEnd = hiddenY; superVisible = false;
        }
        else if (cur == FeverState.SuperFever)
        {
            feverEnd = targetY; feverVisible = true;
            superEnd = targetY; superVisible = true;
        }
        else // None
        {
            feverEnd = hiddenY; feverVisible = false;
            superEnd = hiddenY; superVisible = false;
        }

        // 启用/禁用渲染
        if (feverLine != null) feverLine.enabled = true;
        if (superLine != null) superLine.enabled = superVisible || true; // 始终启用，位置控可见

        // 透明度
        Color feverColTarget = feverColor;
        feverColTarget.a = feverVisible ? 1f : 0f;
        Color superColTarget = superColor;
        superColTarget.a = superVisible ? 1f : 0f;

        float t = 0f;
        Color feverColStart = feverLine.startColor;
        Color superColStart = superLine.startColor;

        while (t < animDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / animDuration);
            // 用 ease-out-cubic
            float ease = 1f - Mathf.Pow(1f - k, 3f);
            float yF = Mathf.Lerp(feverStart, feverEnd, ease);
            float yS = Mathf.Lerp(superStart, superEnd, ease);
            feverGo.localPosition = new Vector3(feverGo.localPosition.x, yF, feverGo.localPosition.z);
            superGo.localPosition = new Vector3(superGo.localPosition.x, yS, superGo.localPosition.z);
            ApplyPeaks(feverLine, feverPeaks, yF);
            ApplyPeaks(superLine, superPeaks, yS);
            feverLine.startColor = feverLine.endColor = Color.Lerp(feverColStart, feverColTarget, ease);
            superLine.startColor = superLine.endColor = Color.Lerp(superColStart, superColTarget, ease);
            yield return null;
        }
        // 收尾
        feverGo.localPosition = new Vector3(feverGo.localPosition.x, feverEnd, feverGo.localPosition.z);
        superGo.localPosition = new Vector3(superGo.localPosition.x, superEnd, superGo.localPosition.z);
        ApplyPeaks(feverLine, feverPeaks, feverEnd);
        ApplyPeaks(superLine, superPeaks, superEnd);
        feverLine.startColor = feverLine.endColor = feverColTarget;
        superLine.startColor = superLine.endColor = superColTarget;
        animRoutine = null;
    }
}
