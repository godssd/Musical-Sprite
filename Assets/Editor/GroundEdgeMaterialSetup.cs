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
            // The material is the SINGLE SOURCE OF TRUTH for all geometry-affecting
            // parameters. Bake the mesh from its CURRENT values so re-running the tool
            // never loses the user's tuned Arena parameters. Fall back to code constants
            // only when the material (or a property) does not exist yet.
            float minX = MinX, maxX = MaxX, minY = MinY, maxY = MaxY;
            float radius = CornerRadius, thickness = GroundThickness;
            float edgeOutset = GrassRimWidth, edgeOverhang = GrassOverhang;

            Material groundMat = AssetDatabase.LoadAssetAtPath<Material>(GroundMatPath);
            if (groundMat != null)
            {
                if (groundMat.HasProperty("_GroundMin"))   { Vector4 g = groundMat.GetVector("_GroundMin");   minX = g.x; minY = g.y; }
                if (groundMat.HasProperty("_GroundMax"))   { Vector4 g = groundMat.GetVector("_GroundMax");   maxX = g.x; maxY = g.y; }
                if (groundMat.HasProperty("_CornerRadius"))     radius    = groundMat.GetFloat("_CornerRadius");
                if (groundMat.HasProperty("_GroundThickness"))  thickness = groundMat.GetFloat("_GroundThickness");
                if (groundMat.HasProperty("_EdgeOutset"))       edgeOutset = groundMat.GetFloat("_EdgeOutset");
                if (groundMat.HasProperty("_EdgeOverhang"))     edgeOverhang = groundMat.GetFloat("_EdgeOverhang");
            }

            Mesh mesh = BuildProceduralGroundMesh(minX, maxX, minY, maxY, radius, thickness, EdgeSpacing, edgeOutset, edgeOverhang);
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
            // The mesh bakes from the CURRENT material values (single source of truth),
            // so your tuned Arena parameters are preserved — we never overwrite them here.
            GenerateProceduralArenaGroundMesh();
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ProceduralGroundMeshPath);
            if (mesh == null)
            {
                Debug.LogError("[GroundEdge] Failed to create procedural ground mesh.");
                return;
            }

            // Only re-point the edge mask texture (import settings + reference).
            // We deliberately do NOT reset any numeric material parameters here.
            Texture2D texE = AssetDatabase.LoadAssetAtPath<Texture2D>(TexEdge);
            EnsureEdgeTextureSettings(texE);
            groundMat.SetTexture("_EdgeTex", texE);
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
        // Stage disc overhang lip
        //
        // Fixes two reported issues at the stage<->ground boundary:
        //   1) The stage side wall "looks recessed / hollow": the disc mesh ends
        //      exactly at r = _DiscRadius (1.2) with NO outward grass fringe, so the
        //      dirt side wall pokes through at the top. The main ground avoids this
        //      because its _EdgeOverhang = 0.08 builds a real overhang ring that caps
        //      the wall top.
        //   2) The black seam line: the rect ground is clipped to
        //      sqrt(clipR^2 + EdgeOutset^2) ~= 1.222 around the stage, but the disc
        //      only reaches r = 1.2, so the ring r in [1.2, 1.222] has neither disc
        //      nor ground -> background shows through as a black line.
        //
        // We add a thin outward grass lip (r 1.2 -> 1.2+overhang) as a CHILD of each
        // stage, rendered with the SAME stage material. The shader computes edgeDist
        // in world space, so the lip is automatically drawn as the grass overhang and
        // covers both the seam and the wall-top gap. No shader change, no grass-logic
        // change — we only use the existing, already-tuned overhang path.
        //
        // Revert: run "Remove Stage Overhang Lips" (or delete the "StageOverhangLip"
        // children + set _EdgeOverhang back to 0 on both stage materials).
        // ----------------------------------------------------------------
        private const string StageLipMeshPath = "Assets/Art/Meshes/StageOverhangLip.asset";
        private const int StageLipSegments = 24;

        [MenuItem("Tools/Musical-Sprite/Add Stage Overhang Lips")]
        public static void AddStageOverhangLips()
        {
            Material matA = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/M_GroundEdge_Stage_A.mat");
            Material matB = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/M_GroundEdge_Stage_B.mat");
            if (matA == null || matB == null)
            {
                Debug.LogError("[GroundEdge] Stage materials not found.");
                return;
            }

            // Overhang amount comes from the material (single source of truth), exactly
            // like the main-ground generator reads _EdgeOverhang. First run: enable it.
            float overhang = matA.GetFloat("_EdgeOverhang");
            if (overhang < 0.001f) overhang = 0.08f;

            float discRadius = 1.2f;
            float topY = 0.15f; // local top height of the disc mesh (world = *4 via scale)
            Mesh lip = BuildStageOverhangLipMesh(discRadius, overhang, topY, StageLipSegments);

            if (!System.IO.Directory.Exists(MeshFolder))
                System.IO.Directory.CreateDirectory(MeshFolder);
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(StageLipMeshPath);
            if (existing != null)
            {
                existing.Clear();
                existing.SetVertices(lip.vertices);
                existing.SetNormals(lip.normals);
                existing.SetUVs(0, lip.uv);
                existing.SetIndices(lip.triangles, MeshTopology.Triangles, 0);
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                lip = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(lip, StageLipMeshPath);
            }
            AssetDatabase.SaveAssets();

            AddLipToStage("LeftBand_Stage", lip, matA);
            AddLipToStage("RightBand_Stage", lip, matB);

            // Commit the overhang amount so the shader renders to the lip's outer edge
            // (clip threshold = _EdgeOverhang).
            matA.SetFloat("_EdgeOverhang", overhang);
            matB.SetFloat("_EdgeOverhang", overhang);
            EditorUtility.SetDirty(matA);
            EditorUtility.SetDirty(matB);
            AssetDatabase.SaveAssets();

            Debug.Log($"[GroundEdge] Added stage overhang lips (discRadius={discRadius}, overhang={overhang}). " +
                      "Revert: 'Remove Stage Overhang Lips' menu, or delete StageOverhangLip children + _EdgeOverhang=0.");
        }

        [MenuItem("Tools/Musical-Sprite/Remove Stage Overhang Lips")]
        public static void RemoveStageOverhangLips()
        {
            RemoveLipFromStage("LeftBand_Stage");
            RemoveLipFromStage("RightBand_Stage");
            Material matA = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/M_GroundEdge_Stage_A.mat");
            Material matB = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/M_GroundEdge_Stage_B.mat");
            if (matA != null) { matA.SetFloat("_EdgeOverhang", 0f); EditorUtility.SetDirty(matA); }
            if (matB != null) { matB.SetFloat("_EdgeOverhang", 0f); EditorUtility.SetDirty(matB); }
            AssetDatabase.SaveAssets();
            Debug.Log("[GroundEdge] Removed stage overhang lips and reset _EdgeOverhang to 0.");
        }

        private static void AddLipToStage(string stageName, Mesh lip, Material mat)
        {
            GameObject stage = GameObject.Find(stageName);
            if (stage == null) { Debug.LogError($"[GroundEdge] {stageName} not found in scene."); return; }
            Transform existingChild = stage.transform.Find("StageOverhangLip");
            if (existingChild != null) Object.DestroyImmediate(existingChild.gameObject);

            GameObject lipGO = new GameObject("StageOverhangLip");
            lipGO.transform.SetParent(stage.transform, false);
            lipGO.transform.localPosition = Vector3.zero;
            lipGO.transform.localRotation = Quaternion.identity;
            lipGO.transform.localScale = Vector3.one;

            MeshFilter mf = lipGO.AddComponent<MeshFilter>();
            mf.sharedMesh = lip;
            MeshRenderer mr = lipGO.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            EditorUtility.SetDirty(stage);
        }

        private static void RemoveLipFromStage(string stageName)
        {
            GameObject stage = GameObject.Find(stageName);
            if (stage == null) return;
            Transform child = stage.transform.Find("StageOverhangLip");
            if (child != null) Object.DestroyImmediate(child.gameObject);
        }

        // Half-disc lip (x >= 0 local half), matching the embedded HalfCylinder's curved
        // edge. Inner edge sits exactly on the disc boundary (r = discRadius, y = topY);
        // outer edge overhangs by `overhang`. Flat at top height so it caps the wall top
        // exactly like the main ground's overhang ring (which is also flat, not drooped).
        private static Mesh BuildStageOverhangLipMesh(float discRadius, float overhang, float topY, int segments)
        {
            float rIn = discRadius;
            float rOut = discRadius + overhang;

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> indices = new List<int>();

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float ang = -Mathf.PI * 0.5f + Mathf.PI * t; // -90deg .. +90deg
                float cx = Mathf.Cos(ang);
                float sz = Mathf.Sin(ang);
                verts.Add(new Vector3(rIn * cx, topY, rIn * sz));
                verts.Add(new Vector3(rOut * cx, topY, rOut * sz));
                normals.Add(Vector3.up);
                normals.Add(Vector3.up);
                uvs.Add(Vector2.zero);
                uvs.Add(Vector2.zero);
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;           // inner i
                int b = i * 2 + 1;       // outer i
                int c = (i + 1) * 2;     // inner i+1
                int d = (i + 1) * 2 + 1; // outer i+1
                // Cull Off in the shader, so winding is irrelevant for visibility.
                indices.Add(a); indices.Add(c); indices.Add(b);
                indices.Add(b); indices.Add(c); indices.Add(d);
            }
            Mesh m = new Mesh();
            m.name = "StageOverhangLip";
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.SetIndices(indices, MeshTopology.Triangles, 0);
            m.RecalculateBounds();
            return m;
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
            float thickness = (mat != null && mat.HasProperty("_GroundThickness")) ? mat.GetFloat("_GroundThickness") : GroundThickness;
            bc.center = new Vector3((MinX + MaxX) * 0.5f, -thickness * 0.5f, (MinY + MaxY) * 0.5f);
            bc.size = new Vector3(MaxX - MinX, thickness, MaxY - MinY);

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
        private static Mesh BuildProceduralGroundMesh(float minX, float maxX, float minY, float maxY, float radius, float thickness, float edgeSpacing, float edgeOutset, float edgeOverhang)
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
                fieldProfile.Add(innerProfile[i] - outwardNormals[i] * edgeOutset);

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector2> uv2s = new List<Vector2>();
            List<int> indices = new List<int>();

            // 2) Inner top face: centre point + inner-profile fan.
            // wrapU: first profile point duplicated at the end with U += 1, so the
            // closing fan triangle interpolates U continuously (0.99 -> 1.0) instead
            // of jumping across the seam (0.99 -> 0.0), which used to smear the
            // grass-edge texture along one triangle (the stray black bar).
            float wrapU = edgeUs[0] + 1.0f;

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
            // Duplicate first inner-field boundary point to close the fan UV loop.
            {
                Vector2 p = fieldProfile[0];
                verts.Add(new Vector3(p.x, 0f, p.y));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0f, 0f));
                uv2s.Add(new Vector2(wrapU, 1f));
            }

            for (int i = 0; i < n; i++)
            {
                int next = i + 1; // no modulo: vertex n is the duplicate of vertex 0
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
                Vector2 p = innerProfile[i] + outwardNormals[i] * edgeOverhang;
                verts.Add(new Vector3(p.x, 0f, p.y));
                normals.Add(Vector3.up);
                uvs.Add(Vector2.zero);
                uv2s.Add(new Vector2(edgeUs[i], 1f)); // outer edge of rim (overhangs real ground edge)
            }
            // Duplicate first outer rim point to close the UV loop cleanly.
            {
                Vector2 p = innerProfile[0] + outwardNormals[0] * edgeOverhang;
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
            //
            //    The closing segment (last profile point -> first profile point) must NOT
            //    wrap UV back to edgeUs[0]=0.0 — that jump (≈0.99 -> 0.0) makes the Repeat-
            //    addressed grass-edge/surface texture interpolate across the whole strip and
            //    smear a stray black bar along one triangle. Instead, for the last segment we
            //    reuse profile point 0 but assign it wrapU (edgeUs[0]+1.0) so UV goes 0.99 -> 1.0
            //    continuously across the seam, exactly like the inner fan and grass rim already do.
            for (int i = 0; i < n; i++)
            {
                bool isLast = (i + 1) >= n;
                Vector2 pA = innerProfile[i];
                Vector2 pB = isLast ? innerProfile[0] : innerProfile[i + 1];
                Vector2 nA = outwardNormals[i];
                Vector2 nB = isLast ? outwardNormals[0] : outwardNormals[i + 1];

                int baseIdx = verts.Count;
                verts.Add(new Vector3(pA.x, 0f, pA.y));
                normals.Add(new Vector3(nA.x, 0f, nA.y));
                uvs.Add(Vector2.zero);
                uv2s.Add(new Vector2(edgeUs[i], 2f));

                verts.Add(new Vector3(pB.x, 0f, pB.y));
                normals.Add(new Vector3(nB.x, 0f, nB.y));
                uvs.Add(Vector2.zero);
                uv2s.Add(new Vector2(isLast ? wrapU : edgeUs[i + 1], 2f));

                verts.Add(new Vector3(pA.x, -thickness, pA.y));
                normals.Add(new Vector3(nA.x, 0f, nA.y));
                uvs.Add(Vector2.zero);
                uv2s.Add(new Vector2(edgeUs[i], 2f));

                verts.Add(new Vector3(pB.x, -thickness, pB.y));
                normals.Add(new Vector3(nB.x, 0f, nB.y));
                uvs.Add(Vector2.zero);
                uv2s.Add(new Vector2(isLast ? wrapU : edgeUs[i + 1], 2f));

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
