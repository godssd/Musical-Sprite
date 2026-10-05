using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Spine.Unity;

/// <summary>
/// 通用角色 Spine 接入工具。
///
/// 把任意角色目录下的 Spine 导出资源（.json + .atlas.txt + .png）一键生成角色外观 prefab，
/// 并把 prefab 填进对应 CharacterDataSO.modelPrefab。此后进 Play 时 CharacterCubeMarker
/// 会自动实例化该 prefab 并隐藏原占位 cube。
///
/// 用法：Unity 顶部菜单 Tools -> Musical Sprite -> Character Spine Setup。
///   1. 选择角色数据（CharacterDataSO）。
///   2. 点击「自动检测」按 animationPrefix / characterId 自动定位美术目录；或手动选择。
///   3. 点击「接入 Spine」。
///
/// 目录约定见《命名规则》§4：Assets/Art/Characters/{英文名}/。
/// animationPrefix 约定见《命名规则》§1/§3 与《角色Spine动画框架》。
/// </summary>
public class CharacterSpineSetupWindow : EditorWindow
{
    private CharacterDataSO characterData;
    private string artFolder = "";
    private float rootScale = 0.139f;
    private bool force = false;
    private string statusMessage = "";
    private bool isError = false;
    private Vector2 scroll;

    [MenuItem("Tools/Musical Sprite/Character Spine Setup")]
    public static void ShowWindow()
    {
        GetWindow<CharacterSpineSetupWindow>("Character Spine Setup");
    }

    private void OnGUI()
    {
        GUILayout.Label("角色 Spine 通用接入工具", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "选择角色数据 → 自动检测/选择美术目录 → 点击「接入 Spine」。\n" +
            "工具会自动：生成 SkeletonData/Atlas 资产、创建 prefab、修复 URP 材质、并把 modelPrefab 填进角色数据。",
            MessageType.Info);

        EditorGUILayout.Space(5);
        characterData = (CharacterDataSO)EditorGUILayout.ObjectField(
            "角色数据 (CharacterDataSO)", characterData, typeof(CharacterDataSO), false);

        EditorGUILayout.BeginHorizontal();
        artFolder = EditorGUILayout.TextField("美术目录", artFolder);
        if (GUILayout.Button("选择", GUILayout.Width(60))) BrowseFolder();
        if (GUILayout.Button("自动检测", GUILayout.Width(80))) AutoDetectFolder();
        EditorGUILayout.EndHorizontal();

        rootScale = EditorGUILayout.FloatField("根节点缩放 (Root Scale)", rootScale);
        force = EditorGUILayout.Toggle("强制重建 (Force)", force);

        EditorGUILayout.Space(8);
        if (GUILayout.Button("接入 Spine", GUILayout.Height(32)))
        {
            Run();
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(statusMessage, isError ? MessageType.Error : MessageType.Info);
        }
    }

    private void BrowseFolder()
    {
        string startDir = string.IsNullOrEmpty(artFolder)
            ? Path.GetFullPath("Assets/Art/Characters")
            : Path.GetFullPath(artFolder);

        if (!Directory.Exists(startDir))
            startDir = Path.GetFullPath("Assets");

        string abs = EditorUtility.OpenFolderPanel("选择角色美术目录", startDir, "");
        if (string.IsNullOrEmpty(abs)) return;

        string dataPath = Path.GetFullPath(Application.dataPath);
        string normAbs = abs.Replace('\\', '/');
        string normData = dataPath.Replace('\\', '/');

        if (!normAbs.StartsWith(normData))
        {
            statusMessage = "请选择项目 Assets 目录下的文件夹。";
            isError = true;
            return;
        }

        artFolder = "Assets" + normAbs.Substring(normData.Length).TrimEnd('/').Replace('\\', '/');
        statusMessage = $"已选择目录：{artFolder}";
        isError = false;
    }

    private void AutoDetectFolder()
    {
        if (characterData == null)
        {
            statusMessage = "请先选择角色数据。";
            isError = true;
            return;
        }

        string prefix = characterData.animationPrefix;
        if (string.IsNullOrEmpty(prefix))
        {
            prefix = CharacterSpineSetup.GuessAnimationPrefix(characterData.characterId);
            if (!string.IsNullOrEmpty(prefix))
            {
                statusMessage = $"animationPrefix 为空，已按角色编号推导：{prefix}。";
                isError = false;
            }
        }

        string folderName = CharacterSpineSetup.ExtractEnglishName(prefix);
        if (string.IsNullOrEmpty(folderName))
        {
            statusMessage = "无法从 animationPrefix 推导英文名目录，请手动选择。";
            isError = true;
            return;
        }

        string candidate = $"Assets/Art/Characters/{folderName}";
        if (!AssetDatabase.IsValidFolder(candidate))
        {
            statusMessage = $"按命名规则未找到目录：{candidate}。请确认目录已创建或手动选择。";
            isError = true;
            return;
        }

        artFolder = candidate;
        statusMessage = $"已自动检测目录：{artFolder}";
        isError = false;
    }

    private void Run()
    {
        if (characterData == null)
        {
            statusMessage = "请先选择角色数据。";
            isError = true;
            return;
        }
        if (string.IsNullOrEmpty(artFolder) || !AssetDatabase.IsValidFolder(artFolder))
        {
            statusMessage = "美术目录无效，请选择或自动检测。";
            isError = true;
            return;
        }

        bool ok = CharacterSpineSetup.Run(characterData, artFolder, rootScale, force);
        statusMessage = ok
            ? "接入完成，请查看 Console 日志，并进 Play 验证 cube 是否被替换。"
            : "接入失败，请查看 Console 错误。";
        isError = !ok;
    }
}

/// <summary>
/// 通用角色 Spine 接入核心逻辑。
/// </summary>
public static class CharacterSpineSetup
{
    private const string UrpPmaShaderName = "Universal Render Pipeline/Spine/Skeleton";

    /// <summary>按命名规则由 characterId 推导 animationPrefix。</summary>
    public static string GuessAnimationPrefix(int characterId)
    {
        switch (characterId)
        {
            case 1: return "Player_01_Bear"; // 小熊（玩家）
            case 2: return "Aibo_1_Bigdog";   // 大狗
            case 3: return "Aibo_2_Shit";     // 屎屎
            case 4: return "Aibo_3_Boom";     // 布姆
            case 5: return "Aibo_4_Black";    // 小黑
            default: return null;
        }
    }

    /// <summary>从 animationPrefix 提取英文名（最后一段），用于定位美术目录。</summary>
    public static string ExtractEnglishName(string animationPrefix)
    {
        if (string.IsNullOrEmpty(animationPrefix)) return null;
        int lastUnderscore = animationPrefix.LastIndexOf('_');
        if (lastUnderscore < 0) return animationPrefix;
        return animationPrefix.Substring(lastUnderscore + 1);
    }

    /// <summary>
    /// 为指定角色接入 Spine。
    /// </summary>
    /// <param name="data">角色数据（会被修改 modelPrefab）</param>
    /// <param name="folder">美术目录，如 Assets/Art/Characters/Bigdog</param>
    /// <param name="rootScale">prefab 根节点缩放</param>
    /// <param name="force">是否强制重建</param>
    /// <returns>是否成功</returns>
    public static bool Run(CharacterDataSO data, string folder, float rootScale, bool force)
    {
        if (data == null)
        {
            Debug.LogError("[CharacterSpineSetup] characterData 为空。");
            return false;
        }

        folder = folder.Replace('\\', '/').TrimEnd('/');
        if (!AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogError($"[CharacterSpineSetup] 目录无效：{folder}");
            return false;
        }

        // 确保 animationPrefix
        string prefix = data.animationPrefix;
        if (string.IsNullOrEmpty(prefix))
        {
            prefix = GuessAnimationPrefix(data.characterId);
            if (!string.IsNullOrEmpty(prefix))
            {
                Undo.RecordObject(data, "Set animationPrefix");
                data.animationPrefix = prefix;
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
                Debug.Log($"[CharacterSpineSetup] animationPrefix 为空，已按角色编号 {data.characterId} 自动写入：{prefix}");
            }
            else
            {
                Debug.LogError($"[CharacterSpineSetup] 无法推导 characterId={data.characterId} 的 animationPrefix，请手动在 CharacterDataSO 中填写。");
                return false;
            }
        }

        // 查找 .json 与 .atlas.txt
        string jsonPath = FindJson(folder, prefix);
        if (string.IsNullOrEmpty(jsonPath))
        {
            Debug.LogError($"[CharacterSpineSetup] 在 {folder} 中找不到 Spine .json 骨骼文件。");
            return false;
        }

        string atlasPath = FindAtlas(folder, Path.GetFileNameWithoutExtension(jsonPath));
        if (string.IsNullOrEmpty(atlasPath))
        {
            Debug.LogError($"[CharacterSpineSetup] 在 {folder} 中找不到 .atlas.txt 文件。");
            return false;
        }

        string jsonFileName = Path.GetFileNameWithoutExtension(jsonPath);
        string skeletonDataPath = $"{folder}/{jsonFileName}_SkeletonData.asset";
        string folderName = Path.GetFileName(folder);
        string prefabPath = $"{folder}/{folderName}.prefab";

        // 二次确认
        string action = force ? "将强制删除并重建 SkeletonDataAsset，覆盖 prefab。" : "非强制模式下会跳过已有效的 prefab。";
        if (!EditorUtility.DisplayDialog("接入角色 Spine",
            $"角色：{data.displayName}（characterId={data.characterId}）\n" +
            $"animationPrefix：{prefix}\n" +
            $"目录：{folder}\n" +
            $"JSON：{jsonPath}\n" +
            $"Atlas：{atlasPath}\n" +
            $"Prefab：{prefabPath}\n\n" + action + "\n\n是否继续？",
            "继续", "取消"))
        {
            Debug.Log("[CharacterSpineSetup] 用户取消。");
            return false;
        }

        // 非强制：若 prefab 已有效则跳过
        if (!force && PrefabAlreadyValid(prefabPath, skeletonDataPath))
        {
            Debug.LogWarning($"[CharacterSpineSetup] {prefabPath} 引用完整且 SkeletonData 有效，跳过。如需重建请勾选 Force。");
            return true;
        }

        // 强制重建：删除旧 SkeletonDataAsset，触发 Spine 重新导入
        var oldSkeletonData = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(skeletonDataPath);
        if (oldSkeletonData != null)
        {
            bool deleted = AssetDatabase.DeleteAsset(skeletonDataPath);
            Debug.Log($"[CharacterSpineSetup] 删除旧 SkeletonDataAsset: {(deleted ? "成功" : "失败或不存在")}");
        }

        AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(jsonPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        var skeletonDataAsset = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(skeletonDataPath);
        if (skeletonDataAsset == null)
        {
            Debug.LogError($"[CharacterSpineSetup] 重新导入后仍未生成 {skeletonDataPath}，请检查 Console 是否有 Spine 导入错误。");
            return false;
        }

        var atlasAssets = skeletonDataAsset.atlasAssets;
        if (atlasAssets == null || atlasAssets.Length == 0 || atlasAssets.Any(a => a == null))
        {
            Debug.LogError("[CharacterSpineSetup] SkeletonDataAsset 的 atlasAssets 为空，Spine 自动导入失败。");
            return false;
        }
        Debug.Log($"[CharacterSpineSetup] SkeletonDataAsset 已生成，关联 atlas 数：{atlasAssets.Length}");

        // 修复 URP PMA 材质与贴图导入设置
        FixMaterialsToUrpPma(atlasAssets);
        FixTextureImportSettings(atlasAssets);

        // 实例化 Spine GameObject
        GameObject spineGo = InstantiateSpineGameObject(skeletonDataAsset);
        if (spineGo == null)
        {
            Debug.LogError("[CharacterSpineSetup] Spine 实例化失败，未创建 prefab。");
            return false;
        }

        var skeletonAnim = spineGo.GetComponent<SkeletonAnimation>();
        if (skeletonAnim != null)
        {
            string initialAnim = FindInitialAnimation(skeletonDataAsset, prefix);
            if (!string.IsNullOrEmpty(initialAnim))
            {
                skeletonAnim.AnimationName = initialAnim;
                skeletonAnim.loop = true;
            }
        }

        // 创建根节点并挂入 Spine。
        // 结构统一约定（与 Shit/Bigdog prefab 一致）：根节点 Identity + scale=1，
        // Spine 子节点 Identity 旋转（默认垂直）+ scale=rootScale。
        // 这样所有角色默认角度一致，后续只靠 lane 的 CharacterSlotPose 微调。
        var root = new GameObject(folderName);
        root.transform.localScale = Vector3.one;
        spineGo.transform.SetParent(root.transform, false);
        spineGo.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        spineGo.transform.localScale = Vector3.one * rootScale;

        var renderer = spineGo.GetComponent<SkeletonRenderer>();
        if (renderer != null && renderer.skeletonDataAsset == null) renderer.skeletonDataAsset = skeletonDataAsset;
        if (skeletonAnim != null && skeletonAnim.skeletonDataAsset == null) skeletonAnim.skeletonDataAsset = skeletonDataAsset;
        if (renderer != null) EditorUtility.SetDirty(renderer);
        if (skeletonAnim != null) EditorUtility.SetDirty(skeletonAnim);
        EditorUtility.SetDirty(spineGo);
        EditorUtility.SetDirty(root);
        AssetDatabase.SaveAssets();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool prefabSuccess);
        GameObject.DestroyImmediate(root);

        if (!prefabSuccess || prefab == null)
        {
            Debug.LogError($"[CharacterSpineSetup] 保存 prefab 失败：{prefabPath}");
            return false;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!ValidatePrefabReferences(prefab, skeletonDataAsset))
        {
            Debug.LogError("[CharacterSpineSetup] Prefab 引用校验失败，请手动检查。");
            return false;
        }
        Debug.Log($"[CharacterSpineSetup] Prefab 已保存并校验通过：{prefabPath}");

        // 填入 modelPrefab
        Undo.RecordObject(data, "Assign modelPrefab");
        data.modelPrefab = prefab;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        Debug.Log($"[CharacterSpineSetup] 已将 prefab 填入 {data.name}.modelPrefab。进 Play 后角色应替换占位 cube。");
        EditorGUIUtility.PingObject(prefab);
        return true;
    }

    private static string FindJson(string folder, string animationPrefix)
    {
        string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { folder });
        string best = null;
        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            if (best == null) best = path;
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(fileName, animationPrefix, StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return best;
    }

    private static string FindAtlas(string folder, string jsonBaseName)
    {
        string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { folder });
        string best = null;
        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (!path.EndsWith(".atlas.txt", StringComparison.OrdinalIgnoreCase)) continue;
            if (best == null) best = path;
            string fileName = Path.GetFileNameWithoutExtension(path); // e.g. Aibo_1_Bigdog.atlas -> Aibo_1_Bigdog
            if (fileName.StartsWith(jsonBaseName, StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return best;
    }

    private static string FindInitialAnimation(SkeletonDataAsset asset, string prefix)
    {
        var sd = asset.GetSkeletonData(true);
        if (sd == null) return null;

        string[] candidates = new[]
        {
            $"{prefix}_00_Idle",
            $"{prefix}_02_Play_Normal"
        };
        foreach (var c in candidates)
        {
            if (sd.FindAnimation(c) != null) return c;
        }

        if (sd.Animations.Count > 0) return sd.Animations.Items[0].Name;
        return null;
    }

    private static GameObject InstantiateSpineGameObject(SkeletonDataAsset skeletonDataAsset)
    {
        var skeletonData = skeletonDataAsset.GetSkeletonData(true);
        if (skeletonData == null)
        {
            Debug.LogError("[CharacterSpineSetup] SkeletonDataAsset.GetSkeletonData() 返回 null，请检查 json 与 atlas 是否兼容。");
            return null;
        }
        var stateData = skeletonDataAsset.GetAnimationStateData();
        if (stateData == null)
        {
            Debug.LogError("[CharacterSpineSetup] SkeletonDataAsset.GetAnimationStateData() 返回 null。");
            return null;
        }
        Debug.Log($"[CharacterSpineSetup] SkeletonData 预加载成功，动画数：{skeletonData.Animations.Count}");

        SkeletonAnimation anim = null;
        try
        {
            anim = InvokeEditorInstantiation(skeletonDataAsset);
        }
        catch (System.Exception e)
        {
            var inner = e.InnerException ?? e;
            Debug.LogWarning($"[CharacterSpineSetup] 方案 A（Spine 编辑器 API）失败，转用手动兜底。\n原因：{inner.Message}");
        }

        if (anim == null)
        {
            anim = ManualCreateSkeletonAnimation(skeletonDataAsset);
        }

        if (anim == null)
        {
            Debug.LogError("[CharacterSpineSetup] Spine 实例化失败，未创建 prefab。");
            return null;
        }

        var renderer = anim.GetComponent<SkeletonRenderer>();
        if (renderer != null)
        {
            try { renderer.Initialize(true); } catch (Exception e) { Debug.LogWarning($"[CharacterSpineSetup] renderer.Initialize 异常：{e.Message}"); }
        }
        var meshRenderer = anim.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.sharedMaterials = CollectMaterialsFromAtlas(skeletonDataAsset.atlasAssets);
            EditorUtility.SetDirty(meshRenderer);
        }
        Debug.Log($"[CharacterSpineSetup] Spine GameObject 已创建。renderer valid={(renderer != null && renderer.IsValid)}, anim valid={anim.IsValid}, materials={(meshRenderer != null ? meshRenderer.sharedMaterials.Length : 0)}");
        return anim.gameObject;
    }

    private static Material[] CollectMaterialsFromAtlas(AtlasAssetBase[] atlasAssets)
    {
        var list = new System.Collections.Generic.List<Material>();
        if (atlasAssets != null)
        {
            foreach (var atlas in atlasAssets)
            {
                if (atlas == null) continue;
                foreach (var mat in atlas.Materials)
                {
                    if (mat != null && !list.Contains(mat)) list.Add(mat);
                }
            }
        }
        return list.ToArray();
    }

    private static SkeletonAnimation InvokeEditorInstantiation(SkeletonDataAsset skeletonDataAsset)
    {
        Type editorInstantiationType = FindSpineEditorInstantiationType();
        if (editorInstantiationType == null)
            throw new System.InvalidOperationException("找不到 Spine.Unity.Editor.EditorInstantiation 类型。");

        MethodInfo method = editorInstantiationType.GetMethod(
            "InstantiateSkeletonAnimation",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new Type[] { typeof(SkeletonDataAsset), typeof(string), typeof(bool), typeof(bool) },
            null);
        if (method == null)
            throw new System.InvalidOperationException("找不到 EditorInstantiation.InstantiateSkeletonAnimation 方法。");

        object result = method.Invoke(null, new object[] { skeletonDataAsset, null, false, false });
        return (SkeletonAnimation)result;
    }

    private static SkeletonAnimation ManualCreateSkeletonAnimation(SkeletonDataAsset skeletonDataAsset)
    {
        try
        {
            var go = new GameObject("Spine GameObject (Manual)");
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();

            var renderer = go.AddComponent<SkeletonRenderer>();
            renderer.skeletonDataAsset = skeletonDataAsset;
            EditorUtility.SetDirty(renderer);

            var anim = go.AddComponent<SkeletonAnimation>();
            anim.skeletonDataAsset = skeletonDataAsset;
            anim.Initialize(true);
            if (anim.skeletonDataAsset == null) anim.skeletonDataAsset = skeletonDataAsset;
            if (renderer.skeletonDataAsset == null) renderer.skeletonDataAsset = skeletonDataAsset;
            EditorUtility.SetDirty(anim);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(go);
            return anim;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CharacterSpineSetup] 手动创建 Spine GameObject 失败：{e.Message}\n{e.StackTrace}");
            return null;
        }
    }

    private static Type FindSpineEditorInstantiationType()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType("Spine.Unity.Editor.EditorInstantiation");
                if (t != null) return t;

                t = asm.GetType("Spine.Unity.Editor.AssetUtility+EditorInstantiation");
                if (t != null) return t;
            }
            catch { /* ignore reflection errors */ }
        }
        return null;
    }

    private static bool ValidatePrefabReferences(GameObject prefab, SkeletonDataAsset expectedData)
    {
        if (prefab == null || expectedData == null) return false;
        var renderer = prefab.GetComponentInChildren<SkeletonRenderer>(true);
        if (renderer == null)
        {
            Debug.LogError("[CharacterSpineSetup] 校验失败：prefab 下找不到 SkeletonRenderer。");
            return false;
        }
        if (renderer.skeletonDataAsset != expectedData)
        {
            Debug.LogError($"[CharacterSpineSetup] 校验失败：SkeletonRenderer.skeletonDataAsset 不匹配。");
            return false;
        }
        var anim = prefab.GetComponentInChildren<SkeletonAnimation>(true);
        if (anim == null)
        {
            Debug.LogWarning("[CharacterSpineSetup] 校验警告：prefab 下找不到 SkeletonAnimation（不影响渲染，但无动画）。");
        }
        return true;
    }

    private static bool PrefabAlreadyValid(string prefabPath, string skeletonDataPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return false;

        var skeletonDataAsset = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(skeletonDataPath);
        if (skeletonDataAsset == null) return false;

        var renderer = prefab.GetComponentInChildren<SkeletonRenderer>(true);
        if (renderer == null || renderer.skeletonDataAsset != skeletonDataAsset) return false;

        var meshRenderer = prefab.GetComponentInChildren<MeshRenderer>(true);
        if (meshRenderer == null) return false;
        var mats = meshRenderer.sharedMaterials;
        if (mats == null || mats.Length == 0 || mats.Any(m => m == null)) return false;

        return true;
    }

    private static void FixMaterialsToUrpPma(AtlasAssetBase[] atlasAssets)
    {
        Shader urpShader = Shader.Find(UrpPmaShaderName);
        if (urpShader == null)
        {
            Debug.LogWarning($"[CharacterSpineSetup] 找不到 URP Spine shader：{UrpPmaShaderName}。请确认 urp-shaders 包已安装。");
            return;
        }

        var materialsTouched = new System.Collections.Generic.List<Material>();
        foreach (var atlas in atlasAssets)
        {
            if (atlas == null) continue;
            foreach (var mat in atlas.Materials)
            {
                if (mat == null) continue;
                if (mat.shader != urpShader)
                {
                    mat.shader = urpShader;
                    if (mat.HasProperty("_StraightAlphaInput"))
                        mat.SetFloat("_StraightAlphaInput", 0f);
                    EditorUtility.SetDirty(mat);
                    materialsTouched.Add(mat);
                }
            }
            EditorUtility.SetDirty(atlas);
        }

        if (materialsTouched.Count > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[CharacterSpineSetup] 已将 {materialsTouched.Count} 个 Spine 材质切换为 {UrpPmaShaderName}");
        }
    }

    private static void FixTextureImportSettings(AtlasAssetBase[] atlasAssets)
    {
        foreach (var atlas in atlasAssets)
        {
            if (atlas == null) continue;
            foreach (var mat in atlas.Materials)
            {
                if (mat == null || !mat.HasProperty("_MainTex")) continue;
                var tex = mat.GetTexture("_MainTex");
                if (tex == null) continue;

                string path = AssetDatabase.GetAssetPath(tex);
                if (string.IsNullOrEmpty(path)) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                bool changed = false;
                if (!importer.sRGBTexture)
                {
                    importer.sRGBTexture = true;
                    changed = true;
                }
                if (!importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = true;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                    Debug.Log($"[CharacterSpineSetup] 已修复贴图导入设置（sRGB + Mipmap）：{path}");
                }
            }
        }
    }
}
