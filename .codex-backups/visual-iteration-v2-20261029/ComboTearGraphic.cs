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
    private Color teamColor;
    private bool rebuilding;
    private Material paperMaterial;
    // Sparse centre, dense perimeter: no horizontal / vertical grid seams.
    private static readonly Vector2[] Seeds = {
        new Vector2(-.22f,.24f), new Vector2(.28f,.12f), new Vector2(-.13f,-.48f),
        new Vector2(-.68f,.65f),new Vector2(.56f,.74f),new Vector2(.66f,-.48f),new Vector2(-.65f,-.57f),
        new Vector2(-.93f,.86f),new Vector2(-.98f,.35f),new Vector2(-.92f,-.18f),new Vector2(-.94f,-.87f),
        new Vector2(-.38f,.99f),new Vector2(.08f,.96f),new Vector2(.91f,.96f),new Vector2(.98f,.46f),
        new Vector2(.96f,-.07f),new Vector2(.94f,-.88f),new Vector2(.35f,-.96f),new Vector2(-.24f,-.99f)
    };

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
            Vector2 middle = (min + max) * .5f, half = (max - min) * .501f;
            glyphHeight = max.y - min.y;
            for (int index = 0; index < Seeds.Length; index++)
            {
                Vector2[] cell = Cell(index,middle,half);
                if(cell.Length<3)continue;
                Vector2 center=Vector2.zero;foreach(var p in cell)center+=p;center/=cell.Length;
                Vector2 relative = new Vector2((center.x - middle.x) / half.x, (center.y - middle.y) / half.y);
                var piece = new Piece { Center = center, Direction = new Vector2(relative.x * 1.25f, relative.y * .48f),
                    Edge = Mathf.Clamp01(relative.magnitude), Rotation = (index % 2 == 0 ? 1f : -1f) * (3f + 9f * Mathf.Abs(relative.x)) };
                for (int glyph = 0; glyph + 3 < count; glyph += 4)
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
        finally { rebuilding = false; }
    }

    private static Vector2[] Cell(int index,Vector2 middle,Vector2 half) {
        var polygon=new List<Vector2>{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)};
        Vector2 seed=Seeds[index];
        for(int k=0;k<Seeds.Length;k++) {
            if(k==index||polygon.Count==0)continue;
            Vector2 normal=Seeds[k]-seed;float limit=(Seeds[k].sqrMagnitude-seed.sqrMagnitude)*.5f;
            var next=new List<Vector2>();Vector2 previous=polygon[polygon.Count-1];float pd=limit-Vector2.Dot(normal,previous);
            foreach(var current in polygon){float d=limit-Vector2.Dot(normal,current);if((d>=0)!=(pd>=0))next.Add(Vector2.LerpUnclamped(previous,current,pd/(pd-d)));if(d>=0)next.Add(current);previous=current;pd=d;}
            polygon=next;
        }
        for(int k=0;k<polygon.Count;k++)polygon[k]=middle+Vector2.Scale(polygon[k],half);
        return polygon.ToArray();
    }

    private static Vector2[] FibreEdge(Vector2 a,Vector2 b,float roughness) {
        bool reverse=a.x>b.x||(Mathf.Abs(a.x-b.x)<.001f&&a.y>b.y);
        Vector2 low=reverse?b:a,high=reverse?a:b,delta=high-low;
        Vector2 normal=new Vector2(-delta.y,delta.x).normalized;
        float phase=(low.x+high.x)*.127f+(low.y+high.y)*.193f;
        var result=new Vector2[5];
        for(int k=0;k<5;k++) {
            float t=k*.25f;
            float offset=k==0||k==4?0:Mathf.Sin(phase+k*2.7f)*Mathf.Min(roughness,delta.magnitude*.055f);
            result[reverse?4-k:k]=Vector2.LerpUnclamped(low,high,t)+normal*offset;
        }
        return result;
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
        float open = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.08f, .44f, t));
        float separate = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.16f,1,t));
        float grey = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.12f, .44f, t));
        if(paperMaterial!=null)paperMaterial.SetFloat("_Grey",grey);
        foreach (Piece piece in pieces)
        {
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.72f - piece.Edge * .06f, 1f, t));
            Color tint = Color.Lerp(teamColor, new Color(.54f, .57f, .58f, teamColor.a), grey);
            tint.a *= alpha;
            Vector2 offset = piece.Direction * glyphHeight * spread * (open * .6f + separate * .8f) * (1+piece.Edge*.35f);
            float radians = piece.Rotation * separate * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians), cos = Mathf.Cos(radians);
            Vector2 Move(Vector2 point) {
                Vector2 p=point-piece.Center;
                float stretch=1+.07f*Mathf.Sin(Mathf.Clamp01(t/.22f)*Mathf.PI);
                return new Vector2((p.x*cos-p.y*sin)*stretch,p.x*sin+p.y*cos)+piece.Center+offset;
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
                        float width=Mathf.Max(.65f,glyphHeight*.009f)*(j%2==0?1:.55f)*open;
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
