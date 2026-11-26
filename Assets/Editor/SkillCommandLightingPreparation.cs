using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>Bakes a path coordinate map, without editing the approved artwork pixels.</summary>
public static class SkillCommandLightingPreparation {
    public static void Prepare(){
        var points=new List<Vector2>();
        void Curve(Vector2 a,Vector2 b,Vector2 c,Vector2 d){for(int i=0;i<32;i++){float t=i/31f,u=1-t;points.Add(u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d);}}
        Curve(new Vector2(514,555),new Vector2(610,420),new Vector2(570,220),new Vector2(416,185));
        Curve(new Vector2(416,185),new Vector2(285,140),new Vector2(140,270),new Vector2(150,430));
        Curve(new Vector2(150,430),new Vector2(150,540),new Vector2(270,592),new Vector2(340,635));
        Curve(new Vector2(340,635),new Vector2(480,705),new Vector2(490,805),new Vector2(345,850));
        Curve(new Vector2(345,850),new Vector2(555,850),new Vector2(610,1210),new Vector2(345,1300));
        Curve(new Vector2(345,1300),new Vector2(490,1360),new Vector2(475,1460),new Vector2(350,1480));
        Curve(new Vector2(350,1480),new Vector2(520,1460),new Vector2(580,1610),new Vector2(530,1770));
        Curve(new Vector2(530,1770),new Vector2(500,1910),new Vector2(320,1990),new Vector2(190,1890));
        Curve(new Vector2(190,1890),new Vector2(125,1850),new Vector2(115,1840),new Vector2(105,1820));
        float[] distances=new float[points.Count];for(int i=1;i<points.Count;i++)distances[i]=distances[i-1]+Vector2.Distance(points[i-1],points[i]);
        const int w=128,h=384;var pixels=new Color[w*h];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++){
            Vector2 p=new Vector2((x+.5f)/w*724,(1-(y+.5f)/h)*2172);float best=float.MaxValue,progress=0;
            for(int i=0;i<points.Count;i++){float d=(p-points[i]).sqrMagnitude;if(d<best){best=d;progress=distances[i]/distances[distances.Length-1];}}
            pixels[y*w+x]=new Color(progress,Mathf.Sqrt(best)/724,0,1);
        }
        const string path="Assets/Resources/BattleVisualV11/SkillCommands/FlowPath.asset";
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if(texture==null){texture=new Texture2D(w,h,TextureFormat.RGHalf,false,true);AssetDatabase.CreateAsset(texture,path);}
        texture.SetPixels(pixels);texture.Apply();texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;EditorUtility.SetDirty(texture);
        var style=Resources.Load<SkillCommandStyle>(SkillCommandStyle.Path);style.flowPath=texture;EditorUtility.SetDirty(style);AssetDatabase.SaveAssets();
    }
}
