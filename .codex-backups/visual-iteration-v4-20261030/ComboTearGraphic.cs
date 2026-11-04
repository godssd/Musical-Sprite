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
    private float units, elapsed, duration = 0.30f, spread = 0.16f, glyphHeight;
    private Vector2 rounding;
    private Vector2 snapshotCenter;
    private Color teamColor;
    private bool rebuilding;
    private Material paperMaterial;
    // Two continuous fibre tears through each glyph, three substantial centre
    // pieces and corner chips. No radial Voronoi / tiled fractures.
    private const int PieceCount=15;

    public bool IsPlaying { get; private set; }
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
        pieces.Clear(); snapshot = null; spriteQuads.Clear(); spriteTexture = null;
        SetVerticesDirty();
    }

    public void Capture(SpriteNumberGraphic source, float seconds, float distance)
    {
        Cancel();
        if (source == null) return;
        source.CopyGlyphQuads(spriteQuads);
        if (spriteQuads.Count == 0) return;
        font = null; spriteTexture = source.mainTexture;
        units = 1f; rounding = Vector2.zero; teamColor = source.color;
        duration = Mathf.Max(.05f, seconds); spread = Mathf.Max(0f, distance);
        RebuildPieces(); IsPlaying = pieces.Count > 0;
        SetAllDirty();
    }

    /// <summary>Game time in battle; explicit editor time in the isolated review.</summary>
    public void Advance(float deltaTime)
    {
        if (!IsPlaying) return;
        elapsed = Mathf.Min(duration, elapsed + Mathf.Max(0f, deltaTime));
        if (elapsed >= duration) { Cancel(); return; }
        SetVerticesDirty();
    }

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
            for (int index = 0; index < PieceCount; index++)
            {
                Vector2[] cell = Cell(index,middle,half,glyph/4);
                if(cell.Length<3)continue;
                Vector2 center=Vector2.zero;foreach(var p in cell)center+=p;center/=cell.Length;
                Vector2 relative = new Vector2((center.x - middle.x) / half.x, (center.y - middle.y) / half.y);
                bool perimeter=index>=3;
                var piece = new Piece { Center = center, Direction = new Vector2(relative.x*(perimeter?1.1f:.40f),relative.y*(perimeter?.65f:.58f)),
                    Edge = perimeter?1:.20f, Rotation = ((index+glyph/4) % 2 == 0 ? 1f : -1f) * (perimeter?11f:3f) };
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
            }
        }
        finally { rebuilding = false; }
    }

    private static Vector2[] Cell(int index,Vector2 middle,Vector2 half,int variant) {
        int band=index<3?index:(index-3)/4;
        float bottom=band==0?-1:band==1?-.36f:.24f,top=band==0?-.36f:band==1?.24f:1;
        float TearY(float x,float y){
            if(Mathf.Abs(y)>.99f)return y;
            float hash=Mathf.Sin(x*17.13f+y*37.71f+variant*7.13f)*43758.5453f;
            float jag=(hash-Mathf.Floor(hash)-.5f)*.045f+Mathf.Sin(x*4.7f+variant*1.81f+y*4.1f)*.028f;
            return y+x*(variant%2==0?.07f:-.085f)+jag;
        }
        var polygon=new List<Vector2>();
        const int samples=12;
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
            float limit=.56f+.07f*Mathf.Sin(variant*2.3f+band*1.7f+corner)+ny*tipY;
            if(index>=3&&(index-3)%4==corner){polygon=Cut(polygon,normal,limit,false);break;}
            polygon=Cut(polygon,normal,limit,true);
        }
        for(int k=0;k<polygon.Count;k++)polygon[k]=middle+Vector2.Scale(polygon[k],half);
        return polygon.ToArray();
    }

    private static Vector2[] FibreEdge(Vector2 a,Vector2 b,float roughness) {
        // The authored seam is already fibred. Subdividing it again creates slivers
        // and can exceed UI mesh limits on long numbers.
        return new[]{a,b};
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
        float open = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.18f, .44f, t));
        float separate = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.44f,1,t));
        float grey = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.28f, .55f, t));
        if(paperMaterial!=null)paperMaterial.SetFloat("_Grey",grey);
        foreach (Piece piece in pieces)
        {
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.77f - piece.Edge * .12f, 1f, t));
            Color tint = Color.Lerp(teamColor, new Color(.72f, .74f, .75f, teamColor.a), grey);
            tint.a *= alpha;
            Vector2 offset = piece.Direction * glyphHeight * spread * (open * .52f + separate * .65f) * (1+piece.Edge*.30f);
            float radians = piece.Rotation * separate * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians), cos = Mathf.Cos(radians);
            Vector2 Move(Vector2 point) {
                Vector2 p=point-piece.Center;
                p=new Vector2(p.x*cos-p.y*sin,p.x*sin+p.y*cos)+piece.Center+offset;
                float stretch=.11f*Mathf.Sin(Mathf.Clamp01(t/.42f)*Mathf.PI)*(1-open*.45f);
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
                // A narrow, uneven paper fibre lip on interior cuts only. Glyph alpha still clips it.
                if(open<.01f)continue;
                Vector2 center=Vector2.zero,uvCenter=Vector2.zero;
                foreach(var p in polygon){center+=p.Position;uvCenter+=p.UV;}center/=polygon.Length;uvCenter/=polygon.Length;
                var cell=piece.Cells[polygonIndex];
                for(int edge=0;edge<polygon.Length;edge++) {
                    Point a=polygon[edge],b=polygon[(edge+1)%polygon.Length];bool cut=false;
                    for(int c=0;c<cell.Length;c++){
                        Vector2 direction=cell[(c+1)%cell.Length]-cell[c];
                        if(Mathf.Abs(Cross(direction,a.Position-cell[c]))<.02f&&Mathf.Abs(Cross(direction,b.Position-cell[c]))<.02f){cut=true;break;}
                    }
                    if(!cut)continue;
                    const int fibres=6;
                    for(int j=0;j<fibres;j++) {
                        Point aa=Point.Lerp(a,b,j/(float)fibres),bb=Point.Lerp(a,b,(j+1)/(float)fibres);
                        float screenScale=canvas!=null?Mathf.Max(.1f,canvas.scaleFactor):1;
                        float width=Mathf.Max(.75f/screenScale,glyphHeight*.009f)*(j%2==0?1:.72f)*open;
                        float ia=Mathf.Min(.15f,width/Mathf.Max(1,Vector2.Distance(aa.Position,center))),ib=Mathf.Min(.15f,width/Mathf.Max(1,Vector2.Distance(bb.Position,center)));
                        Point ai=Point.Lerp(aa,new Point(center,uvCenter),ia),bi=Point.Lerp(bb,new Point(center,uvCenter),ib);
                        int start=helper.currentVertCount;Color paper=new Color(.97f,.95f,.87f,tint.a*.85f);
                        helper.AddVert(Move(aa.Position),paper,aa.UV);helper.AddVert(Move(bb.Position),paper,bb.UV);
                        helper.AddVert(Move(bi.Position),paper,bi.UV);helper.AddVert(Move(ai.Position),paper,ai.UV);
                        helper.AddTriangle(start,start+1,start+2);helper.AddTriangle(start,start+2,start+3);
                    }
                }
            }
        }
    }
}
