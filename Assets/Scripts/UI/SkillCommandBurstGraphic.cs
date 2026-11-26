using UnityEngine;
using UnityEngine.UI;

/// <summary>Short directional paint sparks, driven by the same release clock as the input lock.</summary>
public sealed class SkillCommandBurstGraphic : MaskableGraphic {
    public Vector2[] origins={Vector2.zero};
    float age=100,duration=.85f;
    public void SetTime(float value,float seconds){age=value;duration=seconds;SetVerticesDirty();}
    protected override void OnPopulateMesh(VertexHelper h){
        h.Clear();float start=duration*.72f;if(age<start||age>=duration)return;
        float t=Mathf.Clamp01((age-start)/(duration-start));float alpha=Mathf.SmoothStep(0,1,t/.15f)*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.2f,1,t)))*.8f;
        foreach(var origin in origins)for(int i=0;i<4;i++){
            float a=(52+i*24)*Mathf.Deg2Rad;
            Vector2 direction=new Vector2(Mathf.Cos(a),Mathf.Sin(a)),normal=new Vector2(-direction.y,direction.x);
            float distance=(8+i%3*3)+(1-Mathf.Pow(1-t,3))*(18+i%3*5);
            Vector2 center=origin+direction*distance;float length=(5+i%3)*(1-t*.5f),width=1.25f;
            Color c=Color.Lerp(new Color(1,.72f,.39f),new Color(1,.97f,.85f),i%2);c.a=alpha;
            int n=h.currentVertCount;h.AddVert(center-direction*length,c,Vector2.zero);h.AddVert(center+normal*width,c,Vector2.zero);
            h.AddVert(center+direction*length,c,Vector2.zero);h.AddVert(center-normal*width,c,Vector2.zero);
            h.AddTriangle(n,n+1,n+2);h.AddTriangle(n,n+2,n+3);
        }
    }
}
