using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusicalSprite.Editor
{
    /// <summary>
    /// 场景道具一键生成器：用于外部修改 .unity 后 Unity 没有自动刷新时，
    /// 在编辑器内部重新生成 tree_2~tree_5 测试实例并保存场景。
    ///
    /// 用法：
    ///   菜单 Tools / Musical-Sprite / 场景道具 / 生成测试树木 (tree_2~5)
    ///   运行后会在当前打开的场景中生成 4 个基于 BP_SceneProp.prefab 的实例，
    ///   并替换掉同名的旧实例（如果有）。
    /// </summary>
    public static class ScenePropSpawner
    {
        private const string PrefabPath = "Assets/Art/Prefabs/BP_SceneProp.prefab";

        private static readonly string[] SpritePaths =
        {
            "Assets/Art/tree_2.png",
            "Assets/Art/tree_3.png",
            "Assets/Art/tree_4.png",
            "Assets/Art/tree_5.png",
        };

        private static readonly Vector3[] RootPositions =
        {
            new Vector3(0.54f, -4.7f, 7.3f),
            new Vector3(3.54f, -4.7f, 7.3f),
            new Vector3(6.54f, -4.7f, 7.3f),
            new Vector3(9.54f, -4.7f, 7.3f),
        };

        [MenuItem("Tools/Musical-Sprite/场景道具/生成测试树木 (tree_2~5)", false, 2100)]
        private static void SpawnTreeProps()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[ScenePropSpawner] 找不到预制体：{PrefabPath}");
                return;
            }

            var sprites = new List<Sprite>();
            foreach (var path in SpritePaths)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    Debug.LogError($"[ScenePropSpawner] 找不到贴图：{path}");
                    return;
                }
                sprites.Add(sprite);
            }

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("[ScenePropSpawner] 当前没有打开有效场景。");
                return;
            }

            // 标记场景已脏，后续可保存
            Undo.SetCurrentGroupName("Spawn tree test props");
            int group = Undo.GetCurrentGroup();

            // 清理同名旧实例（无论是否来自外部 YAML）
            var rootObjects = scene.GetRootGameObjects();
            foreach (var go in rootObjects)
            {
                if (go == null) continue;
                if (go.name == "tree_2_Prop" || go.name == "tree_3_Prop" ||
                    go.name == "tree_4_Prop" || go.name == "tree_5_Prop")
                {
                    Undo.DestroyObjectImmediate(go);
                }
            }

            // 子物体 "Sprite" 在预制体里的局部姿态（与 tree_1_Prop 立着姿态一致）
            Vector3 spriteLocalPos = new Vector3(3.12f, 2.95f, -0.78f);
            Vector3 spriteLocalScale = new Vector3(0.61993f, 0.61993f, 0.61993f);
            Quaternion spriteLocalRot = Quaternion.Euler(-26.29f, 0f, 0f);

            for (int i = 0; i < 4; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(instance, $"Create tree_{i + 2}_Prop");

                instance.name = $"tree_{i + 2}_Prop";
                instance.transform.SetPositionAndRotation(RootPositions[i], Quaternion.Euler(0.166f, 180.316f, 0f));
                instance.transform.localScale = Vector3.one;

                // 关闭 billboard，使用烘焙好的立着姿态
                var billboard = instance.GetComponent<ScenePropBillboard>();
                if (billboard != null)
                    billboard.enableBillboard = false;

                // 找到子 Sprite 并设置姿态与贴图
                var spriteRoot = instance.transform.Find("Sprite");
                if (spriteRoot != null)
                {
                    spriteRoot.localPosition = spriteLocalPos;
                    spriteRoot.localRotation = spriteLocalRot;
                    spriteRoot.localScale = spriteLocalScale;

                    var sr = spriteRoot.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        sr.sprite = sprites[i];
                        sr.sortingOrder = -50;
                        sr.size = new Vector2(20.48f, 10.24f);
                    }
                }
                else
                {
                    Debug.LogWarning($"[ScenePropSpawner] {instance.name} 下找不到 Sprite 子物体。");
                }
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ScenePropSpawner] 已生成 tree_2~tree_5 四个测试实例。请按 Ctrl+S 保存场景。");
        }
    }
}
