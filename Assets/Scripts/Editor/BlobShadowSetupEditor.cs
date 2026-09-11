#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 编辑器工具：为场景中的乐队角色方块批量添加 BlobShadow 组件。
/// 这些方块在旧场景中原本没有 CharacterCubeMarker，运行时才由 CharacterBattleSystem 迁移；
/// 本工具在编辑器下直接给它们挂 BlobShadow，方便预览光照/阴影效果。
/// </summary>
public static class BlobShadowSetupEditor
{
    [MenuItem("Tools/Musical Sprite/为角色方块添加 BlobShadow")]
    private static void AttachBlobShadowsToBandMembers()
    {
        // 优先从 LeftBand/RightBand 根节点下找；找不到则全局按名字匹配
        var leftRoot = GameObject.Find("LeftBand");
        var rightRoot = GameObject.Find("RightBand");

        System.Collections.Generic.IEnumerable<GameObject> targets;
        if (leftRoot != null || rightRoot != null)
        {
            var set = new System.Collections.Generic.HashSet<GameObject>();
            if (leftRoot != null)
                foreach (var t in leftRoot.GetComponentsInChildren<Transform>(true))
                    set.Add(t.gameObject);
            if (rightRoot != null)
                foreach (var t in rightRoot.GetComponentsInChildren<Transform>(true))
                    set.Add(t.gameObject);
            targets = set.Where(go => go.name.StartsWith("LeftBand_") || go.name.StartsWith("RightBand_"));
        }
        else
        {
            targets = GameObject.FindObjectsOfType<GameObject>()
                                .Where(go => go.name.StartsWith("LeftBand_") || go.name.StartsWith("RightBand_"));
        }

        int added = 0;
        int skipped = 0;

        // 加载默认材质
        Material defaultMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Materials/M_BlobShadow.mat");

        foreach (var go in targets)
        {
            // 只给有 BoxCollider 的实体方块加（避免误伤到 UI、指示器等空节点）
            if (go.GetComponent<BoxCollider>() == null && go.GetComponent<MeshFilter>() == null)
            {
                skipped++;
                continue;
            }

            var blob = go.GetComponent<BlobShadow>();
            if (blob == null)
            {
                blob = go.AddComponent<BlobShadow>();
                added++;
            }
            else
            {
                skipped++;
            }

            if (blob.shadowMaterial == null && defaultMat != null)
            {
                Undo.RecordObject(blob, "Assign BlobShadow material");
                blob.shadowMaterial = defaultMat;
                EditorUtility.SetDirty(blob);
            }
        }

        Debug.Log($"[BlobShadowSetupEditor] 已为 {added} 个角色方块添加 BlobShadow，跳过 {skipped} 个（已存在或无碰撞体）。材质 = {defaultMat}");
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
}
#endif
