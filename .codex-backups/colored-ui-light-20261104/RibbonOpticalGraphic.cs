using UnityEngine;
using UnityEngine.UI;

/// <summary>Additive optical emitter. The solid ribbon is rendered by a different Graphic.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RibbonOpticalGraphic : MaskableGraphic {
    public Material InstanceMaterial {get;private set;}
    public const float Padding=40;
    public override Texture mainTexture=>Texture2D.whiteTexture;
    public void Configure(){InstanceMaterial=new Material(Resources.Load<Shader>("Shaders/RibbonOpticsV7")){hideFlags=HideFlags.HideAndDontSave};material=InstanceMaterial;raycastTarget=false;}
    public void SetState(FullRibbonHUDStyle style,int side,Vector4 cursor,Vector4 low,Vector4 high,Vector4 f0,Vector4 f1,float visible,float motion,bool interval,bool healing,float flash,float visibility){
        var m=InstanceMaterial;Vector2 size=style.ribbonSize;
        m.SetVector("_BarSize",new Vector4(size.x,size.y,0,0));m.SetVector("_CursorBoundary",cursor);
        m.SetVector("_LowBoundary",low);m.SetVector("_HighBoundary",high);m.SetVector("_FlashBoundary0",f0);m.SetVector("_FlashBoundary1",f1);
        m.SetFloat("_SideSign",side==0?1:-1);m.SetFloat("_Bow",style.opticalBow);
        m.SetFloat("_Visible",visible);m.SetFloat("_Visibility",visibility);m.SetFloat("_Motion",motion);
        m.SetFloat("_Interval",interval?1:0);m.SetFloat("_Flash",flash);
        m.SetFloat("_CoreWidth",style.opticalCoreWidth);m.SetFloat("_Overflow",style.opticalOverflow);
        m.SetFloat("_NearRadius",style.opticalNearRadius);m.SetFloat("_FarRadius",style.opticalFarRadius);
        m.SetFloat("_NearStrength",style.opticalNearStrength*motion);m.SetFloat("_FarStrength",style.opticalFarStrength*motion);
        m.SetFloat("_PendingHaloStrength",style.pendingHaloStrength);
        var ribbon=style.ribbon[side];var region=ribbon.rect;
        m.SetTexture("_RibbonTex",ribbon.texture);m.SetVector("_RibbonUV",new Vector4(region.x/ribbon.texture.width,region.y/ribbon.texture.height,region.width/ribbon.texture.width,region.height/ribbon.texture.height));
        Color team=style.Team(side);
        m.SetColor("_CoreColor",Linear(Color.Lerp(team,Color.white,.83f)));
        m.SetColor("_HaloColor",Linear(team));
        m.SetColor("_PendingColor",Linear(healing?style.healingColor:style.lossColor));
        if(style.ribbonProfiles!=null&&style.ribbonProfiles.Length>side)m.SetTexture("_CurveTex",style.ribbonProfiles[side]);
        SetVerticesDirty();
    }
    static Color Linear(Color c)=>QualitySettings.activeColorSpace==ColorSpace.Linear?c.linear:c;
    protected override void OnPopulateMesh(VertexHelper h){
        h.Clear();Rect r=GetPixelAdjustedRect();float x=Padding/Mathf.Max(1,r.width),y=Padding/Mathf.Max(1,r.height);
        h.AddVert(new Vector3(r.xMin-Padding,r.yMin-Padding),color,new Vector2(-x,-y));
        h.AddVert(new Vector3(r.xMin-Padding,r.yMax+Padding),color,new Vector2(-x,1+y));
        h.AddVert(new Vector3(r.xMax+Padding,r.yMax+Padding),color,new Vector2(1+x,1+y));
        h.AddVert(new Vector3(r.xMax+Padding,r.yMin-Padding),color,new Vector2(1+x,-y));
        h.AddTriangle(0,1,2);h.AddTriangle(0,2,3);
    }
    protected override void OnDestroy(){if(InstanceMaterial!=null){if(Application.isPlaying)Destroy(InstanceMaterial);else DestroyImmediate(InstanceMaterial);}base.OnDestroy();}
}

/// <summary>CPU side of the shared finite boundary used by both ribbon passes.</summary>
public static class RibbonVisualGeometry {
    public static Vector4 Boundary(FullRibbonHUDStyle style,int side,float progress){
        float u=Mathf.Lerp(style.masterArtwork?.066f:0,style.masterArtwork?.98f:1,Mathf.Clamp01(progress));if(side==1)u=1-u;
        float lo=.28f,hi=.72f;
        if(style.ribbonProfiles!=null&&style.ribbonProfiles.Length>side&&style.ribbonProfiles[side]!=null&&style.ribbonProfiles[side].isReadable){var c=style.ribbonProfiles[side].GetPixelBilinear(u,.5f);lo=c.r;hi=c.g;}
        return new Vector4(u*style.ribbonSize.x,(lo+hi)*.5f*style.ribbonSize.y,Mathf.Max(1,(hi-lo)*.5f*style.ribbonSize.y),0);
    }
}
