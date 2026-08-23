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
    [Tooltip("每次命中推动多少单位")]
    public float pushPerHit = 0.05f;

    [Tooltip("中线归中/平滑移动的速度")]
    public float smoothSpeed = 5f;

    [Header("地面引用")]
    [Tooltip("左侧红色地面")]
    public Transform leftGround;

    [Tooltip("右侧蓝色地面")]
    public Transform rightGround;

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
        float diff = _rightScore - _leftScore; // 右玩家领先为正 -> 中线左移
        float targetX = Mathf.Clamp(diff * pushPerHit, minX, maxX);

        _currentX = Mathf.Lerp(_currentX, targetX, Time.deltaTime * smoothSpeed);

        Vector3 pos = transform.position;
        pos.x = _currentX;
        transform.position = pos;

        UpdateGroundArea();
    }

    /// <summary>
    /// 根据中线当前 X 更新红蓝地面的面积。
    /// 中线左侧始终为红色，右侧始终为蓝色。
    /// </summary>
    private void UpdateGroundArea()
    {
        if (leftGround == null || rightGround == null) return;

        float halfWidth = arenaTotalWidth * 0.5f;
        float leftCenter = (-halfWidth + _currentX) * 0.5f;
        float leftWidth = _currentX - (-halfWidth);

        float rightCenter = (_currentX + halfWidth) * 0.5f;
        float rightWidth = halfWidth - _currentX;

        // 更新左侧红色地面
        Vector3 lp = leftGround.position;
        lp.x = leftCenter;
        leftGround.position = lp;

        Vector3 ls = leftGround.localScale;
        ls.x = leftWidth;
        leftGround.localScale = ls;

        // 更新右侧蓝色地面
        Vector3 rp = rightGround.position;
        rp.x = rightCenter;
        rightGround.position = rp;

        Vector3 rs = rightGround.localScale;
        rs.x = rightWidth;
        rightGround.localScale = rs;
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
