using UnityEngine;

/// <summary>
/// P0 冲击波静态预览器（脱离运行时）。
/// 在 Scene 视图直接看两道拱形冲击波从 ±判定线出发、中央对撞的效果，
/// 方便 P1（拱形 mesh）/ P2（shader 渐变）实时调形状与渐变。
///
/// 逻辑约定（与 BattleCenterLine 一致，不重造）：
///   红墙覆盖区间 [leftEdge, front]   蓝墙覆盖区间 [front, rightEdge]
///   稳态下 front = centerX（分差驱动的中缝）；开场演示下两波相向从 ±edge 推到 0 相遇。
/// 红方红、蓝方蓝；红墙 alpha 在 x=leftEdge 处为 0（判定线消失），在 front(中缝) 处最浓。
/// 蓝墙相反：x=rightEdge 处为 0，front 处最浓。渐变由 ShockwaveMeshGenerator 顶点色实现。
/// </summary>
[ExecuteAlways]
public class ShockwavePreview : MonoBehaviour
{
    [Header("场地边界（判定线）")]
    public float leftEdge = -6f;
    public float rightEdge = 6f;

    [Header("稳态中缝 centerX（模拟 BattleCenterLine.currentX 分差位置）")]
    [Range(-6f, 6f)]
    public float centerX = 0f;

    [Header("开场对撞演示（两波相向从 ±edge 推到 0）")]
    public bool autoClash = false;
    public float clashDuration = 1.5f;
    [Range(0f, 1f)] public float clashProgress = 0f;

    [Header("（只读/自动维护）真实墙引用")]
    public Transform redWall;
    public Transform blueWall;

    private Material _wallMat;
    private float _elapsed;

    void OnValidate() { EnsureWalls(); ApplyToWalls(); }
    void Awake() { EnsureWalls(); ApplyToWalls(); }
#if UNITY_EDITOR
    void Start() { EnsureWalls(); ApplyToWalls(); }
#endif

    void Update()
    {
        if (autoClash)
        {
            _elapsed += Time.deltaTime;
            clashProgress = Mathf.PingPong(_elapsed / clashDuration, 1f);
        }
        ApplyToWalls();
    }

    private Material GetWallMat()
    {
        if (_wallMat == null)
        {
            var s = Shader.Find("MusicalSprite/ShockwaveUnlit");
            if (s != null) _wallMat = new Material(s);
        }
        return _wallMat;
    }

    private void EnsureWalls()
    {
        if (redWall == null)
        {
            var go = new GameObject("RedWall");
            go.transform.SetParent(transform, false);
            var mg = go.AddComponent<ShockwaveMeshGenerator>();
            mg.side = ShockwaveMeshGenerator.Side.Red;
            redWall = go.transform;
        }
        if (blueWall == null)
        {
            var go = new GameObject("BlueWall");
            go.transform.SetParent(transform, false);
            var mg = go.AddComponent<ShockwaveMeshGenerator>();
            mg.side = ShockwaveMeshGenerator.Side.Blue;
            blueWall = go.transform;
        }
        var mat = GetWallMat();
        if (mat != null)
        {
            var rm = redWall.GetComponent<MeshRenderer>();
            if (rm.sharedMaterial == null) rm.sharedMaterial = mat;
            var bm = blueWall.GetComponent<MeshRenderer>();
            if (bm.sharedMaterial == null) bm.sharedMaterial = mat;
        }
    }

    private void GetFronts(out float rFront, out float bFront)
    {
        if (autoClash)
        {
            rFront = Mathf.Lerp(leftEdge, 0f, clashProgress);
            bFront = Mathf.Lerp(rightEdge, 0f, clashProgress);
        }
        else
        {
            rFront = centerX;
            bFront = centerX;
        }
    }

    private void ApplyToWalls()
    {
        EnsureWalls();
        GetFronts(out float rFront, out float bFront);

        var rm = redWall.GetComponent<ShockwaveMeshGenerator>();
        rm.backX = leftEdge;
        rm.frontX = rFront;

        var bm = blueWall.GetComponent<ShockwaveMeshGenerator>();
        bm.backX = rightEdge;
        bm.frontX = bFront;
    }

    void OnDrawGizmos()
    {
        GetFronts(out float rFront, out float bFront);
        // 仅保留中缝参考线（白），真实拱形已由子物体 mesh 接管显示
        Gizmos.color = Color.white;
        Gizmos.DrawLine(new Vector3((rFront + bFront) * 0.5f, 0f, -3.75f),
                        new Vector3((rFront + bFront) * 0.5f, 1.5f, 3.75f));
    }
}
