using UnityEngine;

[CreateAssetMenu(menuName="Musical Sprite/Skill Command Style")]
public sealed class SkillCommandStyle : ScriptableObject {
    public const string Path="BattleVisualV11/SkillCommands/Style";
    public bool useSemanticArtwork=true;
    public Sprite[] icons=new Sprite[3];
    [Header("V4 原稿整组素材（不拆分拉伸外框）")]
    public Sprite artworkFrame;
    public float artworkHeight=600;
    public Vector2[] artworkIconUV={new Vector2(.504f,.818f),new Vector2(.482f,.500f),new Vector2(.449f,.206f)};
    [Range(0,4)] public float pressGlow=.8f,releaseGlow=2.4f;
    public Color team=new Color(1,.32f,.30f),cream=new Color(1,.97f,.90f);
    public float hitSize=160,frameSize=146,iconSize=78,spacing=180,rightInset=32;
    [Min(.05f)] public float resetDuration=.28f;
    [Min(.05f)] public float releaseDuration=.38f;
    public bool HasIcons=>icons!=null&&icons.Length==3&&icons[0]!=null&&icons[1]!=null&&icons[2]!=null;
}
