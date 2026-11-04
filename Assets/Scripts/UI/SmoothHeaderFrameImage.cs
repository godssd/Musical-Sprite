using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

/// <summary>Continuous texture warp: rigid endcaps, C2-smooth extra length across the middle.</summary>
public sealed class SmoothHeaderFrameImage : Image
{
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        if (sprite == null) { base.OnPopulateMesh(helper); return; }
        helper.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        Vector4 uv = DataUtility.GetOuterUV(sprite);
        float sourceWidth = sprite.rect.width;
        float nativeWidth = sourceWidth * rect.height / Mathf.Max(1f, sprite.rect.height);
        float baseWidth = Mathf.Min(nativeWidth, rect.width);
        float extraWidth = rect.width - baseWidth;
        float left = Mathf.Clamp01(sprite.border.x / sourceWidth);
        float right = Mathf.Clamp(1f - sprite.border.z / sourceWidth, left + .001f, 1f);
        const int columns = 256;
        for (int i = 0; i <= columns; i++)
        {
            float u = i / (float)columns;
            float t = Mathf.Clamp01((u - left) / Mathf.Max(.001f, right - left));
            // Quintic smootherstep: first and second derivatives vanish at both endcap joins.
            float blend = t * t * t * (10f + t * (-15f + 6f * t));
            float x = rect.xMin + u * baseWidth + extraWidth * blend;
            float textureX = Mathf.Lerp(uv.x, uv.z, u);
            Add(x, rect.yMin, textureX, uv.y);
            Add(x, rect.yMax, textureX, uv.w);
            if (i == columns) continue;
            int n = i * 2;
            helper.AddTriangle(n, n + 1, n + 2);
            helper.AddTriangle(n + 1, n + 3, n + 2);
        }
        void Add(float x, float y, float u, float v)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = new Vector3(x, y, 0f);
            vertex.uv0 = new Vector2(u, v); vertex.color = color;
            helper.AddVert(vertex);
        }
    }
}
