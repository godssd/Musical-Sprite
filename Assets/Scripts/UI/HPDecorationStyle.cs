using UnityEngine;

/// <summary>Persistent shared settings for the red / blue HP brush accents.</summary>
public sealed class HPDecorationStyle : ScriptableObject
{
    [Header("血条细节（像素，相对原 Canvas）")]
    public bool showDetails = true;
    [Tooltip("向分数方向延伸的长度。红蓝自动镜像；不改变可填充区域。"), Min(0f)]
    public float tailExtension = 190f;
    [Range(0f, 1f)] public float tailOpacity = .85f;
    public float tailHeight = 65f;
    public float tailYOffset = 3f;
    public Vector2 outerLeafSize = new Vector2(40f, 50f);
    [Tooltip("X 从外侧边缘向内，Y 从血条根底边向上；左右共用同一数值。")]
    public Vector2 outerLeafOffset = new Vector2(-3f, 78f);
    public Vector2 strokesSize = new Vector2(46f, 20f);
    [Tooltip("X 从内侧边缘向外，Y 从根底边向上。")]
    public Vector2 strokesOffset = new Vector2(-38f, 43f);
    public Vector2 dotOffset = new Vector2(9f, 38f);
    [Min(1f)] public float dotSize = 7f;
    public Vector2 innerLeafSize = new Vector2(34f, 50f);
    [Tooltip("X 为相对细尾内侧端点的偏移；Y 为相对血条根底边的高度。")]
    public Vector2 innerLeafOffset = new Vector2(18f, 61f);
    [Header("顶部 HP 与分数组合布局（1920×1080 设计坐标）")]
    public Vector2 headerSize = new Vector2(646f, 142f);
    [Range(.5f, 1.5f)] public float headerScale = 1.15f;
    public Vector2 screenInset = new Vector2(24f, 18f);
    public Vector2 hpRootOffset = new Vector2(40f, 22f);
    public Vector2 hpRootSize = new Vector2(350f, 100f);
    public Vector2 bodyOffset = new Vector2(35f, 19f);
    public Vector2 bodySize = new Vector2(228f, 24f);
    public Vector2 numberOffset = new Vector2(40f, 48f);
    public Vector2 numberSize = new Vector2(240f, 48f);
    public float hpDigitHeight = 44f;
    public float hpLabelHeightRatio = .73f;
    public float hpLabelGap = 14f;
    public Vector2 scoreOffset = new Vector2(400f, 16f);
    public Vector2 scoreSize = new Vector2(210f, 80f);
    public float scoreDigitHeight = 80f;
    public int Revision { get; private set; }
    private void OnValidate() { Revision++; }
}
