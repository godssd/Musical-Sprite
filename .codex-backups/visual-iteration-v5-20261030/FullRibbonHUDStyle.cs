using UnityEngine;

[CreateAssetMenu(menuName = "Musical Sprite/Full Ribbon HUD Style")]
public sealed class FullRibbonHUDStyle : ScriptableObject
{
    public const string ResourcePath = "BattleVisualV11/FullRibbonHUDStyle";
    [Header("启用与布局（关闭后重新进入 Play 可回退旧版）")]
    [InspectorName("启用新血条")] public bool useFullRibbon = true;
    [InspectorName("屏幕边距")] public Vector2 screenInset = new Vector2(24, 18);
    [InspectorName("整体缩放")] public float scale = 1.12f;
    [InspectorName("中央安全间距")] public float centerGap = 80;
    public Vector2 teamSize = new Vector2(760, 200);
    public Vector2 ribbonPosition = new Vector2(156, 113);
    public Vector2 ribbonSize = new Vector2(565, 48);
    public float scoreHeight = 76;
    public float hpHeight = 38;
    [Header("常态拆层：红蓝已镜像，数字不镜像")]
    public Sprite[] ribbon = new Sprite[2];
    public Sprite[] avatarFrame = new Sprite[2];
    public Sprite[] crescent = new Sprite[2];
    public Sprite[] outerLeaves = new Sprite[2];
    public Sprite[] innerLeaves = new Sprite[2];
    public Sprite[] marks = new Sprite[2];
    public Sprite[] dot = new Sprite[2];
    public Sprite avatarMask;
    [InspectorName("自定义头像：0 红 / 1 蓝")] public Sprite[] avatars = new Sprite[2];
    public Sprite hpLabel;
    public Sprite[] digits = new Sprite[10];
    public Color red = new Color(1,.318f,.294f);
    public Color blue = new Color(.478f,.639f,1);
    [InspectorName("红方空槽底色")] public Color redEmpty = new Color(.35f,.23f,.27f);
    [InspectorName("蓝方空槽底色")] public Color blueEmpty = new Color(.24f,.31f,.45f);
    [InspectorName("数字与文字奶白色")] public Color letteringColor = new Color(1,.965f,.88f,1);
    [Header("先闪光，再实色停留与追赶（秒）")]
    [InspectorName("白闪时长"),Min(.01f)] public float flashDuration = .09f;
    [InspectorName("黄绿区间停留时长"),Min(0)] public float holdDuration = .10f;
    [InspectorName("损血追赶时长"),Min(.01f)] public float lossDuration = .28f;
    [InspectorName("回血追赶时长"),Min(.01f)] public float healDuration = .45f;
    public Color lossColor = new Color(.66f,.51f,.22f,1);
    public Color healingColor = new Color(.47f,.79f,.36f,1);
    [InspectorName("白闪溢光强度"),Range(0,2)] public float flashGlow = .85f;
    [InspectorName("黄绿虚血贴形辉光"),Range(0,2)] public float pendingGlow = .8f;
    [InspectorName("虚血内亮层"),Range(0,1)] public float pendingInnerLight = .32f;
    [Header("刻度：常态内收，移动时发光溢出")]
    [InspectorName("常态刻度宽度"),Min(.3f)] public float idleTickWidth = 2.4f;
    [InspectorName("移动刻度宽度"),Min(.3f)] public float movingTickWidth = 4.2f;
    [InspectorName("移动刻度溢光强度"),Range(0,3)] public float tickGlow = 1.5f;
    [InspectorName("溢光范围"),Range(1,24)] public float glowRadius = 9;
    [Header("濒危只压暗至常态，不额外增亮")]
    [Range(0,1)] public float dangerEnter = .25f;
    [Range(0,1)] public float dangerExit = .30f;
    [InspectorName("濒危最低亮度"),Range(.05f,1)] public float dangerMinBrightness = .28f;
    [InspectorName("濒危暗闪周期"),Min(.2f)] public float dangerPeriod = 1.25f;
    [Header("入场：贴形白光与高亮推进头")]
    [InspectorName("入场加载时长"),Min(.1f)] public float entranceDuration = .65f;
    [InspectorName("完成变色时长"),Min(.01f)] public float entranceResolve = .12f;
    [Range(0,3)] public float entranceGlow = .4f;
    [InspectorName("入场前沿高亮强度"),Range(0,3)] public float entranceHeadGlow = 1.8f;
    [Range(0,1)] public float chimeVolume = .16f;
    [Header("小火苗：始终位于分数后")]
    public float feverHeight = 32;
    public float superFeverHeight = 46;
    public float superSpread = 14;
    public float flameSpeed = 5;
    [Range(0,8)] public int feverSparks = 2;
    [Range(0,8)] public int superSparks = 4;
    public float extinguishDuration = .22f;
    public float zeroBurstDuration = .32f;

    public Color Team(int side) => side == 0 ? red : blue;
    public Color Empty(int side) => side == 0 ? redEmpty : blueEmpty;
    public bool HasArtwork {
        get {
            if(hpLabel == null || avatarMask == null || digits == null || digits.Length != 10)return false;
            foreach(var d in digits)if(d == null)return false;
            foreach(var a in new[]{ribbon,avatarFrame,crescent,outerLeaves,innerLeaves,marks,dot})
                if(a == null || a.Length != 2 || a[0] == null || a[1] == null)return false;
            return true;
        }
    }
}
