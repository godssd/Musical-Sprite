using System.Collections.Generic;
using UnityEngine;

/// <summary>VFXSandbox trial only. WALL.fbx remains the structural wall;
/// the blades, wake and chips are separate painted meshes.</summary>
[ExecuteAlways]
[RequireComponent(typeof(ShockwavePreview))]
public sealed class SandboxShockwaveArt : MonoBehaviour
{
    public ShockwavePreview preview;
    public Material accentMaterial;
    [Range(0f, 1f)] public float structuralEdge = 0.72f;
    [Range(0f, 1f)] public float shortWake = 0.44f;
    [Range(0f, 1f)] public float rearGlow = 0.20f;
    [Range(0f, 1f)] public float shards = 0.62f;
    public float shardTravelSpeed = 0.54f;

    private readonly GameObject[,] parts = new GameObject[2, 3];
    private readonly Mesh[,] meshes = new Mesh[2, 3];
    private static bool IsTrial(GameObject go) => go.scene.name.StartsWith("VFXSandbox");

    private void OnEnable()
    {
        if (!IsTrial(gameObject)) return;
        if (preview == null) preview = GetComponent<ShockwavePreview>();
        EnsureParts();
    }
    private void OnDisable() { Release(); }
    private void OnDestroy() { Release(); }
    private static void Dispose(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
    }
    private void Release()
    {
        for (int s = 0; s < 2; s++)
        for (int l = 0; l < 3; l++)
        {
            Dispose(parts[s, l]);
            Dispose(meshes[s, l]);
            parts[s, l] = null;
            meshes[s, l] = null;
        }
    }
    private void EnsureParts()
    {
        if (preview == null || accentMaterial == null) return;
        string[] layerNames = { "EnergyBlades", "ShortWake", "SurfaceMarks" };
        for (int s = 0; s < 2; s++)
        for (int l = 0; l < 3; l++)
        {
            if (parts[s, l] != null) continue;
            string name = "Painted" + (s == 0 ? "Red" : "Blue") + "_" + layerNames[l];
            var go = new GameObject(name);
            go.hideFlags = HideFlags.DontSave;
            var mesh = new Mesh { name = name + "Mesh", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = accentMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = l == 0 ? 19 : l == 1 ? 21 : 23;
            parts[s, l] = go;
            meshes[s, l] = mesh;
        }
    }
    private void LateUpdate()
    {
        if (!IsTrial(gameObject) || preview == null || accentMaterial == null) return;
        EnsureParts();
        float time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        BuildSide(0, preview.redWall, -1f, time);
        BuildSide(1, preview.blueWall, 1f, time);
    }
    private static float Curl(float u)
    {
        float k = Mathf.Clamp01((Mathf.Abs(u) - 0.62f) / 0.38f);
        return k * k * (3f - 2f * k);
    }
    private static Vector3 OnWall(Transform wall, Bounds b, float u, float height)
    {
        float x = Mathf.Lerp(b.min.x, b.max.x, Mathf.Lerp(0.47f, 0.02f, Curl(u)));
        return wall.TransformPoint(new Vector3(x, b.center.y + b.extents.y * u * 0.97f,
            Mathf.Lerp(b.min.z, b.max.z, height)));
    }
    private sealed class Shape
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Color> c = new List<Color>();
        public readonly List<int> t = new List<int>();
        public void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e,
            Color ca, Color cb, Color cd, Color ce)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(d); v.Add(e);
            c.Add(ca); c.Add(cb); c.Add(cd); c.Add(ce);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i + 2); t.Add(i + 1); t.Add(i + 3);
        }
        public void Stroke(Vector3 a, Vector3 b, float wa, float wb, Color ca, Color cb)
        {
            Vector3 across = Vector3.Cross(Vector3.up, b - a).normalized;
            if (across.sqrMagnitude < 0.01f) across = Vector3.right;
            Quad(a - across * wa, a + across * wa, b - across * wb, b + across * wb,
                ca, ca, cb, cb);
        }
        public void Ribbon(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            float width, Color left, Color right)
        {
            const int steps = 16;
            Vector3 priorLeft = Vector3.zero, priorRight = Vector3.zero;
            for (int i = 0; i <= steps; i++)
            {
                float f = i / (float)steps;
                float inv = 1f - f;
                Vector3 point = inv*inv*inv*p0 + 3f*inv*inv*f*p1
                              + 3f*inv*f*f*p2 + f*f*f*p3;
                Vector3 tangent = 3f*inv*inv*(p1-p0) + 6f*inv*f*(p2-p1)
                                + 3f*f*f*(p3-p2);
                Vector3 across = Vector3.Cross(Vector3.up,tangent).normalized;
                float edge = width*Mathf.Pow(Mathf.Sin(Mathf.PI*f),.75f);
                Vector3 a = point-across*edge*.75f;
                Vector3 b = point+across*edge;
                if (i > 0)
                {
                    Color ca = left, cb = right;
                    float fade = Mathf.SmoothStep(0f,1f,Mathf.Min(f*5f,(1f-f)*5f));
                    ca.a *= fade; cb.a *= fade;
                    Quad(priorLeft,priorRight,a,b,ca,cb,ca,cb);
                }
                priorLeft = a; priorRight = b;
            }
        }
        public void Apply(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
        }
    }
    private void BuildSide(int index, Transform wall, float side, float time)
    {
        MeshFilter filter = wall == null ? null : wall.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
        {
            for (int l = 0; l < 3; l++) if (meshes[index, l] != null) meshes[index, l].Clear();
            return;
        }
        Bounds b = filter.sharedMesh.bounds;
        var blades = new Shape();
        var wake = new Shape();
        var marks = new Shape();
        Blades(blades, wall, b, side, time);
        Wake(wake, wall, b, side, time);
        Marks(marks, wall, b, side, time);
        blades.Apply(meshes[index, 0]);
        wake.Apply(meshes[index, 1]);
        marks.Apply(meshes[index, 2]);
    }
    private void Blades(Shape shape, Transform wall, Bounds b, float side, float time)
    {
        bool red = side < 0f;
        Color shadow = red ? new Color(.39f,.055f,.085f,.70f) : new Color(.035f,.10f,.41f,.72f);
        Color body = red ? new Color(.91f,.18f,.12f,.76f) : new Color(.10f,.32f,.94f,.78f);
        Color inner = red ? new Color(1.18f,.53f,.31f,.62f) : new Color(.34f,.72f,1.16f,.62f);
        for (int end = 0; end < 2; end++)
        {
            float wing = end == 0 ? -1f : 1f;
            Vector3 root = OnWall(wall,b,wing*.52f,.48f);
            Vector3 shoulder = OnWall(wall,b,wing*.73f,.50f)+Vector3.right*side*.12f;
            Vector3 bend = OnWall(wall,b,wing*.91f,.54f)+Vector3.right*side*.28f;
            Vector3 tip = OnWall(wall,b,wing*.99f,.57f)
                        + Vector3.forward*wing*.39f + Vector3.right*side*.36f;
            float pulse = 1f+.03f*Mathf.Sin(time*3.2f+wing);
            shape.Ribbon(root,shoulder,bend,tip,.31f*pulse,shadow,body);
            // One nested paint stroke suggests volume; it stays inside the broad sweep.
            shape.Ribbon(root+Vector3.right*side*.045f,
                shoulder+Vector3.right*side*.10f,
                bend+Vector3.right*side*.05f,
                tip-Vector3.forward*wing*.09f,.095f,body,inner);
        }
    }
    private void Wake(Shape shape, Transform wall, Bounds b, float side, float time)
    {
        bool red = side < 0f;
        Color dark = red ? new Color(.65f,.075f,.10f,shortWake*.57f)
                         : new Color(.035f,.13f,.67f,shortWake*.60f);
        Color mid = red ? new Color(1.03f,.26f,.16f,shortWake*.53f)
                        : new Color(.13f,.40f,1.04f,shortWake*.55f);
        Color light = red ? new Color(1.24f,.54f,.31f,shortWake*.37f)
                          : new Color(.36f,.76f,1.22f,shortWake*.38f);
        for (int layer = 0; layer < 3; layer++)
        {
            float span = layer == 0 ? -.16f : layer == 1 ? .02f : .17f;
            float len = layer == 0 ? .54f : layer == 1 ? .64f : .43f;
            Vector3 a = OnWall(wall,b,span,.10f)+Vector3.right*side*.09f;
            Vector3 d = a+Vector3.right*side*(len+.025f*Mathf.Sin(time*3.2f+layer));
            Color col = layer == 0 ? dark : layer == 1 ? mid : light;
            Color fade = col; fade.a = 0f;
            shape.Stroke(a,d,layer == 1 ? .20f : .15f,.005f,col,fade);
        }
        Color wash = red ? new Color(1f,.19f,.08f,rearGlow*.31f)
                         : new Color(.10f,.34f,1f,rearGlow*.31f);
        Vector3 g0 = OnWall(wall,b,-.35f,.01f);
        Vector3 g1 = OnWall(wall,b,.35f,.01f);
        g0.y = g1.y = .045f;
        Color clear = wash; clear.a = 0f;
        shape.Quad(g0,g1,g0+Vector3.right*side*.60f,
            g1+Vector3.right*side*.60f,wash,wash,clear,clear);
    }
    private void Marks(Shape shape, Transform wall, Bounds b, float side, float time)
    {
        bool red = side < 0f;
        Color dark = red ? new Color(.52f,.08f,.11f,structuralEdge*.58f)
                         : new Color(.04f,.13f,.56f,structuralEdge*.61f);
        Color light = red ? new Color(1.17f,.75f,.39f,structuralEdge*.63f)
                          : new Color(.43f,.90f,1.30f,structuralEdge*.63f);
        float[] ranges = { -.98f,-.72f,-.37f,-.10f,.18f,.44f,.70f,.97f };
        for (int k = 0; k < 4; k++)
        for (int i = 0; i < 6; i++)
        {
            float u0 = Mathf.Lerp(ranges[k*2],ranges[k*2+1],i/6f);
            float u1 = Mathf.Lerp(ranges[k*2],ranges[k*2+1],(i+1)/6f);
            Vector3 a = OnWall(wall,b,u0,.91f)+Vector3.right*side*.018f;
            Vector3 d = OnWall(wall,b,u1,.91f)+Vector3.right*side*.018f;
            float taper0 = Mathf.Sin(Mathf.PI*i/6f);
            float taper1 = Mathf.Sin(Mathf.PI*(i+1)/6f);
            shape.Stroke(a,d,.012f+.022f*taper0,.012f+.022f*taper1,
                k%2 == 0 ? light : dark,light);
        }
        for (int i = 0; i < 6; i++)
        {
            float age = Mathf.Repeat(time*shardTravelSpeed*.65f+i*.177f
                                    +(red ? 0f : .11f),1f);
            float wing = (i&1) == 0 ? -1f : 1f;
            float u = wing*Mathf.Lerp(.35f,.99f,Mathf.Min(age*1.18f,1f));
            Vector3 center = OnWall(wall,b,u,.63f)
                           + Vector3.right*side*(.035f+.32f*age*age)
                           + Vector3.up*(.045f+.12f*age)
                           + Vector3.forward*wing*(.32f*age*age);
            Vector3 travel = Vector3.forward*wing*(.16f-age*.07f);
            Vector3 width = Vector3.right*side*(.10f-age*.045f);
            Color chip = red ? new Color(1.29f,.39f,.18f,shards)
                             : new Color(.23f,.61f,1.28f,shards);
            chip.a *= Mathf.SmoothStep(0f,1f,Mathf.Min(age*7f,(1f-age)*4f));
            Color chipTip = light; chipTip.a = chip.a*.65f;
            shape.Quad(center-travel,center+width,
                center+travel*.8f,center-width*.34f,
                chip,chipTip,chipTip,chip);
        }
    }
}
