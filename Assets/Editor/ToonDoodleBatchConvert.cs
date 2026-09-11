using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace MusicalSprite.Editor
{
    /// <summary>
    /// M2 · 全局 Toon 明暗分块：把场景/选中物体的材质一键替换为 ToonDoodle（方向性 Cartoon 分层）。
    /// - 非破坏式：只改 Renderer.sharedMaterials 引用，原始材质资产保留在磁盘，可 Ctrl+Z 撤销。
    /// - 自动保留原材质 albedo（_BaseMap / _BaseColor），套用 COTL 预设的 Toon / Doodle / 描边参数。
    /// - 跳过项目自定义特殊 shader（地面/血条/阴影/草/雾），避免破坏中性画布与 HUD。
    /// </summary>
    public static class ToonDoodleBatchConvert
    {
        private const string ShaderName = "MusicalSprite/ToonDoodle";
        private const string AutoFolder = "Assets/Art/Materials/ToonDoodle_Auto";

        // 不转换的自定义 shader 前缀（保持 GroundEdge 中性画布 / HP 血条 / 阴影 / 草 / 雾 原样）
        private static readonly HashSet<string> SkipShaderPrefixes = new HashSet<string>
        {
            "MusicalSprite/GroundEdge",
            "MusicalSprite/HPBar",
            "MusicalSprite/BlobShadow",
            "MusicalSprite/GrassFringe",
            "MusicalSprite/ScenePropSprite",
            "MusicalSprite/Fog",
        };

        [MenuItem("Tools/Musical-Sprite/Convert Selected to ToonDoodle")]
        public static void ConvertSelected()
        {
            var targets = Selection.gameObjects;
            if (targets.Length == 0)
            {
                Debug.LogWarning("[ToonDoodle] 请先在 Hierarchy 选中要转换的物体，再执行本菜单。");
                return;
            }
            Convert(targets);
        }

        [MenuItem("Tools/Musical-Sprite/Convert Open Scenes to ToonDoodle")]
        public static void ConvertOpenScenes()
        {
            var roots = new List<GameObject>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var go in scene.GetRootGameObjects())
                    roots.Add(go);
            }
            if (roots.Count == 0)
            {
                Debug.LogWarning("[ToonDoodle] 当前没有已加载的场景。");
                return;
            }
            Convert(roots.ToArray());
        }

        private static void Convert(GameObject[] roots)
        {
            Shader toon = Shader.Find(ShaderName);
            if (toon == null)
            {
                Debug.LogError($"[ToonDoodle] 找不到 Shader '{ShaderName}'，请先确认 ToonDoodle.shader 已导入且无编译错误（Assets → Refresh）。");
                return;
            }
            if (!Directory.Exists(AutoFolder)) Directory.CreateDirectory(AutoFolder);

            int converted = 0, skipped = 0;
            var cache = new Dictionary<Material, Material>(); // 同一原始材质只生成一次 Toon 变体

            foreach (var root in roots)
            {
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        var src = mats[m];
                        if (src == null) { skipped++; continue; }
                        if (src.shader != null && src.shader.name == ShaderName) { skipped++; continue; } // 已是 Toon
                        if (ShouldSkip(src)) { skipped++; continue; }

                        if (!cache.TryGetValue(src, out var dst))
                        {
                            dst = CreateToonVariant(toon, src);
                            cache[src] = dst;
                        }
                        mats[m] = dst;
                        changed = true;
                    }
                    if (changed)
                    {
                        Undo.RecordObject(r, "ToonDoodle convert");
                        r.sharedMaterials = mats;
                        converted++;
                    }
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[ToonDoodle] 转换完成：{converted} 个 Renderer 已套用 ToonDoodle，{skipped} 个跳过（特殊 shader / 已是 Toon）。" +
                      $"生成材质在 {AutoFolder}，可 Ctrl+Z 撤销。");
        }

        private static bool ShouldSkip(Material src)
        {
            if (src.shader == null) return true;
            foreach (var prefix in SkipShaderPrefixes)
                if (src.shader.name.StartsWith(prefix)) return true;
            return false;
        }

        private static Material CreateToonVariant(Shader toon, Material src)
        {
            var mat = new Material(toon);

            // 保留原有 albedo
            Texture tex = src.GetTexture("_BaseMap") ?? src.mainTexture;
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            Color col = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.color;
            mat.SetColor("_BaseColor", col);

            // COTL 预设（与 Create ToonDoodle Sample Material 一致）
            mat.SetFloat("_Doodle", 1f);
            mat.SetFloat("_DoodleSize", 8.1f);
            mat.SetFloat("_DoodleSpeed", 9.2f);
            mat.SetFloat("_DoodleIntensity", 0.005f);
            mat.SetFloat("_StepCount", 4f);
            mat.SetFloat("_ShadowThreshold", 0.35f);
            mat.SetColor("_ShadowColor", new Color(0.35f, 0.30f, 0.45f, 1f));
            mat.SetFloat("_HighlightThreshold", 0.75f);
            mat.SetColor("_HighlightColor", new Color(1.1f, 1.05f, 0.95f, 1f));
            mat.SetFloat("_OutlineWidth", 0.02f);
            mat.SetColor("_OutlineColor", new Color(0.05f, 0.05f, 0.08f, 1f));
            mat.SetFloat("_OutlineDoodle", 1f);

            string safeName = string.IsNullOrEmpty(src.name) ? "mat" : src.name;
            string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(AutoFolder, "TD_" + safeName + ".mat"));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
