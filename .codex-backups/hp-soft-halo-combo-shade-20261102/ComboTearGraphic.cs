using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A reusable UI mesh of the actual outgoing glyphs, clipped into stable irregular pieces.
/// No render-texture capture, readable font texture, per-piece GameObjects or materials.
/// </summary>
[DisallowMultipleComponent]
public sealed class ComboTearGraphic : MaskableGraphic
{
    private struct Point
    {
        public Vector2 Position, UV;
        public Point(Vector2 position, Vector2 uv) { Position = position; UV = uv; }
        public static Point Lerp(Point a, Point b, float t) =>
            new Point(Vector2.LerpUnclamped(a.Position, b.Position, t), Vector2.LerpUnclamped(a.UV, b.UV, t));
    }

    private sealed class Piece
    {
        public Vector2 Center, Direction;
        public float Edge, Rotation;
        public bool Debris;
        public Color SourceColor;
        public Rect GlyphBounds, GlyphUV;
        public readonly List<Point[]> Polygons = new List<Point[]>();
        public readonly List<Vector2[]> Cells = new List<Vector2[]>();
    }

    private readonly List<Piece> pieces = new List<Piece>(21);
    private readonly List<Point> clipA = new List<Point>(12), clipB = new List<Point>(12);
    private readonly TextGenerator generator = new TextGenerator();
    private Font font;
    private Texture spriteTexture;
    private readonly List<UIVertex> spriteQuads = new List<UIVertex>(40);
    private TextGenerationSettings settings;
    private string snapshot;
    private string glyphCharacters;
    private bool newNumber,decorationSnapshot;
    private float yieldAge;
    [System.Serializable] private sealed class TearPath { public float[] upper,lower; public Vector2[] chips; }
    [System.Serializable] private sealed class TearLibrary { public TearPath[] digits; }
    private static TearLibrary paths;
    private float units, elapsed, duration = 0.75f, spread = 0.16f, glyphHeight;
    private Vector2 rounding;
    private Vector2 snapshotCenter;
    private Color teamColor;
    private bool rebuilding;
    private Material paperMaterial;
    // Two continuous fibre tears through each glyph, three substantial centre
    // pieces and corner chips. No radial Voronoi / tiled fractures.
    private const int PieceCount=15;

    public bool IsPlaying { get; private set; }
    public bool IsDecorationSnapshot=>decorationSnapshot;
    public float FadeAlpha=>1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.45f,1,elapsed/Mathf.Max(.01f,duration)));
    public override Texture mainTexture => font != null && font.material != null
        ? font.material.mainTexture : spriteTexture != null ? spriteTexture : Texture2D.whiteTexture;

    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;
        Font.textureRebuilt += OnFontRebuilt;
    }

    protected override void OnDisable()
    {
        Font.textureRebuilt -= OnFontRebuilt;
        Cancel();
        base.OnDisable();
    }

    protected override void OnDestroy()
    {
        if(paperMaterial!=null){if(Application.isPlaying)Destroy(paperMaterial);else DestroyImmediate(paperMaterial);}
        ((System.IDisposable)generator).Dispose();
        base.OnDestroy();
    }

    public void Capture(Text source, float seconds, float distance)
    {
        Cancel();
        if (source == null || source.font == null || string.IsNullOrEmpty(source.text)) return;
        font = source.font;
        snapshot = source.text;
        glyphCharacters=snapshot;
        settings = source.GetGenerationSettings(source.rectTransform.rect.size);
        settings.color = Color.white;
        units = 1f / source.pixelsPerUnit;
        teamColor = source.color;
        duration = Mathf.Max(.05f, seconds);
        spread = Mathf.Max(0f, distance);
        // Match Text's own rounding, which matters on scaled screen-space canvases.
        source.cachedTextGenerator.PopulateWithErrors(snapshot, settings, source.gameObject);
        var vertices = source.cachedTextGenerator.verts;
        rounding = Vector2.zero;
        if (vertices.Count > 0)
        {
            Vector2 first = (Vector2)vertices[0].position * units;
            rounding = source.PixelAdjustPoint(first) - first;
        }
        RebuildPieces();
        IsPlaying = pieces.Count > 0;
        SetAllDirty();
    }

    public void Cancel()
    {
        IsPlaying = false; elapsed = 0f;
        pieces.Clear(); snapshot = null;glyphCharacters=null;newNumber=false;decorationSnapshot=false;yieldAge=0;spriteQuads.Clear(); spriteTexture = null;
        SetVerticesDirty();
    }

    public void Capture(SpriteNumberGraphic source, float seconds, float distance)
    {
        Cancel();
        if (source == null) return;
        source.CopyGlyphQuads(spriteQuads);
        glyphCharacters=source.DisplayText;
        if (spriteQuads.Count == 0) return;
        font = null; spriteTexture = source.mainTexture;
        units = 1f; rounding = Vector2.zero; teamColor = source.color;
        duration = Mathf.Max(.05f, seconds); spread = Mathf.Max(0f, distance);
        RebuildPieces(); IsPlaying = pieces.Count > 0;
        SetAllDirty();
    }

    public void CaptureImages(IEnumerable<Image> sources,float seconds,float distance){
        Cancel();decorationSnapshot=true;font=null;units=1;rounding=Vector2.zero;teamColor=Color.white;
        foreach(var source in sources){
            if(source==null||source.sprite==null)continue;
            if(spriteTexture!=null&&spriteTexture!=source.sprite.texture)throw new System.InvalidOperationException("A decoration batch must share an atlas");
            spriteTexture=source.sprite.texture;Rect r=source.GetPixelAdjustedRect();
            float aspect=source.sprite.rect.width/source.sprite.rect.height;
            if(source.preserveAspect){if(r.width/r.height>aspect){float w=r.height*aspect;r.x+=(r.width-w)*.5f;r.width=w;}else{float h=r.width/aspect;r.y+=(r.height-h)*.5f;r.height=h;}}
            Vector4 uv=UnityEngine.Sprites.DataUtility.GetOuterUV(source.sprite);
            var matrix=rectTransform.worldToLocalMatrix*source.rectTransform.localToWorldMatrix;
            void Add(Vector2 point,Vector2 tex){var v=UIVertex.simpleVert;v.position=matrix.MultiplyPoint3x4(point);v.uv0=tex;v.color=source.color;spriteQuads.Add(v);}
            Add(new Vector2(r.xMin,r.yMin),new Vector2(uv.x,uv.y));Add(new Vector2(r.xMin,r.yMax),new Vector2(uv.x,uv.w));
            Add(new Vector2(r.xMax,r.yMax),new Vector2(uv.z,uv.w));Add(new Vector2(r.xMax,r.yMin),new Vector2(uv.z,uv.y));
        }
        duration=Mathf.Max(.05f,seconds);spread=Mathf.Max(0,distance);RebuildPieces();IsPlaying=pieces.Count>0;SetAllDirty();
    }

    /// <summary>Game time in battle; explicit editor time in the isolated review.</summary>
    public void Advance(float deltaTime)
    {
        if (!IsPlaying) return;
        elapsed = Mathf.Min(duration, elapsed + Mathf.Max(0f, deltaTime));
        if(newNumber)yieldAge+=Mathf.Max(0,deltaTime);
        if (elapsed >= duration) { Cancel(); return; }
        SetVerticesDirty();
    }

    public void YieldToNewNumber(){if(IsPlaying&&!newNumber){newNumber=true;yieldAge=0;}}

    private void EnsurePaperMaterial() {
        if(paperMaterial!=null)return;
        var shader=Resources.Load<Shader>("Shaders/ComboPaper");
        if(shader==null)return;
        paperMaterial=new Material(shader){name="ComboPaper_Runtime"};material=paperMaterial;
    }

    private void OnFontRebuilt(Font changed)
    {
        // Dynamic fonts can repack when any other HUD text requests new glyphs.
        if (changed != font || rebuilding || !IsPlaying) return;
        RebuildPieces();
        SetAllDirty();
    }

    private void RebuildPieces()
    {
        if (rebuilding || (spriteQuads.Count == 0 && (font == null || string.IsNullOrEmpty(snapshot)))) return;
        rebuilding = true;
        EnsurePaperMaterial();
        try
        {
            pieces.Clear();
            IList<UIVertex> vertices;
            int count;
            if (spriteQuads.Count > 0) { vertices = spriteQuads; count = vertices.Count; }
            else
            {
                generator.Invalidate(); generator.Populate(snapshot, settings);
                vertices = generator.verts;
                count = vertices.Count - 4; // TextGenerator's trailing dummy quad.
            }
            if (count < 4) return;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
            for (int i = 0; i < count; i++)
            {
                Vector2 p = (Vector2)vertices[i].position * units + rounding;
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            if (max.x - min.x < .001f || max.y - min.y < .001f) return;
            snapshotCenter=(min+max)*.5f;
            glyphHeight = max.y - min.y;
            // Give each glyph its own broad ink-bearing centre. A single fracture map
            // over the whole string previously spent its large cells between digits.
            for (int glyph = 0; glyph + 3 < count; glyph += 4)
            {
            Vector2 glyphMin=(Vector2)vertices[glyph].position*units+rounding,glyphMax=glyphMin;
            for(int v=0;v<4;v++){Vector2 p=(Vector2)vertices[glyph+v].position*units+rounding;glyphMin=Vector2.Min(glyphMin,p);glyphMax=Vector2.Max(glyphMax,p);}
            Vector2 middle=(glyphMin+glyphMax)*.5f,half=(glyphMax-glyphMin)*.501f;
            Vector2 uvMin=vertices[glyph].uv0,uvMax=uvMin;
            for(int v=1;v<4;v++){Vector2 uv=vertices[glyph+v].uv0;uvMin=Vector2.Min(uvMin,uv);uvMax=Vector2.Max(uvMax,uv);}
            if(decorationSnapshot){
                Vector2 relative=(middle-snapshotCenter)/Mathf.Max(1,glyphHeight);
                var item=new Piece{Center=middle,Direction=relative*.48f,Edge=.20f,Rotation=(glyph/4%2==0?1:-1)*2.5f,SourceColor=vertices[glyph].color,GlyphBounds=Rect.MinMaxRect(glyphMin.x,glyphMin.y,glyphMax.x,glyphMax.y),GlyphUV=Rect.MinMaxRect(uvMin.x,uvMin.y,uvMax.x,uvMax.y)};
                var quad=new Point[4];for(int v=0;v<4;v++)quad[v]=new Point((Vector2)vertices[glyph+v].position,vertices[glyph+v].uv0);
                item.Polygons.Add(quad);pieces.Add(item);continue;
            }
            for (int index = 0; index < PieceCount; index++)
            {
                int value=glyphCharacters!=null&&glyph/4<glyphCharacters.Length?glyphCharacters[glyph/4]-'0':5;
                Vector2[] cell = Cell(index,middle,half,glyph/4,Mathf.Clamp(value,0,9));
                if(cell.Length<3)continue;
                Vector2 center=Vector2.zero;foreach(var p in cell)center+=p;center/=cell.Length;
                Vector2 relative = new Vector2((center.x - middle.x) / half.x, (center.y - middle.y) / half.y);
                bool perimeter=index>=3;
                var piece = new Piece { Center = center, Direction = new Vector2(relative.x*(perimeter?1.1f:.40f),relative.y*(perimeter?.65f:.58f)),
                    Edge = perimeter?1:.20f, Rotation = ((index+glyph/4) % 2 == 0 ? 1f : -1f) * (perimeter?11f:3f),
                    GlyphBounds=Rect.MinMaxRect(glyphMin.x,glyphMin.y,glyphMax.x,glyphMax.y),GlyphUV=Rect.MinMaxRect(uvMin.x,uvMin.y,uvMax.x,uvMax.y) };
                {
                    for(int edge=0;edge<cell.Length;edge++) {
                        Vector2[] fibre=FibreEdge(cell[edge],cell[(edge+1)%cell.Length],glyphHeight*.012f);
                        for(int step=0;step<fibre.Length-1;step++) {
                            clipA.Clear();
                            for(int v=0;v<4;v++) {
                                UIVertex vertex=vertices[glyph+v];
                                clipA.Add(new Point((Vector2)vertex.position*units+rounding,vertex.uv0));
                            }
                            Clip(new[]{center,fibre[step],fibre[step+1]});
                            if(clipA.Count>=3){piece.Polygons.Add(clipA.ToArray());piece.Cells.Add(new[]{fibre[step],fibre[step+1]});}
                        }
                    }
                }
                if (piece.Polygons.Count > 0) pieces.Add(piece);
            }
            int digitIndex=glyphCharacters!=null&&glyph/4<glyphCharacters.Length?Mathf.Clamp(glyphCharacters[glyph/4]-'0',0,9):5;
            var authored=paths!=null&&paths.digits!=null?paths.digits[digitIndex]:null;
            if(authored!=null&&authored.chips!=null)for(int chip=0;chip<authored.chips.Length;chip++){
                Vector2 anchor=middle+Vector2.Scale(authored.chips[chip],half);
                float size=glyphHeight*(chip%2==0?.014f:.021f);
                var fragment=new Piece{Center=anchor,Direction=new Vector2(authored.chips[chip].x*1.7f,(chip%2==0?1:-1)*.5f),Edge=1,Rotation=chip%2==0?18:-23,Debris=true};
                Point At(Vector2 p)=>new Point(p,new Vector2(Mathf.Lerp(uvMin.x,uvMax.x,Mathf.InverseLerp(glyphMin.x,glyphMax.x,p.x)),Mathf.Lerp(uvMin.y,uvMax.y,Mathf.InverseLerp(glyphMin.y,glyphMax.y,p.y))));
                fragment.Polygons.Add(new[]{At(anchor+new Vector2(-size,-size*.6f)),At(anchor+new Vector2(size*.85f,-size*.4f)),At(anchor+new Vector2(size*.2f,size))});
                pieces.Add(fragment);
            }
            }
        }
        finally { rebuilding = false; }
    }

    private static Vector2[] Cell(int index,Vector2 middle,Vector2 half,int variant,int digit) {
        int band=index<3?index:(index-3)/4;
        float bottom=band==0?-1:band==1?-.36f:.24f,top=band==0?-.36f:band==1?.24f:1;
        float TearY(float x,float y){
            if(Mathf.Abs(y)>.99f)return y;
            if(paths==null){var json=Resources.Load<TextAsset>("BattleVisualV11/ComboTearPaths");if(json!=null)paths=JsonUtility.FromJson<TearLibrary>(json.text);}
            var path=paths!=null&&paths.digits!=null&&paths.digits.Length==10?paths.digits[digit]:null;
            var knots=path==null?null:y>0?path.upper:path.lower;
            float baseY=y;
            if(knots!=null&&knots.Length>=2){float u=(x+1)*.5f*(knots.Length-1);int k=Mathf.Min(knots.Length-2,Mathf.FloorToInt(u));baseY=Mathf.Lerp(knots[k],knots[k+1],Mathf.SmoothStep(0,1,u-k));}
            float hash=Mathf.Sin(x*17.13f+y*37.71f+variant*7.13f)*43758.5453f;
            float jag=(hash-Mathf.Floor(hash)-.5f)*.026f;
            return baseY+jag;
        }
        var polygon=new List<Vector2>();
        const int samples=24;
        for(int k=0;k<=samples;k++){float x=Mathf.Lerp(-1,1,k/(float)samples);polygon.Add(new Vector2(x,TearY(x,bottom)));}
        for(int k=samples;k>=0;k--){float x=Mathf.Lerp(-1,1,k/(float)samples);polygon.Add(new Vector2(x,TearY(x,top)));}
        // Trim isolated corner chips from each broad piece. Long vertical strips
        // would look machine-cut and sever the original curved glyph strokes.
        List<Vector2> Cut(List<Vector2> input,Vector2 normal,float limit,bool inside){
            var output=new List<Vector2>();if(input.Count==0)return output;
            Vector2 previous=input[input.Count-1];float pd=limit-Vector2.Dot(normal,previous);if(!inside)pd=-pd;
            foreach(var p in input){float d=limit-Vector2.Dot(normal,p);if(!inside)d=-d;
                if((d>=0)!=(pd>=0))output.Add(Vector2.LerpUnclamped(previous,p,pd/(pd-d)));
                if(d>=0)output.Add(p);previous=p;pd=d;}
            return output;
        }
        for(int corner=0;corner<4;corner++){
            float nx=corner%2==0?-1:1,ny=corner<2?-.68f:.68f;
            Vector2 normal=new Vector2(nx,ny);
            float tipY=corner<2?TearY(nx,bottom):TearY(nx,top);
            float limit=.83f+.04f*Mathf.Sin(variant*2.3f+band*1.7f+corner)+ny*tipY;
            if(index>=3&&(index-3)%4==corner){polygon=Cut(polygon,normal,limit,false);break;}
            polygon=Cut(polygon,normal,limit,true);
        }
        for(int k=0;k<polygon.Count;k++)polygon[k]=middle+Vector2.Scale(polygon[k],half);
        return polygon.ToArray();
    }

    private static Vector2[] FibreEdge(Vector2 a,Vector2 b,float roughness) {
        // Small deterministic paper fibres on the diagonal chip cuts. Shared
        // endpoints produce the same edge from either neighbouring piece.
        if(Vector2.Distance(a,b)<roughness*5)return new[]{a,b};
        bool reverse=a.x>b.x||(Mathf.Abs(a.x-b.x)<.001f&&a.y>b.y);
        Vector2 start=reverse?b:a,end=reverse?a:b,normal=new Vector2(-(end-start).y,(end-start).x).normalized;
        var result=new Vector2[3];result[0]=start;result[2]=end;
        result[1]=(start+end)*.5f+normal*Mathf.Sin((start.x+end.y)*9.13f)*Mathf.Min(roughness*.30f,Vector2.Distance(a,b)*.025f);
        if(reverse)System.Array.Reverse(result);return result;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private void Clip(Vector2[] cell)
    {
        for (int edge = 0; edge < cell.Length && clipA.Count > 0; edge++)
        {
            clipB.Clear();
            Vector2 start = cell[edge], direction = cell[(edge + 1) % cell.Length] - start;
            Point previous = clipA[clipA.Count - 1];
            float previousDistance = Cross(direction, previous.Position - start);
            foreach (Point current in clipA)
            {
                float distance = Cross(direction, current.Position - start);
                if ((distance >= 0f) != (previousDistance >= 0f))
                    clipB.Add(Point.Lerp(previous, current, previousDistance / (previousDistance - distance)));
                if (distance >= 0f) clipB.Add(current);
                previous = current; previousDistance = distance;
            }
            clipA.Clear(); clipA.AddRange(clipB);
        }
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        if (!IsPlaying || elapsed >= duration) return;
        float t = elapsed / duration;
        float open = 1-Mathf.Pow(1-Mathf.Clamp01(elapsed/.10f),3);
        float separate = 1-Mathf.Exp(-9*Mathf.Max(0,elapsed-.045f));
        float grey = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.10f, .29f, elapsed));
        if(newNumber)grey=Mathf.Max(grey,Mathf.SmoothStep(0,1,yieldAge/.16f));
        if(paperMaterial!=null)paperMaterial.SetFloat("_Grey",grey);
        foreach (Piece piece in pieces)
        {
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(piece.Debris?.30f:.45f, 1f, t));
            if(piece.Debris)alpha*=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.20f,.43f,t));
            alpha*=newNumber?Mathf.Lerp(1,.90f,Mathf.SmoothStep(0,1,yieldAge/.18f)):1;
            Color original=decorationSnapshot?piece.SourceColor:teamColor;
            Color tint = Color.Lerp(original, new Color(.72f, .74f, .75f, original.a), grey);
            float dim=1;
            tint.r*=dim;tint.g*=dim;tint.b*=dim;
            tint.a *= alpha;
            Vector2 offset = piece.Direction * glyphHeight * spread * (open * .80f + separate * .85f) * (1+piece.Edge*.30f);
            float radians = piece.Rotation * separate * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians), cos = Mathf.Cos(radians);
            Vector2 Move(Vector2 point) {
                Vector2 p=point-piece.Center;
                p=new Vector2(p.x*cos-p.y*sin,p.x*sin+p.y*cos)+piece.Center+offset;
                float stretch=(decorationSnapshot?.025f:.11f)*Mathf.Sin(Mathf.Clamp01(elapsed/.10f)*Mathf.PI)*(1-open*.45f);
                return new Vector2(snapshotCenter.x+(p.x-snapshotCenter.x)*(1-stretch*.25f),snapshotCenter.y+(p.y-snapshotCenter.y)*(1+stretch));
            }
            for(int polygonIndex=0;polygonIndex<piece.Polygons.Count;polygonIndex++)
            {
                Point[] polygon=piece.Polygons[polygonIndex];
                int first = helper.currentVertCount;
                foreach (Point point in polygon)
                {
                    helper.AddVert(Move(point.Position), tint, point.UV);
                }
                for (int index = 1; index < polygon.Length - 1; index++)
                    helper.AddTriangle(first, first + index, first + index + 1);
            }
            // Draw the fibre lip ONCE per continuous piece boundary. Using each
            // fan triangle's centroid produces dotted / grid-like paper edges.
            float paperReveal=piece.Edge>.5f?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.30f,.55f,t)):open;
            if(paperReveal>.001f)foreach(var edge in piece.Cells){
                Vector2 a=edge[0],b=edge[1];Rect bounds=piece.GlyphBounds;
                const float epsilon=.15f;
                bool outside=(Mathf.Abs(a.x-bounds.xMin)<epsilon&&Mathf.Abs(b.x-bounds.xMin)<epsilon)||
                    (Mathf.Abs(a.x-bounds.xMax)<epsilon&&Mathf.Abs(b.x-bounds.xMax)<epsilon)||
                    (Mathf.Abs(a.y-bounds.yMin)<epsilon&&Mathf.Abs(b.y-bounds.yMin)<epsilon)||
                    (Mathf.Abs(a.y-bounds.yMax)<epsilon&&Mathf.Abs(b.y-bounds.yMax)<epsilon);
                if(outside)continue;
                Vector2 normal=new Vector2(-(b-a).y,(b-a).x).normalized;
                if(Vector2.Dot(normal,piece.Center-(a+b)*.5f)<0)normal=-normal;
                float screenScale=canvas!=null?Mathf.Max(.1f,canvas.scaleFactor):1;
                float width=Mathf.Max(1.25f/screenScale,glyphHeight*.011f);
                Vector2 ai=a+normal*width,bi=b+normal*width;
                Vector2 UV(Vector2 p){var r=piece.GlyphUV;return new Vector2(Mathf.Lerp(r.xMin,r.xMax,Mathf.InverseLerp(bounds.xMin,bounds.xMax,p.x)),Mathf.Lerp(r.yMin,r.yMax,Mathf.InverseLerp(bounds.yMin,bounds.yMax,p.y)));}
                int first=helper.currentVertCount;Color paper=new Color(.97f*dim,.95f*dim,.87f*dim,tint.a*.85f*paperReveal);
                helper.AddVert(Move(a),paper,UV(a));helper.AddVert(Move(b),paper,UV(b));helper.AddVert(Move(bi),paper,UV(bi));helper.AddVert(Move(ai),paper,UV(ai));
                helper.AddTriangle(first,first+1,first+2);helper.AddTriangle(first,first+2,first+3);

            }
        }
    }
}
