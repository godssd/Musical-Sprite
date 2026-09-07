using UnityEngine;

/// <summary>
/// 中间粉色竖杠（中线）。
/// 根据双方命中情况左右移动，命中多的一方会把中线往对方推。
/// 同时控制红蓝地面的面积随中线位置变化。
/// </summary>
public class BattleCenterLine : MonoBehaviour
{
    [Header("移动范围")]
    [Tooltip("中线最左能到的 X 坐标")]
    public float minX = -3f;

    [Tooltip("中线最右能到的 X 坐标")]
    public float maxX = 3f;

    [Header("移动参数")]
    [Tooltip("分差 5000 时中线移动 5 个单位")]
    public float pushPerHit = 0.001f;

    [Tooltip("中线归中/平滑移动的速度")]
    public float smoothSpeed = 5f;

    [Header("分数引用")]
    [Tooltip("可选：直接引用 ScoreManager，用真实分差驱动中线移动")]
    public ScoreManager scoreManager;

    [Header("地面引用")]
    [Tooltip("左侧红色地面（保留引用，但地面为静态，不再被缩放/移动）")]
    public Transform leftGround;

    [Tooltip("右侧蓝色地面（保留引用，但地面为静态，不再被缩放/移动）")]
    public Transform rightGround;

    [Header("地面材质(可选,用于优势显现)")]
    [Tooltip("红方 GroundEdge 材质；赋值后会根据优势设置 _Reveal，使红方地面显现更多")]
    public Material leftGroundMat;

    [Tooltip("蓝方 GroundEdge 材质；赋值后会根据优势设置 _Reveal，使蓝方地面显现更多")]
    public Material rightGroundMat;

    [Header("新地面：单块 ArenaGround")]
    [Tooltip("单场地面材质（M_GroundEdge_Arena），含红蓝两张贴图。优先使用此字段设置中缝 _CenterLineX。")]
    public Material groundMaterial;

    [Tooltip("草丛流苏材质（M_GrassFringe）。赋值后会跟随 groundMaterial 一起设置 _CenterLineX，使草丛颜色随粉杠变化。")]
    public Material grassMaterial;

    [Tooltip("单场地面对象 Transform（ArenaGround）")]
    public Transform ground;

    [Tooltip("场地总宽度（从 -X 到 +X）")]
    public float arenaTotalWidth = 16f;

    [Header("只读状态")]
    [SerializeField] private float _currentX;
    [SerializeField] private float _leftScore;   // 左玩家累计命中优势
    [SerializeField] private float _rightScore;  // 右玩家累计命中优势

    public float currentX => _currentX;

    void Start()
    {
        _currentX = 0f;
        _leftScore = 0f;
        _rightScore = 0f;
    }

    void Update()
    {
        // 根据双方得分差计算目标位置
        // 优先使用 ScoreManager 的真实分差；未引用时回退到内部累计
        // 注意：中线应该向分数更低的一方移动，所以用 左分 - 右分
        float diff;
        if (scoreManager != null)
        {
            diff = scoreManager.GetLeftScore() - scoreManager.GetRightScore();
        }
        else
        {
            diff = _leftScore - _rightScore;
        }

        // 分差 5000 时移动 5 个单位；允许超过原 minX/maxX，让粉杠随分差继续推进
        float targetX = diff * pushPerHit;

        _currentX = Mathf.Lerp(_currentX, targetX, Time.deltaTime * smoothSpeed);

        Vector3 pos = transform.position;
        pos.x = _currentX;
        transform.position = pos;

        UpdateGroundArea();
    }

    /// <summary>
    /// 更新红蓝地面的"优势显现"。
    /// 新方案：单块地面 ArenaGround，材质含红蓝两张贴图。
    /// 粉杠 X 坐标直接驱动材质 _CenterLineX：
    ///   - 粉杠右移（左/红方优势）-> 红线覆盖更多区域
    ///   - 粉杠左移（右/蓝方优势）-> 蓝线覆盖更多区域
    /// 兼容旧方案：如果只有 leftGroundMat/rightGroundMat，仍用 _Reveal 控制。
    /// </summary>
    private void UpdateGroundArea()
    {
        // 新方案：直接把粉杠 X 传给单块地面材质 + 草丛流苏材质
        if (groundMaterial != null)
        {
            groundMaterial.SetFloat("_CenterLineX", _currentX);
            if (grassMaterial != null)
                grassMaterial.SetFloat("_CenterLineX", _currentX);
            return;
        }

        // 旧方案兼容：各自材质的 _Reveal
        float halfWidth = arenaTotalWidth * 0.5f;
        float a = Mathf.Clamp(_currentX / halfWidth, -1f, 1f);

        if (leftGroundMat != null)
            leftGroundMat.SetFloat("_Reveal", 0.5f + 0.5f * a);
        if (rightGroundMat != null)
            rightGroundMat.SetFloat("_Reveal", 0.5f - 0.5f * a);
    }

    /// <summary>
    /// 注册一次命中。
    /// side: 0 = 左玩家命中（中线向右推），1 = 右玩家命中（中线向左推）
    /// </summary>
    public void RegisterHit(int side, float accuracy)
    {
        float weight = Mathf.Clamp01(1f - accuracy); // 越接近 Perfect，权重越大（这里用 1 - 误差比例）
        if (side == 0)
        {
            _leftScore += weight;
        }
        else
        {
            _rightScore += weight;
        }
    }

    /// <summary>
    /// 重置对战状态。
    /// </summary>
    public void ResetBattle()
    {
        _leftScore = 0f;
        _rightScore = 0f;
        _currentX = 0f;
    }
}
