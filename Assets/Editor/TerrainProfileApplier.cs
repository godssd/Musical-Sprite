using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MusicalSprite.Terrain;

namespace MusicalSprite.EditorTools
{
    /// <summary>
    /// 地形配置一键应用：把 TerrainProfileSO 的全部参数写进地面 / 台面材质，
    /// 并把台面材质指回场景对象。换地形 = 选中一份 Profile → 跑菜单。
    /// </summary>
    public static class TerrainProfileApplier
    {
        private const string StageLeftName = "LeftBand_Stage";
        private const string StageRightName = "RightBand_Stage";
        private const string StageMatLeftName = "M_GroundEdge_Stage_A";
        private const string StageMatRightName = "M_GroundEdge_Stage_B";
        private const string GroundEdgeShaderName = "MusicalSprite/GroundEdge";

        [MenuItem("Tools/Musical-Sprite/Terrain/Apply Selected Terrain Profile")]
        public static void ApplySelected()
        {
            var profile = Selection.activeObject as TerrainProfileSO;
            if (profile == null)
            {
                Debug.LogError("[Terrain] 请先在 Project 窗口选中一个 TerrainProfileSO，再运行本菜单。");
                return;
            }
            Apply(profile);
        }

        public static void Apply(TerrainProfileSO p)
        {
            var shader = Shader.Find(GroundEdgeShaderName);
            if (shader == null)
            {
                Debug.LogError("[Terrain] 找不到 MusicalSprite/GroundEdge 着色器。");
                return;
            }

            // ---------- 1) 地面材质：场景中 ShapeMode < 0.5 的 GroundEdge ----------
            Material groundMat = FindGroundMaterial(shader);
            if (groundMat == null)
            {
                Debug.LogError("[Terrain] 场景中未找到圆角矩形模式的 GroundEdge 地面材质。");
            }
            else
            {
                groundMat.SetTexture("_RedMap", p.groundRedMap);
                groundMat.SetTexture("_BlueMap", p.groundBlueMap);
                groundMat.SetTexture("_EdgeTex", p.grassMask);
                groundMat.SetColor("_BaseColor", p.groundTint);
                groundMat.SetFloat("_CenterLineX", p.centerLineX);
                groundMat.SetFloat("_CenterBlend", p.centerBlend);
                groundMat.SetFloat("_EdgeOutset", p.edgeOutset);
                groundMat.SetFloat("_EdgeTexTiling", p.edgeTiling);
                groundMat.SetFloat("_EdgeVerticalScale", p.edgeVerticalScale);
                groundMat.SetFloat("_EdgeCutoff", p.edgeCutoff);
                groundMat.SetFloat("_EdgeOverhang", p.edgeOverhang);
                groundMat.SetVector("_GroundMin", p.groundMin);
                groundMat.SetVector("_GroundMax", p.groundMax);
                groundMat.SetFloat("_CornerRadius", p.cornerRadius);
                groundMat.SetFloat("_GroundThickness", p.groundThickness);
                groundMat.SetColor("_SideColor", p.sideColor);
                groundMat.SetColor("_ShadowColor", p.shadowColor);
                groundMat.SetFloat("_ShadowIntensity", p.shadowIntensity);
                groundMat.SetFloat("_ShadowSoftness", p.shadowSoftness);
                groundMat.SetColor("_OutlineColor", p.outlineColor);
                groundMat.SetFloat("_EdgeOutlineWidth", p.outlineWidth);
                groundMat.SetFloat("_ShapeMode", 0f);
                EditorUtility.SetDirty(groundMat);
                Debug.Log($"[Terrain] 地面材质已写入：{groundMat.name}");
            }

            // ---------- 2) 台面材质 ----------
            Material matL = LoadMaterial(StageMatLeftName);
            Material matR = LoadMaterial(StageMatRightName);

            Vector2 centerL, centerR;
            float radiusL, radiusR;
            ResolveStageGeometry(StageLeftName, p, true, out centerL, out radiusL);
            ResolveStageGeometry(StageRightName, p, false, out centerR, out radiusR);

            ApplyStage(matL, p, p.stageMapLeft, centerL, radiusL, "左台面");
            ApplyStage(matR, p, p.stageMapRight, centerR, radiusR, "右台面");

            // ---------- 3) 台面材质指回场景对象 ----------
            AssignStageMaterial(StageLeftName, matL);
            AssignStageMaterial(StageRightName, matR);

            AssetDatabase.SaveAssets();
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[Terrain] Profile「{p.name}」应用完成。");
        }

        private static void ApplyStage(Material mat, TerrainProfileSO p, Texture2D map,
            Vector2 center, float radius, string label)
        {
            if (mat == null) return;
            mat.SetTexture("_StageMap", map);
            mat.SetTexture("_EdgeTex", p.grassMask);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_ShapeMode", 1f);
            mat.SetVector("_DiscCenter", new Vector4(center.x, center.y, 0f, 0f));
            mat.SetFloat("_DiscRadius", radius);
            // 圆盘模式没有外扩几何：overhang 置 0，让草边 t 在边界处正好取到 1（草尖朝外）
            mat.SetFloat("_EdgeOutset", p.stageEdgeOutset);
            mat.SetFloat("_EdgeOverhang", 0f);
            mat.SetFloat("_EdgeTexTiling", p.stageEdgeTiling);
            mat.SetFloat("_EdgeVerticalScale", p.stageEdgeVerticalScale);
            mat.SetFloat("_EdgeCutoff", p.stageEdgeCutoff);
            mat.SetColor("_OutlineColor", p.outlineColor);
            mat.SetFloat("_EdgeOutlineWidth", p.outlineWidth);
            mat.SetFloat("_CenterLineX", p.centerLineX);
            mat.SetFloat("_CenterBlend", p.centerBlend);
            mat.SetColor("_SideColor", p.sideColor);
            mat.SetColor("_ShadowColor", p.shadowColor);
            mat.SetFloat("_ShadowIntensity", p.shadowIntensity);
            mat.SetFloat("_ShadowSoftness", p.shadowSoftness);
            EditorUtility.SetDirty(mat);
            Debug.Log($"[Terrain] {label}已写入：center=({center.x:F2},{center.y:F2}) r={radius:F2} map={(map ? map.name : "无")}");
        }

        /// <summary>圆心=台面根物体世界原点（半圆盘的直边中点）；半径=合并包围盒最大 XZ 半尺寸。</summary>
        private static void ResolveStageGeometry(string objectName, TerrainProfileSO p,
            bool isLeft, out Vector2 center, out float radius)
        {
            center = isLeft
                ? new Vector2(p.stageCenterLeftOverride.x, p.stageCenterLeftOverride.y)
                : new Vector2(p.stageCenterRightOverride.x, p.stageCenterRightOverride.y);
            radius = p.stageRadiusManual;

            var go = FindInOpenScenes(objectName);
            if (go == null)
            {
                Debug.LogWarning($"[Terrain] 场景中未找到 {objectName}，使用 Profile 手动值。");
                return;
            }

            if (p.autoStageCenterFromScene)
            {
                Vector3 wp = go.transform.position;
                center = new Vector2(wp.x, wp.z);
            }

            if (p.autoStageRadiusFromScene)
            {
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                    radius = Mathf.Max(b.extents.x, b.extents.z);
                }
            }
        }

        /// <summary>把台面材质指回场景对象。对台面根物体自身的 Renderer 强制赋值（确定性），
        /// 子对象（角色槽位、指示器等）不碰；被替换掉的旧材质名会打进日志。</summary>
        private static void AssignStageMaterial(string objectName, Material mat)
        {
            if (mat == null) return;
            var go = FindInOpenScenes(objectName);
            if (go == null)
            {
                Debug.LogWarning($"[Terrain] 场景中未找到 {objectName}，跳过材质指回。");
                return;
            }
            int slots = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                // 只接管台面根物体自带的渲染器；子对象可能是角色/指示器，不碰
                if (r.gameObject != go.gameObject) continue;
                var mats = r.sharedMaterials;
                var oldNames = new string[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    oldNames[i] = mats[i] != null ? mats[i].name : "<null>";
                    mats[i] = mat;
                    slots++;
                }
                r.sharedMaterials = mats;
                Debug.Log($"[Terrain] {objectName} 根渲染器材质：[{string.Join(", ", oldNames)}] → {mat.name}");
            }
            if (slots == 0)
                Debug.LogWarning($"[Terrain] {objectName} 根物体上没有 Renderer！请检查层级结构。");
        }

        private static Material FindGroundMaterial(Shader shader)
        {
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null && m.shader == shader && m.HasProperty("_ShapeMode")
                        && m.GetFloat("_ShapeMode") < 0.5f)
                        return m;
                }
            }
            return null;
        }

        private static Material LoadMaterial(string name)
        {
            var guids = AssetDatabase.FindAssets($"{name} t:Material");
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null && mat.name == name) return mat;
            }
            Debug.LogError($"[Terrain] 找不到材质 {name}。");
            return null;
        }

        private static GameObject FindInOpenScenes(string objectName)
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var hit = FindChild(root.transform, objectName);
                    if (hit != null) return hit.gameObject;
                }
            }
            return null;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var hit = FindChild(parent.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
