using UnityEngine;
using UnityEditor;

namespace MusicalSprite.Editor
{
    public static class ToonDoodleMaterialSetup
    {
        private const string ShaderName = "MusicalSprite/ToonDoodle";
        private const string OutputFolder = "Assets/Art/Materials";
        private const string MaterialName = "M_ToonDoodle_Sample.mat";

        [MenuItem("Tools/Musical-Sprite/Create ToonDoodle Sample Material")]
        public static void CreateSampleMaterial()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[ToonDoodle] 找不到 Shader '{ShaderName}'。" +
                    "请先确认 Assets/Shaders/ToonDoodle.shader 已导入且没有编译错误。");
                return;
            }

            if (!System.IO.Directory.Exists(OutputFolder))
            {
                System.IO.Directory.CreateDirectory(OutputFolder);
                AssetDatabase.Refresh();
            }

            string path = System.IO.Path.Combine(OutputFolder, MaterialName);
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            Material mat = new Material(shader);
            // 默认值：按 COTL 风格与项目美术方向预设
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
            mat.SetColor("_BaseColor", Color.white);

            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = mat;

            Debug.Log($"[ToonDoodle] 已创建示例材质：{path}");
        }
    }
}
