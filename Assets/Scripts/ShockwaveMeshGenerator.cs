using UnityEngine;

/// <summary>
/// P1 拱形冲击波 mesh 程序化生成器（A 方案）。
///
/// 形状：一面"拱形幕墙"
///   - X 方向：从 backX（判定线侧）延伸到 frontX（前沿/中缝侧）
///   - Z 方向：铺满场地深度 z ∈ [-zHalfRange, zHalfRange]
///   - Y 方向：拱高，两侧边缘(z=±zHalfRange)拱起、中央(z=0)低平（cos 曲线，方便对撞贴合）
///
/// 渐变 / 颜色 / 边缘光 全部由 P2 shader（MusicalSprite/ShockwaveUnlit）按
/// 物体局部坐标 + _BackX/_FrontX/_ArchHeight 实时推导，本脚本只负责：
///   - 生成几何（顶点色留白，颜色交给材质）
///   - 把几何锚点 + 外观参数同步进材质，使 shader 在墙移动时自动跟随
///
/// 参数全部 Inspector 可调。Refresh() 带脏检查：几何参数变化才重建 mesh，
/// 外观参数每帧低成本写材质，避免对撞动画每帧产生 mesh 垃圾。
/// 运行时由 ShockwavePreview 驱动 backX / frontX（红方 leftEdge→centerX，蓝方 rightEdge→centerX）。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class ShockwaveMeshGenerator : MonoBehaviour
{
    public enum Side { Red, Blue }

    [Header("几何")]
    public float archHeight = 1.2f;    // 中央（z=0）最高，边缘（z=±）接地；两墙对撞时中间先贴合
    public float zHalfRange = 3.75f;   // 场地半深
    public int zSegments = 32;         // 沿深度分段
    public int xSegments = 24;         // 沿 X（推进）分段

    [Header("导入模型（WALL.fbx）—— 优先使用，几何以美术模型为准")]
    [Tooltip("开启后直接用美术给的 FBX 模型作为冲击波网格，不再程序化生成。关闭则回退到程序化拱形（可回退）")]
    public bool useImportedMesh = true;
    [Tooltip("冲击波网格。留空时 Editor 下自动从『导入模型路径』加载第一个 Mesh")]
    public Mesh importedMesh;
#if UNITY_EDITOR
    [Tooltip("Editor 下自动加载的模型路径（支持 .fbx / .obj，取其第一个 Mesh）")]
    public string importedMeshAssetPath = "Assets/Art/VFX/WALL.fbx";
#endif
    [Header("导入模型自动适配（把模型缩放平移到战场区间）")]
    [Tooltip("开启后按 backX/frontX 与下面的目标尺寸自动缩放平移；关闭则保留你手动调好的 Transform")]
    public bool autoFit = false;
    [Tooltip("适配目标：Z 半深（战场纵深一半）")]
    public float fitTargetZHalf = 3.75f;
    [Tooltip("适配目标：墙高度")]
    public float fitTargetHeight = 1.2f;
    [Tooltip("适配目标：墙底离地高度")]
    public float fitBaseY = 0f;

    [Header("X 区间（运行时由 ShockwavePreview 驱动）")]
    public float backX = -6f;          // 判定线侧
    public float frontX = 0f;          // 前沿 / 中缝侧

    [Header("侧别")]
    public Side side = Side.Red;

    [Header("外观（P2 shader 读取，Inspector 可调，蓝墙由预览器初始化）")]
    [Tooltip("沿 X 的 alpha 淡入指数：0=判定线消失，1=中缝最浓。>1 更集中在中缝")]
    public float fadePower = 1.1f;
    [Tooltip("色相渐变曲率：判定线深 -> 中缝亮（曲线弯曲程度，不影响比重）")]
    public float gradientPower = 1.0f;
    [Tooltip("Deep/Tip 颜色控制比重：0=Deep 占绝大部分（Tip 只在前沿很小一块），1=Tip 占绝大部分（Deep 只在根部很小一块），0.5=各占约一半")]
    [Range(0f, 1f)] public float gradientBalance = 0.5f;
    public float edgeGlow = 0.18f;
    [Range(0f, 1f)] public float opacity = 0.40f;
    public Color colorDeep = new Color(0.55f, 0.08f, 0.15f);
    public Color colorTip  = new Color(1.0f, 0.38f, 0.42f);
    [Header("纠色系数（乘法，红蓝各自独立；默认白=不改）")]
    [Tooltip("整体乘到 colorDeep/colorTip 上，用于一键纠色而不必分别调 deep/tip")]
    public Color colorTint = Color.white;

    private Mesh _mesh;
    // 几何脏检查缓存
    private float _cBackX, _cFrontX, _cArch, _cZHalf;
    private int   _cXSeg, _cZSeg;

    void OnValidate() { Refresh(); }
    void Awake() { Refresh(); }
#if UNITY_EDITOR
    void Start() { if (_mesh == null) Refresh(); }
#endif

    /// <summary>预览器每帧调用：几何脏才重建，外观参数每帧写材质。</summary>
    public void Refresh()
    {
        if (useImportedMesh)
        {
#if UNITY_EDITOR
            EnsureImportedMesh();
#endif
            if (importedMesh != null)
            {
                var mf0 = GetComponent<MeshFilter>();
                if (mf0 != null && mf0.sharedMesh != importedMesh) mf0.sharedMesh = importedMesh;
                // autoFit 关闭时保留你手动调好的 Transform，不覆盖（Refresh 每帧都会跑）。
                if (autoFit) FitImportedMesh();
                SyncMaterial();
                return;
            }
        }
        else
        {
            ResetTransformForMesh();
        }

        bool geoDirty = _mesh == null
            || !Mathf.Approximately(_cBackX, backX)
            || !Mathf.Approximately(_cFrontX, frontX)
            || !Mathf.Approximately(_cArch, archHeight)
            || !Mathf.Approximately(_cZHalf, zHalfRange)
            || _cXSeg != xSegments
            || _cZSeg != zSegments;
        if (geoDirty) RebuildMesh();
        SyncMaterial();
    }

    /// <summary>程序化模式下顶点已含真实坐标，transform 必须归位，否则与导入模型残留的缩放叠加。</summary>
    void ResetTransformForMesh()
    {
        if (!Mathf.Approximately(transform.localScale.x, 1f) ||
            !Mathf.Approximately(transform.localScale.y, 1f) ||
            !Mathf.Approximately(transform.localScale.z, 1f))
            transform.localScale = Vector3.one;
        if (transform.localPosition.sqrMagnitude > 1e-6f) transform.localPosition = Vector3.zero;
    }

#if UNITY_EDITOR
    /// <summary>Editor 下 importedMesh 为空时，自动从 importedMeshAssetPath 取第一个 Mesh。</summary>
    void EnsureImportedMesh()
    {
        if (importedMesh != null) return;
        if (string.IsNullOrEmpty(importedMeshAssetPath)) return;

        // 路径不存在时 LoadAllAssetsAtPath 返回空数组，无需预先 File.Exists 检查。
        foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(importedMeshAssetPath))
        {
            if (asset is Mesh m)
            {
                importedMesh = m;
                return;
            }
        }
    }
#endif

    /// <summary>把导入模型的 bounds 缩放平移到 [backX, frontX] × [fitBaseY, +height] × [-zHalf, +zHalf]。
    /// scale.x 符号天然背负了蓝墙镜像（蓝墙 backX=+6 > frontX，差值为负）。
    /// autoFit=false 时保留当前 Transform，由你手动对位。</summary>
    void FitImportedMesh()
    {
        if (importedMesh == null) return;
        var b = importedMesh.bounds;
        if (b.size.x < 1e-5f || b.size.y < 1e-5f || b.size.z < 1e-5f) return;

        float sx = (frontX - backX) / b.size.x;
        float sy = fitTargetHeight / b.size.y;
        float sz = (2f * fitTargetZHalf) / b.size.z;
        var scale = new Vector3(sx, sy, sz);

        // 目标盒中心：X 取区间中点，Y 取底 + 半高，Z 取 0
        var targetCenter = new Vector3((backX + frontX) * 0.5f, fitBaseY + fitTargetHeight * 0.5f, 0f);
        var localCenterScaled = new Vector3(b.center.x * sx, b.center.y * sy, b.center.z * sz);

        transform.localScale = scale;
        transform.localPosition = targetCenter - localCenterScaled;
    }

    void RebuildMesh()
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

        for (int ix = 0; ix < xCount; ix++)
        {
            float tx = ix / (float)(xCount - 1);              // 0=back(判定线) 1=front(中缝)
            float x = Mathf.Lerp(backX, frontX, tx);

            for (int iz = 0; iz < zCount; iz++)
            {
                float tz = iz / (float)(zCount - 1);          // 0=边缘 1=中央
                float z = Mathf.Lerp(-zHalfRange, zHalfRange, tz);
                float y = archHeight * Mathf.Sin(tz * Mathf.PI); // 中央最高、边缘接地；对撞时中间先贴合

                int idx = ix * zCount + iz;
                verts[idx] = new Vector3(x, y, z);
                cols[idx] = Color.white;                      // 颜色交给 shader 材质
                uvs[idx]  = new Vector2(tx, tz);
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

        _cBackX = backX; _cFrontX = frontX; _cArch = archHeight;
        _cZHalf = zHalfRange; _cXSeg = xSegments; _cZSeg = zSegments;
    }

    void SyncMaterial()
    {
        var mr = GetComponent<MeshRenderer>();
        if (mr == null) return;
        if (mr.sharedMaterial == null)
            mr.sharedMaterial = new Material(Shader.Find("MusicalSprite/ShockwaveUnlit"));

        var m = mr.sharedMaterial;
        m.SetColor("_ColorDeep", colorDeep * colorTint);
        m.SetColor("_ColorTip", colorTip * colorTint);

        // Shader 用顶点「局部坐标 x」推导渐变。
        // autoFit=true 时 mesh 被缩放到 [backX, frontX]，用 mesh.bounds 锚定；
        // autoFit=false 时由你手动摆放，直接用 backX/frontX 作为渐变锚点。
        float bx = backX, fx = frontX, arch = archHeight;
        if (useImportedMesh && importedMesh != null && autoFit)
        {
            var b = importedMesh.bounds;
            bx = b.min.x;
            fx = b.max.x;
            arch = fitTargetHeight;
        }

        m.SetFloat("_BackX", bx);
        m.SetFloat("_FrontX", fx);
        m.SetFloat("_ArchHeight", arch);
        m.SetFloat("_FadePower", fadePower);
        m.SetFloat("_GradientPower", gradientPower);
        m.SetFloat("_GradientBalance", gradientBalance);
        m.SetFloat("_EdgeGlow", edgeGlow);
        m.SetFloat("_Opacity", opacity);
    }
}
