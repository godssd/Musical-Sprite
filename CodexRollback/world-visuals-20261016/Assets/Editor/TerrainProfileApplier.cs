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
        private const string DefaultProfilePath = "Assets/TerrainProfiles/Forest.asset";

        [MenuItem("Tools/Musical-Sprite/Terrain/Apply Selected Terrain Profile")]
        public static void ApplySelected()
        {
            var profile = Selection.activeObject as TerrainProfileSO;
            if (profile == null)
            {
                // 兜底：没选中时自动用默认 Profile，不再报错打断
                profile = AssetDatabase.LoadAssetAtPath<TerrainProfileSO>(DefaultProfilePath);
                if (profile == null)
                {
                    Debug.LogError($"[Terrain] 未选中 TerrainProfileSO，且默认配置 {DefaultProfilePath} 不存在。请选中一份 Profile 后再运行。");
                    return;
                }
                Debug.Log($"[Terrain] 未选中 Profile，自动使用默认配置：{profile.name}");
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
                groundMat.SetFloat("_ShapeMode", 0f);
                EditorUtility.SetDirty(groundMat);
                Debug.Log($"[Terrain] 地面材质已写入：{groundMat.name}");
            }

            // ---------- 2) 台面材质 ----------
            Material matL = LoadMaterial(StageMatLeftName);
            Material matR = LoadMaterial(StageMatRightName);

            Vector2 centerL, centerR;
            float radiusL, radiusR;
            ResolveStageGeometry(StageLeftName, p, true, out centerL, out radiusL, out float topYL);
            ResolveStageGeometry(StageRightName, p, false, out centerR, out radiusR, out float topYR);

            ApplyStage(matL, p, p.stageMapLeft, centerL, radiusL, topYL, "左台面");
            ApplyStage(matR, p, p.stageMapRight, centerR, radiusR, topYR, "右台面");

            // ---------- 2.5) 地面材质：写入台面占位裁剪 ----------
            // 台面压在主地面边界上，地面草沿带会从台面直边侧壁下面探出来；
            // shader 里矩形模式对台面圆盘范围内的地面片段直接 clip。
            // 半径为台面半径 + 极小余量（仅防 z-fight）。台面 _EdgeOverhang=0（外挑已关闭），
            // 余量过大（旧值 0.02）会在台面直边处留下裸露缝隙，形成竖向黑线；收到 0.0005 即不可见。
            if (groundMat != null)
            {
                groundMat.SetVector("_StageClipA",
                    new Vector4(centerR.x, centerR.y, radiusR + 0.0005f, 1f));
                groundMat.SetVector("_StageClipB",
                    new Vector4(centerL.x, centerL.y, radiusL + 0.0005f, 1f));
                EditorUtility.SetDirty(groundMat);
                Debug.Log($"[Terrain] 地面台面占位裁剪已写入：A=({centerR.x:F2},{centerR.y:F2}) r={radiusR + 0.0005f:F4}  B=({centerL.x:F2},{centerL.y:F2}) r={radiusL + 0.0005f:F4}");
            }

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
            Vector2 center, float radius, float topY, string label)
        {
            if (mat == null) return;
            mat.SetTexture("_StageMap", map);
            mat.SetTexture("_EdgeTex", p.grassMask);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_ShapeMode", 1f);
            mat.SetVector("_DiscCenter", new Vector4(center.x, center.y, 0f, 0f));
            mat.SetFloat("_DiscRadius", radius);
            // 侧壁判定高度 = 台面顶面世界 Y。侧壁网格法线被共用顶点平均坏，
            // shader 只能靠世界高度区分顶面/侧壁（见 GroundEdge.shader 的 _DiscTopY）。
            mat.SetFloat("_DiscTopY", topY);
            // 圆盘模式没有外扩几何：overhang 置 0，让草边 t 在边界处正好取到 1（草尖朝外）
            mat.SetFloat("_EdgeOutset", p.stageEdgeOutset);
            mat.SetFloat("_EdgeOverhang", 0f);
            mat.SetFloat("_EdgeTexTiling", p.stageEdgeTiling);
            mat.SetFloat("_EdgeVerticalScale", p.stageEdgeVerticalScale);
            mat.SetFloat("_EdgeCutoff", p.stageEdgeCutoff);
            mat.SetColor("_OutlineColor", p.outlineColor);
            mat.SetFloat("_CenterLineX", p.centerLineX);
            mat.SetFloat("_CenterBlend", p.centerBlend);
            mat.SetColor("_SideColor", p.sideColor);
            mat.SetColor("_ShadowColor", p.shadowColor);
            mat.SetFloat("_ShadowIntensity", p.shadowIntensity);
            mat.SetFloat("_ShadowSoftness", p.shadowSoftness);
            EditorUtility.SetDirty(mat);
            Debug.Log($"[Terrain] {label}已写入：center=({center.x:F2},{center.y:F2}) r={radius:F2} topY={topY:F2} map={(map ? map.name : "无")}");
        }

        /// <summary>圆心=台面根物体世界原点（半圆盘的直边中点）；半径=合并包围盒最大 XZ 半尺寸；
        /// topY=台面根物体自身包围盒的世界最高点（侧壁/顶面判定阈值 _DiscTopY）。</summary>
        private static void ResolveStageGeometry(string objectName, TerrainProfileSO p,
            bool isLeft, out Vector2 center, out float radius, out float topY)
        {
            center = isLeft
                ? new Vector2(p.stageCenterLeftOverride.x, p.stageCenterLeftOverride.y)
                : new Vector2(p.stageCenterRightOverride.x, p.stageCenterRightOverride.y);
            radius = p.stageRadiusManual;
            topY = 0.6f; // 兜底：网格 y∈[0,0.15] × localScale.y=4

            var go = FindInOpenScenes(objectName);
            if (go == null)
            {
                Debug.LogWarning($"[Terrain] 场景中未找到 {objectName}，使用 Profile 手动值。");
                return;
            }

            // 只看台面根物体自身的 Renderer：子对象（角色/指示器）会抬高包围盒
            var rootRenderer = go.GetComponent<Renderer>();
            if (rootRenderer != null)
                topY = rootRenderer.bounds.max.y;

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
