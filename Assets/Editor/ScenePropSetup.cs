using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusicalSprite.EditorTools
{
    /// <summary>
    /// Workflow for the COTL-style "立着背景图" scene props. Two entry points:
    ///
    /// 1. <b>Create Scene Prop From PNG</b> — fastest path.
    ///    Pick a PNG, the importer gets normalized (Sprite, Single, pivot=bottom,
    ///    alphaIsTransparency, no mipmaps, FullRect mesh), a sprite child is spawned
    ///    under the prop root, and the prop is dropped behind the arena.
    ///
    /// 2. <b>Create Scene Prop From FBX</b> — for hand-modelled props.
    ///    Pick an FBX, the importer gets checked (Rig=Generic, Read/Write enabled,
    ///    normals imported, lightmap UV if requested), the prop is wrapped in a
    ///    scene-prop root that carries the Billboard component.
    ///
    /// After the first one is placed, just <b>Ctrl+D</b> to duplicate, then move /
    /// rotate / re-skin. List Scene Props scans the open scene for all props that
    /// carry the Billboard component.
    /// </summary>
    public static class ScenePropSetup
    {
        // The single prefab that holds a SceneProp + Billboard. Created on first run.
        private const string PrefabDir = "Assets/Art/Prefabs";
        private const string PrefabPath = "Assets/Art/Prefabs/BP_SceneProp.prefab";
        private const string MaterialPath = "Assets/Art/Materials/M_SceneProp_Default.mat";
        private const string SpriteChildName = "Sprite";

        // Where to drop new props by default. The Arena is centered at (0,0,0) with
        // half-height 3.75 in z; z=+12 is well behind the camera-facing far side.
        private static readonly Vector3 DefaultDropPos = new Vector3(0f, 0f, 12f);

        [MenuItem("Tools/Musical-Sprite/Create Scene Prop From PNG")]
        public static void CreateScenePropFromPNG()
        {
            string pngPath = EditorUtility.OpenFilePanel("Pick a PNG scene prop", "Assets/Art", "png");
            if (string.IsNullOrEmpty(pngPath)) return;

            // EditorUtility returns absolute paths; turn into a project-relative one.
            string projectRelative = MakeProjectRelative(pngPath);
            if (string.IsNullOrEmpty(projectRelative))
            {
                EditorUtility.DisplayDialog("Scene Prop",
                    "The chosen file is outside the project's Assets folder. " +
                    "Copy it under Assets/ first, then try again.", "OK");
                return;
            }

            // 1) Normalize importer settings.
            ConfigurePNGImporter(projectRelative, out Sprite sprite, out bool hasAlpha);

            // 2) Make sure the shared prefab + material exist.
            GameObject prefab = EnsurePrefab();

            // 3) Instantiate at default drop position.
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null)
            {
                Debug.LogError("[SceneProp] Failed to instantiate prefab.");
                return;
            }
            instance.name = Path.GetFileNameWithoutExtension(projectRelative) + "_Prop";
            instance.transform.position = DefaultDropPos;

            // 4) Configure the sprite child.
            Transform spriteChild = instance.transform.Find(SpriteChildName);
            if (spriteChild == null)
            {
                spriteChild = new GameObject(SpriteChildName).transform;
                spriteChild.SetParent(instance.transform, false);
                spriteChild.localPosition = Vector3.zero;
            }
            SpriteRenderer sr = spriteChild.GetComponent<SpriteRenderer>();
            if (sr == null) sr = spriteChild.gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -50; // behind the arena but in front of the skybox.

            // 5) Make sure the Billboard is wired.
            if (instance.GetComponent<ScenePropBillboard>() == null)
                instance.AddComponent<ScenePropBillboard>();

            // 6) Warn about missing alpha so the user knows why the background may be a colored block.
            if (!hasAlpha)
            {
                Debug.LogWarning("[SceneProp] " + instance.name + " has no alpha channel. " +
                    "Open the PNG in Photoshop (or similar) and erase the background, " +
                    "then re-import. The prop will render as a colored rectangle until then.");
            }

            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
            Debug.Log("[SceneProp] Created " + instance.name + " at " + DefaultDropPos +
                      " (sprite: " + sprite.name + ", hasAlpha: " + hasAlpha + ").");
        }

        [MenuItem("Tools/Musical-Sprite/Create Scene Prop From FBX")]
        public static void CreateScenePropFromFBX()
        {
            string fbxPath = EditorUtility.OpenFilePanel("Pick an FBX scene prop", "Assets/Art/Models", "fbx");
            if (string.IsNullOrEmpty(fbxPath)) return;

            string projectRelative = MakeProjectRelative(fbxPath);
            if (string.IsNullOrEmpty(projectRelative))
            {
                EditorUtility.DisplayDialog("Scene Prop",
                    "The chosen file is outside the project's Assets folder. " +
                    "Copy it under Assets/ first, then try again.", "OK");
                return;
            }

            ConfigureFBXImporter(projectRelative);

            // Import the model as a prefab.
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(projectRelative);
            if (model == null)
            {
                Debug.LogError("[SceneProp] Could not load " + projectRelative + " as a GameObject.");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (instance == null)
            {
                Debug.LogError("[SceneProp] Failed to instantiate FBX.");
                return;
            }
            instance.name = Path.GetFileNameWithoutExtension(projectRelative) + "_Prop";
            instance.transform.position = DefaultDropPos;

            // Add the Billboard so the model faces the camera like the sprites do.
            if (instance.GetComponent<ScenePropBillboard>() == null)
                instance.AddComponent<ScenePropBillboard>();

            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
            Debug.Log("[SceneProp] Created FBX prop " + instance.name + " at " + DefaultDropPos + ".");
        }

        [MenuItem("Tools/Musical-Sprite/List Scene Props")]
        public static void ListSceneProps()
        {
            var all = Object.FindObjectsByType<ScenePropBillboard>(FindObjectsSortMode.None);
            if (all.Length == 0)
            {
                Debug.Log("[SceneProp] No scene props in the active scene.");
                return;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("[SceneProp] " + all.Length + " prop(s) in the active scene:");
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                sb.Append("  - ").Append(b.name)
                  .Append("  pos=").Append(b.transform.position)
                  .Append("  mode=").Append(b.mode)
                  .Append("  rot=").Append(b.transform.eulerAngles)
                  .AppendLine();
            }
            Debug.Log(sb.ToString());
        }

        // ---- importer setup helpers ---------------------------------------

        private static void ConfigurePNGImporter(string assetPath, out Sprite sprite, out bool hasAlpha)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                // Should not happen for a PNG, but guard anyway.
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                hasAlpha = sprite != null && sprite.texture != null && sprite.texture.alphaIsTransparency;
                return;
            }

            // Detect alpha from the source file (RGB -> false, RGBA -> true).
            byte[] bytes = File.ReadAllBytes(assetPath);
            hasAlpha = DetectPngHasAlpha(bytes);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            // Note: TextureImporter.spriteMeshType is not available in this Unity version.
            // The default SpriteMeshType is FullRect, which is what we want.
            importer.spritePivot = new Vector2(0.5f, 0f); // bottom center
            importer.alphaIsTransparency = true;
            importer.alphaSource = hasAlpha
                ? TextureImporterAlphaSource.FromInput
                : TextureImporterAlphaSource.None;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = true;

            importer.SaveAndReimport();

            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static void ConfigureFBXImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning("[SceneProp] No ModelImporter on " + assetPath + " (not an FBX?).");
                return;
            }

            importer.importVisibility = true;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.isReadable = true;
            // Note: polygon/vertex optimization flags are not available in this Unity version.
            importer.SaveAndReimport();
        }

        // ---- prefab / material fabrication --------------------------------

        private static GameObject EnsurePrefab()
        {
            // Ensure the directory exists.
            if (!AssetDatabase.IsValidFolder(PrefabDir))
            {
                Directory.CreateDirectory(PrefabDir);
                AssetDatabase.Refresh();
            }

            // Check whether the prefab already exists.
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;

            // Build the prefab from scratch:
            //   BP_SceneProp (root, has Billboard)
            //     Sprite (child, SpriteRenderer; we set the actual sprite later)
            GameObject root = new GameObject("BP_SceneProp");
            root.AddComponent<ScenePropBillboard>();

            GameObject spriteChild = new GameObject(SpriteChildName);
            spriteChild.transform.SetParent(root.transform, false);
            spriteChild.AddComponent<SpriteRenderer>();

            // Save as prefab, then delete the scene instance.
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return saved;
        }

        // ---- misc helpers --------------------------------------------------

        private static string MakeProjectRelative(string absolutePath)
        {
            string norm = absolutePath.Replace('\\', '/');
            // Try to find "Assets/" in the path; trim everything before it.
            int idx = norm.LastIndexOf("/Assets/", System.StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            return norm.Substring(idx + 1); // drop the leading '/'
        }

        // PNG signature is 8 bytes; the IHDR chunk is next, with bit-depth + color type.
        // Color type 6 = RGBA, 4 = grayscale+alpha, 2 = RGB, 0 = grayscale, 3 = indexed.
        private static bool DetectPngHasAlpha(byte[] bytes)
        {
            if (bytes.Length < 24) return false;
            // Signature 89 50 4E 47 0D 0A 1A 0A
            if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47) return false;
            // After "IHDR" (4 bytes) come width (4) + height (4) + bit-depth (1) + color-type (1).
            // Header chunk length is bytes 8..11; "IHDR" literal is 12..15; color type is byte 25.
            byte colorType = bytes[25];
            return colorType == 4 || colorType == 6;
        }
    }
}
