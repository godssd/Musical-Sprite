using System.Collections.Generic;
using UnityEngine;

/// <summary>Quiet continuous rail underneath the four independent lane segments.</summary>
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
        if (shader == null)
        {
            Debug.LogError("[JudgementRailVisual] Missing lane shader.");
            return;
        }
        original = originalRenderer;
        Color colour = side == 0 ? new Color(1f, 0.42f, 0.36f)
                                  : new Color(0.39f, 0.70f, 1f);
        rail = new GameObject(name + "_QuietRail");
        rail.transform.SetParent(transform.parent, true);
        rail.transform.position = transform.position;
        mesh = BuildRail(colour);
        rail.AddComponent<MeshFilter>().sharedMesh = mesh;
        material = new Material(shader) { renderQueue = 3005 };
        rail.AddComponent<MeshRenderer>().sharedMaterial = material;
        if (original != null) original.enabled = false;
    }

    private static Mesh BuildRail(Color colour)
    {
        var vertices = new List<Vector3>();
        var colours = new List<Color>();
        var triangles = new List<int>();
        // Wide translucent tint and a narrow painted edge, with no dark housing.
        AddStrip(0.065f, 0.033f, new Color(colour.r, colour.g, colour.b, 0.12f));
        AddStrip(0.018f, 0.036f, new Color(colour.r, colour.g, colour.b, 0.60f));
        var result = new Mesh { name = "QuietJudgementRail" };
        result.SetVertices(vertices);
        result.SetColors(colours);
        result.SetTriangles(triangles, 0);
        result.RecalculateBounds();
        return result;

        void AddStrip(float halfWidth, float y, Color tint)
        {
            int n = vertices.Count;
            vertices.Add(new Vector3(-halfWidth, y, -3.4f));
            vertices.Add(new Vector3(halfWidth, y, -3.4f));
            vertices.Add(new Vector3(-halfWidth, y, 3.4f));
            vertices.Add(new Vector3(halfWidth, y, 3.4f));
            for (int i = 0; i < 4; i++) colours.Add(tint);
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
