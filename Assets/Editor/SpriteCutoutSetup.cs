// MusicalSprite.Editor / SpriteCutoutSetup
//
// Makes scene-prop sprites (tree_1_Prop, etc.) intersect the volumetric fog by
// switching their SpriteRenderer material from the built-in transparent
// "Sprites/Default" to the depth-writing "MusicalSprite/SpriteCutout" shader.
//
// The change is applied to the BP_SceneProp PREFAB so every instance (including
// tree_1_Prop) inherits it. "Reset" restores the built-in transparent material.
//
// Both actions are idempotent and reversible.

using UnityEngine;
using UnityEditor;

namespace MusicalSprite.Editor
{
    public static class SpriteCutoutSetup
    {
        private const string ShaderPath = "Assets/Shaders/SpriteCutout.shader";
        private const string MatPath    = "Assets/Art/Materials/M_SpriteCutout.mat";
        private const string PrefabPath = "Assets/Art/Prefabs/BP_SceneProp.prefab";

        [MenuItem("Tools/Musical-Sprite/Setup Sprite Cutout")]
        public static void SetupSpriteCutout()
        {
            // 1. Ensure the cutout material exists. (Normally it is already created
            //    in version control; this menu makes the workflow robust if it ever
            //    goes missing.)
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null)
            {
                Shader sh = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
                if (sh == null)
                {
                    Debug.LogError("[SpriteCutout] Cannot create material: shader not found at " +
                                   ShaderPath + ". Wait for Unity to finish compiling, then retry.");
                    return;
                }

                if (!AssetDatabase.IsValidFolder("Assets/Art/Materials"))
                    AssetDatabase.CreateFolder("Assets/Art", "Materials");

                mat = new Material(sh);
                mat.name = "M_SpriteCutout";
                AssetDatabase.CreateAsset(mat, MatPath);
                AssetDatabase.SaveAssets();
                mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
                Debug.Log("[SpriteCutout] Created material at " + MatPath);
            }

            // 2. Assign it to the scene-prop prefab's SpriteRenderer.
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[SpriteCutout] Prefab not found at " + PrefabPath);
                return;
            }

            SpriteRenderer sr = prefab.GetComponentInChildren<SpriteRenderer>(true);
            if (sr == null)
            {
                Debug.LogError("[SpriteCutout] No SpriteRenderer found inside " + PrefabPath);
                return;
            }

            sr.sharedMaterial = mat;
            EditorUtility.SetDirty(prefab);
            PrefabUtility.SavePrefabAsset(prefab);
            AssetDatabase.SaveAssets();

            Debug.Log("[SpriteCutout] Done. " + prefab.name +
                      " (incl. tree_1_Prop) now uses the depth-writing cutout material " +
                      "and can intersect the fog. Tune 'Alpha Cutoff' on M_SpriteCutout " +
                      "to control edge hardness.");
        }

        [MenuItem("Tools/Musical-Sprite/Reset Sprite Material (Default)")]
        public static void ResetSpriteMaterial()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[SpriteCutout] Prefab not found at " + PrefabPath);
                return;
            }

            SpriteRenderer sr = prefab.GetComponentInChildren<SpriteRenderer>(true);
            if (sr == null)
            {
                Debug.LogError("[SpriteCutout] No SpriteRenderer found inside " + PrefabPath);
                return;
            }

            Material def = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites/Default.mat");
            sr.sharedMaterial = def;
            EditorUtility.SetDirty(prefab);
            PrefabUtility.SavePrefabAsset(prefab);
            AssetDatabase.SaveAssets();

            Debug.Log("[SpriteCutout] Reset " + prefab.name +
                      " material to built-in Sprites/Default (transparent, no depth write).");
        }
    }
}
