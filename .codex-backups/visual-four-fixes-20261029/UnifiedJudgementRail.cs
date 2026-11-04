using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Four judgement segments and five shared, straight lane boundaries.</summary>
[DisallowMultipleComponent]
public sealed class UnifiedJudgementRail : MonoBehaviour
{
    public JudgementRailStyle style;
    [Range(0, 1)] public int side;
    [Min(0.01f)] public float laneSpacing = 1.7f;
    [HideInInspector] public float railWidth = 0.065f;
    [HideInInspector] public float railHaloHalfWidth = 0.25f;
    [HideInInspector] public float boundaryHaloHalfWidth = 0.23f;
    [HideInInspector] public float hitReachFraction = 0.45f;
    [HideInInspector] public float guideReachFraction = 0.72f;
    [HideInInspector] public float hitDuration = 0.28f;
    [HideInInspector] public float releaseDuration = 0.10f;

    public const int LaneCount = 4;
    public const int BoundaryCount = LaneCount + 1;
    private readonly bool[] held = new bool[LaneCount];
    private readonly bool[] sleeping = new bool[LaneCount];
    private readonly float[] press = new float[LaneCount];
    private readonly float[] hit = new float[LaneCount];
    private readonly float[] peakHold = new float[LaneCount];
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Material> materials = new List<Material>();
    private readonly List<GameObject> pieces = new List<GameObject>();
    private readonly List<Renderer> originals = new List<Renderer>();
    private readonly List<bool> originalEnabled = new List<bool>();
    private readonly MeshRenderer[] railLights = new MeshRenderer[LaneCount];
    private readonly MeshRenderer[] washes = new MeshRenderer[LaneCount];
    private readonly MeshRenderer[] edges = new MeshRenderer[BoundaryCount];
    private MeshRenderer idleRail, idleGuides;
    private Transform hitPoint;
    private BattleCenterLine centerLine;
    private MaterialPropertyBlock properties;
    private bool configured;
    public bool IsConfigured => configured;
    private int styleRevision = int.MinValue;

    private static readonly int StrengthId = Shader.PropertyToID("_Strength");
    private static readonly int HitMixId = Shader.PropertyToID("_HitMix");

    public void Configure(int ownerSide, Transform anchor, float spacing,
        BattleCenterLine seam, Renderer oldRail = null)
    {
        if (configured) return;
        var shader = Resources.Load<Shader>("Shaders/JudgementSoftLight");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogError("[V11 Rail] JudgementSoftLight is missing or unsupported; original rail retained.", this);
            return;
        }
        side = ownerSide == 1 ? 1 : 0;
        laneSpacing = Mathf.Max(0.01f, spacing);
        hitPoint = anchor;
        centerLine = seam;
        properties = new MaterialPropertyBlock();
        style = style != null ? style : Resources.Load<JudgementRailStyle>(JudgementRailStyle.ResourcePath);
        if (style != null) CopyStyleGeometry();
        var accent = side == 0 ? new Color(1f, 0.34f, 0.30f) : new Color(0.35f, 0.62f, 1f);
        var idle = side == 0 ? new Color(0.73f, 0.32f, 0.31f) : new Color(0.38f, 0.50f, 0.73f);
        Material flat = MakeMaterial(shader, "IdleRail", 0, 3005, false, idle);
        Material guides = MakeMaterial(shader, "IdleGuides", 1, 3006, false, accent);
        Material wash = MakeMaterial(shader, "LaneWash", 4, 3007, false, accent);
        Material edge = MakeMaterial(shader, "SharedBoundaryGlow", 3, 3010, true, accent);
        Material rail = MakeMaterial(shader, "SegmentGlow", 2, 3012, true, accent);

        var railMesh = new MeshBuilder();
        for (int lane = 0; lane < LaneCount; lane++)
        {
            float z = LaneCenter(lane), half = laneSpacing * 0.5f - 0.018f;
            railMesh.Quad(-railWidth * 0.5f, railWidth * 0.5f, z - half, z + half);
            railLights[lane] = Piece("Segment_" + lane, Quad(-railHaloHalfWidth,
                railHaloHalfWidth, z - half, z + half), rail, 0.008f);
            washes[lane] = Piece("Wash_" + lane,
                Quad(0f, 1f, z - laneSpacing * 0.5f, z + laneSpacing * 0.5f), wash, 0.002f);
        }
        idleRail = Piece("FlatFourSegments", railMesh.Build("V11_FourSegments"), flat, 0f);
        var guideMesh = new MeshBuilder();
        for (int boundary = 0; boundary < BoundaryCount; boundary++)
        {
            float z = (boundary - LaneCount * 0.5f) * laneSpacing;
            guideMesh.Quad(0f, 1f, z - 0.013f, z + 0.013f);
            edges[boundary] = Piece("SharedEdge_" + boundary,
                Quad(0f, 1f, z - boundaryHaloHalfWidth, z + boundaryHaloHalfWidth), edge, 0.006f);
        }
        idleGuides = Piece("FiveGuideLines", guideMesh.Build("V11_FiveBoundaries"), guides, 0.001f);
        configured = true;
        RefreshParameters();
        HideOriginal(oldRail);
        RefreshGeometry();
        RefreshLights();
    }

    public void HideOriginal(Renderer renderer)
    {
        if (!configured || renderer == null || originals.Contains(renderer)) return;
        originals.Add(renderer);
        originalEnabled.Add(renderer.enabled);
        renderer.enabled = false;
    }

    public void SetHeld(int lane, bool on)
    {
        if (!Valid(lane)) return;
        held[lane] = on && !sleeping[lane];
        if (held[lane]) press[lane] = 1f;
        RefreshLights();
    }

    public void PulsePress(int lane)
    {
        if (!Valid(lane) || sleeping[lane]) return;
        press[lane] = 1f;
        RefreshLights();
    }

    public void Hit(int lane)
    {
        if (!Valid(lane) || sleeping[lane]) return;
        hit[lane] = 1f;
        peakHold[lane] = style != null ? Mathf.Max(0f, style.hitPeakHold) : 0f;
        RefreshLights();
    }

    public void SetSleep(int lane, bool on)
    {
        if (!Valid(lane)) return;
        sleeping[lane] = on;
        if (on) { held[lane] = false; press[lane] = hit[lane] = peakHold[lane] = 0f; }
        RefreshLights();
    }

    public void ClearPreviewState()
    {
        for (int i = 0; i < LaneCount; i++)
        {
            held[i] = sleeping[i] = false;
            press[i] = hit[i] = peakHold[i] = 0f;
        }
        RefreshLights();
    }

    // Explicit stepping also drives the isolated editor review without ExecuteAlways.
    public void Advance(float deltaTime)
    {
        RefreshParameters();
        deltaTime = Mathf.Max(0f, deltaTime);
        for (int i = 0; i < LaneCount; i++)
        {
            press[i] = held[i] ? 1f : Mathf.Max(0f, press[i] - deltaTime / Mathf.Max(0.01f, releaseDuration));
            float fadeDelta = Mathf.Max(0f, deltaTime - peakHold[i]);
            peakHold[i] = Mathf.Max(0f, peakHold[i] - deltaTime);
            hit[i] = Mathf.Max(0f, hit[i] - fadeDelta / Mathf.Max(0.01f, hitDuration));
        }
        RefreshGeometry();
        RefreshLights();
    }

    private void Update() => Advance(Time.deltaTime);

    private void LateUpdate() => RefreshGeometry();

    private void RefreshGeometry()
    {
        if (!configured) return;
        if (hitPoint != null) transform.position = hitPoint.position + Vector3.up * 0.035f;
        // NoteSpawner's lane offsets use world Z, independently of the anchor's mesh scale.
        transform.rotation = Quaternion.identity;
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(SafeInverse(parentScale.x),
            SafeInverse(parentScale.y), SafeInverse(parentScale.z));
        float direction = side == 0 ? 1f : -1f;
        float seamX = centerLine != null ? centerLine.currentX : 0f;
        float padding = style != null ? Mathf.Max(0f, style.seamPadding) : 0.25f;
        float available = Mathf.Max(0f, direction * (seamX - transform.position.x) - padding);
        float reach = available * hitReachFraction;
        float guides = available * guideReachFraction;
        idleGuides.transform.localScale = new Vector3(direction * guides, 1f, 1f);
        idleGuides.enabled = guides > 0.001f && isActiveAndEnabled;
        for (int i = 0; i < LaneCount; i++)
            washes[i].transform.localScale = new Vector3(direction * reach, 1f, 1f);
        for (int i = 0; i < BoundaryCount; i++)
            edges[i].transform.localScale = new Vector3(direction * reach, 1f, 1f);
    }

    private void RefreshLights()
    {
        if (!configured || properties == null) return;
        for (int i = 0; i < LaneCount; i++)
        {
            float h = sleeping[i] ? 0f : Smooth(hit[i]);
            float p = sleeping[i] ? 0f : Smooth(press[i]);
            Apply(railLights[i], Mathf.Max(p * (style != null ? style.pressStrength : 0.58f),
                h * (style != null ? style.hitStrength : 1f)), h);
            Apply(washes[i], Mathf.Max(p * (style != null ? style.pressWashStrength : 0.30f), h), h);
        }
        for (int boundary = 0; boundary < BoundaryCount; boundary++)
        {
            float lower = boundary > 0 && !sleeping[boundary - 1] ? hit[boundary - 1] : 0f;
            float upper = boundary < LaneCount && !sleeping[boundary] ? hit[boundary] : 0f;
            // A shared edge has one renderer and one maximum envelope, never a sum.
            float strength = Smooth(Mathf.Max(lower, upper));
            Apply(edges[boundary], strength * (style != null ? style.edgeStrength : 1f), strength);
        }
    }

    private void Apply(MeshRenderer renderer, float strength, float mix)
    {
        bool visible = isActiveAndEnabled && strength > 0.001f;
        renderer.enabled = visible;
        if (!visible) return;
        properties.Clear();
        properties.SetFloat(StrengthId, strength);
        properties.SetFloat(HitMixId, mix);
        renderer.SetPropertyBlock(properties);
    }

    private Material MakeMaterial(Shader shader, string label, int kind, int queue, bool additive, Color accent)
    {
        var material = new Material(shader) { name = "V11_" + label, renderQueue = queue };
        material.SetFloat("_Kind", kind);
        material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        material.SetColor("_NearColor", accent);
        material.SetColor("_FarColor", new Color(0.60f, 0.49f, 0.78f));
        material.SetColor("_CoreColor", side == 0 ? new Color(1f, 0.94f, 0.84f) : new Color(0.86f, 0.97f, 1f));
        materials.Add(material);
        return material;
    }

    public void RefreshParameters()
    {
        if (!configured || style == null || styleRevision == style.Revision) return;
        CopyStyleGeometry();
        // Small meshes rebuild only when the asset changes, never on every hit/frame.
        {
            var body = new MeshBuilder();
            for (int lane = 0; lane < LaneCount; lane++)
            {
                float z = LaneCenter(lane), half = Mathf.Max(0.01f, laneSpacing * 0.5f - Mathf.Max(0f, style.segmentGap));
                body.Quad(-railWidth * 0.5f, railWidth * 0.5f, z - half, z + half);
                ReplaceMesh(railLights[lane], Quad(-railHaloHalfWidth, railHaloHalfWidth, z - half, z + half));
            }
            ReplaceMesh(idleRail, body.Build("V11_FourSegments"));
            var guides = new MeshBuilder();
            for (int edge = 0; edge < BoundaryCount; edge++)
            {
                float z = (edge - LaneCount * 0.5f) * laneSpacing;
                float width = Mathf.Max(0.001f, style.guideHalfWidth);
                guides.Quad(0f, 1f, z - width, z + width);
                ReplaceMesh(edges[edge], Quad(0f, 1f, z - boundaryHaloHalfWidth, z + boundaryHaloHalfWidth));
            }
            ReplaceMesh(idleGuides, guides.Build("V11_FiveBoundaries"));
        }
        foreach (var material in materials) style.ApplyTo(material, side);
        styleRevision = style.Revision;
        RefreshGeometry();
        RefreshLights();
    }

    private void CopyStyleGeometry()
    {
        railWidth = Mathf.Max(0.005f, style.railWidth);
        railHaloHalfWidth = Mathf.Max(0.01f, style.railHaloWidth);
        boundaryHaloHalfWidth = Mathf.Max(0.01f, style.edgeHaloWidth);
        hitReachFraction = Mathf.Clamp01(style.lightReach);
        guideReachFraction = Mathf.Clamp01(style.guideReach);
        hitDuration = Mathf.Max(0.01f, style.hitFadeDuration);
        releaseDuration = Mathf.Max(0.01f, style.releaseDuration);
    }

    private void ReplaceMesh(MeshRenderer renderer, Mesh replacement)
    {
        var filter = renderer.GetComponent<MeshFilter>();
        var previous = filter.sharedMesh;
        filter.sharedMesh = replacement;
        meshes.Remove(previous);
        meshes.Add(replacement);
        Release(previous);
    }

    private MeshRenderer Piece(string label, Mesh mesh, Material material, float height)
    {
        var go = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * height;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        meshes.Add(mesh);
        pieces.Add(go);
        return renderer;
    }

    private float LaneCenter(int lane) => (lane - (LaneCount - 1) * 0.5f) * laneSpacing;
    private static bool Valid(int lane) => lane >= 0 && lane < LaneCount;
    private static float Smooth(float value) => value * value * (3f - 2f * value);
    private static float SafeInverse(float value) => Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;

    private static Mesh Quad(float x0, float x1, float z0, float z1)
    {
        var builder = new MeshBuilder();
        builder.Quad(x0, x1, z0, z1);
        return builder.Build("V11_LightQuad");
    }

    private void OnDisable()
    {
        for (int i = 0; i < LaneCount; i++) { held[i] = false; press[i] = hit[i] = peakHold[i] = 0f; }
        foreach (var piece in pieces) if (piece != null) piece.SetActive(false);
        RestoreOriginals();
    }

    private void OnEnable()
    {
        if (!configured) return;
        foreach (var piece in pieces) if (piece != null) piece.SetActive(true);
        foreach (var original in originals) if (original != null) original.enabled = false;
        RefreshGeometry();
        RefreshLights();
    }

    private void RestoreOriginals()
    {
        for (int i = 0; i < originals.Count; i++)
            if (originals[i] != null) originals[i].enabled = originalEnabled[i];
    }

    private void OnDestroy()
    {
        RestoreOriginals();
        foreach (var mesh in meshes) Release(mesh);
        foreach (var material in materials) Release(material);
    }

    private static void Release(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
    }

    private sealed class MeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();
        public void Quad(float x0, float x1, float z0, float z1)
        {
            int n = vertices.Count;
            vertices.Add(new Vector3(x0, 0f, z0)); vertices.Add(new Vector3(x0, 0f, z1));
            vertices.Add(new Vector3(x1, 0f, z0)); vertices.Add(new Vector3(x1, 0f, z1));
            uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(0f, 1f));
            uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f));
            triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
            triangles.Add(n + 1); triangles.Add(n + 3); triangles.Add(n + 2);
        }
        public Mesh Build(string label)
        {
            var mesh = new Mesh { name = label };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
