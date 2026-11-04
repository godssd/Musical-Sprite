using System.Collections.Generic;
using UnityEngine;

/// <summary>Small hand-painted hit glints on a continuous rail.</summary>
public class LaneIndicator : MonoBehaviour
{
    [Header("Existing references")]
    public Renderer targetRenderer;
    public Material activeMaterial;
    public Material idleMaterial;

    [Header("Timing")]
    public float flashDuration = 0.12f;
    public float popScale = 1.6f;
    [Min(0.01f)] public float hitDuration = 0.22f;
    [Min(0f)] public float guideLength = 3.1f;
    [Min(0f)] public float guideWidth = 0.045f;

    [Header("Palette")]
    public Color idleColor = new Color(0.11f, 0.12f, 0.14f, 1f);
    public Color pressColor = new Color(0.76f, 0.76f, 0.76f, 1f);
    public Color redHitColor = new Color(1f, 0.27f, 0.10f, 1f);
    public Color blueHitColor = new Color(0.16f, 0.58f, 1f, 1f);

    private readonly List<GameObject> pieces = new List<GameObject>();
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Material> materials = new List<Material>();
    private Renderer faceRenderer;
    private Renderer haloRenderer;
    private Renderer guideRenderer;
    private Material faceMaterial;
    private Material haloMaterial;
    private float pressTimer;
    private float hitTimer;
    private bool held;
    private bool sleeping;
    private int side;

    public void ConfigureSide(int ownerSide)
    {
        side = ownerSide == 1 ? 1 : 0;
        if (faceRenderer != null) return;
        var graphic = Resources.Load<Shader>("Shaders/LaneGraphic");
        var additive = Resources.Load<Shader>("Shaders/LaneLampGlow");
        if (graphic == null || additive == null)
        {
            Debug.LogError("[LaneIndicator] Missing lane shader; original lamp retained.");
            return;
        }

        CreatePiece("LampRim", BuildKeyMesh(), graphic, out _);
        faceRenderer = CreatePiece("KeyFace", BuildFaceMesh(), graphic, out faceMaterial);
        guideRenderer = CreatePiece("QuietGuides", BuildGuideMesh(side == 0 ? 1f : -1f,
            side == 0 ? redHitColor : blueHitColor), graphic, out _);
        haloRenderer = CreatePiece("ShortFlash", BuildHaloMesh(), additive, out haloMaterial);
        haloRenderer.enabled = false;
        // Retain the original collider and placement; replace its box silhouette.
        if (targetRenderer != null) targetRenderer.enabled = false;
    }

    private Renderer CreatePiece(string suffix, Mesh mesh, Shader shader, out Material material)
    {
        var piece = new GameObject(name + "_" + suffix);
        piece.transform.SetParent(transform.parent, true);
        piece.transform.position = transform.position;
        var filter = piece.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = piece.AddComponent<MeshRenderer>();
        material = new Material(shader);
        material.renderQueue = shader.name.EndsWith("LaneLampGlow") ? 3020 : 3010;
        renderer.sharedMaterial = material;
        pieces.Add(piece);
        meshes.Add(mesh);
        materials.Add(material);
        return renderer;
    }

    private void Update()
    {
        if (faceMaterial == null) return;
        if (pressTimer > 0f) pressTimer = Mathf.Max(0f, pressTimer - Time.deltaTime);
        if (hitTimer > 0f) hitTimer = Mathf.Max(0f, hitTimer - Time.deltaTime);
        float press = flashDuration > 0f ? Mathf.Clamp01(pressTimer / flashDuration) : 0f;
        float hit = hitDuration > 0f ? Mathf.Clamp01(hitTimer / hitDuration) : 0f;
        bool pressed = held || press > 0f;
        Color face = sleeping ? Color.black : hit > 0f
            ? Color.Lerp(pressColor, side == 0 ? redHitColor : blueHitColor, hit)
            : pressed ? pressColor : idleColor;
        faceMaterial.SetColor("_Tint", face);

        if (guideRenderer != null) guideRenderer.enabled = !sleeping;
        if (haloRenderer == null) return;
        float alpha = sleeping ? 0f : hit > 0f ? 0.54f * hit : pressed ? 0.16f : 0f;
        haloRenderer.enabled = alpha > 0.003f;
        if (haloRenderer.enabled)
        {
            Color tint = hit > 0f ? side == 0 ? redHitColor : blueHitColor : pressColor;
            haloMaterial.SetColor("_Tint", new Color(tint.r, tint.g, tint.b, alpha));
        }
    }

    public void Flash() { Hold(true); held = false; }
    public void Hold(bool on)
    {
        if (sleeping) return;
        held = on;
        if (on) pressTimer = flashDuration;
    }
    public void Hit() { if (!sleeping) hitTimer = hitDuration; }
    public void SetSleep(bool on)
    {
        sleeping = on;
        if (!on) return;
        held = false;
        pressTimer = hitTimer = 0f;
        if (haloRenderer != null) haloRenderer.enabled = false;
    }

    private static Mesh BuildKeyMesh()
    {
        var b = new MeshBuilder();
        b.Ellipse(0.18f, 0.095f, 0.051f, new Color(0.025f, 0.025f, 0.045f, 0.86f));
        b.Ellipse(0.155f, 0.07f, 0.053f, new Color(0.14f, 0.14f, 0.17f, 0.94f));
        return b.Finish("LaneLampRim");
    }

    private static Mesh BuildFaceMesh()
    {
        var b = new MeshBuilder();
        b.Ellipse(0.135f, 0.044f, 0.056f, Color.white);
        b.Ellipse(0.075f, 0.021f, 0.058f, new Color(1f, 1f, 1f, 0.5f));
        return b.Finish("LaneKeyFace");
    }

    private Mesh BuildGuideMesh(float direction, Color tint)
    {
        var b = new MeshBuilder();
        float end = Mathf.Max(0.1f, guideLength) * direction;
        float width = Mathf.Clamp(guideWidth * 0.45f, 0.008f, 0.026f);
        // Only two lane borders, never a beam down the middle of a lane.
        foreach (float z in new[] { -0.76f, 0.76f })
            b.Taper(0.12f * direction, end, z - width, z + width, 0.048f,
                new Color(tint.r, tint.g, tint.b, 0.24f),
                new Color(tint.r, tint.g, tint.b, 0.025f));
        return b.Finish("LaneQuietGuides");
    }

    private static Mesh BuildHaloMesh()
    {
        var b = new MeshBuilder();
        b.Ellipse(0.30f, 0.12f, 0.061f, new Color(1f, 1f, 1f, 0.13f));
        b.Ellipse(0.20f, 0.067f, 0.062f, new Color(1f, 1f, 1f, 0.40f));
        return b.Finish("LaneShortFlash");
    }

    private void OnDestroy()
    {
        if (targetRenderer != null) targetRenderer.enabled = true;
        foreach (var piece in pieces) if (piece != null) Destroy(piece);
        foreach (var mesh in meshes) if (mesh != null) Destroy(mesh);
        foreach (var material in materials) if (material != null) Destroy(material);
    }

    private sealed class MeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();

        public void Ellipse(float radiusX, float radiusZ, float y, Color color)
        {
            const int count = 16;
            int first = vertices.Count;
            vertices.Add(new Vector3(0f, y, 0f));
            colors.Add(color);
            for (int i = 0; i <= count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radiusX, y,
                    Mathf.Sin(angle) * radiusZ));
                colors.Add(color);
                if (i > 0)
                {
                    triangles.Add(first);
                    triangles.Add(first + i);
                    triangles.Add(first + i + 1);
                }
            }
        }

        public void Quad(float x0, float x1, float z0, float z1, float y, Color color)
        {
            Taper(x0, x1, z0, z1, y, color, color);
        }

        public void Taper(float x0, float x1, float z0, float z1, float y,
            Color near, Color far)
        {
            int n = vertices.Count;
            vertices.Add(new Vector3(x0, y, z0));
            vertices.Add(new Vector3(x0, y, z1));
            vertices.Add(new Vector3(x1, y, z0));
            vertices.Add(new Vector3(x1, y, z1));
            colors.Add(near); colors.Add(near); colors.Add(far); colors.Add(far);
            triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
            triangles.Add(n + 1); triangles.Add(n + 3); triangles.Add(n + 2);
        }

        public Mesh Finish(string meshName)
        {
            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
