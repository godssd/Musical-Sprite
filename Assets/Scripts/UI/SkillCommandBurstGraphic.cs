using UnityEngine;
using UnityEngine.UI;

/// <summary>Short directional paint sparks, driven by the same release clock as the input lock.</summary>
public sealed class SkillCommandBurstGraphic : MaskableGraphic {
    float age=100,duration=.38f;
    public void SetTime(float value,float seconds){age=value;duration=seconds;SetVerticesDirty();}
    protected override void OnPopulateMesh(VertexHelper h){
        h.Clear();float start=duration*.36f;if(age<start||age>=duration)return;
        float t=Mathf.Clamp01((age-start)/(duration-start));float alpha=1-Mathf.SmoothStep(.25f,1,t);
        for(int i=0;i<8;i++){
            float a=(-148+i*17)*Mathf.Deg2Rad;
            Vector2 direction=new Vector2(Mathf.Cos(a),Mathf.Sin(a)),normal=new Vector2(-direction.y,direction.x);
            float distance=(12+i%3*6)+(1-Mathf.Pow(1-t,3))*(26+i%3*12);
            Vector2 center=direction*distance;float length=(6+i%3*2)*(1-t*.5f),width=1.4f;
            Color c=Color.Lerp(new Color(1,.72f,.39f),new Color(1,.97f,.85f),i%2);c.a=alpha;
            int n=h.currentVertCount;h.AddVert(center-direction*length,c,Vector2.zero);h.AddVert(center+normal*width,c,Vector2.zero);
            h.AddVert(center+direction*length,c,Vector2.zero);h.AddVert(center-normal*width,c,Vector2.zero);
            h.AddTriangle(n,n+1,n+2);h.AddTriangle(n,n+2,n+3);
        }
    }
}
