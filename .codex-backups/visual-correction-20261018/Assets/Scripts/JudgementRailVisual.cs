using System.Collections.Generic;
using UnityEngine;

/// <summary>Painted judgement rail. The existing hit line collider remains authoritative.</summary>
public sealed class JudgementRailVisual : MonoBehaviour
{
    private MeshRenderer original;
    private GameObject rail;
    private Mesh mesh;
    private Material material;

    public void Configure(int side, MeshRenderer originalRenderer)
    {
        if (rail != null) return;
        var shader = Resources.Load<Shader>("Shaders/LaneGraphic");
        if (shader == null) return;
        original = originalRenderer;
        var tint = side == 0 ? new Color(0.95f, 0.17f, 0.15f, 1f)
                             : new Color(0.19f, 0.51f, 1f, 1f);
        rail = new GameObject(name + "_PaintedRail");
        rail.transform.SetParent(transform.parent, true);
        rail.transform.position = transform.position;
        var filter = rail.AddComponent<MeshFilter>();
        mesh = BuildRail(tint);
        filter.sharedMesh = mesh;
        var renderer = rail.AddComponent<MeshRenderer>();
        material = new Material(shader) { renderQueue = 3005 };
        renderer.sharedMaterial = material;
        if (original != null) original.enabled = false;
    }

    private static Mesh BuildRail(Color tint)
    {
        var vertices = new List<Vector3>(20);
        var colors = new List<Color>(20);
        var triangles = new List<int>(30);
        AddStrip(0.085f, 0.032f, new Color(0.028f, 0.022f, 0.035f, 0.82f));
        AddStrip(0.052f, 0.034f, new Color(tint.r, tint.g, tint.b, 0.78f));
        AddStrip(0.018f, 0.036f, new Color(tint.r, tint.g, tint.b, 0.95f));
        AddStrip(0.006f, 0.038f, new Color(0.85f, 0.90f, 0.95f, 0.33f));
        var result = new Mesh { name = "PaintedJudgementRail" };
        result.SetVertices(vertices);
        result.SetColors(colors);
        result.SetTriangles(triangles, 0);
        result.RecalculateBounds();
        return result;

        void AddStrip(float halfWidth, float y, Color color)
        {
            int n = vertices.Count;
            vertices.Add(new Vector3(-halfWidth, y, -3.25f));
            vertices.Add(new Vector3(halfWidth, y, -3.25f));
            vertices.Add(new Vector3(-halfWidth, y, 3.25f));
            vertices.Add(new Vector3(halfWidth, y, 3.25f));
            for (int i = 0; i < 4; i++) colors.Add(color);
            triangles.Add(n); triangles.Add(n + 2); triangles.Add(n + 1);
            triangles.Add(n + 1); triangles.Add(n + 2); triangles.Add(n + 3);
        }
    }

    private void OnDestroy()
    {
        if (rail != null) Destroy(rail);
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
        if (original != null) original.enabled = true;
    }
}
