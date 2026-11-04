using UnityEngine;
using UnityEngine.UI;

/// <summary>Vector ribbon body for the isolated HP interval sample, not the complete HUD artwork.</summary>
[AddComponentMenu("UI/Musical Sprite/Ribbon HP Image")]
public sealed class RibbonHPImage : Image
{
    public bool mirrorShape;

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect rect = GetPixelAdjustedRect();
        const int steps = 80;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float x = mirrorShape ? 1f - t : t;
            float center = 0.47f + 0.12f * Mathf.Sin(t * Mathf.PI) - 0.06f * t;
            float cap = Mathf.Sqrt(Mathf.Clamp01(Mathf.Min(t, 1f - t) / 0.04f));
            float half = (0.22f - t * 0.025f) * cap;
            Add(center - half, 0f);
            Add(center + half, 1f);
            if (i < steps)
            {
                int n = i * 2;
                helper.AddTriangle(n, n + 1, n + 2);
                helper.AddTriangle(n + 1, n + 3, n + 2);
            }

            void Add(float y, float v)
            {
                var vertex = UIVertex.simpleVert;
                vertex.position = new Vector3(rect.x + x * rect.width, rect.y + y * rect.height, 0f);
                vertex.color = color;
                vertex.uv0 = new Vector2(x, v);
                helper.AddVert(vertex);
            }
        }
    }
}
