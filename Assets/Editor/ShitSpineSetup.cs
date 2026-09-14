using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Spine.Unity;

/// <summary>
/// 一键把屎屎的 Spine 4.3 导出资源接入为角色外观 prefab，并填进 Aibo_2_Shit.asset。
/// 操作：Unity 顶部菜单 -> Musical Sprite -> Setup Shit Spine Character
/// </summary>
public static class ShitSpineSetup
{
    private const string Folder = "Assets/Art/Characters/Shit";
    private const string JsonPath = Folder + "/shit.json";
    private const string AtlasPath = Folder + "/shit.atlas.txt";
    private const string SkeletonDataPath = Folder + "/shit_SkeletonData.asset";
    private const string PrefabPath = Folder + "/Shit.prefab";
    private const string CharacterDataPath = "Assets/Data/Characters/Aibo_2_Shit.asset";

    private const string IdleAnimationName = "Aibo_2_Shit_00_Idle";
    private const string UrpPmaShaderName = "Universal Render Pipeline/Spine/Skeleton";
    // 屎屎 Spine 导出 scale 为 0.01，骨架本身尺寸较大；先放大到 1.0 保证场上可见，之后可在 Inspector 微调。
    private const float PrefabRootScale = 1.0f;

    // 移到 Danger Zone 子菜单，避免顶部一屏误点；点击后仍需二次确认，且 prefab 已正常会自动跳过。
    [MenuItem("Musical Sprite/Danger Zone/Setup Shit Spine Character (重建 Spine 资产)", priority = 300)]
    public static void Run() => Run(false);

    /// <summary>
    /// 重建屎屎 Spine prefab。
    /// force=false 时：若现有 Shit.prefab 引用完整且 SkeletonData 可加载，则直接跳过（防误点弄坏好的 prefab）。
    /// 这是破坏式操作（删除并重建 SkeletonDataAsset、覆盖 Shit.prefab），仅在重新导出 Spine 美术后使用。
    /// 强制重建：在 Editor 控制台执行 ShitSpineSetup.Run(true)
    /// </summary>
    public static void Run(bool force)
    {
        // 0. 危险操作二次确认（默认取消）
        if (!EditorUtility.DisplayDialog(
            "危险操作：重建屎屎 Spine",
            "此操作会删除并重建 Shit_SkeletonData.asset，并覆盖 Shit.prefab。\n\n仅在「重新用 Spine 编辑器导出了屎屎美术」后才需要。\n正常项目请勿点击，否则可能把正常的 prefab 弄成绿块。",
            "我确定要重建", "取消"))
        {
            Debug.Log("[ShitSpineSetup] 已取消，未做任何改动。");
            return;
        }

        // 1. 若现有 prefab 已正常且非强制，直接跳过（防误点把好的 prefab 弄坏）
        if (!force && PrefabAlreadyValid())
        {
            Debug.LogWarning("[ShitSpineSetup] 现有 Shit.prefab 引用完整（SkeletonData + 材质），无需重建，已安全跳过。" +
                             "如需强制重建请执行 ShitSpineSetup.Run(true)。");
            return;
        }

        // 2. 强制重新导入：先删除旧的 SkeletonDataAsset，让 Spine 自动重建 AtlasAsset + SkeletonDataAsset
        if (AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(SkeletonDataPath) != null)
        {
            bool deleted = AssetDatabase.DeleteAsset(SkeletonDataPath);
            Debug.Log($"[ShitSpineSetup] 删除旧 SkeletonDataAsset: {(deleted ? "成功" : "失败或不存在")}");
        }

        AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(JsonPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        // 2. 读取重建后的 SkeletonDataAsset
        var skeletonDataAsset = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(SkeletonDataPath);
        if (skeletonDataAsset == null)
        {
            Debug.LogError($"[ShitSpineSetup] 重新导入后仍未生成 {SkeletonDataPath}，请检查 Console 是否有 Spine 导入错误。");
            return;
        }

        // 确认 atlas 已正确关联
        var atlasAssets = skeletonDataAsset.atlasAssets;
        if (atlasAssets == null || atlasAssets.Length == 0 || atlasAssets.Any(a => a == null))
        {
            Debug.LogError("[ShitSpineSetup] SkeletonDataAsset 的 atlasAssets 仍为空，Spine 自动导入失败。");
            return;
        }

        Debug.Log($"[ShitSpineSetup] SkeletonDataAsset 已生成，关联 atlas 数：{atlasAssets.Length}");

        // 3. 修正材质为 URP PMA（项目用 URP，默认 Spine/Skeleton 会粉/不渲染）
        FixMaterialsToUrpPma(atlasAssets);

        // 4. 用 Spine 编辑器 API 创建带 SkeletonRenderer + SkeletonAnimation 的游戏对象
        GameObject spineGo = InstantiateSpineGameObject(skeletonDataAsset);
        if (spineGo == null)
        {
            Debug.LogError("[ShitSpineSetup] Spine 实例化失败，未创建 prefab。");
            return;
        }

        var skeletonAnim = spineGo.GetComponent<SkeletonAnimation>();
        if (skeletonAnim != null)
        {
            skeletonAnim.AnimationName = IdleAnimationName;
            skeletonAnim.loop = true;
        }

        // 5. 套一个带缩放的根节点，使屎屎尺寸与场上 cube 匹配
        var root = new GameObject("Shit");
        root.transform.localScale = Vector3.one * PrefabRootScale;
        spineGo.transform.SetParent(root.transform, false);
        spineGo.transform.localPosition = Vector3.zero;
        spineGo.transform.localRotation = Quaternion.identity;

        // 关键：保存 prefab 前强制标记所有对象 dirty，防止引用丢失
        var spineRenderer = spineGo.GetComponent<SkeletonRenderer>();
        var spineAnim = spineGo.GetComponent<SkeletonAnimation>();
        if (spineRenderer != null && spineRenderer.skeletonDataAsset == null)
            spineRenderer.skeletonDataAsset = skeletonDataAsset;
        if (spineAnim != null && spineAnim.skeletonDataAsset == null)
            spineAnim.skeletonDataAsset = skeletonDataAsset;
        if (spineRenderer != null) EditorUtility.SetDirty(spineRenderer);
        if (spineAnim != null) EditorUtility.SetDirty(spineAnim);
        EditorUtility.SetDirty(spineGo);
        EditorUtility.SetDirty(root);
        AssetDatabase.SaveAssets();

        // 6. 存为 prefab
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool prefabSuccess);
        GameObject.DestroyImmediate(root);

        if (!prefabSuccess || prefab == null)
        {
            Debug.LogError($"[ShitSpineSetup] 保存 prefab 失败：{PrefabPath}");
            return;
        }

        // 6.5 验证 prefab 引用确实写进去了（防止再次变成绿色方块）
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (!ValidatePrefabReferences(prefab, skeletonDataAsset))
        {
            Debug.LogError("[ShitSpineSetup] Prefab 引用校验失败，请手动检查 Shit.prefab 的 SkeletonRenderer.skeletonDataAsset 是否为空。");
            return;
        }

        Debug.Log($"[ShitSpineSetup] Prefab 已保存并校验通过：{PrefabPath}");

        // 7. 填进角色数据 ScriptableObject
        var characterData = AssetDatabase.LoadAssetAtPath<CharacterDataSO>(CharacterDataPath);
        if (characterData == null)
        {
            Debug.LogError($"[ShitSpineSetup] 找不到角色数据：{CharacterDataPath}");
            return;
        }

        Undo.RecordObject(characterData, "Assign Shit modelPrefab");
        characterData.modelPrefab = prefab;
        EditorUtility.SetDirty(characterData);
        AssetDatabase.SaveAssets();

        Debug.Log($"[ShitSpineSetup] 已把 prefab 填入 {CharacterDataPath} 的 modelPrefab。进 Play 后屎屎应替换黄色方块。");
        EditorGUIUtility.PingObject(prefab);
    }

    /// <summary>
    /// 创建带 SkeletonRenderer + SkeletonAnimation 的游戏对象。
    /// 先尝试 Spine 编辑器内部 API；若内部 Initialize 抛异常，则 fallback 到手动 AddComponent + Initialize(true)。
    /// </summary>
    private static GameObject InstantiateSpineGameObject(SkeletonDataAsset skeletonDataAsset)
    {
        // 预加载：确认 SkeletonData 和 AnimationStateData 都已可用
        var skeletonData = skeletonDataAsset.GetSkeletonData(true);
        if (skeletonData == null)
        {
            Debug.LogError("[ShitSpineSetup] SkeletonDataAsset.GetSkeletonData() 返回 null，请检查 shit.json 与 atlas 是否兼容。");
            return null;
        }
        var stateData = skeletonDataAsset.GetAnimationStateData();
        if (stateData == null)
        {
            Debug.LogError("[ShitSpineSetup] SkeletonDataAsset.GetAnimationStateData() 返回 null。");
            return null;
        }
        Debug.Log($"[ShitSpineSetup] SkeletonData 预加载成功，动画数：{skeletonData.Animations.Count}");

        // 方案 A：反射调用 Spine 编辑器菜单用的 InstantiateSkeletonAnimation
        SkeletonAnimation anim = null;
        try
        {
            anim = InvokeEditorInstantiation(skeletonDataAsset);
        }
        catch (System.Exception e)
        {
            var inner = e.InnerException ?? e;
            Debug.LogWarning($"[ShitSpineSetup] 方案 A（Spine 编辑器 API）失败，转用手动兜底。\n原因：{inner.Message}\n堆栈：{inner.StackTrace}");
        }

        // 方案 B：手动创建并初始化
        if (anim == null)
        {
            anim = ManualCreateSkeletonAnimation(skeletonDataAsset);
        }

        if (anim == null)
        {
            Debug.LogError("[ShitSpineSetup] Spine 实例化失败，未创建 prefab。");
            return null;
        }

        var renderer = anim.GetComponent<SkeletonRenderer>();
        Debug.Log($"[ShitSpineSetup] Spine GameObject 已创建。renderer valid={(renderer != null && renderer.IsValid)}, anim valid={anim.IsValid}");
        return anim.gameObject;
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
            throw new System.InvalidOperationException("找不到 EditorInstantiation.InstantiateSkeletonAnimation(SkeletonDataAsset,string,bool,bool) 方法。");

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
            // 先显式绑定数据再 Initialize，防止 Initialize 后引用被清空
            anim.skeletonDataAsset = skeletonDataAsset;
            // 关键：编辑器模式下用 true，让组件立即创建 Skeleton/AnimationState
            anim.Initialize(true);
            // Initialize 后再次确认，某些 spine-unity 版本会重置字段
            if (anim.skeletonDataAsset == null) anim.skeletonDataAsset = skeletonDataAsset;
            if (renderer.skeletonDataAsset == null) renderer.skeletonDataAsset = skeletonDataAsset;
            EditorUtility.SetDirty(anim);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(go);
            return anim;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ShitSpineSetup] 方案 B（手动创建）也失败：{e.Message}\n{e.StackTrace}");
            return null;
        }
    }

    private static bool ValidatePrefabReferences(GameObject prefab, SkeletonDataAsset expectedData)
    {
        if (prefab == null || expectedData == null) return false;
        var renderer = prefab.GetComponentInChildren<SkeletonRenderer>(true);
        if (renderer == null)
        {
            Debug.LogError("[ShitSpineSetup] 校验失败：prefab 下找不到 SkeletonRenderer。");
            return false;
        }
        if (renderer.skeletonDataAsset != expectedData)
        {
            Debug.LogError($"[ShitSpineSetup] 校验失败：SkeletonRenderer.skeletonDataAsset 不匹配。期望={expectedData.name}，实际={(renderer.skeletonDataAsset == null ? "null" : renderer.skeletonDataAsset.name)}");
            return false;
        }
        var anim = prefab.GetComponentInChildren<SkeletonAnimation>(true);
        if (anim == null)
        {
            Debug.LogWarning("[ShitSpineSetup] 校验警告：prefab 下找不到 SkeletonAnimation（不影响渲染，但无动画）。");
        }
        return true;
    }

    /// <summary>
    /// 现有 Shit.prefab 是否引用完整且可正常驱动：SkeletonRenderer 有 SkeletonData、MeshRenderer 材质非空。
    /// 用于防误点保护——已正常就不重建。
    /// </summary>
    private static bool PrefabAlreadyValid()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return false;

        var renderer = prefab.GetComponentInChildren<SkeletonRenderer>(true);
        if (renderer == null || renderer.skeletonDataAsset == null) return false;

        var meshRenderer = prefab.GetComponentInChildren<MeshRenderer>(true);
        if (meshRenderer == null) return false;
        var mats = meshRenderer.sharedMaterials;
        if (mats == null || mats.Length == 0 || mats.Any(m => m == null)) return false;

        return true;
    }

    private static Type FindSpineEditorInstantiationType()
    {
        // Spine 4.3 中 EditorInstantiation 是 Spine.Unity.Editor 命名空间下的独立 static class，
        // 与 AssetUtility 同级（不是 AssetUtility 的嵌套类）。嵌套类型用 '+'，同级类型用 '.'。
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType("Spine.Unity.Editor.EditorInstantiation");
                if (t != null) return t;

                // 向后兼容：某些旧版 spine-unity 可能把它作为 AssetUtility 的嵌套类
                t = asm.GetType("Spine.Unity.Editor.AssetUtility+EditorInstantiation");
                if (t != null) return t;
            }
            catch { /* ignore reflection errors */ }
        }
        return null;
    }

    private static void FixMaterialsToUrpPma(AtlasAssetBase[] atlasAssets)
    {
        Shader urpShader = Shader.Find(UrpPmaShaderName);
        if (urpShader == null)
        {
            Debug.LogWarning($"[ShitSpineSetup] 找不到 URP Spine shader：{UrpPmaShaderName}。请确认 urp-shaders 包已安装。");
            return;
        }

        var materialsTouched = new List<Material>();
        foreach (var atlas in atlasAssets)
        {
            if (atlas == null) continue;
            foreach (var mat in atlas.Materials)
            {
                if (mat == null) continue;
                if (mat.shader != urpShader)
                {
                    mat.shader = urpShader;
                    // PMA 输入（关闭 Straight Alpha）
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
            Debug.Log($"[ShitSpineSetup] 已将 {materialsTouched.Count} 个 Spine 材质切换为 {UrpPmaShaderName}");
        }
    }
}
