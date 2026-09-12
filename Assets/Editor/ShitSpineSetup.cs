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

    [MenuItem("Musical Sprite/Setup Shit Spine Character", priority = 300)]
    public static void Run()
    {
        // 1. 强制重新导入：先删除旧的 SkeletonDataAsset，让 Spine 自动重建 AtlasAsset + SkeletonDataAsset
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

        // 6. 存为 prefab
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool prefabSuccess);
        GameObject.DestroyImmediate(root);

        if (!prefabSuccess || prefab == null)
        {
            Debug.LogError($"[ShitSpineSetup] 保存 prefab 失败：{PrefabPath}");
            return;
        }

        Debug.Log($"[ShitSpineSetup] Prefab 已保存：{PrefabPath}");

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
            // 关键：编辑器模式下用 true，让组件立即创建 Skeleton/AnimationState
            anim.Initialize(true);
            EditorUtility.SetDirty(anim);
            EditorUtility.SetDirty(go);
            return anim;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ShitSpineSetup] 方案 B（手动创建）也失败：{e.Message}\n{e.StackTrace}");
            return null;
        }
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
