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
    }

    private readonly List<Piece> pieces = new List<Piece>(21);
    private readonly List<Point> clipA = new List<Point>(12), clipB = new List<Point>(12);
    private readonly TextGenerator generator = new TextGenerator();
    private Font font;
    private TextGenerationSettings settings;
    private string snapshot;
    private float units, elapsed, duration = 0.30f, spread = 0.16f, glyphHeight;
    private Vector2 rounding;
    private Color teamColor;
    private bool rebuilding;
    private static readonly float[] Columns = { -1f, -.88f, -.67f, -.32f, .32f, .67f, .88f, 1f };
    private static readonly float[] Rows = { -1f, -.30f, .30f, 1f };

    public bool IsPlaying { get; private set; }
    public override Texture mainTexture => font != null && font.material != null
        ? font.material.mainTexture : Texture2D.whiteTexture;

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
        pieces.Clear(); snapshot = null;
        SetVerticesDirty();
    }

    /// <summary>Game time in battle; explicit editor time in the isolated review.</summary>
    public void Advance(float deltaTime)
    {
        if (!IsPlaying) return;
        elapsed = Mathf.Min(duration, elapsed + Mathf.Max(0f, deltaTime));
        if (elapsed >= duration) { Cancel(); return; }
        SetVerticesDirty();
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
        if (font == null || string.IsNullOrEmpty(snapshot) || rebuilding) return;
        rebuilding = true;
        try
        {
            pieces.Clear();
            generator.Invalidate();
            generator.Populate(snapshot, settings);
            var vertices = generator.verts;
            int count = vertices.Count - 4; // TextGenerator's trailing dummy quad.
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
            for (int row = 0; row < Rows.Length - 1; row++)
            for (int column = 0; column < Columns.Length - 1; column++)
            {
                // Shared jagged boundaries, broad central pieces and progressively smaller outer pieces.
                Vector2[] cell = { Node(column, row, middle, half), Node(column + 1, row, middle, half),
                    Node(column + 1, row + 1, middle, half), Node(column, row + 1, middle, half) };
                Vector2 center = (cell[0] + cell[1] + cell[2] + cell[3]) * .25f;
                Vector2 relative = new Vector2((center.x - middle.x) / half.x, (center.y - middle.y) / half.y);
                var piece = new Piece { Center = center, Direction = new Vector2(relative.x * 1.25f, relative.y * .48f),
                    Edge = Mathf.Abs(relative.x), Rotation = ((row * 7 + column) % 2 == 0 ? 1f : -1f) * (3f + 5f * Mathf.Abs(relative.x)) };
                for (int glyph = 0; glyph + 3 < count; glyph += 4)
                {
                    clipA.Clear();
                    for (int v = 0; v < 4; v++)
                    {
                        UIVertex vertex = vertices[glyph + v];
                        clipA.Add(new Point((Vector2)vertex.position * units + rounding, vertex.uv0));
                    }
                    Clip(cell);
                    if (clipA.Count >= 3) piece.Polygons.Add(clipA.ToArray());
                }
                if (piece.Polygons.Count > 0) pieces.Add(piece);
            }
        }
        finally { rebuilding = false; }
    }

    private static Vector2 Node(int column, int row, Vector2 middle, Vector2 half)
    {
        float x = Columns[column], y = Rows[row];
        if (column > 0 && column < Columns.Length - 1 && row > 0 && row < Rows.Length - 1)
            x += ((column + row * 3) % 3 - 1) * .023f;
        if (row > 0 && row < Rows.Length - 1)
            y += ((column * 5 + row * 7) % 5 - 2) * .065f;
        return middle + Vector2.Scale(new Vector2(x, y), half);
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
        float open = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .44f, t));
        float separate = Mathf.SmoothStep(0f, 1f, t);
        float grey = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.16f, .60f, t));
        foreach (Piece piece in pieces)
        {
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f - piece.Edge * .14f, 1f - piece.Edge * .08f, t));
            Color tint = Color.Lerp(teamColor, new Color(.54f, .57f, .58f, teamColor.a), grey);
            tint.a *= alpha;
            Vector2 offset = piece.Direction * glyphHeight * spread * (open * .45f + separate * .55f);
            float radians = piece.Rotation * separate * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians), cos = Mathf.Cos(radians);
            foreach (Point[] polygon in piece.Polygons)
            {
                int first = helper.currentVertCount;
                foreach (Point point in polygon)
                {
                    Vector2 p = point.Position - piece.Center;
                    p = new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos) + piece.Center + offset;
                    helper.AddVert(p, tint, point.UV);
                }
                for (int index = 1; index < polygon.Length - 1; index++)
                    helper.AddTriangle(first, first + index, first + index + 1);
            }
        }
    }
}
