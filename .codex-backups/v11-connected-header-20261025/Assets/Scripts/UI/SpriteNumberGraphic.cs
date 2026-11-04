using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Variable-length numbers from the handpainted 0–9 atlas, with stable actual glyph UVs.</summary>
public sealed class SpriteNumberGraphic : MaskableGraphic
{
    private string number = "";
    private readonly List<UIVertex> quads = new List<UIVertex>(40);
    public float digitHeight = 100f;
    public float spacing = 3f;
    public override Texture mainTexture => HandpaintedHUD.EnsureLoaded() ? HandpaintedHUD.DigitTexture : Texture2D.whiteTexture;

    public void SetNumber(int value, bool showZero = false)
    {
        string next = value > 0 || showZero ? Mathf.Max(0, value).ToString() : "";
        if (next == number) return;
        number = next; SetVerticesDirty();
    }

    public void CopyGlyphQuads(List<UIVertex> result)
    {
        result.Clear();
        if (!HandpaintedHUD.EnsureLoaded() || number.Length == 0) return;
        Rect bounds = GetPixelAdjustedRect();
        float height = Mathf.Min(digitHeight, bounds.height);
        float width = 0f;
        foreach (char ch in number)
        {
            var sprite = HandpaintedHUD.Digit(ch - '0');
            width += sprite.rect.width / sprite.rect.height * height;
        }
        width += spacing * (number.Length - 1);
        float scale = Mathf.Min(1f, bounds.width / Mathf.Max(.01f, width));
        height *= scale; width *= scale;
        float x = bounds.center.x - width * .5f;
        float y = bounds.center.y - height * .5f;
        foreach (char ch in number)
        {
            var sprite = HandpaintedHUD.Digit(ch - '0');
            Rect region = sprite.rect;
            float glyphWidth = region.width / region.height * height;
            Vector2 min = new Vector2(region.x / sprite.texture.width, region.y / sprite.texture.height);
            Vector2 max = new Vector2(region.xMax / sprite.texture.width, region.yMax / sprite.texture.height);
            Add(new Vector2(x, y), min);
            Add(new Vector2(x, y + height), new Vector2(min.x, max.y));
            Add(new Vector2(x + glyphWidth, y + height), max);
            Add(new Vector2(x + glyphWidth, y), new Vector2(max.x, min.y));
            x += glyphWidth + spacing * scale;
        }
        void Add(Vector2 position, Vector2 uv)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = position; vertex.uv0 = uv; vertex.color = color;
            result.Add(vertex);
        }
    }

    public Rect NumberBounds()
    {
        CopyGlyphQuads(quads);
        if (quads.Count == 0) return Rect.zero;
        Vector2 min = quads[0].position, max = min;
        foreach (var vertex in quads)
        {
            min = Vector2.Min(min, vertex.position); max = Vector2.Max(max, vertex.position);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear(); CopyGlyphQuads(quads);
        for (int i = 0; i + 3 < quads.Count; i += 4)
        {
            for (int v = 0; v < 4; v++) helper.AddVert(quads[i + v]);
            helper.AddTriangle(i, i + 1, i + 2); helper.AddTriangle(i, i + 2, i + 3);
        }
    }
}
