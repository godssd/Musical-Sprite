using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class SkillCommandLinkGraphic : MaskableGraphic {
    public float spacing=180,radius=73;
    protected override void OnPopulateMesh(VertexHelper h){
        h.Clear();for(int n=0;n<2;n++){
            Vector2 p=new Vector2(radius*.94f,spacing*(1-n));
            Vector2 q=new Vector2(radius*.94f,spacing*(0-n));
            SkillCommandRibbonMesh.Curve(h,p,p+new Vector2(radius*.36f,-spacing*.36f),q+new Vector2(radius*.36f,spacing*.40f),q,3.4f,3.4f,color);
        }
    }
}

// Native vector strokes follow the selected command-strip drawing in a vertical layout.
// Icon artwork and pointer rectangles remain independent of this decorative geometry.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SkillCommandFrameGraphic : MaskableGraphic {
    public int command;
    protected override void OnPopulateMesh(VertexHelper h){
        h.Clear();float r=Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.5f;
        Vector2 P(Vector2 p)=>p*r;
        if(command==1){
            SkillCommandRibbonMesh.Arc(h,P,82,-73,1,.026f,.28f,color);
            SkillCommandRibbonMesh.Arc(h,P,145,288,.95f,.032f,.052f,color);
        }else{
            SkillCommandRibbonMesh.Arc(h,P,command==0?58:94,command==0?-250:402,.94f,.034f,.105f,color);
            SkillCommandRibbonMesh.Arc(h,P,command==0?101:66,command==0?146:17,.91f,.025f,.033f,color);
        }
        if(command==1){
            Drop(h,P,new Vector2(-1.16f,.36f),new Vector2(-1.40f,.53f),.044f,color);
            Drop(h,P,new Vector2(-1.20f,.05f),new Vector2(-1.52f,.05f),.05f,color);
            Drop(h,P,new Vector2(-1.16f,-.28f),new Vector2(-1.40f,-.44f),.043f,color);
        }else{
            float sign=command==0?1:-1;
            Drop(h,P,new Vector2(sign*1.15f,-.20f),new Vector2(sign*1.36f,.07f),.055f,color);
            Drop(h,P,new Vector2(sign*1.14f,-.48f),new Vector2(sign*1.43f,-.38f),.05f,color);
        }
    }
    static void Drop(VertexHelper h,System.Func<Vector2,Vector2> p,Vector2 a,Vector2 b,float width,Color color){
        SkillCommandRibbonMesh.Curve(h,p(a),p(Vector2.Lerp(a,b,.35f)),p(Vector2.Lerp(a,b,.75f)),p(b),0,0,color,width*Vector2.Distance(p(Vector2.zero),p(Vector2.right))*2);
    }
}

static class SkillCommandRibbonMesh {
    public static void Arc(VertexHelper h,System.Func<Vector2,Vector2> p,float start,float end,float radius,float tip,float body,Color c){
        Vector2 Point(float t){float a=Mathf.Lerp(start,end,t)*Mathf.Deg2Rad;return p(new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius);}
        float scale=Vector2.Distance(p(Vector2.zero),p(Vector2.right));
        Stroke(h,Point,t=>Mathf.Lerp(tip,body,Mathf.Pow(Mathf.Sin(t*Mathf.PI),1.25f))*scale,c,96);
    }
    public static void Curve(VertexHelper h,Vector2 a,Vector2 b,Vector2 c,Vector2 d,float start,float end,Color tint,float swell=0){
        Vector2 Point(float t){float u=1-t;return u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d;}
        Stroke(h,Point,t=>Mathf.Lerp(start,end,t)+swell*Mathf.Sin(t*Mathf.PI),tint,48);
    }
    static void Stroke(VertexHelper h,System.Func<float,Vector2> point,System.Func<float,float> width,Color tint,int count){
        int first=h.currentVertCount;Color clear=tint;clear.a=0;
        for(int i=0;i<=count;i++){
            float t=i/(float)count;Vector2 p=point(t),v=point(Mathf.Min(1,t+.001f))-point(Mathf.Max(0,t-.001f));
            Vector2 n=new Vector2(-v.y,v.x).normalized;float w=Mathf.Max(.05f,width(t))*.5f;
            h.AddVert(p+n*(w+.85f),clear,Vector2.zero);h.AddVert(p+n*w,tint,Vector2.zero);
            h.AddVert(p-n*w,tint,Vector2.zero);h.AddVert(p-n*(w+.85f),clear,Vector2.zero);
            if(i==0)continue;int j=first+(i-1)*4;for(int k=0;k<3;k++){h.AddTriangle(j+k,j+k+4,j+k+1);h.AddTriangle(j+k+1,j+k+4,j+k+5);}
        }
    }
}
