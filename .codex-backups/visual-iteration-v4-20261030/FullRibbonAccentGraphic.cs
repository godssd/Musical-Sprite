using UnityEngine;
using UnityEngine.UI;

/// <summary>Small two-colour flame tongues and sparse embers; no layout deformation.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class FullRibbonAccentGraphic : MaskableGraphic
{
    public FullRibbonHUDStyle style;
    public float level, clock, burstAge=100, burstDuration=.3f;
    public bool burstOnly;
    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();if(style==null)return;
        Rect r=rectTransform.rect;
        if(burstOnly) {
            float t=Mathf.Clamp01(burstAge/Mathf.Max(.01f,burstDuration));if(t>=1)return;
            for(int k=0;k<7;k++) {
                float a=k*2.39996f;Vector2 direction=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                Vector2 p=r.center+direction*(5+t*27);
                Diamond(vh,p,Mathf.Lerp(9,2,t),new Color(1,.91f,.65f,(1-t)*.10f));
                Diamond(vh,p,Mathf.Lerp(5,1,t),new Color(1,.91f,.65f,1-t));
                Diamond(vh,p,Mathf.Lerp(2,.5f,t),new Color(1,.98f,.9f,1-t));
            }return;
        }
        if(level<.005f)return;
        const int count=5;
        float super=Mathf.SmoothStep(0,1,Mathf.Clamp01(level-1));
        float h=Mathf.Lerp(style.feverHeight,style.superFeverHeight,Mathf.Clamp01(level-1));
        float spread=level>1?style.superSpread*(level-1):0;
        float alpha=Mathf.Clamp01(level);
        for(int n=0;n<count;n++) {
            float presence=(n==0||n==4)?super:1;
            if(presence<.001f)continue;
            float u=(n+.5f)/count;
            float x=Mathf.Lerp(r.xMin-spread,r.xMax+spread,u);
            float phase=clock*style.flameSpeed+n*1.93f;
            float height=h*(.7f+.22f*Mathf.Sin(phase)) * alpha * presence;
            float width=Mathf.Min(27,r.width/count*.65f);
            Vector2 bottom=new Vector2(x,r.yMin);
            Flame(vh,bottom,width,height,Mathf.Sin(phase)*7,new Color(1,.39f,.19f,alpha*presence));
            Flame(vh,bottom+new Vector2(0,1),width*.52f,height*.65f,Mathf.Sin(phase)*4,new Color(1,.82f,.38f,alpha*presence));
        }
        int embers=Mathf.Max(style.feverSparks,style.superSparks);
        for(int n=0;n<embers;n++) {
            float presence=n<style.feverSparks?1:super;
            float t=Mathf.Repeat(clock*.65f+n*.37f,1);
            Vector2 p=new Vector2(Mathf.Lerp(r.xMin,r.xMax,Mathf.Repeat(n*.618f+.17f,1))+Mathf.Sin(clock+n)*4,r.yMin+h*.6f+t*30);
            Diamond(vh,p,1.5f,new Color(1,.77f,.35f,Mathf.Sin(t*Mathf.PI)*alpha*presence));
        }
    }
    static Vector2 Cubic(Vector2 a,Vector2 b,Vector2 c,Vector2 d,float t) {float u=1-t;return u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d;}
    static void Flame(VertexHelper vh,Vector2 p,float width,float height,float lean,Color color) {
        const int samples=8;
        var points=new Vector2[samples*2+2];
        Vector2 left=p+new Vector2(-width*.5f,0),right=p+new Vector2(width*.5f,0),tip=p+new Vector2(lean,height);
        for(int i=0;i<=samples;i++)points[i]=Cubic(left,p+new Vector2(-width,height*.45f),tip+new Vector2(-width*.35f,-height*.18f),tip,i/(float)samples);
        for(int i=0;i<=samples;i++)points[samples+1+i]=Cubic(tip,tip+new Vector2(-width*.2f,-height*.55f),p+new Vector2(width,height*.35f),right,i/(float)samples);
        int start=vh.currentVertCount;vh.AddVert(p+new Vector2(0,height*.25f),color,Vector2.zero);
        foreach(var point in points)vh.AddVert(point,color,Vector2.zero);
        for(int i=0;i<points.Length;i++)vh.AddTriangle(start,start+1+i,start+1+(i+1)%points.Length);
    }
    static void Diamond(VertexHelper vh,Vector2 p,float size,Color color) {
        int s=vh.currentVertCount;
        vh.AddVert(p+Vector2.up*size*1.6f,color,Vector2.zero);vh.AddVert(p+Vector2.right*size*.65f,color,Vector2.zero);
        vh.AddVert(p-Vector2.up*size*1.6f,color,Vector2.zero);vh.AddVert(p-Vector2.right*size*.65f,color,Vector2.zero);
        vh.AddTriangle(s,s+1,s+2);vh.AddTriangle(s,s+2,s+3);
    }
}
