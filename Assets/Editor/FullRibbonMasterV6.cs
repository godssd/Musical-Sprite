using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Deterministic separation of the approved flattened master; no new artwork.</summary>
public static class FullRibbonMasterV6 {
    const string Folder="Assets/Art/UI/V11-Handpainted/FullRibbonMasterV6";
    public static void Batch(){
        Directory.CreateDirectory(Folder);
        var src=new Texture2D(2,2,TextureFormat.RGBA32,false);
        src.LoadImage(File.ReadAllBytes("DesignPreviews/TeamHUD-MotionV6-20261028/locked-base-reference.png"));
        Texture2D Cut(string name,RectInt rect,int channel){
            var tex=new Texture2D(rect.width,rect.height,TextureFormat.RGBA32,false);
            for(int y=0;y<rect.height;y++)for(int x=0;x<rect.width;x++){
                var c=src.GetPixel(rect.x+x,src.height-1-(rect.y+y));
                float red=c.r-Mathf.Max(c.g,c.b),green=c.g-Mathf.Max(c.r,c.b),blue=c.b-Mathf.Max(c.r,c.g);
                float difference=channel==0?red:channel==1?green:channel==2?blue:Mathf.Max(green,Mathf.Max(red,blue));
                c.a=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.035f,.13f,difference));
                if(channel==2&&c.b<.45f)c.a=0;
                if(channel==3&&c.b<.45f&&blue>Mathf.Max(red,green))c.a=0;
                if(c.r>.70f&&c.g>.70f&&c.b>.65f)c.a=0;
                if(name.StartsWith("prefix")&&rect.y+y>325&&green<red)c.a=0;
                if(name=="frame-red"&&new Vector2((x-65)/65f,(y-63)/63f).magnitude<.82f)c.a=0;
                tex.SetPixel(x,rect.height-1-y,c);
            }
            if(name.StartsWith("ribbon")||name.StartsWith("frame")||name.StartsWith("prefix")){
                var pixels=tex.GetPixels();var seen=new bool[pixels.Length];var components=new System.Collections.Generic.List<System.Collections.Generic.List<int>>();
                for(int seed=0;seed<pixels.Length;seed++){
                    if(seen[seed]||pixels[seed].a<.08f)continue;
                    var component=new System.Collections.Generic.List<int>();var queue=new System.Collections.Generic.Queue<int>();queue.Enqueue(seed);seen[seed]=true;
                    while(queue.Count>0){int at=queue.Dequeue();component.Add(at);int xx=at%rect.width,yy=at/rect.width;
                        foreach(int step in new[]{-1,1,-rect.width,rect.width}){int next=at+step;if(next<0||next>=pixels.Length||Mathf.Abs(next%rect.width-xx)>1||seen[next]||pixels[next].a<.08f)continue;seen[next]=true;queue.Enqueue(next);}
                    }
                    components.Add(component);
                }
                components.Sort((a,b)=>b.Count.CompareTo(a.Count));var keep=new bool[pixels.Length];
                for(int c=0;c<components.Count;c++){
                    var component=components[c];
                    bool retain=c==0||(name.StartsWith("ribbon")&&c==1&&component.Count>components[0].Count*.04f);
                    if(name.StartsWith("prefix")){
                        int minX=rect.width,maxX=0,minY=rect.height,maxY=0;foreach(int at in component){minX=Mathf.Min(minX,at%rect.width);maxX=Mathf.Max(maxX,at%rect.width);minY=Mathf.Min(minY,at/rect.width);maxY=Mathf.Max(maxY,at/rect.width);}
                        retain=!(maxX-minX>=16&&maxX-minX>(maxY-minY+1)*3);
                    }
                    if(retain)foreach(int at in component)keep[at]=true;
                }
                for(int i=0;i<pixels.Length;i++)if(!keep[i])pixels[i].a=0;
                tex.SetPixels(pixels);
            }
            tex.Apply();File.WriteAllBytes(Folder+"/"+name+".png",tex.EncodeToPNG());return tex;
        }
        // Rectangles are in the original 1536 x 1024 master, not reinterpreted sketches.
        var redBar=Cut("ribbon-red",new RectInt(140,304,546,71),0);
        var blueBar=Cut("ribbon-blue",new RectInt(869,304,546,71),2);
        Cut("outer-red",new RectInt(24,225,52,76),1);
        Cut("outer-blue",new RectInt(1445,225,72,78),1);
        Cut("end-red",new RectInt(661,298,49,58),1);
        Cut("end-blue",new RectInt(846,287,48,65),1);
        Cut("prefix-red",new RectInt(412,277,69,55),3);
        Cut("prefix-blue",new RectInt(1048,295,59,33),3);
        Cut("frame-red",new RectInt(52,240,130,126),0);
        Cut("frame-blue",new RectInt(1363,240,128,126),2);
        void Profile(Texture2D bar,string name){
            var tex=new Texture2D(256,1,TextureFormat.RGBA32,false,true);
            for(int k=0;k<256;k++){
                int x=Mathf.RoundToInt(k/255f*(bar.width-1)),lo=bar.height,hi=-1;
                int run=-1,best=0;
                for(int y=0;y<=bar.height;y++){
                    bool ink=y<bar.height&&bar.GetPixel(x,y).a>.5f;
                    if(ink&&run<0)run=y;
                    if(!ink&&run>=0){if(y-run>best){best=y-run;lo=run;hi=y-1;}run=-1;}
                }
                if(hi<lo){lo=bar.height/2;hi=lo;}
                tex.SetPixel(k,0,new Color(lo/(float)bar.height,(hi+1)/(float)bar.height,0,1));
            }
            tex.Apply();File.WriteAllBytes(Folder+"/"+name+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
        }
        Profile(redBar,"profile-red");Profile(blueBar,"profile-blue");
        AssetDatabase.Refresh();
        foreach(string path in Directory.GetFiles(Folder,"*.png")){
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            bool profile=Path.GetFileName(path).StartsWith("profile");
            importer.textureType=profile?TextureImporterType.Default:TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=!profile;
            importer.sRGBTexture=!profile;importer.isReadable=profile;importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;
            if(!profile){var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.FullRect;importer.SetTextureSettings(settings);}
            importer.SaveAndReimport();
        }
        var style=Resources.Load<FullRibbonHUDStyle>(FullRibbonHUDStyle.ResourcePath);
        Sprite S(string n)=>AssetDatabase.LoadAssetAtPath<Sprite>(Folder+"/"+n+".png");
        style.masterArtwork=true;
        style.ribbon=new[]{S("ribbon-red"),S("ribbon-blue")};
        style.avatarFrame=new[]{S("frame-red"),S("frame-blue")};
        style.outerLeaves=new[]{S("outer-red"),S("outer-blue")};
        style.innerLeaves=new[]{S("end-red"),S("end-blue")};
        style.marks=new[]{S("prefix-red"),S("prefix-blue")};
        style.ribbonProfiles=new[]{AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/profile-red.png"),AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/profile-blue.png")};
        style.teamSize=new Vector2(760,200);
        style.ribbonPosition=new Vector2(128,93);style.ribbonSize=new Vector2(605,79);
        style.outerLeafPosition=new Vector2(0,5);style.outerLeafSize=new Vector2(58,84);
        style.innerLeafOffset=new Vector2(-28,-7);style.innerLeafSize=new Vector2(54,64);
        style.scoreHeight=88;style.hpHeight=52;style.digitWidthScale=.79f;
        style.screenInset=new Vector2(30,18);
        style.letteringColor=new Color(1,.95f,.86f,1);
        style.lossDuration=.46f;style.healDuration=.56f;
        style.pendingGlow=.8f;style.pendingInnerLight=.32f;style.flashGlow=1.55f;
        style.tickGlow=2.5f;style.glowRadius=20;
        style.entranceDuration=.78f;style.entranceResolve=.10f;
        style.entranceGlow=.85f;style.entranceHeadGlow=2.8f;
        EditorUtility.SetDirty(style);AssetDatabase.SaveAssets();
        BakeDistanceFields(style);
        UnityEngine.Object.DestroyImmediate(src);UnityEngine.Object.DestroyImmediate(redBar);UnityEngine.Object.DestroyImmediate(blueBar);
        File.WriteAllText("DesignPreviews/VisualIterationV6-20261101/master-source.txt","Source: locked-base-reference.png. Cropped in source pixel coordinates; chromatic alpha separation only; combined crescent/ribbon silhouette, leaves and score-prefix retained from the approved original. No AI redraw, no SampleScene save.");
    }
    static void BakeDistanceFields(FullRibbonHUDStyle style){
        const int pad=128;string folder="Assets/Resources/BattleVisualV11/MasterV6Glow";Directory.CreateDirectory(folder);
        var sprites=new System.Collections.Generic.HashSet<Sprite>();
        foreach(var array in new[]{style.ribbon,style.avatarFrame,style.crescent,style.outerLeaves,style.innerLeaves,style.marks,style.dot,style.digits})foreach(var sprite in array)if(sprite!=null)sprites.Add(sprite);
        sprites.Add(style.avatarMask);sprites.Add(style.hpLabel);
        foreach(var sprite in sprites){
            var source=new Texture2D(2,2,TextureFormat.RGBA32,false);source.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite.texture)));
            int sw=(int)sprite.rect.width,sh=(int)sprite.rect.height,w=sw+pad*2,h=sh+pad*2;
            var d=new float[w*h];for(int i=0;i<d.Length;i++)d[i]=10000;
            for(int y=0;y<sh;y++)for(int x=0;x<sw;x++)if(source.GetPixel((int)sprite.rect.x+x,(int)sprite.rect.y+y).a>.45f)d[(y+pad)*w+x+pad]=0;
            for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++){int i=y*w+x;d[i]=Mathf.Min(d[i],Mathf.Min(d[i-1]+1,Mathf.Min(d[i-w]+1,Mathf.Min(d[i-w-1]+1.414214f,d[i-w+1]+1.414214f))));}
            for(int y=h-2;y>0;y--)for(int x=w-2;x>0;x--){int i=y*w+x;d[i]=Mathf.Min(d[i],Mathf.Min(d[i+1]+1,Mathf.Min(d[i+w]+1,Mathf.Min(d[i+w-1]+1.414214f,d[i+w+1]+1.414214f))));}
            var texture=new Texture2D(w,h,TextureFormat.RGBA32,false,true);var pixels=new Color32[d.Length];for(int i=0;i<d.Length;i++){byte v=(byte)Mathf.Clamp(Mathf.RoundToInt(d[i]/pad*255),0,255);pixels[i]=new Color32(v,v,v,255);}texture.SetPixels32(pixels);texture.Apply();
            File.WriteAllBytes(folder+"/"+sprite.texture.name+".png",texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(texture);
        }
        AssetDatabase.Refresh();foreach(string path in Directory.GetFiles(folder,"*.png")){var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Default;importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=4096;importer.filterMode=FilterMode.Bilinear;importer.sRGBTexture=false;importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();}
    }
}
