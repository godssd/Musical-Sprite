using UnityEngine;

[CreateAssetMenu(menuName="Musical Sprite/Handpainted Judgements")]
public sealed class HandpaintedJudgementStyle : ScriptableObject {
    public const string Path="BattleVisualV11/Judgements/Style";
    public bool enabledInGame=true;
    [Min(16)] public float glyphHeight=40;
    public Color red=new Color(1,.32f,.30f),blue=new Color(.48f,.64f,1),miss=new Color(.48f,.43f,.55f);
    [Min(.2f)] public float lifetime=.55f;
    public float rise=14;
}
