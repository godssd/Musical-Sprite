using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusicalSprite.Editor
{
    /// <summary>
    /// 一次性工具：把 EnchantSkins 下某技能皮肤的导入设置同步为已验收的 skill_4 参数。
    /// 不会自动运行，只在点击菜单时执行，避免干扰项目中其他贴图（地面、Spine atlas 等）。
    /// </summary>
    public static class EnchantSkinImporter
    {
        private const string ReferenceSkillId = "skill_4_croissant_heal";
        private const string ReferenceKind = "Note_Tap";

        [MenuItem("Musical Sprite/附魔换色/同步 skill_5 导入设置到 skill_4")]
        public static void SyncSkill5ImportSettings()
        {
            SyncSkillImportSettings("skill_5_bomb_rain");
        }

        [MenuItem("Musical Sprite/附魔换色/同步 skill_3 导入设置到 skill_4")]
        public static void SyncSkill3ImportSettings()
        {
            SyncSkillImportSettings("skill_3_howl");
        }

        private static void SyncSkillImportSettings(string targetSkillId)
        {
            string refPath = $"Assets/Resources/EnchantSkins/{ReferenceSkillId}/{ReferenceKind}_{ReferenceSkillId}.png";
            TextureImporter refImporter = AssetImporter.GetAtPath(refPath) as TextureImporter;
            if (refImporter == null)
            {
                Debug.LogError($"[EnchantSkinImporter] 找不到参考贴图或其 importer：{refPath}");
                return;
            }

            string targetDir = $"Assets/Resources/EnchantSkins/{targetSkillId}";
            if (!Directory.Exists(targetDir))
            {
                Debug.LogError($"[EnchantSkinImporter] 目标目录不存在：{targetDir}");
                return;
            }

            string[] pngs = Directory.GetFiles(targetDir, "*.png", SearchOption.TopDirectoryOnly);
            int changed = 0;
            foreach (string fullPath in pngs)
            {
                string assetPath = fullPath.Replace('\\', '/').Replace(Application.dataPath, "Assets");
                TextureImporter ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (ti == null) continue;

                // 仅同步会影响 Resources.Load<Texture2D> 显示的关键参数；
                // 不动 GUID、不动用户数据、不动平台覆盖设置。
                ti.textureType = refImporter.textureType;
                ti.spriteImportMode = refImporter.spriteImportMode;
                ti.sRGBTexture = refImporter.sRGBTexture;
                ti.mipmapEnabled = refImporter.mipmapEnabled;
                ti.wrapMode = refImporter.wrapMode;
                ti.filterMode = refImporter.filterMode;
                ti.maxTextureSize = refImporter.maxTextureSize;
                ti.textureCompression = refImporter.textureCompression;
                ti.compressionQuality = refImporter.compressionQuality;

                EditorUtility.SetDirty(ti);
                ti.SaveAndReimport();
                changed++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[EnchantSkinImporter] 已为 {targetSkillId} 的 {changed} 张贴图同步 {ReferenceSkillId} 的导入设置。");
        }
    }
}
