using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class SkillCommandLinkGraphic : MaskableGraphic {
    public float spacing=180,radius=66;
    protected override void OnPopulateMesh(VertexHelper h){
        h.Clear();for(int n=0;n<2;n++){
            float top=spacing*(1-n)-radius,bottom=spacing*(0-n)+radius;
            for(int i=0;i<20;i++){
                float a=i/20f,b=(i+1)/20f;
                Vector2 P(float t)=>new Vector2(8*Mathf.Sin(t*Mathf.PI),Mathf.Lerp(top,bottom,t));
                Vector2 p=P(a),q=P(b),normal=new Vector2(-(q-p).y,(q-p).x).normalized*1.25f;int v=h.currentVertCount;
                h.AddVert(p+normal,color,Vector2.zero);h.AddVert(q+normal,color,Vector2.zero);h.AddVert(q-normal,color,Vector2.zero);h.AddVert(p-normal,color,Vector2.zero);h.AddTriangle(v,v+1,v+2);h.AddTriangle(v,v+2,v+3);
            }
        }
    }
}
