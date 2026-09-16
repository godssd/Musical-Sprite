using UnityEngine;

/// <summary>
/// 临时音轨可视化 / 调参工具（调完即可删除，纯可视化、零玩法依赖）。
/// 作用：在不改动 NoteSpawner / BattleCenterLine 任何逻辑的前提下，于场景中画出
///   - 玩家侧（及可选镜像蓝侧）的 4 条轨道带（沿 Z 排布，间距 = laneSpacing）
///   - 中央判定线（位于 judgeLineX，沿 Z 横跨两侧）
///   - 音符半径参考圆（默认半径 = laneHalfWidth = 0.45，用于看比例）
/// 并提供可拖动手柄（见 NoteTrackRigEditor）：
///   拖轨道带 Z 边缘 → 改 laneSpacing（= 正式音轨宽度参数）
///   拖判定线 → 改 judgeLineX（= 正式判定线 X）
/// 调好后把 Inspector 里的 laneSpacing / judgeLineX 报给 AI，由 AI 烤进正式参数。
/// </summary>
[ExecuteInEditMode]
public class NoteTrackRig : MonoBehaviour
{
    [Header("轨道数（每侧）")]
    public int laneCount = 4;

    [Header("轨道 Z 间距 —— 就是正式的 laneSpacing，关键输出")]
    public float laneSpacing = 1.0f;

    [Header("单轨视觉半宽（Z）。默认 = 音符半径 0.45，仅用于画带与边缘")]
    public float laneHalfWidth = 0.45f;

    [Header("判定线 X —— 就是正式的 judgeLineX，关键输出")]
    public float judgeLineX = 0f;

    [Header("玩家侧远端生成点 X（仅用于画走廊，默认 -8）")]
    public float playerStartX = -8f;

    [Header("镜像显示蓝侧（蓝侧与玩家侧沿判定线对称）")]
    public bool mirrorBlue = true;

    [Header("地面高度（仅抬升 gizmo 防 z-fight）")]
    public float groundY = 0.02f;

    /// <summary>单侧轨道带的半宽（最外轨中心 ± laneHalfWidth）</summary>
    public float BandHalfWidth => (laneCount - 1) * 0.5f * laneSpacing + laneHalfWidth;

    private void OnValidate()
    {
        laneCount = Mathf.Max(1, laneCount);
        laneSpacing = Mathf.Max(0.2f, laneSpacing);
        laneHalfWidth = Mathf.Max(0.05f, laneHalfWidth);
    }

    // 实际绘制交给 Editor 的 OnSceneGUI（手柄更灵活）；
    // 这里用 Gizmos 做常驻可视化，未选中也能在 Scene 视图看到轨道带。
    void OnDrawGizmos()
    {
        float bandHalf = BandHalfWidth;

        // 玩家侧轨道带：x ∈ [playerStartX, judgeLineX]
        DrawSide(playerStartX, judgeLineX, bandHalf, new Color(1f, 0.4f, 0.4f, 0.18f));
        // 蓝侧镜像：x ∈ [judgeLineX, -playerStartX]
        if (mirrorBlue)
            DrawSide(judgeLineX, -playerStartX, bandHalf, new Color(0.4f, 0.6f, 1f, 0.18f));

        // 判定线（横跨两侧整条带）
        float lineHalf = bandHalf + laneHalfWidth;
        Gizmos.color = Color.yellow;
        Vector3 a = new Vector3(judgeLineX, groundY, -lineHalf);
        Vector3 b = new Vector3(judgeLineX, groundY, lineHalf);
        Gizmos.DrawLine(a, b);
        // 判定线两端小标记
        Gizmos.DrawCube(a, Vector3.one * 0.12f);
        Gizmos.DrawCube(b, Vector3.one * 0.12f);

        // 音符半径参考圆（在每个轨道与判定线交点处）
        Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
        for (int side = 0; side < (mirrorBlue ? 2 : 1); side++)
        {
            for (int i = 0; i < laneCount; i++)
            {
                float z = (i - (laneCount - 1) * 0.5f) * laneSpacing;
                Gizmos.DrawWireSphere(new Vector3(judgeLineX, groundY, z), laneHalfWidth);
            }
        }
    }

    private void DrawSide(float xMin, float xMax, float bandHalf, Color c)
    {
        // 整条带外框（淡）
        Gizmos.color = c;
        Vector3 center = new Vector3((xMin + xMax) * 0.5f, groundY, 0f);
        Vector3 size = new Vector3(Mathf.Abs(xMax - xMin), 0.001f, bandHalf * 2f);
        Gizmos.DrawCube(center, size);

        // 每条轨道中心虚线
        Gizmos.color = new Color(c.r, c.g, c.b, 0.6f);
        for (int i = 0; i < laneCount; i++)
        {
            float z = (i - (laneCount - 1) * 0.5f) * laneSpacing;
            Gizmos.DrawLine(new Vector3(xMin, groundY, z), new Vector3(xMax, groundY, z));
        }
    }
}
