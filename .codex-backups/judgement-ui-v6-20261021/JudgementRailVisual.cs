using System.Collections.Generic;
using UnityEngine;

/// <summary>Painted judgement rail. The existing hit line collider remains authoritative.</summary>
public sealed class JudgementRailVisual : MonoBehaviour
{
    private MeshRenderer original;
    private GameObject rail;
    private Mesh mesh;
    private Material material;
    private GameObject hitGlow;
    private Mesh glowMesh;
    private Material glowMaterial;
    private Color glowColor;
    private float glowTimer;
    private const float GlowDuration = 0.26f;

    public void Configure(int side, MeshRenderer originalRenderer)
    {
        if (rail != null) return;
        var shader = Resources.Load<Shader>("Shaders/LaneGraphic");
        var additive = Resources.Load<Shader>("Shaders/LaneLampGlow");
        if (shader == null || additive == null) return;
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
        glowColor = tint;
        hitGlow = new GameObject(name + "_LocalHitGlow");
        hitGlow.transform.SetParent(transform.parent, true);
        hitGlow.transform.position = transform.position;
        glowMesh = BuildGlow();
        hitGlow.AddComponent<MeshFilter>().sharedMesh = glowMesh;
        glowMaterial = new Material(additive) { renderQueue = 3021 };
        hitGlow.AddComponent<MeshRenderer>().sharedMaterial = glowMaterial;
        hitGlow.SetActive(false);
        if (original != null) original.enabled = false;
    }

    public void PulseAt(float worldZ)
    {
        if (hitGlow == null) return;
        hitGlow.transform.position = new Vector3(transform.position.x, transform.position.y, worldZ);
        glowTimer = GlowDuration;
        hitGlow.SetActive(true);
    }

    private void Update()
    {
        if (glowTimer <= 0f || hitGlow == null) return;
        glowTimer = Mathf.Max(0f, glowTimer - Time.deltaTime);
        float strength = Mathf.Pow(glowTimer / GlowDuration, 1.4f);
        glowMaterial.SetColor("_Tint", new Color(glowColor.r, glowColor.g, glowColor.b, strength));
        if (glowTimer <= 0f) hitGlow.SetActive(false);
    }

    private static Mesh BuildGlow()
    {
        var vertices = new List<Vector3>(20);
        var colors = new List<Color>(20);
        var triangles = new List<int>(48);
        float[] z = { -1.0f, -0.55f, 0f, 0.55f, 1.0f };
        float[] alpha = { 0f, 0.25f, 0.6f, 0.25f, 0f };
        AddBand(0.17f, 0.046f, 0.5f);
        AddBand(0.035f, 0.048f, 1f);
        var result = new Mesh { name = "JudgementRailHitGlow" };
        result.SetVertices(vertices);
        result.SetColors(colors);
        result.SetTriangles(triangles, 0);
        result.RecalculateBounds();
        return result;

        void AddBand(float halfWidth, float y, float opacity)
        {
            int start = vertices.Count;
            for (int i = 0; i < z.Length; i++)
            {
                vertices.Add(new Vector3(-halfWidth, y, z[i]));
                vertices.Add(new Vector3(halfWidth, y, z[i]));
                var color = new Color(1f, 1f, 1f, alpha[i] * opacity);
                colors.Add(color); colors.Add(color);
                if (i == 0) continue;
                int n = start + 2 * i;
                triangles.Add(n - 2); triangles.Add(n); triangles.Add(n - 1);
                triangles.Add(n - 1); triangles.Add(n); triangles.Add(n + 1);
            }
        }
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
        if (hitGlow != null) Destroy(hitGlow);
        if (mesh != null) Destroy(mesh);
        if (glowMesh != null) Destroy(glowMesh);
        if (material != null) Destroy(material);
        if (glowMaterial != null) Destroy(glowMaterial);
        if (original != null) original.enabled = true;
    }
}
