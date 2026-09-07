using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

namespace MusicalSprite.Editor
{
    /// <summary>
    /// Ground tooling:
    /// 1) Create/refresh M_GroundEdge_Arena (rounded slab: red/blue blend + dirt sides + outward grass fringe).
    /// 2) Generate Procedural Arena Ground: builds a perfect rounded-rectangle slab
    ///    so the FBX seam/scale issues are gone.
    /// 3) Replace demo ground with the procedural slab.
    /// 4) Update Ground Textures: reload dimian_1_A/B and GroundEdge_Template.
    /// </summary>
    public static class GroundEdgeMaterialSetup
    {
        private const string GroundShaderName = "MusicalSprite/GroundEdge";
        private const string GroundShaderPath = "Assets/Shaders/GroundEdge.shader";

        private const string TexRed = "Assets/Art/dimian_1_A.png";
        private const string TexBlue = "Assets/Art/dimian_1_B.png";
        private const string TexEdge = "Assets/Art/Textures/GroundEdge_Template.png";
        private const string MatFolder = "Assets/Art/Materials";
        private const string GroundMatPath = "Assets/Art/Materials/M_GroundEdge_Arena.mat";
        private const string MeshFolder = "Assets/Art/Meshes";
        private const string ProceduralGroundMeshPath = "Assets/Art/Meshes/ArenaGround_Procedural.asset";

        // Inner ground bounds (the slab footprint). The grass fringe grows on this
        // perimeter; tweak these to match your desired arena size.
        private const float MinX = -8f;
        private const float MaxX = 8f;
        private const float MinY = -3.75f;
        private const float MaxY = 3.75f;

        // Rounded-corner radius of the slab. Set to 0 for sharp corners.
        private const float CornerRadius = 2.0f;

        // Slab thickness (side-wall drop). Raised from 0.15 so the dirt side is
        // visible under the game camera's angled top-down view.
        private const float GroundThickness = 0.8f;

        // Mesh tessellation of the rounded corners. More segments = smoother arc;
        // fewer segments = more visible "short straight lines" (COTL style).
        private const int CornerSegments = 8;

        // Straight-edge spacing (world units) between vertices on the slab perimeter.
        private const float EdgeSpacing = 0.25f;

        // Grass rim width. IMPORTANT: this is an EDGE EFFECT — the grass occupies the
        // OUTERMOST strip of the REAL ground and grows INWARD. It adds NO area, so the
        // playfield size is unchanged and the dirt wall sits at the real edge (nothing
        // sticks out). Keep this in sync with the material's _EdgeOutset.
        private const float GrassRimWidth = 0.35f;

        // Grass tip overhang beyond the real ground edge. Kept small: just enough
        // to cover the side-wall top seam so it never "pops out", without blocking
        // the side wall in the game camera.
        private const float GrassOverhang = 0.08f;

        [MenuItem("Tools/Musical-Sprite/Create GroundEdge Arena Material")]
        public static void CreateMaterial()
        {
            Shader groundShader = LoadShader(GroundShaderName, GroundShaderPath);
            if (groundShader == null) return;

            Texture2D texR = AssetDatabase.LoadAssetAtPath<Texture2D>(TexRed);
            Texture2D texB = AssetDatabase.LoadAssetAtPath<Texture2D>(TexBlue);
            Texture2D texE = AssetDatabase.LoadAssetAtPath<Texture2D>(TexEdge);
            if (texR == null || texB == null)
            {
                Debug.LogError($"[GroundEdge] Ground maps not found: {TexRed} / {TexBlue}");
                return;
            }

            if (!System.IO.Directory.Exists(MatFolder))
            {
                System.IO.Directory.CreateDirectory(MatFolder);
                AssetDatabase.Refresh();
            }

            // ---- Base ground material (slab + outward grass fringe in one shader) ----
            Material groundMat = EnsureMaterial(GroundMatPath, groundShader, "M_GroundEdge_Arena");
            groundMat.SetTexture("_RedMap", texR);
            groundMat.SetTexture("_BlueMap", texB);
            groundMat.SetTexture("_EdgeTex", texE);

            // Make sure the grass mask tiles continuously around the perimeter.
            EnsureEdgeTextureSettings(texE);
            groundMat.SetColor("_BaseColor", Color.white);
            groundMat.SetFloat("_CenterLineX", 0f);
            groundMat.SetFloat("_CenterBlend", 0.15f);
            groundMat.SetFloat("_EdgeOutset", GrassRimWidth);
            groundMat.SetFloat("_EdgeOverhang", GrassOverhang);
            groundMat.SetFloat("_EdgeTexTiling", 4.0f);
            groundMat.SetFloat("_EdgeVerticalScale", 1.0f);
            groundMat.SetFloat("_EdgeCutoff", 0.05f);
            groundMat.SetFloat("_EdgeBrightness", 1.05f);
            groundMat.SetFloat("_DebugMode", 0.0f); // OFF: shows real red/blue ground + grass mask
            groundMat.SetVector("_GroundMin", new Vector4(MinX, MinY, 0, 0));
            groundMat.SetVector("_GroundMax", new Vector4(MaxX, MaxY, 0, 0));
            groundMat.SetFloat("_CornerRadius", CornerRadius);
            groundMat.SetFloat("_GroundThickness", GroundThickness);
            groundMat.SetColor("_SideColor", new Color(0.45f, 0.32f, 0.22f, 1f));
            groundMat.SetColor("_BottomColor", new Color(0.30f, 0.20f, 0.15f, 1f));
            groundMat.SetVector("_SideLightDir", new Vector4(0.5f, 0.3f, 0.8f, 0f));

            AssetDatabase.SaveAssets();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = groundMat;
            Debug.Log("[GroundEdge] Created/refreshed M_GroundEdge_Arena (ground slab + outward grass fringe).");
        }

        [MenuItem("Tools/Musical-Sprite/Generate Procedural Arena Ground Mesh")]
        public static void GenerateProceduralArenaGroundMesh()
        {
            Mesh mesh = BuildProceduralGroundMesh(MinX, MaxX, MinY, MaxY, CornerRadius, GroundThickness, EdgeSpacing);
            if (!System.IO.Directory.Exists(MeshFolder))
            {
                System.IO.Directory.CreateDirectory(MeshFolder);
                AssetDatabase.Refresh();
            }

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(ProceduralGroundMeshPath);
            if (existing != null)
            {
                existing.Clear();
                existing.SetVertices(mesh.vertices);
                existing.SetNormals(mesh.normals);
                existing.SetUVs(0, mesh.uv);
                existing.SetUVs(1, mesh.uv2);
                existing.SetIndices(mesh.triangles, MeshTopology.Triangles, 0);
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                mesh = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, ProceduralGroundMeshPath);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GroundEdge] Generated procedural arena ground mesh: {mesh.vertexCount} verts, {mesh.triangles.Length / 3} tris, saved to {ProceduralGroundMeshPath}");
        }

        [MenuItem("Tools/Musical-Sprite/Replace Demo Ground With New Model")]
        public static void ReplaceGround()
        {
            Material groundMat = AssetDatabase.LoadAssetAtPath<Material>(GroundMatPath);
            if (groundMat == null)
            {
                Debug.Log("[GroundEdge] Materials not found, creating them first...");
                CreateMaterial();
                groundMat = AssetDatabase.LoadAssetAtPath<Material>(GroundMatPath);
                if (groundMat == null)
                {
                    Debug.LogError("[GroundEdge] Failed to create ground material.");
                    return;
                }
            }

            // Always regenerate the procedural mesh so shader/mesh changes are picked up.
            GenerateProceduralArenaGroundMesh();
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ProceduralGroundMeshPath);
            if (mesh == null)
            {
                Debug.LogError("[GroundEdge] Failed to create procedural ground mesh.");
                return;
            }

            // Refresh code-driven parameters without overwriting runtime values like _CenterLineX.
            Texture2D texE = AssetDatabase.LoadAssetAtPath<Texture2D>(TexEdge);
            EnsureEdgeTextureSettings(texE);

            groundMat.SetTexture("_EdgeTex", texE);
            groundMat.SetFloat("_EdgeOutset", GrassRimWidth);
            groundMat.SetFloat("_EdgeOverhang", GrassOverhang);
            groundMat.SetFloat("_GroundThickness", GroundThickness);
            groundMat.SetFloat("_EdgeTexTiling", 4.0f);
            groundMat.SetFloat("_EdgeCutoff", 0.05f);
            groundMat.SetFloat("_EdgeBrightness", 1.05f);
            EditorUtility.SetDirty(groundMat);

            foreach (string old in new[] { "ArenaLeft", "ArenaRight", "ArenaGround", "GrassFringe" })
            {
                GameObject o = GameObject.Find(old);
                if (o != null) Undo.DestroyObjectImmediate(o);
            }

            GameObject go = CreateProceduralGroundObject("ArenaGround", mesh, groundMat);
            HookCenterLine(groundMat);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[GroundEdge] Replaced demo ground with procedural rounded-rectangle slab (ground + grass fringe in one shader).");
        }

        [MenuItem("Tools/Musical-Sprite/Update Ground Textures")]
        public static void UpdateGroundTextures()
        {
            Material groundMat = AssetDatabase.LoadAssetAtPath<Material>(GroundMatPath);
            if (groundMat == null)
            {
                Debug.LogError("[GroundEdge] Please run 'Create GroundEdge Arena Material' first.");
                return;
            }

            Texture2D texR = AssetDatabase.LoadAssetAtPath<Texture2D>(TexRed);
            Texture2D texB = AssetDatabase.LoadAssetAtPath<Texture2D>(TexBlue);
            Texture2D texE = AssetDatabase.LoadAssetAtPath<Texture2D>(TexEdge);

            bool changed = false;
            if (texR != null) { groundMat.SetTexture("_RedMap", texR); changed = true; }
            if (texB != null) { groundMat.SetTexture("_BlueMap", texB); changed = true; }
            if (texE != null && groundMat.HasProperty("_EdgeTex")) { groundMat.SetTexture("_EdgeTex", texE); changed = true; }

            if (changed)
            {
                EditorUtility.SetDirty(groundMat);
                AssetDatabase.SaveAssets();
                Debug.Log("[GroundEdge] Ground textures updated.");
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------
        private static Shader LoadShader(string name, string path)
        {
            Shader s = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (s == null) s = Shader.Find(name);
            if (s == null)
            {
                Debug.LogError($"[GroundEdge] Shader '{name}' not found at '{path}'. Fix compile errors first.");
                return null;
            }
            if (s.passCount <= 0)
            {
                Debug.LogError($"[GroundEdge] Shader '{name}' has no passes (compile failed).");
                return null;
            }
            return s;
        }

        private static Material EnsureMaterial(string path, Shader shader, string name)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                mat.name = name;
                AssetDatabase.CreateAsset(mat, AssetDatabase.GenerateUniqueAssetPath(path));
            }
            else
            {
                mat.shader = shader;
            }
            return mat;
        }

        private static void EnsureEdgeTextureSettings(Texture2D texE)
        {
            if (texE == null) return;
            string edgePath = AssetDatabase.GetAssetPath(texE);
            TextureImporter ti = AssetImporter.GetAtPath(edgePath) as TextureImporter;
            if (ti == null) return;

            bool changed = false;
            if (ti.wrapModeU != TextureWrapMode.Repeat) { ti.wrapModeU = TextureWrapMode.Repeat; changed = true; }
            if (ti.wrapModeV != TextureWrapMode.Repeat) { ti.wrapModeV = TextureWrapMode.Repeat; changed = true; }
            if (ti.wrapModeW != TextureWrapMode.Repeat) { ti.wrapModeW = TextureWrapMode.Repeat; changed = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
            if (changed)
            {
                ti.SaveAndReimport();
                Debug.Log($"[GroundEdge] Set {edgePath} to Repeat + alphaIsTransparency.");
            }
        }

        private static void HookCenterLine(Material groundMat)
        {
            BattleCenterLine bcl = Object.FindObjectOfType<BattleCenterLine>();
            if (bcl == null) return;
            Undo.RecordObject(bcl, "Hook up ground material");

            if (groundMat != null)
            {
                bcl.groundMaterial = groundMat;
                GameObject g = GameObject.Find("ArenaGround");
                if (g != null) bcl.ground = g.transform;
            }
            EditorUtility.SetDirty(bcl);
        }

        private static GameObject CreateProceduralGroundObject(string name, Mesh mesh, Material mat)
        {
            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3((MinX + MaxX) * 0.5f, -GroundThickness * 0.5f, (MinY + MaxY) * 0.5f);
            bc.size = new Vector3(MaxX - MinX, GroundThickness, MaxY - MinY);

            Debug.Log($"[GroundEdge] {name}: procedural slab bounds={mesh.bounds}, thickness={GroundThickness}");
            return go;
        }

        // ----------------------------------------------------------------
        // Procedural rounded-rectangle slab mesh
        //
        // Three distinct parts so the fragment shader never confuses the inner
        // ground with the grass fringe:
        //   1) Inner top face   : centre + inner-profile fan (uv2.y = 0/1)
        //   2) Grass edge ring  : quads from inner profile to outer profile
        //                         (uv2.y = 1). The outward offset is baked here.
        //   3) Side dirt walls  : outer profile pulled down (uv2.y = 2)
        // ----------------------------------------------------------------
        private static Mesh BuildProceduralGroundMesh(float minX, float maxX, float minY, float maxY, float radius, float thickness, float edgeSpacing)
        {
            // 1) Inner profile of the top face.
            List<Vector2> innerProfile = BuildRoundedRectPath(minX, maxX, minY, maxY, radius, edgeSpacing, CornerSegments);
            int n = innerProfile.Count;

            // Pre-compute normalised perimeter coordinate for every profile point.
            float[] edgeUs = new float[n];
            float totalLen = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                totalLen += Vector2.Distance(innerProfile[i], innerProfile[j]);
            }
            float accum = 0f;
            for (int i = 0; i < n; i++)
            {
                edgeUs[i] = (totalLen > 0.0001f) ? (accum / totalLen) : 0f;
                int j = (i + 1) % n;
                accum += Vector2.Distance(innerProfile[i], innerProfile[j]);
            }

            // Pre-compute a smooth outward normal for every profile point.
            // Profile is CCW, so the outward normal of a segment is (t.y, -t.x).
            Vector2[] outwardNormals = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                int next = (i + 1) % n;
                Vector2 tPrev = (innerProfile[i] - innerProfile[prev]).normalized;
                Vector2 tNext = (innerProfile[next] - innerProfile[i]).normalized;
                Vector2 nPrev = new Vector2(tPrev.y, -tPrev.x);
                Vector2 nNext = new Vector2(tNext.y, -tNext.x);
                outwardNormals[i] = ((nPrev + nNext) * 0.5f).normalized;
            }

            // Grass rim is an EDGE EFFECT: it occupies the outermost strip of the
            // REAL ground (innerProfile), growing INWARD by GrassRimWidth. No new
            // area is added and the dirt wall sits at the real edge (innerProfile),
            // so nothing "sticks out".
            List<Vector2> fieldProfile = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
                fieldProfile.Add(innerProfile[i] - outwardNormals[i] * GrassRimWidth);

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector2> uv2s = new List<Vector2>();
            List<int> indices = new List<int>();

            // 2) Inner top face: centre point + inner-profile fan.
            int centerIdx = verts.Count;
            verts.Add(new Vector3((minX + maxX) * 0.5f, 0f, (minY + maxY) * 0.5f));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));
            uv2s.Add(new Vector2(0.5f, 0f)); // interior point, not an edge

            int innerStart = verts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = fieldProfile[i];
                verts.Add(new Vector3(p.x, 0f, p.y));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0f, 0f));
                uv2s.Add(new Vector2(edgeUs[i], 1f)); // inner-field boundary (inner edge of rim)
            }

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                // Profile is CCW; in Unity's left-handed coordinate system the
                // visible face needs clockwise winding when viewed from above.
                indices.Add(centerIdx);
                indices.Add(innerStart + next);
                indices.Add(innerStart + i);
            }

            // 3) Grass edge rim: a quad strip occupying the OUTERMOST strip of the real
            //    ground — fieldProfile (inner edge, meets the red/blue field) out to
            //    innerProfile (outer edge = the real ground boundary). No geometry is
            //    created beyond the real ground, so no area is added.
            //
            // Duplicate the first point at the end with U += 1 so the UV wrap-around
            // seam does not compress the grass tiling into a tiny closing segment.
            float wrapU = edgeUs[0] + 1.0f;

            int rimInnerStart = verts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = fieldProfile[i];
                verts.Add(new Vector3(p.x, 0f, p.y));
                normals.Add(Vector3.up);
                uvs.Add(Vector2.zero);
                uv2s.Add(new Vector2(edgeUs[i], 1f)); // inner edge of rim
            }
            // Duplicate first inner rim point to close the UV loop cleanly.
            {
                Vector2 p = fieldProfile[0];
                verts.Add(new Vector3(p.x, 0f, p.y));
                normals.Add(Vector3.up);
                uvs.Add(Vector2.zero);
                uv2s.Add(new Vector2(wrapU, 1f));
            }

            int rimOuterStart = verts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = innerProfile[i] + outwardNormals[i] * GrassOverhang;
                verts.Add(new Vector3(p.x, 0f, p.y));
                normals.Add(Vector3.up);
                uvs.Add(Vector2.zero);
                uv2s.Add(new Vector2(edgeUs[i], 1f)); // outer edge of rim (overhangs real ground edge)
            }
            // Duplicate first outer rim point to close the UV loop cleanly.
            {
                Vector2 p = innerProfile[0] + outwardNormals[0] * GrassOverhang;
                verts.Add(new Vector3(p.x, 0f, p.y));
                normals.Add(Vector3.up);
                uvs.Add(Vector2.zero);
                uv2s.Add(new Vector2(wrapU, 1f));
            }

            for (int i = 0; i < n; i++)
            {
                int j = i + 1; // no modulo needed: vertex n is the duplicate of vertex 0
                // The rim must face UP. In Unity's left-handed coordinate system
                // the visible (front-facing) winding for a CCW profile is:
                // inner_i -> inner_j -> outer_j -> outer_i when viewed from above.
                indices.Add(rimInnerStart + i);
                indices.Add(rimInnerStart + j);
                indices.Add(rimOuterStart + j);

                indices.Add(rimInnerStart + i);
                indices.Add(rimOuterStart + j);
                indices.Add(rimOuterStart + i);
            }

            // 4) Side dirt walls: built at the REAL ground edge (innerProfile) and
            //    pulled straight down. This is directly under the ground edge, so the
            //    brown dirt no longer "sticks out" beyond the playfield.
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector2 pA = innerProfile[i];
                Vector2 pB = innerProfile[j];
                Vector2 nA = outwardNormals[i];
                Vector2 nB = outwardNormals[j];

                int baseIdx = verts.Count;
                verts.Add(new Vector3(pA.x, 0f, pA.y));
                normals.Add(new Vector3(nA.x, 0f, nA.y));
                uvs.Add(Vector2.zero);
                uv2s.Add(new Vector2(edgeUs[i], 2f));

                verts.Add(new Vector3(pB.x, 0f, pB.y));
                normals.Add(new Vector3(nB.x, 0f, nB.y));
                uvs.Add(Vector2.zero);
                uv2s.Add(new Vector2(edgeUs[j], 2f));

                verts.Add(new Vector3(pA.x, -thickness, pA.y));
                normals.Add(new Vector3(nA.x, 0f, nA.y));
                uvs.Add(Vector2.zero);
                uv2s.Add(new Vector2(edgeUs[i], 2f));

                verts.Add(new Vector3(pB.x, -thickness, pB.y));
                normals.Add(new Vector3(nB.x, 0f, nB.y));
                uvs.Add(Vector2.zero);
                uv2s.Add(new Vector2(edgeUs[j], 2f));

                // Outward-facing triangles for a CCW profile.
                indices.Add(baseIdx + 0);
                indices.Add(baseIdx + 1);
                indices.Add(baseIdx + 2);

                indices.Add(baseIdx + 1);
                indices.Add(baseIdx + 3);
                indices.Add(baseIdx + 2);
            }

            Mesh mesh = new Mesh();
            mesh.name = "ArenaGround_Procedural";
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uv2s);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
            // Do NOT call RecalculateNormals(); we set them manually.
            Debug.Log($"[GroundEdge] Built arena mesh: {verts.Count} verts, {indices.Count / 3} tris, bounds={mesh.bounds}");
            return mesh;
        }

        // ----------------------------------------------------------------
        // Perimeter path for the procedural slab: straight edges split by edgeSpacing,
        // corners split into CornerSegments short straight pieces so the arc
        // reads as "short straight lines forming a curve" (COTL style).
        // ----------------------------------------------------------------
        private static List<Vector2> BuildRoundedRectPath(float minX, float maxX, float minY, float maxY, float r, float edgeSpacing, int cornerSegments)
        {
            var pts = new List<Vector2>();
            r = Mathf.Min(r, (maxX - minX) * 0.5f, (maxY - minY) * 0.5f);
            r = Mathf.Max(0f, r);

            AddLineByLength(pts, new Vector2(minX + r, minY), new Vector2(maxX - r, minY), edgeSpacing);
            AddArcBySegments(pts, new Vector2(maxX - r, minY + r), r, -90f, 0f, cornerSegments);
            AddLineByLength(pts, new Vector2(maxX, minY + r), new Vector2(maxX, maxY - r), edgeSpacing);
            AddArcBySegments(pts, new Vector2(maxX - r, maxY - r), r, 0f, 90f, cornerSegments);
            AddLineByLength(pts, new Vector2(maxX - r, maxY), new Vector2(minX + r, maxY), edgeSpacing);
            AddArcBySegments(pts, new Vector2(minX + r, maxY - r), r, 90f, 180f, cornerSegments);
            AddLineByLength(pts, new Vector2(minX, maxY - r), new Vector2(minX, minY + r), edgeSpacing);
            AddArcBySegments(pts, new Vector2(minX + r, minY + r), r, 180f, 270f, cornerSegments);

            // Remove consecutive (and wrap-around) duplicate points so no zero-length segment is created.
            var clean = new List<Vector2>();
            for (int i = 0; i < pts.Count; i++)
            {
                if (clean.Count > 0 && Vector2.Distance(clean[clean.Count - 1], pts[i]) < 1e-4f) continue;
                clean.Add(pts[i]);
            }
            if (clean.Count > 1 && Vector2.Distance(clean[clean.Count - 1], clean[0]) < 1e-4f)
                clean.RemoveAt(clean.Count - 1);
            return clean;
        }

        private static void AddLineByLength(List<Vector2> pts, Vector2 a, Vector2 b, float spacing)
        {
            float d = Vector2.Distance(a, b);
            int n = Mathf.Max(1, Mathf.RoundToInt(d / spacing));
            for (int i = 0; i <= n; i++) pts.Add(Vector2.Lerp(a, b, (float)i / n));
        }

        private static void AddArcBySegments(List<Vector2> pts, Vector2 center, float radius, float a0deg, float a1deg, int segments)
        {
            int n = Mathf.Max(1, segments);
            for (int i = 0; i <= n; i++)
            {
                float ang = Mathf.Lerp(a0deg, a1deg, (float)i / n) * Mathf.PI / 180f;
                pts.Add(center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius);
            }
        }
    }
}
