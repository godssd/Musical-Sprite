using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using MusicalSprite.FX;

namespace MusicalSprite.EditorTools
{
    /// <summary>
    /// Abyss Fog: scene-volume, height-graded, animated fog that lives BELOW the
    /// arena platform (Cult of the Lamb style).
    ///
    /// Design (after the "continuous Y gradient" rework):
    ///   - A stack of many THIN horizontal planes is generated under the platform.
    ///   - Each plane's color/alpha is set by Setup from a continuous function of
    ///     its world-Y: shallow + faint near the platform, deep + dark near the
    ///     abyss floor, solid black at the very bottom.
    ///   - Because they are stacked densely and each is thin, an object dipping into
    ///     the fog is progressively occluded from the bottom up — the exact
    ///     "deeper = darker" COTL look. Horizontal distance does NOT change the
    ///     color/alpha (same depth -> same fog), matching the reference.
    ///   - The shader adds: noise-broken soft edges, vertex wave undulation, and a
    ///     far horizontal fade so the fog never cuts off on screen.
    ///
    /// Reversible: "Remove Abyss Fog" deletes the whole hierarchy + generated
    /// materials/meshes.
    /// </summary>
    public static class AbyssFogSetup
    {
        // ---- Paths ----
        private const string ShaderName     = "MusicalSprite/AbyssFogLayer";
        private const string ShaderPath     = "Assets/Shaders/AbyssFogLayer.shader";
        private const string MatFolder      = "Assets/Art/Materials";
        private const string MeshFolder     = "Assets/Art/Meshes";
        private const string AnimatorScript = "Assets/Scripts/AbyssFogAnimator.cs";
        private const string ParentName     = "[AbyssFog]";
        private const string GroundName     = "ArenaGround";

        // Bump when material layout changes shape so old assets are rebuilt.
        private const float FogVersion = 2f;
        private const string VersionProp = "_FogVersion";

        // Horizontal extent of every fog layer (world units, full span).
        // Large enough that the fog reaches well past the camera frustum edge.
        private const float FogSpanX = 64.0f;
        private const float FogSpanZ = 64.0f;

        // Vertical layout of the abyss.
        private const float TopY    = -0.85f;   // just below the platform bottom
        private const float BottomY = -4.20f;   // deepest fog layer before solid black
        private const int   LayerCount = 12;    // thin stacked planes (drop on mobile if needed)

        // Fade so the outer rim feathers instead of hard-cutting.
        private const float HorizFadeStart = 20.0f;
        private const float HorizFadeEnd   = 32.0f;

        // Color gradient (shallow -> deep).
        private static readonly Color TopColor    = new Color(0.24f, 0.11f, 0.32f, 1f);
        private static readonly Color BottomColor = new Color(0.012f, 0.006f, 0.035f, 1f);

        private static readonly string[] RendererPaths =
        {
            "Assets/Settings/PC_Renderer.asset",
            "Assets/Settings/Mobile_Renderer.asset",
        };

        private const float DefaultPlatformHalfX = 8.0f;
        private const float DefaultPlatformHalfZ = 3.75f;

        // ---- Fog layer preset ----
        private struct LayerSpec
        {
            public string name;
            public float  y;
            public Color  color;
            public float  alpha;
            public bool   isBottom;
        }

        private static List<LayerSpec> BuildLayerSpecs()
        {
            var layers = new List<LayerSpec>();

            for (int i = 0; i < LayerCount; i++)
            {
                float t = (float)i / (LayerCount - 1);          // 0 at top, 1 at bottom
                float y = Mathf.Lerp(TopY, BottomY, t);

                // Deeper -> denser. Pow gives a gentle top, quick build toward the floor.
                float alpha = Mathf.Pow(t, 0.85f) * 0.85f + 0.04f;
                Color col = Color.Lerp(TopColor, BottomColor, t);

                layers.Add(new LayerSpec
                {
                    name   = "Layer" + (i + 1),
                    y      = y,
                    color  = col,
                    alpha  = alpha,
                    isBottom = false,
                });
            }

            // Solid black floor of the abyss.
            layers.Add(new LayerSpec
            {
                name   = "Bottom",
                y      = BottomY - 0.6f,
                color  = Color.black,
                alpha  = 1.0f,
                isBottom = true,
            });

            return layers;
        }

        // ===================================================================
        // Public menu items
        // ===================================================================
        [MenuItem("Tools/Musical-Sprite/Setup Abyss Fog")]
        public static void SetupAbyssFog()
        {
            if (System.IO.File.Exists(ShaderPath))
                AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceUpdate);

            Shader sh = Shader.Find(ShaderName);
            if (sh == null)
            {
                Debug.LogError("[AbyssFog] Shader not found: " + ShaderName +
                               " (compile errors? check " + ShaderPath + ")");
                return;
            }

            CleanupOldHeightFog();

            // Platform center (for parenting the hierarchy).
            Vector3 platformCenter = Vector3.zero;
            var ground = GameObject.Find(GroundName);
            if (ground != null) platformCenter = ground.transform.position;

            var layers = BuildLayerSpecs();
            var mats = new Material[layers.Count];
            for (int i = 0; i < layers.Count; i++)
                mats[i] = EnsureLayerMaterial(layers[i], sh);

            CreateHierarchy(layers, mats, platformCenter);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            Debug.Log("[AbyssFog] Setup complete: " + layers.Count +
                      " layers (" + (layers.Count - 1) + " fog + 1 bottom) under [" + ParentName + "]");
        }

        [MenuItem("Tools/Musical-Sprite/Remove Abyss Fog")]
        public static void RemoveAbyssFog()
        {
            var existing = GameObject.Find(ParentName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
                Debug.Log("[AbyssFog] Removed [" + ParentName + "] hierarchy");
            }
            else
            {
                Debug.Log("[AbyssFog] No [" + ParentName + "] found; nothing to remove.");
            }

            // Delete all generated layer materials + meshes (any count).
            int deleted = 0;
            if (System.IO.Directory.Exists(MatFolder))
            {
                foreach (var f in System.IO.Directory.GetFiles(MatFolder, "M_AbyssFog_*.mat"))
                {
                    AssetDatabase.DeleteAsset(f.Replace(Application.dataPath, "Assets").Replace('\\', '/'));
                    deleted++;
                }
            }
            if (System.IO.Directory.Exists(MeshFolder))
            {
                foreach (var f in System.IO.Directory.GetFiles(MeshFolder, "AbyssFogLayer_*.asset"))
                {
                    AssetDatabase.DeleteAsset(f.Replace(Application.dataPath, "Assets").Replace('\\', '/'));
                    deleted++;
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[AbyssFog] Removed " + deleted + " generated material/mesh file(s).");
        }

        [MenuItem("Tools/Musical-Sprite/Reimport Abyss Fog Shader")]
        public static void ReimportShader()
        {
            if (System.IO.File.Exists(ShaderPath))
            {
                AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceUpdate);
                Debug.Log("[AbyssFog] Reimported " + ShaderPath);
            }
            else
            {
                Debug.LogError("[AbyssFog] Shader not found at " + ShaderPath);
            }
        }

        // ===================================================================
        // Hierarchy creation
        // ===================================================================
        private static void CreateHierarchy(List<LayerSpec> layers, Material[] mats, Vector3 platformCenter)
        {
            var existing = GameObject.Find(ParentName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var parent = new GameObject(ParentName);
            Undo.RegisterCreatedObjectUndo(parent, "Create " + ParentName);
            parent.transform.position = platformCenter;
            parent.transform.rotation = Quaternion.identity;

            for (int i = 0; i < layers.Count; i++)
            {
                var spec = layers[i];
                var go = new GameObject(spec.name);
                Undo.RegisterCreatedObjectUndo(go, "Create " + spec.name);
                go.transform.SetParent(parent.transform, false);
                go.transform.localPosition = new Vector3(0f, spec.y - platformCenter.y, 0f);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one; // mesh already at full span

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = EnsureLayerMesh(spec);

                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mats[i];
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            var animator = parent.GetComponent<AbyssFogAnimator>();
            if (animator == null) animator = parent.AddComponent<AbyssFogAnimator>();
        }

        // ===================================================================
        // Layer mesh (procedural horizontal XZ plane, fixed large span)
        // ===================================================================
        private static Mesh EnsureLayerMesh(LayerSpec spec)
        {
            string meshPath = MeshFolder + "/AbyssFogLayer_" + spec.name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh != null) return mesh;

            if (!System.IO.Directory.Exists(MeshFolder))
            {
                System.IO.Directory.CreateDirectory(MeshFolder);
                AssetDatabase.Refresh();
            }

            float spanX = FogSpanX;
            float spanZ = FogSpanZ;

            // Moderate tessellation: enough for the vertex wave, cheap otherwise.
            int segX = 24;
            int segZ = 24;

            int vertCount = (segX + 1) * (segZ + 1);
            Vector3[] verts = new Vector3[vertCount];
            Vector2[] uvs = new Vector2[vertCount];
            int[] tris = new int[segX * segZ * 6];

            for (int z = 0; z <= segZ; z++)
            {
                for (int x = 0; x <= segX; x++)
                {
                    int i = z * (segX + 1) + x;
                    float u = (float)x / segX;
                    float v = (float)z / segZ;
                    verts[i] = new Vector3((u - 0.5f) * spanX, 0f, (v - 0.5f) * spanZ);
                    uvs[i] = new Vector2(u, v);
                }
            }

            int tri = 0;
            for (int z = 0; z < segZ; z++)
            {
                for (int x = 0; x < segX; x++)
                {
                    int i0 = z * (segX + 1) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + (segX + 1);
                    int i3 = i2 + 1;
                    tris[tri++] = i0;
                    tris[tri++] = i2;
                    tris[tri++] = i1;
                    tris[tri++] = i1;
                    tris[tri++] = i2;
                    tris[tri++] = i3;
                }
            }

            mesh = new Mesh();
            mesh.name = "AbyssFogLayer_" + spec.name;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, meshPath);
            return mesh;
        }

        // ===================================================================
        // Material
        // ===================================================================
        private static Material EnsureLayerMaterial(LayerSpec spec, Shader sh)
        {
            string matPath = MatFolder + "/M_AbyssFog_" + spec.name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            bool needsBuild = (mat == null)
                              || (mat.shader != sh)
                              || (!mat.HasProperty(VersionProp))
                              || !Mathf.Approximately(mat.GetFloat(VersionProp), FogVersion);

            if (needsBuild)
            {
                if (mat == null)
                {
                    if (!System.IO.Directory.Exists(MatFolder))
                    {
                        System.IO.Directory.CreateDirectory(MatFolder);
                        AssetDatabase.Refresh();
                    }
                    mat = new Material(sh);
                    mat.name = "M_AbyssFog_" + spec.name;
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else
                {
                    mat.shader = sh;
                }
            }

            // Per-layer gradient values.
            mat.SetColor("_Color", spec.color);
            mat.SetFloat("_Alpha", spec.alpha);
            mat.SetFloat("_IsBottom", spec.isBottom ? 1f : 0f);

            // Shared visual params (identical across layers for a uniform look).
            mat.SetFloat("_EdgeSoftness", 0.8f);
            mat.SetFloat("_NoiseScale", 0.12f);
            mat.SetFloat("_NoiseSpeed", 0.08f);
            mat.SetFloat("_NoiseStrength", 0.7f);
            mat.SetFloat("_WaveAmp", 0.30f);
            mat.SetFloat("_WaveScale", 0.22f);
            mat.SetFloat("_WaveSpeed", 0.5f);
            mat.SetFloat("_HorizFadeStart", HorizFadeStart);
            mat.SetFloat("_HorizFadeEnd", HorizFadeEnd);

            mat.SetFloat(VersionProp, FogVersion);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ===================================================================
        // Cleanup of the old HeightFog system (legacy, safe to keep)
        // ===================================================================
        private static void CleanupOldHeightFog()
        {
            int featuresRemoved = 0;
            int filesRemoved = 0;

            foreach (string p in RendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(p);
                if (data == null) continue;

                for (int i = data.rendererFeatures.Count - 1; i >= 0; i--)
                {
                    var f = data.rendererFeatures[i];
                    if (f == null) continue;
                    if (f.GetType().Name == "HeightFogRendererFeature")
                    {
                        data.rendererFeatures.RemoveAt(i);
                        AssetDatabase.RemoveObjectFromAsset(f);
                        Object.DestroyImmediate(f, true);
                        featuresRemoved++;
                    }
                }
                if (featuresRemoved > 0) EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();

            string[] oldFiles =
            {
                "Assets/Scripts/HeightFogRendererFeature.cs",
                "Assets/Scripts/HeightFogRenderPass.cs",
                "Assets/Editor/HeightFogSetup.cs",
                "Assets/Editor/HeightFogDiagnostics.cs",
                "Assets/Shaders/HeightFog.shader",
                "Assets/Art/Materials/M_HeightFog.mat",
            };
            foreach (string f in oldFiles)
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(f) != null)
                {
                    AssetDatabase.DeleteAsset(f);
                    filesRemoved++;
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (featuresRemoved > 0 || filesRemoved > 0)
            {
                Debug.Log("[AbyssFog] Cleanup: removed " + featuresRemoved +
                          " renderer feature(s) and " + filesRemoved + " file(s).");
            }
        }
    }
}
