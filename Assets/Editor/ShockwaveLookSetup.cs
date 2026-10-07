using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 冲击波 P1（发光层 + 渲染层级）的一键应用 / 关闭。
///
/// 为什么需要菜单：C# 字段默认值对「已经序列化进场景的组件」无效（Unity 只在新建组件时取默认值），
/// 而 MeshRenderer.sortingOrder 只在 SyncMaterial() 被调用时才写入 —— Edit 模式下组件不会自动 Awake。
/// 所以在 Edit 模式点一次菜单把值写进场景并落盘，是最可靠的做法。
///
/// 安全约束：SaveAssets / SaveOpenScenes / Undo 在 Play 模式下禁止调用（会抛 InvalidOperationException），
/// 因此统一用 Application.isPlaying 分支：Play 模式只改内存看效果，不做任何落盘。
/// </summary>
public static class ShockwaveLookSetup
{
    private const string Root = "Tools/Musical-Sprite/";

    [MenuItem(Root + "冲击波 P1：应用发光 + 层级")]
    public static void ApplyP1()
    {
        var gens = Object.FindObjectsByType<ShockwaveMeshGenerator>(FindObjectsSortMode.None);
        if (gens == null || gens.Length == 0)
        {
            Debug.LogWarning("[ShockwaveLookSetup] 场景里没找到 ShockwaveMeshGenerator。");
            return;
        }

        bool playing = Application.isPlaying;
        int n = 0;
        foreach (var g in gens)
        {
            if (g == null) continue;
            if (!playing) Undo.RecordObject(g, "Apply Shockwave P1");

            g.glowIntensity = 1.6f;          // >1 才能越过 Bloom 阈值泛光
            g.glowArch = 2.0f;
            g.glowFront = 1.6f;
            g.glowTailCut = 0.15f;
            g.glowColor = g.colorTip;        // 默认跟随各自的前沿色（红/蓝自动区分）
            g.sortingOrder = 20;             // 盖住地面花草等透明装饰
            g.Refresh();
            if (!playing) EditorUtility.SetDirty(g);
            n++;
        }

        // 场景里已有 Global Volume + Bloom（Assets/Settings/SampleSceneProfile.asset），只需调亮，不要新建
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

        if (playing)
        {
            Debug.Log("[ShockwaveLookSetup] Play 模式：已在内存中应用 P1（" + n + " 堵墙 / " + bloomFixed
                + " 个 Bloom），不会保存。退出 Play 后请再点一次菜单落盘。");
            return;
        }

        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();
        }

        Debug.Log("[ShockwaveLookSetup] 已应用 P1 并保存：glowIntensity 1.6 / glowArch 2.0 / glowFront 1.6"
            + " / glowTailCut 0.15 / sortingOrder 20（" + n + " 堵墙），Bloom threshold 0.7 / intensity 0.85 / scatter 0.5（"
            + bloomFixed + " 个）。");
    }

    [MenuItem(Root + "冲击波 P1：关闭发光（保留层级）")]
    public static void DisableGlow()
    {
        var gens = Object.FindObjectsByType<ShockwaveMeshGenerator>(FindObjectsSortMode.None);
        if (gens == null || gens.Length == 0) return;

        bool playing = Application.isPlaying;
        foreach (var g in gens)
        {
            if (g == null) continue;
            if (!playing) Undo.RecordObject(g, "Disable Shockwave Glow");
            g.glowIntensity = 0f;    // 0 = 第二 Pass 直接 discard，等于没加这个 Pass
            g.Refresh();
            if (!playing) EditorUtility.SetDirty(g);
        }

        if (playing)
        {
            Debug.Log("[ShockwaveLookSetup] Play 模式：已在内存中关闭发光，不保存。");
            return;
        }

        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();
        }
        Debug.Log("[ShockwaveLookSetup] 已关闭冲击波发光（glowIntensity = 0）并保存。");
    }
}
