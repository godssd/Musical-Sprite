using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

// Asset preparation only: split generated layers, trim transparent padding,
// discard disconnected alpha specks, export shared masks and mirrored variants.
// No runtime code and no new creative geometry for the approved artwork.
public static class TeamHudAssetPack
{
    static string output;
    static readonly Color Red = Color.FromArgb(255,81,75);
    static readonly Color Blue = Color.FromArgb(122,163,255);
    static readonly Color Cream = Color.FromArgb(255,247,222);
    public static readonly List<string> Report = new List<string>();

    public static Bitmap Load(string path) {
        using (var source = Image.FromFile(path)) {
            var b = new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(b)) {g.CompositingMode=CompositingMode.SourceCopy;g.DrawImageUnscaled(source,0,0);} return b;
        }
    }
    static byte[] Bytes(Bitmap b) {
        var d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        var a=new byte[b.Width*b.Height*4];
        for(int y=0;y<b.Height;y++) Marshal.Copy(IntPtr.Add(d.Scan0,y*d.Stride),a,y*b.Width*4,b.Width*4);
        b.UnlockBits(d); return a;
    }
    static Bitmap FromBytes(byte[] a,int w,int h) {
        var b=new Bitmap(w,h,PixelFormat.Format32bppArgb);
        var d=b.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        for(int y=0;y<h;y++)Marshal.Copy(a,y*w*4,IntPtr.Add(d.Scan0,y*d.Stride),w*4);
        b.UnlockBits(d);return b;
    }
    static Bitmap CleanTrim(Bitmap input,int keepComponents,string name) {
        int w=input.Width,h=input.Height,n=w*h;var a=Bytes(input);var seen=new bool[n];
        var components=new List<List<int>>();var q=new Queue<int>();
        for(int i=0;i<n;i++) {
            if(seen[i]||a[i*4+3]<32)continue;
            var c=new List<int>();seen[i]=true;q.Enqueue(i);
            while(q.Count>0) {
                int p=q.Dequeue();c.Add(p);int x=p%w,y=p/w;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){
                    int xx=x+dx,yy=y+dy;if(xx<0||xx>=w||yy<0||yy>=h)continue;
                    int k=yy*w+xx;if(!seen[k]&&a[k*4+3]>=32){seen[k]=true;q.Enqueue(k);}
                }
            }
            components.Add(c);
        }
        components.Sort((x,y)=>y.Count.CompareTo(x.Count));
        if(components.Count==0)throw new InvalidOperationException("Empty alpha: "+name);
        var keep=new bool[n];int minX=w,minY=h,maxX=0,maxY=0;
        for(int ci=0;ci<Math.Min(keepComponents,components.Count);ci++) {
            if(components[ci].Count<components[0].Count*.015)continue;
            foreach(int k in components[ci]) {
                int x=k%w,y=k/w;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++) {
                    int xx=x+dx,yy=y+dy;if(xx>=0&&xx<w&&yy>=0&&yy<h)keep[yy*w+xx]=true;
                }
            }
        }
        for(int i=0;i<n;i++) {
            if(!keep[i]){a[i*4]=a[i*4+1]=a[i*4+2]=a[i*4+3]=0;continue;}
            if(a[i*4+3]==0)continue;int x=i%w,y=i/w;
            minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);
        }
        using(var clean=FromBytes(a,w,h)) {
            var trimmed=clean.Clone(new Rectangle(minX,minY,maxX-minX+1,maxY-minY+1),PixelFormat.Format32bppArgb);
            var padded=new Bitmap(trimmed.Width+16,trimmed.Height+16,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(padded)){g.DrawImageUnscaled(trimmed,8,8);}trimmed.Dispose();
            Report.Add(name+": "+padded.Width+"x"+padded.Height+", keep "+keepComponents+" component(s), alpha padding 8px");return padded;
        }
    }
    static Bitmap Colorize(Bitmap b,Color tint,bool solidMask) {
        var a=Bytes(b);
        for(int i=0;i<a.Length;i+=4) {
            float shade=solidMask?1f:Math.Max(a[i],Math.Max(a[i+1],a[i+2]))/255f;
            a[i]=(byte)(tint.B*shade);a[i+1]=(byte)(tint.G*shade);a[i+2]=(byte)(tint.R*shade);
        }return FromBytes(a,b.Width,b.Height);
    }
    static void Save(Bitmap b,string name) {b.Save(Path.Combine(output,name+".png"),ImageFormat.Png);}
    static Bitmap Pad(Bitmap b) {
        var result=new Bitmap(b.Width+16,b.Height+16,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(result))g.DrawImageUnscaled(b,8,8);
        return result;
    }
    static void Variants(Bitmap b,string name,bool team) {
        Save(b,name+"-red");
        using(var mask=Colorize(b,Color.White,true)) {
            Save(mask,name+"-mask-red");mask.RotateFlip(RotateFlipType.RotateNoneFlipX);Save(mask,name+"-mask-blue");
        }
        using(var blue=Colorize(b,Blue,false)){blue.RotateFlip(RotateFlipType.RotateNoneFlipX);Save(blue,name+"-blue");}
    }
    public static void Run(string root,string ribbonPath,string partsPath,string destination) {
        output=destination;Directory.CreateDirectory(output);Report.Clear();
        using(var raw=Load(ribbonPath))using(var body=CleanTrim(raw,1,"ribbon")) {
            Variants(body,"ribbon",true);
            foreach(var entry in new[]{new[]{"empty","red"},new[]{"loss","red"},new[]{"heal","red"}}) {
                string state=entry[0];Color c=state=="empty"?Color.FromArgb(68,57,68):state=="loss"?Color.FromArgb(168,131,57):Color.FromArgb(119,201,91);
                using(var variant=Colorize(body,c,true)){Save(variant,"ribbon-"+state+"-red");variant.RotateFlip(RotateFlipType.RotateNoneFlipX);Save(variant,"ribbon-"+state+"-blue");}
            }
        }
        using(var atlas=Load(partsPath)) {
            string[] names={"avatar-frame","identity-crescent","outer-leaves","inner-leaves","score-marks","outer-dot"};
            int[] counts={1,1,2,2,3,1};
            for(int i=0;i<6;i++) {
                // Generated parts have uneven row spacing. Use verified source bands;
                // a uniform 3-row grid would cut off the bottom of the avatar ring.
                int x=i%2*atlas.Width/2;
                int bandStart=i<2?0:i<4?500:900;
                int bandEnd=i<2?500:i<4?900:1254;
                int y=(int)Math.Round(bandStart*atlas.Height/1254.0);
                int endY=(int)Math.Round(bandEnd*atlas.Height/1254.0);
                using(var cell=atlas.Clone(new Rectangle(x,y,atlas.Width/2,endY-y),PixelFormat.Format32bppArgb))
                using(var cleaned=CleanTrim(cell,counts[i],names[i])) {
                    if(i==2||i==3){Save(cleaned,names[i]+"-red");cleaned.RotateFlip(RotateFlipType.RotateNoneFlipX);Save(cleaned,names[i]+"-blue");}
                    else Variants(cleaned,names[i],true);
                }
            }
        }
        // Reuse the existing approved 0-9 glyph pixels; atlas JSON has bottom-left coordinates.
        int[,] regions={{48,525,254,330},{374,514,191,353},{627,522,267,337},{947,516,237,348},{1236,512,269,347},{48,105,244,346},{337,106,256,342},{644,108,250,341},{941,100,260,350},{1257,105,241,345}};
        using(var atlas=Load(Path.Combine(root,"Assets/Resources/BattleVisualV11/HudAssets/digits-v1.png"))) {
            for(int i=0;i<10;i++)using(var crop=atlas.Clone(new Rectangle(regions[i,0],atlas.Height-regions[i,1]-regions[i,3],regions[i,2],regions[i,3]),PixelFormat.Format32bppArgb))using(var glyph=Pad(crop)) {
                Save(glyph,"digit-"+i+"-cream");
                using(var red=Colorize(glyph,Red,false))Save(red,"digit-"+i+"-red");
                using(var blue=Colorize(glyph,Blue,false))Save(blue,"digit-"+i+"-blue");
            }
        }
        using(var atlas=Load(Path.Combine(root,"Assets/Resources/BattleVisualV11/HudAssets/labels-v1.png")))
        using(var crop=atlas.Clone(new Rectangle(232,atlas.Height-588-249,340,249),PixelFormat.Format32bppArgb))using(var hp=Pad(crop))Save(hp,"label-hp-cream");
        // Geometry-only avatar clipping utility, not a replacement for the painted frame.
        using(var mask=new Bitmap(256,256,PixelFormat.Format32bppArgb)) {
            using(var g=Graphics.FromImage(mask)){g.SmoothingMode=SmoothingMode.AntiAlias;g.FillEllipse(Brushes.White,4,4,248,248);}Save(mask,"avatar-content-mask");
        }
        File.WriteAllLines(Path.Combine(output,"alpha-preparation.txt"),Report);
    }

    static void Draw(Graphics g,string file,float x,float y,float w,float h,bool flip=false) {
        using(var b=Load(Path.Combine(output,file+".png"))) {if(flip)b.RotateFlip(RotateFlipType.RotateNoneFlipX);g.DrawImage(b,x,y,w,h);}
    }
    static float NumberWidth(string text,float height) {
        return text.Length*(height*.8f+2)-2;
    }
    static void Number(Graphics g,string text,string color,float x,float y,float height) {
        foreach(char c in text)using(var b=Load(Path.Combine(output,"digit-"+c+"-"+color+".png"))){float w=height*b.Width/b.Height;g.DrawImage(b,x+(height*.8f-w)*.5f,y,w,height);x+=height*.8f+2;}
    }
    static void Header(Graphics g,bool blue,float x,float y,float hpFraction,string hp,string score) {
        var state=g.Save();g.TranslateTransform(x,y);if(blue){g.TranslateTransform(720,0);g.ScaleTransform(-1,1);}
        string team=blue?"blue":"red";
        // Mirror coordinates once. Flip mirrored source back before drawing inside the mirrored group.
        Draw(g,"outer-leaves-"+team,3,15,66,72,blue);
        Draw(g,"outer-dot-"+team,20,98,10,13,blue);
        using(var brush=new SolidBrush(Color.FromArgb(35,43,49)))g.FillEllipse(brush,43,45,105,105);
        Draw(g,"avatar-frame-"+team,30,32,132,132,blue);
        Draw(g,"identity-crescent-"+team,121,104,80,66,blue);
        Draw(g,"ribbon-empty-"+team,156,113,505,48,blue);
        var clip=g.Save();g.SetClip(new RectangleF(156,100,505*hpFraction,80));Draw(g,"ribbon-"+team,156,113,505,48,blue);g.Restore(clip);
        if(hpFraction<1f && hpFraction>0f) {
            var tick=g.Save();g.SetClip(new RectangleF(156+505*hpFraction-1f,100,2f,80));
            Draw(g,"ribbon-mask-"+team,156,113,505,48,blue);g.Restore(tick);
        }
        Draw(g,"inner-leaves-"+team,655,76,44,48,blue);
        float numberWidth=NumberWidth(score,64),numberStart=645-numberWidth;
        Draw(g,"score-marks-"+team,numberStart-42,99,33,20,blue);
        g.Restore(state);
        // Text glyphs never mirror. Whole number width is reserved for six glyphs in live layout.
        Draw(g,"label-hp-cream",x+(blue?405:171),y+58,46,34);
        Number(g,hp,team,x+(blue?459:225),y+53,36);
        float sw=NumberWidth(score,64);Number(g,score,"cream",x+(blue?75:645-sw),y+42,64);
        using(var font=new Font("Microsoft YaHei",13))using(var brush=new SolidBrush(Color.FromArgb(120,134,148)))
            g.DrawString("可替换头像",font,brush,x+(blue?574:40),y+173);
    }
    public static void Preview(string destination,string previewPath) {
        output=destination;
        using(var b=new Bitmap(1536,1050,PixelFormat.Format32bppArgb))using(var g=Graphics.FromImage(b)) {
            g.Clear(Color.FromArgb(14,30,44));g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            using(var f=new Font("Microsoft YaHei",26,FontStyle.Bold))using(var ink=new SolidBrush(Cream))g.DrawString("常态拆层素材 · 组装审查",f,ink,35,20);
            using(var f=new Font("Microsoft YaHei",13))using(var ink=new SolidBrush(Color.FromArgb(177,193,204)))g.DrawString("新素材组合预览，非 Unity 实机；头像留空以便换图。分数区预留固定六位。",f,ink,38,70);
            Header(g,false,10,120,1,"326","584200");Header(g,true,790,120,1,"326","458000");
            Header(g,false,10,355,.5f,"163","5842");Header(g,true,790,355,.5f,"163","4580");
            Header(g,false,10,590,.15f,"49","58");Header(g,true,790,590,.15f,"49","45");
            using(var f=new Font("Microsoft YaHei",14))using(var ink=new SolidBrush(Cream))g.DrawString("独立图片字库（沿用已有笔形）：",f,ink,40,850);
            Number(g,"0123456789","cream",430,840,55);
            using(var f=new Font("Microsoft YaHei",13))using(var ink=new SolidBrush(Color.FromArgb(177,193,204)))g.DrawString("同一血槽 alpha 派生红 / 蓝、空槽、暗黄、绿色；蓝方从左端扣除。所有动效尚未接入。",f,ink,40,960);
            b.Save(previewPath,ImageFormat.Png);
        }
    }
    public static string Verify(string destination) {
        int checkedSprites=0,checkedPairs=0;
        foreach(string path in Directory.GetFiles(destination,"*.png"))using(var b=Load(path)) {
            if(b.GetPixel(0,0).A!=0||b.GetPixel(b.Width-1,b.Height-1).A!=0)throw new Exception("Nontransparent padding: "+path);
            bool nonempty=false;var pixels=Bytes(b);for(int i=3;i<pixels.Length;i+=4)if(pixels[i]!=0){nonempty=true;break;}
            if(!nonempty)throw new Exception("Empty sprite: "+path);checkedSprites++;
        }
        foreach(string name in new[]{"ribbon","avatar-frame","identity-crescent","score-marks","outer-dot"}) {
            using(var red=Load(Path.Combine(destination,name+"-mask-red.png")))
            using(var blue=Load(Path.Combine(destination,name+"-mask-blue.png"))) {
                blue.RotateFlip(RotateFlipType.RotateNoneFlipX);
                var a=Bytes(red);var b=Bytes(blue);
                if(a.Length!=b.Length)throw new Exception("Mask dimensions differ: "+name);
                for(int i=3;i<a.Length;i+=4)if(a[i]!=b[i])throw new Exception("Mask alpha differs: "+name);
                checkedPairs++;
            }
        }
        using(var ring=Load(Path.Combine(destination,"avatar-frame-red.png")))
            if(ring.GetPixel(ring.Width/2,ring.Height/2).A!=0)throw new Exception("Avatar frame center is not transparent");
        return "PASS: "+checkedSprites+" nonempty PNGs with transparent padding; "+checkedPairs+" exact mirrored alpha mask pairs; avatar frame has transparent center.";
    }
}
