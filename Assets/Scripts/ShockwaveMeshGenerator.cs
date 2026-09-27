using UnityEngine;

/// <summary>
/// P1 拱形冲击波 mesh 程序化生成器（A 方案：复杂度低，先试，不行再考虑美术 FBX）。
///
/// 形状：一面"拱形幕墙"
///   - X 方向：从 backX（判定线侧）延伸到 frontX（前沿/中缝侧）
///   - Z 方向：铺满场地深度 z ∈ [-zHalfRange, zHalfRange]
///   - Y 方向：拱高，中央(z=0)最高、边缘(z=±zHalfRange)接地（sin 曲线）
///
/// 顶点色（P1 渐变雏形，对应你描述的"从判定线淡入"）：
///   - 红墙 / 蓝墙 基础色由 side 决定
///   - alpha 沿 X：backX(判定线)=0 完全消失，frontX(中缝)=最浓（fadePower 控制曲率）
///   - 此 alpha 渐变即 P2 shader 的"单侧渐变"雏型，P2 会换成贴图 + 色相渐变
///
/// 参数全部 Inspector 可调，配合 [ExecuteAlways] 在 Scene 直接看形状与渐变。
/// 运行时由 ShockwavePreview 驱动 backX / frontX（红方 leftEdge→centerX，蓝方 rightEdge→centerX）。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class ShockwaveMeshGenerator : MonoBehaviour
{
    public enum Side { Red, Blue }

    [Header("几何")]
    public float archHeight = 1.2f;    // z=0 处拱顶高度
    public float zHalfRange = 3.75f;   // 场地半深
    public int zSegments = 32;         // 沿深度分段
    public int xSegments = 24;         // 沿 X（推进）分段

    [Header("X 区间（静态预览，运行时由 ShockwavePreview 驱动）")]
    public float backX = -6f;          // 判定线侧
    public float frontX = 0f;          // 前沿 / 中缝侧

    [Header("侧别与渐变")]
    public Side side = Side.Red;
    [Tooltip("沿 X 的 alpha 渐变指数：0=判定线消失，1=前沿最浓。>1 更集中在中缝")]
    public float fadePower = 1f;

    private Mesh _mesh;

    void OnValidate() { Rebuild(); }
    void Awake() { Rebuild(); }

#if UNITY_EDITOR
    void Start() { if (_mesh == null) Rebuild(); }
#endif

    void Rebuild()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf == null) return;

        if (_mesh == null) { _mesh = new Mesh(); _mesh.name = "ShockwaveMesh"; }
        _mesh.Clear();

        int xCount = Mathf.Max(2, xSegments + 1);
        int zCount = Mathf.Max(2, zSegments + 1);
        int vertCount = xCount * zCount;

        Vector3[] verts = new Vector3[vertCount];
        Color[] cols = new Color[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        Vector3[] norms = new Vector3[vertCount];

        Color baseCol = side == Side.Red
            ? new Color(1f, 0.25f, 0.30f)
            : new Color(0.30f, 0.55f, 1f);

        for (int ix = 0; ix < xCount; ix++)
        {
            float tx = ix / (float)(xCount - 1);              // 0=back(判定线) 1=front(中缝)
            float x = Mathf.Lerp(backX, frontX, tx);
            float alpha = Mathf.Pow(tx, fadePower);           // 判定线消失，中缝最浓

            for (int iz = 0; iz < zCount; iz++)
            {
                float tz = iz / (float)(zCount - 1);          // 0=边缘 1=中央
                float z = Mathf.Lerp(-zHalfRange, zHalfRange, tz);
                float y = archHeight * Mathf.Sin(tz * Mathf.PI); // 中央最高、边缘接地

                int idx = ix * zCount + iz;
                verts[idx] = new Vector3(x, y, z);
                cols[idx] = new Color(baseCol.r, baseCol.g, baseCol.b, alpha);
                uvs[idx] = new Vector2(tx, tz);
                norms[idx] = new Vector3(0f, 1f, 0f);
            }
        }

        int triCount = (xCount - 1) * (zCount - 1) * 6;
        int[] tris = new int[triCount];
        int t = 0;
        for (int ix = 0; ix < xCount - 1; ix++)
        {
            for (int iz = 0; iz < zCount - 1; iz++)
            {
                int a = ix * zCount + iz;
                int b = a + 1;
                int c = (ix + 1) * zCount + iz;
                int d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        }

        _mesh.SetVertices(verts);
        _mesh.SetColors(cols);
        _mesh.SetUVs(0, uvs);
        _mesh.SetNormals(norms);
        _mesh.SetIndices(tris, MeshTopology.Triangles, 0);
        _mesh.RecalculateBounds();

        mf.sharedMesh = _mesh;
    }
}
