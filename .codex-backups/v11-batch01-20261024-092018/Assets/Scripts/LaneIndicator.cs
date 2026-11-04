using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One full judgement segment, with a quiet lane UI and layered input / hit light.</summary>
public class LaneIndicator : MonoBehaviour
{
    public Renderer targetRenderer;
    public Material activeMaterial;
    public Material idleMaterial;
    public float flashDuration = 0.12f;
    public float popScale = 1.6f;
    [Min(0.01f)] public float hitDuration = 0.22f;
    [Min(0f)] public float guideLength = 3.1f;
    [Min(0f)] public float guideWidth = 0.045f;
    public Color idleColor = new Color(0.11f, 0.12f, 0.14f, 1f);
    public Color pressColor = new Color(0.76f, 0.76f, 0.76f, 1f);
    public Color redHitColor = new Color(1f, 0.27f, 0.10f, 1f);
    public Color blueHitColor = new Color(0.16f, 0.58f, 1f, 1f);

    private readonly List<GameObject> pieces = new List<GameObject>();
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Material> materials = new List<Material>();
    private Renderer quiet, wash, edges, glow, core;
    private Material washMat, edgeMat, glowMat, coreMat;
    private float pressTimer, hitTimer;
    private bool held, sleeping;
    private int side;
    private Color Accent => side == 0 ? new Color(1f, 0.37f, 0.25f) : new Color(0.28f, 0.68f, 1f);
    private static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

    public void ConfigureSide(int ownerSide)
    {
        side = ownerSide == 1 ? 1 : 0;
        if (quiet != null) return;
        var alpha = Resources.Load<Shader>("Shaders/LaneGraphic");
        var additive = Resources.Load<Shader>("Shaders/LaneLampGlow");
        if (alpha == null || additive == null)
        {
            Debug.LogError("[LaneIndicator] Missing lane shader.");
            return;
        }
        float dir = side == 0 ? 1f : -1f;
        float length = Mathf.Max(2.7f, guideLength);
        quiet = Piece("QuietLane", QuietMesh(dir, length), alpha, 3010, out _);
        wash = Piece("HitWash", WashMesh(dir, length), alpha, 3011, out washMat);
        edges = Piece("FadingEdges", EdgeMesh(dir, length), additive, 3016, out edgeMat);
        glow = Piece("SoftRailLight", RailMesh(false), additive, 3020, out glowMat);
        core = Piece("FineRailCore", RailMesh(true), additive, 3022, out coreMat);
        wash.enabled = edges.enabled = glow.enabled = core.enabled = false;
        if (targetRenderer != null) targetRenderer.enabled = false;
    }

    private Renderer Piece(string suffix, Mesh mesh, Shader shader, int queue, out Material material)
    {
        var piece = new GameObject(name + "_" + suffix);
        piece.transform.SetParent(transform.parent, true);
        piece.transform.position = transform.position;
        piece.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = piece.AddComponent<MeshRenderer>();
        material = new Material(shader) { renderQueue = queue };
        renderer.sharedMaterial = material;
        pieces.Add(piece); meshes.Add(mesh); materials.Add(material);
        return renderer;
    }

    private void Update()
    {
        if (coreMat == null) return;
        pressTimer = Mathf.Max(0f, pressTimer - Time.deltaTime);
        hitTimer = Mathf.Max(0f, hitTimer - Time.deltaTime);
        float press = held ? 1f : pressTimer / Mathf.Max(0.01f, flashDuration);
        float hit = hitTimer / Mathf.Max(0.28f, hitDuration);
        float strength = Mathf.Max(press * 0.75f, hit);
        quiet.enabled = !sleeping;
        core.enabled = glow.enabled = !sleeping && strength > 0.004f;
        wash.enabled = edges.enabled = !sleeping && hit > 0.004f;
        if (!core.enabled) return;
        Color accent = Accent;
        Color white = hit > 0f ? Color.Lerp(Color.white, accent, 0.24f) : Color.white;
        Color soft = hit > 0f ? accent : Color.white;
        coreMat.SetColor("_Tint", A(white, strength));
        glowMat.SetColor("_Tint", A(soft, strength * (hit > 0f ? 0.96f : 0.67f)));
        if (hit > 0f)
        {
            washMat.SetColor("_Tint", new Color(1f, 1f, 1f, hit));
            edgeMat.SetColor("_Tint", new Color(1f, 1f, 1f, hit));
        }
    }

    public void Flash() { Hold(true); held = false; }
    public void Hold(bool on)
    {
        if (sleeping) return;
        held = on;
        if (on) pressTimer = flashDuration;
    }
    public void Hit() { if (!sleeping) hitTimer = Mathf.Max(0.28f, hitDuration); }
    public void SetSleep(bool on)
    {
        sleeping = on;
        if (on) { held = false; pressTimer = hitTimer = 0f; }
    }

    private Mesh QuietMesh(float dir, float length)
    {
        var b = new Builder();
        Color near = Accent, far = Color.Lerp(Accent, new Color(0.58f, 0.48f, 0.70f), 0.48f);
        float[] x = { 0.10f, 0.6f, 1.5f, length };
        float[] z = { -0.76f, -0.58f, 0f, 0.58f, 0.76f };
        b.Grid(x, z, dir, 0.048f, (i, j) =>
        {
            float fade = 1f - i / 3f;
            float rim = Mathf.Pow(Mathf.Abs(j - 2f) * 0.5f, 3f);
            return A(Color.Lerp(far, near, fade), (0.055f + 0.075f * rim) * fade);
        });
        foreach (float edge in new[] { -0.75f, 0.75f })
            b.Edge(dir, length, edge, 0.014f, 0.050f, A(near, 0.23f), A(far, 0f));
        for (int band = 0; band < 2; band++)
        {
            float center = 1.0f + band * 1.15f;
            float[] bandX = { center - 0.15f, center, center + 0.15f };
            float[] bandZ = { -0.72f, -0.48f, 0f, 0.48f, 0.72f };
            b.Grid(bandX, bandZ, dir, 0.051f, (i, j) =>
                A(Color.Lerp(near, far, band * 0.35f),
                    (i == 1 ? 0.085f : 0f) * (j == 0 || j == 4 ? 0f : 1f)));
        }
        // Quiet metre marks imply travel without enclosing the lane in a frame.
        for (int i = 1; i <= 3; i++)
        {
            float mark = dir * (0.55f + i * 0.68f);
            b.Quad(mark - 0.021f, mark + 0.021f, -0.018f, 0.018f,
                0.053f, A(near, 0.18f - i * 0.035f));
        }
        return b.Finish("QuietLaneUI");
    }

    private Mesh WashMesh(float dir, float length)
    {
        var b = new Builder();
        Color near = side == 0 ? new Color(1f, 0.31f, 0.24f) : new Color(0.27f, 0.66f, 1f);
        Color mid = side == 0 ? new Color(0.87f, 0.43f, 0.48f) : new Color(0.34f, 0.56f, 0.94f);
        Color far = new Color(0.55f, 0.46f, 0.73f);
        float[] x = { -0.1f, 0.08f, 0.25f, 0.55f, 1.05f, 1.85f, 2.65f, length, length + 0.18f };
        float[] z = { -0.86f, -0.76f, -0.58f, 0f, 0.58f, 0.76f, 0.86f };
        float[] ax = { 0f, 0.12f, 0.21f, 0.19f, 0.14f, 0.085f, 0.04f, 0.012f, 0f };
        float[] az = { 0f, 0.63f, 1f, 0.86f, 1f, 0.63f, 0f };
        b.Grid(x, z, dir, 0.052f, (i, j) =>
            A(i < 4 ? Color.Lerp(near, mid, i / 4f) : Color.Lerp(mid, far, (i - 4f) / 4f),
              ax[i] * az[j]));
        return b.Finish("HitColourWash");
    }

    private Mesh EdgeMesh(float dir, float length)
    {
        var b = new Builder();
        Color near = side == 0 ? new Color(1f, 0.69f, 0.54f) : new Color(0.71f, 0.89f, 1f);
        Color mid = side == 0 ? new Color(1f, 0.37f, 0.39f) : new Color(0.35f, 0.67f, 1f);
        Color far = new Color(0.56f, 0.44f, 0.77f);
        float[] x = { 0f, 0.14f, 0.38f, 0.8f, 1.35f, 2.1f, 2.9f, length + 0.18f };
        float[] ax = { 0.27f, 0.36f, 0.32f, 0.25f, 0.17f, 0.09f, 0.03f, 0f };
        float[] az = { 0f, 0.16f, 1f, 1f, 0.16f, 0f };
        foreach (float edge in new[] { -0.76f, 0.76f })
        {
            float[] z = { edge - 0.22f, edge - 0.06f, edge - 0.008f,
                          edge + 0.008f, edge + 0.06f, edge + 0.22f };
            b.Grid(x, z, dir, 0.057f, (i, j) =>
                A(i < 3 ? Color.Lerp(near, mid, i / 3f) : Color.Lerp(mid, far, (i - 3f) / 4f),
                  ax[i] * az[j]));
        }
        return b.Finish("HitFadingEdges");
    }

    private static Mesh RailMesh(bool fine)
    {
        var b = new Builder();
        float[] x = fine
            ? new[] { -0.035f, -0.015f, 0f, 0.015f, 0.035f }
            : new[] { -0.26f, -0.15f, -0.055f, 0f, 0.055f, 0.15f, 0.26f };
        float[] ax = fine
            ? new[] { 0f, 0.76f, 1f, 0.76f, 0f }
            : new[] { 0f, 0.07f, 0.35f, 0.52f, 0.35f, 0.07f, 0f };
        float[] z = { -0.80f, -0.75f, -0.63f, 0f, 0.63f, 0.75f, 0.80f };
        float[] az = { 0f, 0.53f, 1f, 1f, 1f, 0.53f, 0f };
        b.Grid(x, z, 1f, fine ? 0.066f : 0.063f, (i, j) =>
            new Color(1f, 1f, 1f, ax[i] * az[j]));
        return b.Finish(fine ? "FullSegmentCore" : "FullSegmentSoftGlow");
    }

    private void OnDestroy()
    {
        if (targetRenderer != null) targetRenderer.enabled = true;
        foreach (var piece in pieces) if (piece != null) Destroy(piece);
        foreach (var mesh in meshes) if (mesh != null) Destroy(mesh);
        foreach (var material in materials) if (material != null) Destroy(material);
    }

    private sealed class Builder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();
        public void Grid(float[] xs, float[] zs, float direction, float y, Func<int, int, Color> color)
        {
            int first = vertices.Count;
            for (int j = 0; j < zs.Length; j++)
                for (int i = 0; i < xs.Length; i++)
                {
                    vertices.Add(new Vector3(xs[i] * direction, y, zs[j]));
                    colors.Add(color(i, j));
                }
            for (int j = 0; j < zs.Length - 1; j++)
                for (int i = 0; i < xs.Length - 1; i++)
                {
                    int n = first + j * xs.Length + i;
                    triangles.Add(n); triangles.Add(n + xs.Length); triangles.Add(n + 1);
                    triangles.Add(n + 1); triangles.Add(n + xs.Length); triangles.Add(n + xs.Length + 1);
                }
        }
        public void Edge(float direction, float length, float z, float width,
            float y, Color near, Color far)
        {
            float[] xs = { 0.08f, 0.32f, 1f, 2f, length };
            float[] zs = { z - width, z, z + width };
            Grid(xs, zs, direction, y, (i, j) =>
            {
                Color c = Color.Lerp(near, far, i / 4f);
                c.a *= j == 1 ? 1f : 0.28f;
                return c;
            });
        }
        public void Quad(float x0, float x1, float z0, float z1, float y, Color color)
        {
            int n = vertices.Count;
            vertices.Add(new Vector3(x0, y, z0)); vertices.Add(new Vector3(x0, y, z1));
            vertices.Add(new Vector3(x1, y, z0)); vertices.Add(new Vector3(x1, y, z1));
            for (int i = 0; i < 4; i++) colors.Add(color);
            triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
            triangles.Add(n + 1); triangles.Add(n + 3); triangles.Add(n + 2);
        }
        public Mesh Finish(string meshName)
        {
            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices); mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
