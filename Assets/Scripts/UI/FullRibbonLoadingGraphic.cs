using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One union silhouette and one pair of soft fields for the whole loading UI.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class FullRibbonLoadingGraphic : MaskableGraphic {
    public const float Padding=32;
    const float Resolution=1.5f;
    static readonly Dictionary<Texture2D,Color32[]> alphaCache=new Dictionary<Texture2D,Color32[]>();
    Texture2D mask;bool ownsMask;
    public Texture2D MaskForBaking=>mask;
    public string BuildKey{get;private set;}
    public bool UsedBakedMask{get;private set;}
    Material instance;
    public bool HasMask=>mask!=null;
    public double LastBuildMilliseconds{get;private set;}
    public bool IsVisible {get;private set;}
    public override Texture mainTexture=>mask!=null?mask:Texture2D.blackTexture;
    public void Configure(){instance=new Material(Resources.Load<Shader>("Shaders/UnifiedLoadingV7")){hideFlags=HideFlags.HideAndDontSave};material=instance;raycastTarget=false;}
    public void BuildMask(IList<FullRibbonImage> sources,RectTransform group,FullRibbonHUDStyle style,int side){
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var keyText=new System.Text.StringBuilder("V7.2/").Append(side).Append('/').Append(style.teamSize.ToString("F4"));
        foreach(var image in sources)if(image.transform.parent==group&&!image.name.StartsWith("ScoreDigit_")&&(image.enabled||image.name=="ScoreMarks")){
            keyText.Append('/').Append(image.name).Append('/').Append(image.artwork.texture.name).Append('/').Append(image.artwork.rect.ToString("F4")).Append('/').Append(image.rectTransform.anchoredPosition.ToString("F4")).Append('/').Append(image.rectTransform.rect.size.ToString("F4"));
        }
        using(var hash=System.Security.Cryptography.MD5.Create())BuildKey=System.BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(keyText.ToString()))).Replace("-","");
        var keyAsset=Resources.Load<TextAsset>("BattleVisualV11/V7Loading/key-"+side);
        var baked=Resources.Load<Texture2D>("BattleVisualV11/V7Loading/mask-"+side);
        if(keyAsset!=null&&baked!=null&&keyAsset.text.Trim()==BuildKey){ReleaseMask();mask=baked;ownsMask=false;UsedBakedMask=true;SetAllDirty();LastBuildMilliseconds=watch.Elapsed.TotalMilliseconds;return;}
        UsedBakedMask=false;
        int w=Mathf.CeilToInt((style.teamSize.x+Padding*2)*Resolution),h=Mathf.CeilToInt((style.teamSize.y+Padding*2)*Resolution);
        var alpha=new float[w*h];
        foreach(var image in sources){
            if(image.transform.parent!=group||image.name.StartsWith("ScoreDigit_"))continue;
            bool marks=image.name=="ScoreMarks";
            if(!image.enabled&&!marks)continue;
            Sprite sprite=marks?style.marks[0]:image.artwork;
            var data=Read(sprite.texture);int sw=sprite.texture.width;
            var rt=image.rectTransform;Vector2 pos=new Vector2(rt.anchoredPosition.x,-rt.anchoredPosition.y),size=rt.rect.size;
            // No score on entrance. The three team-colour punctuation strokes are decoration.
            if(marks){pos=new Vector2(side==0?430:style.teamSize.x-430-76,63);size=new Vector2(76,61);}
            int x0=Mathf.Max(0,Mathf.FloorToInt((pos.x+Padding)*Resolution)),x1=Mathf.Min(w,Mathf.CeilToInt((pos.x+size.x+Padding)*Resolution));
            int y0=Mathf.Max(0,Mathf.FloorToInt((pos.y+Padding)*Resolution)),y1=Mathf.Min(h,Mathf.CeilToInt((pos.y+size.y+Padding)*Resolution));
            for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){
                float u=((x+.5f)/Resolution-Padding-pos.x)/size.x,v=1-((y+.5f)/Resolution-Padding-pos.y)/size.y;
                if(marks&&side==1)u=1-u;
                float sx=sprite.rect.x+Mathf.Clamp01(u)*sprite.rect.width-.5f,sy=sprite.rect.y+Mathf.Clamp01(v)*sprite.rect.height-.5f;
                int ix=Mathf.FloorToInt(sx),iy=Mathf.FloorToInt(sy);float fx=sx-ix,fy=sy-iy;
                float A(int xx,int yy){var c=data[Mathf.Clamp(yy,0,sprite.texture.height-1)*sw+Mathf.Clamp(xx,0,sw-1)];return marks&&!(c.r>c.g*1.25f&&c.r>c.b*1.25f)?0:c.a/255f;}
                float a=Mathf.Lerp(Mathf.Lerp(A(ix,iy),A(ix+1,iy),fx),Mathf.Lerp(A(ix,iy+1),A(ix+1,iy+1),fx),fy);
                int at=(h-1-y)*w+x;alpha[at]=Mathf.Max(alpha[at],a);
            }
        }
        float[] near=Blur(alpha,w,h,2),far=Blur(alpha,w,h,10);
        var pixels=new Color32[alpha.Length];for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32((byte)Mathf.RoundToInt(alpha[i]*255),(byte)Mathf.RoundToInt(near[i]*255),(byte)Mathf.RoundToInt(far[i]*255),255);
        ReleaseMask();ownsMask=true;mask=new Texture2D(w,h,TextureFormat.RGBA32,false,true){name="LoadingUnion_V7",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};mask.SetPixels32(pixels);mask.Apply(false,true);SetAllDirty();LastBuildMilliseconds=watch.Elapsed.TotalMilliseconds;
    }
    static Color32[] Read(Texture2D source){
        if(alphaCache.TryGetValue(source,out var pixels))return pixels;
        if(source.isReadable)pixels=source.GetPixels32();
        else{
            var rt=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);var previous=RenderTexture.active;
            var copy=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,true);
            try{Graphics.Blit(source,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,source.width,source.height),0,0);copy.Apply();pixels=copy.GetPixels32();}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);if(Application.isPlaying)Destroy(copy);else DestroyImmediate(copy);}
        }
        alphaCache[source]=pixels;return pixels;
    }
    static float[] Blur(float[] source,int w,int h,int radius){
        var a=(float[])source.Clone();var b=new float[a.Length];float divisor=1f/(radius*2+1);
        for(int pass=0;pass<3;pass++){
            for(int y=0;y<h;y++){
                float sum=0;for(int k=-radius;k<=radius;k++)sum+=a[y*w+Mathf.Clamp(k,0,w-1)];
                for(int x=0;x<w;x++){b[y*w+x]=sum*divisor;sum+=a[y*w+Mathf.Clamp(x+radius+1,0,w-1)]-a[y*w+Mathf.Clamp(x-radius,0,w-1)];}
            }
            for(int x=0;x<w;x++){
                float sum=0;for(int k=-radius;k<=radius;k++)sum+=b[Mathf.Clamp(k,0,h-1)*w+x];
                for(int y=0;y<h;y++){a[y*w+x]=sum*divisor;sum+=b[Mathf.Clamp(y+radius+1,0,h-1)*w+x]-b[Mathf.Clamp(y-radius,0,h-1)*w+x];}
            }
        }
        return a;
    }
    public void SetState(FullRibbonHUDStyle style,int side,float progress,float fade,float visibility){
        IsVisible=visibility>.001f&&HasMask;
        float travel=style.teamSize.x*progress;
        float barLeft=side==0?style.ribbonPosition.x:style.teamSize.x-style.ribbonPosition.x-style.ribbonSize.x;
        float forward=Mathf.Clamp(travel,style.ribbonPosition.x+style.ribbonSize.x*.066f,style.ribbonPosition.x+style.ribbonSize.x*.98f);
        float physical=side==0?forward:style.teamSize.x-forward,u=Mathf.Clamp01((physical-barLeft)/style.ribbonSize.x);
        float center=.5f,half=.22f,slope=0;
        var profile=style.ribbonProfiles!=null&&style.ribbonProfiles.Length>side?style.ribbonProfiles[side]:null;
        if(profile!=null&&profile.isReadable){
            var c=profile.GetPixelBilinear(u,.5f);center=(c.r+c.g)*.5f;half=(c.g-c.r)*.5f;
            var a=profile.GetPixelBilinear(Mathf.Clamp01(u-.012f),.5f);var b=profile.GetPixelBilinear(Mathf.Clamp01(u+.012f),.5f);
            slope=-((b.r+b.g-a.r-a.g)*.5f)*style.ribbonSize.y/(.024f*style.ribbonSize.x);
        }
        Vector2 tangent=new Vector2(side==0?1:-1,(side==0?1:-1)*slope).normalized;
        instance.SetVector("_MaskDimensions",new Vector4(style.teamSize.x+Padding*2,style.teamSize.y+Padding*2,Padding,Padding));
        instance.SetVector("_Head",new Vector4(physical,style.ribbonPosition.y+(1-center)*style.ribbonSize.y,Mathf.Max(2,half*style.ribbonSize.y),travel>=style.ribbonPosition.x+style.ribbonSize.x*.066f?1:0));
        instance.SetVector("_Tangent",new Vector4(tangent.x,tangent.y,-tangent.y,tangent.x));instance.SetFloat("_TeamWidth",style.teamSize.x);instance.SetFloat("_Side",side);instance.SetFloat("_Travel",travel);
        instance.SetFloat("_Visible",IsVisible?visibility:0);instance.SetFloat("_NearStrength",style.loadingNearGlow);instance.SetFloat("_FarStrength",style.loadingFarGlow);
        Color white=new Color(1,.985f,.94f,1);instance.SetColor("_White",QualitySettings.activeColorSpace==ColorSpace.Linear?white.linear:white);
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper h){h.Clear();Rect r=GetPixelAdjustedRect();h.AddVert(new Vector3(r.xMin,r.yMin),color,new Vector2(0,0));h.AddVert(new Vector3(r.xMin,r.yMax),color,new Vector2(0,1));h.AddVert(new Vector3(r.xMax,r.yMax),color,new Vector2(1,1));h.AddVert(new Vector3(r.xMax,r.yMin),color,new Vector2(1,0));h.AddTriangle(0,1,2);h.AddTriangle(0,2,3);}
    void ReleaseMask(){if(mask!=null&&ownsMask){if(Application.isPlaying)Destroy(mask);else DestroyImmediate(mask);}mask=null;ownsMask=false;}
    protected override void OnDestroy(){ReleaseMask();if(instance!=null){if(Application.isPlaying)Destroy(instance);else DestroyImmediate(instance);}base.OnDestroy();}
}
