using UnityEngine;

/// <summary>Independent V5 settings; the earlier HPDecorationStyle remains available for rollback.</summary>
[CreateAssetMenu(menuName = "Musical Sprite/Connected Header Style")]
public sealed class ConnectedHeaderStyle : ScriptableObject
{
    public const string ResourcePath = "BattleVisualV11/ConnectedHeaderStyle";
    [Header("运行选择（切换后重新进入 Play）")]
    [InspectorName("使用连体新版")] public bool useConnectedHeader = true;
    [Header("已批准的生产素材")]
    [InspectorName("红方连体轮廓")] public Sprite redFrame;
    [InspectorName("蓝方连体轮廓（已镜像）")] public Sprite blueFrame;
    [InspectorName("红方中性血槽遮罩")] public Sprite body;
    [InspectorName("蓝方中性血槽（已镜像）")] public Sprite blueBody;
    [Header("相机安全区域 / 1920×1080 设计坐标")]
    [InspectorName("整体缩放"), Range(.3f, 1.5f)] public float scale = 1f;
    [InspectorName("外侧及顶部边距")] public Vector2 screenInset = new Vector2(24f, 18f);
    [InspectorName("中央预留间距"), Min(0f)] public float centerGap = 100f;
    [InspectorName("容器高度"), Min(1f)] public float height = 160f;
    [InspectorName("末端叶片预留宽度"), Min(0f)] public float endPadding = 40f;
    [Header("连体曲线：只伸缩中间段，左右端保持形状")]
    [InspectorName("轮廓上边距")] public float frameTop = 50f;
    [InspectorName("轮廓高度"), Min(1f)] public float frameHeight = 79.5f;
    [InspectorName("分数后卷钩余量"), Min(0f)] public float scoreTailSpace = 44f;
    [Tooltip("增长即时扩展，避免叶片遮住新数字；收短时平滑回收。")]
    [InspectorName("收短平滑时间"), Range(0f, .5f)] public float resizeTime = .12f;
    [Header("HP：位置 X 从外侧向内，Y 从顶部向下")]
    [InspectorName("HP 容器尺寸")] public Vector2 hpRootSize = new Vector2(380f, 160f);
    [InspectorName("血槽位置")] public Vector2 bodyOffset = new Vector2(62f, 70f);
    [InspectorName("血槽尺寸")] public Vector2 bodySize = new Vector2(286f, 37f);
    [InspectorName("HP 数字位置")] public Vector2 numberOffset = new Vector2(66f, 22f);
    [InspectorName("HP 数字区域")] public Vector2 numberSize = new Vector2(270f, 44f);
    [InspectorName("血量数字高度"), Min(1f)] public float hpDigitHeight = 38f;
    [InspectorName("HP 标签高度比例"), Range(.5f, 1.5f)] public float hpLabelRatio = .79f;
    [InspectorName("HP 标签后间距")] public float hpLabelGap = 10f;
    [Header("分数：固定靠 HP 一端，随数字宽度延长")]
    [InspectorName("分数位置")] public Vector2 scoreOffset = new Vector2(400f, 0f);
    [InspectorName("分数高度"), Min(1f)] public float scoreDigitHeight = 72f;
    [InspectorName("数字弹动宽度余量"), Range(0f, .15f)] public float scorePunchAllowance = .04f;
    [Header("少量固定装饰")]
    [InspectorName("显示叶片与笔触")] public bool showDetails = true;
    [InspectorName("外侧叶片位置")] public Vector2 outerLeafOffset = Vector2.zero;
    [InspectorName("外侧叶片尺寸")] public Vector2 outerLeafSize = new Vector2(52f, 60f);
    [InspectorName("末端叶片相对卷钩位置")] public Vector2 innerLeafOffset = new Vector2(-12f, 12f);
    [InspectorName("末端叶片尺寸")] public Vector2 innerLeafSize = new Vector2(43f, 50f);
    [InspectorName("三笔触位置")] public Vector2 marksOffset = new Vector2(358f, 69f);
    [InspectorName("三笔触尺寸")] public Vector2 marksSize = new Vector2(30f, 18f);
    public bool HasArtwork => redFrame != null && blueFrame != null && body != null;
    public int Revision { get; private set; }
    private void OnValidate() { Revision++; }
}
