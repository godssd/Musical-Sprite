using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 冲击波发光配套：创建 / 修复场景里的 Global Volume + Bloom。
/// 之前摄像机虽然开了后处理（m_RenderPostProcessing=1），但场景里没有任何 Volume，
/// 所以 Bloom 从未生效 —— 这正是"冲击波自身不发光、也无法对周围形成光效"的根因。
///
/// 行为：
///   - 编辑器加载后自动检查一次（幂等：已有含 Bloom 的 Global Volume 就跳过，不重复创建）。
///   - 菜单 Tools/Musical-Sprite/创建冲击波 Bloom Volume 可手动触发。
///   - VolumeProfile 落盘到 Assets/Settings/ShockwaveBloomProfile.asset，场景保存。
/// 回退：删除场景里的 "Global Volume (Shockwave Bloom)" 物体 + 删掉该 profile 资产即可。
/// </summary>
public static class ShockwaveBloomSetup
{
    const string ProfilePath = "Assets/Settings/ShockwaveBloomProfile.asset";
    const string GoName = "Global Volume (Shockwave Bloom)";

    [MenuItem("Tools/Musical-Sprite/创建冲击波 Bloom Volume")]
    public static void MenuEnsure()
    {
        Ensure(true);
    }

    /// <summary>
    /// 把"推荐亮度参数"写入场景里所有 ShockwaveMeshGenerator 并落盘。
    /// 原因：opacity / edgeGlow 等字段早就被序列化进场景了，改 C# 里的默认值
    /// 不会影响已存在的组件（Unity 只在新建组件时才用默认值），必须显式写入。
    /// 注意：不会动 fadePower / gradientBalance / colorDeep / colorTip 等你手动调过的外观值。
    /// </summary>
    [MenuItem("Tools/Musical-Sprite/重新绑定 WALL.fbx 到冲击波")]
    public static void RebindWallMesh()
    {
        const string path = "Assets/Art/VFX/WALL.fbx";
        Mesh wallMesh = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is Mesh m) { wallMesh = m; break; }
        }
        if (wallMesh == null)
        {
            Debug.LogError("[ShockwaveBloomSetup] 在 " + path + " 里没找到 Mesh，请确认文件存在且包含 Mesh。");
            return;
        }

        int count = 0;
        foreach (var g in Object.FindObjectsByType<ShockwaveMeshGenerator>(FindObjectsSortMode.None))
        {
            if (g == null) continue;
            Undo.RecordObject(g, "Rebind WALL.fbx");
            g.useImportedMesh = true;
            g.importedMesh = wallMesh;
#if UNITY_EDITOR
            g.importedMeshAssetPath = path;
#endif
            g.autoFit = true;
            g.Refresh();
            EditorUtility.SetDirty(g);
            count++;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();
        }
        Debug.Log("[ShockwaveBloomSetup] 已为 " + count + " 个冲击波墙重新绑定 " + path + "，并启用 autoFit。");
    }

    [MenuItem("Tools/Musical-Sprite/冲击波调试红色 (开关)")]
    public static void ToggleDebugSolid()
    {
        bool anyOn = false;
        foreach (var g in Object.FindObjectsByType<ShockwaveMeshGenerator>(FindObjectsSortMode.None))
        {
            if (g == null) continue;
            var mr = g.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) continue;
            bool isOn = mr.sharedMaterial.IsKeywordEnabled("_DEBUG_SOLID");
            anyOn |= isOn;
        }
        bool setOn = !anyOn;
        int count = 0;
        foreach (var g in Object.FindObjectsByType<ShockwaveMeshGenerator>(FindObjectsSortMode.None))
        {
            if (g == null) continue;
            var mr = g.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) continue;
            if (setOn) mr.sharedMaterial.EnableKeyword("_DEBUG_SOLID");
            else mr.sharedMaterial.DisableKeyword("_DEBUG_SOLID");
            count++;
        }
        Debug.Log("[ShockwaveBloomSetup] 冲击波调试红色已 " + (setOn ? "开启" : "关闭") + "（" + count + " 个材质）。");
    }

    [MenuItem("Tools/Musical-Sprite/修复冲击波材质 (Reset to ShockwaveUnlit)")]
    public static void FixMaterials()
    {
        int fixedCount = 0;
        var shader = Shader.Find("MusicalSprite/ShockwaveUnlit");
        foreach (var g in Object.FindObjectsByType<ShockwaveMeshGenerator>(FindObjectsSortMode.None))
        {
            if (g == null) continue;
            var mr = g.GetComponent<MeshRenderer>();
            if (mr == null) continue;
            if (mr.sharedMaterial != null && mr.sharedMaterial.shader == shader) continue;
            Undo.RecordObject(mr, "Reset Shockwave Material");
            mr.sharedMaterial = new Material(shader);
            g.Refresh();
            EditorUtility.SetDirty(mr);
            fixedCount++;
        }
        if (fixedCount > 0)
        {
            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveOpenScenes();
            }
            Debug.Log("[ShockwaveBloomSetup] 已修复 " + fixedCount + " 个冲击波墙的材质为 MusicalSprite/ShockwaveUnlit。");
        }
        else
        {
            Debug.Log("[ShockwaveBloomSetup] 所有冲击波墙材质都已经是 MusicalSprite/ShockwaveUnlit，无需修复。");
        }
    }

    [MenuItem("Tools/Musical-Sprite/应用冲击波推荐亮度参数")]
    public static void ApplyRecommendedLook()
    {
        var gens = Object.FindObjectsByType<ShockwaveMeshGenerator>(FindObjectsSortMode.None);
        if (gens == null || gens.Length == 0)
        {
            Debug.LogWarning("[ShockwaveBloomSetup] 场景里没找到 ShockwaveMeshGenerator，先确认场景里有冲击波墙。");
            return;
        }

        bool playing = Application.isPlaying;

        foreach (var g in gens)
        {
            if (g == null) continue;
            if (!playing) Undo.RecordObject(g, "Apply Shockwave Recommended Look");
            g.fadePower = 1.0f;      // 关键：2.6 会让整条墙八成区域全透明，是"看不到冲击波"的元凶
            g.opacity = 0.70f;
            g.edgeGlow = 0.35f;
            g.glowIntensity = 2.2f;
            g.glowArch = 1.5f;
            g.glowFront = 1.4f;
            g.glowTailCut = 0.05f;
            g.Refresh();
            if (!playing) EditorUtility.SetDirty(g);
        }

        // 同步调亮场景里已有的 Bloom（只改 Bloom 这一项，不动 profile 里其它 override）
        int bloomFixed = 0;
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {
            if (v == null || v.profile == null) continue;
            if (!v.profile.TryGet<Bloom>(out var bl)) continue;
            bl.threshold.Override(0.7f);
            bl.intensity.Override(0.85f);
            bl.scatter.Override(0.5f);
            if (!playing) EditorUtility.SetDirty(v.profile);
            bloomFixed++;
        }

        if (!playing)
        {
            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveOpenScenes();
            }
            Debug.Log("[ShockwaveBloomSetup] 已对 " + gens.Length + " 个冲击波墙应用推荐亮度参数"
                + "（fadePower 1.0 / opacity 0.70 / edgeGlow 0.35 / glowIntensity 2.2 / glowArch 1.5 / glowFront 1.4 / glowTailCut 0.05），"
                + "并调整了 " + bloomFixed + " 个 Bloom（threshold 0.7 / intensity 0.85 / scatter 0.5）。");
        }
        else
        {
            Debug.Log("[ShockwaveBloomSetup] 已在 Play 模式下临时应用亮度参数（可直接看效果），但**不会保存**。退出 Play 模式后请再点一次本菜单落盘。");
        }
    }

    [InitializeOnLoadMethod]
    static void Auto()
    {
        // 延迟到场景就绪后再检查，避免在域重载 / 场景未加载时误操作
        EditorApplication.delayCall += () =>
        {
            if (!Application.isPlaying) Ensure(false);
        };
    }

    static void Ensure(bool verbose)
    {
        // 已存在含 Bloom 的 Global Volume -> 跳过（幂等）
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {
            if (v == null || !v.isGlobal || v.profile == null) continue;
            if (v.profile.TryGet<Bloom>(out _))
            {
                if (verbose) Debug.Log("[ShockwaveBloomSetup] 场景中已存在含 Bloom 的 Global Volume，跳过。");
                return;
            }
        }

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        if (!profile.TryGet<Bloom>(out var bloom))
        {
            bloom = profile.Add<Bloom>();
            bloom.threshold.Override(0.9f);          // 只有亮过 0.9 的像素才泛光（降低泛光面积）
            bloom.intensity.Override(0.6f);          // 泛光强度（当前过曝，先降到 0.6）
            bloom.scatter.Override(0.45f);           // 扩散，越小越收敛
            bloom.clamp.Override(1.0f);              // 限制最亮像素，防过曝
            bloom.tint.Override(Color.white);
            bloom.highQualityFiltering.Override(false); // 移动端性能优先
            EditorUtility.SetDirty(profile);
        }
        AssetDatabase.SaveAssets();

        var go = GameObject.Find(GoName);
        if (go == null) go = new GameObject(GoName);
        var vol = go.GetComponent<Volume>();
        if (vol == null) vol = go.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.profile = profile;

        EditorUtility.SetDirty(go);

        // 没有有效场景（例如刚打开工程、还没加载场景）时只落盘 profile，不碰场景
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();
        }

        Debug.Log("[ShockwaveBloomSetup] 已创建 Global Volume + Bloom（profile: " + ProfilePath + "），冲击波发光需要它才会泛光。", go);
    }
}
