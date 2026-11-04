using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicalSprite.Terrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusicalSprite.EditorTools
{
    /// <summary>一次性、可重复运行的场景接入工具；只处理 SampleScene 的世界外观。</summary>
    public static class WorldVisualSceneInstaller
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string Art = "Assets/Art/";
        private const string SetDir = "Assets/WorldVisualSets";
        private const string RootName = "WorldVisualAssets_Codex";

        [MenuItem("Tools/Musical-Sprite/World Visuals/Install A-B Scene Assets")]
        public static void Install()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (GameObject.Find(RootName) != null)
            {
                Debug.LogError("[WorldVisuals] 已接入过，停止以避免覆盖手动摆放。需要重建时先恢复备份场景。");
                return;
            }

            Directory.CreateDirectory(SetDir);
            AssetDatabase.Refresh();
            var setA = BuildSet('A');
            var setB = BuildSet('B');

            var root = new GameObject(RootName).transform;
            var a = NewGroup("PlayerA_Territory", root);
            var b = NewGroup("PlayerB_Territory", root);
            NewGroup("Neutral_Special", root);
            var feverRoot = NewGroup("Fever_IndependentOfSeam", root);

            // 旧树的位置保留。成对实例重叠摆放，由同一世界坐标中缝逐像素接管可见部分。
            for (int i = 1; i <= 5; i++)
            {
                var old = GameObject.Find($"tree_{i}_Prop");
                if (old == null) continue;
                var copy = UnityEngine.Object.Instantiate(old);
                copy.name = $"tree_{i}_B_Prop";
                copy.transform.SetParent(b, true);
                SetSprite(copy, $"tree_{i}_B.png");
                SetOwnership(copy, WorldVisualOwnership.Kind.PlayerB);

                old.name = $"tree_{i}_A_Prop";
                old.transform.SetParent(a, true);
                SetSprite(old, $"tree_{i}_A.png");
                SetOwnership(old, WorldVisualOwnership.Kind.PlayerA);
            }

            // 新画的图按每张一个物件建立，可在场景内直接移动。初始放在远景；
            // A/B 同一编号共用锚点，之后可分别微调。
            string[] additions = { "2.1", "2.2", "2.3", "2.4", "2.5", "2.6", "2.7" };
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Prefabs/BP_SceneProp.prefab");
            if (prefab == null) throw new InvalidOperationException("缺少 BP_SceneProp.prefab");
            for (int i = 0; i < additions.Length; i++)
            {
                string id = additions[i];
                float x = -5.4f + i * 1.8f;
                for (int side = 0; side < 2; side++)
                {
                    char letter = side == 0 ? 'A' : 'B';
                    string file = id == "2.4" && letter == 'A' ? "tree_2.4 _A.png" : $"tree_{id}_{letter}.png";
                    var sprite = LoadSprite(Art + file);
                    if (sprite == null) continue;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    instance.name = $"tree_{id}_{letter}_Prop";
                    instance.transform.SetParent(side == 0 ? a : b, true);
                    instance.transform.position = new Vector3(x, -4.7f, 5.4f);
                    var billboard = instance.GetComponent<MusicalSprite.ScenePropBillboard>();
                    if (billboard != null) billboard.enableBillboard = false;
                    var visual = instance.GetComponentInChildren<SpriteRenderer>();
                    if (visual != null)
                    {
                        visual.sprite = sprite;
                        visual.sortingOrder = -50;
                        visual.transform.localPosition = new Vector3(0f, 2.95f, 0f);
                        visual.transform.localRotation = Quaternion.Euler(-26.29f, 0f, 0f);
                        visual.transform.localScale = Vector3.one * 0.62f;
                    }
                    SetOwnership(instance, side == 0 ? WorldVisualOwnership.Kind.PlayerA : WorldVisualOwnership.Kind.PlayerB);
                }
            }

            var center = UnityEngine.Object.FindFirstObjectByType<BattleCenterLine>();
            var binder = root.gameObject.AddComponent<ArenaWorldVisualBinder>();
            binder.playerA = setA;
            binder.playerB = setB;
            binder.centerLine = center;
            binder.leftStage = FindRenderer("LeftBand_Stage");
            binder.rightStage = FindRenderer("RightBand_Stage");

            MakeFeverSide(0, feverRoot, setA);
            MakeFeverSide(1, feverRoot, setB);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[WorldVisuals] A/B 场景件、外观插槽和双侧分层过热已接入 SampleScene。");
        }

        private static PlayerWorldVisualSetSO BuildSet(char side)
        {
            string path = $"{SetDir}/PlayerWorld_{side}.asset";
            var set = AssetDatabase.LoadAssetAtPath<PlayerWorldVisualSetSO>(path);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<PlayerWorldVisualSetSO>();
                AssetDatabase.CreateAsset(set, path);
            }
            set.groundMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Art}dimian_1_{side}.png");
            set.stageMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Art}yuantai_1_{side}.png");
            var props = new List<Sprite>();
            for (int i = 1; i <= 5; i++) AddSprite(props, $"tree_{i}_{side}.png");
            foreach (string id in new[] { "2.1", "2.2", "2.3", "2.4", "2.5", "2.6", "2.7" })
                AddSprite(props, id == "2.4" && side == 'A' ? "tree_2.4 _A.png" : $"tree_{id}_{side}.png");
            set.sceneProps = props.ToArray();
            set.feverScenery = LoadSprite($"{Art}1__fever{side}.png");
            set.superFeverScenery = LoadSprite($"{Art}1__super_fever{side}.png");
            // 当前仅有过热测试图，普通背景先复用同一临时图；日后直接换此插槽。
            set.normalBackground = set.feverScenery;
            EditorUtility.SetDirty(set);
            return set;
        }

        private static void AddSprite(List<Sprite> list, string name)
        {
            var sprite = LoadSprite(Art + name);
            if (sprite != null) list.Add(sprite);
        }

        private static Sprite LoadSprite(string path)
        {
            if (!File.Exists(path)) { Debug.LogWarning("[WorldVisuals] 缺少贴图 " + path); return null; }
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.spritePivot = new Vector2(0.5f, 0f);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Transform NewGroup(string name, Transform parent)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        private static void SetSprite(GameObject obj, string file)
        {
            var sprite = LoadSprite(Art + file);
            var renderer = obj.GetComponentInChildren<SpriteRenderer>();
            if (renderer != null && sprite != null) renderer.sprite = sprite;
        }

        private static void SetOwnership(GameObject obj, WorldVisualOwnership.Kind kind)
        {
            var ownership = obj.GetComponent<WorldVisualOwnership>();
            if (ownership == null) ownership = obj.AddComponent<WorldVisualOwnership>();
            ownership.kind = kind;
            ownership.Apply();
        }

        private static Renderer FindRenderer(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.GetComponent<Renderer>() : null;
        }

        private static void MakeFeverSide(int side, Transform parent, PlayerWorldVisualSetSO set)
        {
            var sideRoot = NewGroup(side == 0 ? "Fever_A" : "Fever_B", parent);
            sideRoot.position = new Vector3(side == 0 ? -4f : 4f, -1.7f, 0f);
            var controller = sideRoot.gameObject.AddComponent<FeverSceneryController>();
            controller.side = side;
            controller.background = MakeFeverSprite("Background_Static", set.normalBackground, sideRoot, 5.6f);
            controller.background.localPosition = new Vector3(0f, 0f, 5.6f);
            controller.feverLayer = MakeFeverSprite("Fever_Rising", set.feverScenery, sideRoot, 5.1f);
            controller.superFeverLayer = MakeFeverSprite("SuperFever_Rising_Front", set.superFeverScenery, sideRoot, 4.7f);
            controller.feverShownLocalPosition = new Vector3(0f, 0.55f, 5.1f);
            controller.feverHiddenLocalPosition = new Vector3(0f, -4f, 5.1f);
            controller.superShownLocalPosition = new Vector3(0f, 1.1f, 4.7f);
            controller.superHiddenLocalPosition = new Vector3(0f, -4f, 4.7f);
            controller.feverLayer.localPosition = controller.feverHiddenLocalPosition;
            controller.superFeverLayer.localPosition = controller.superHiddenLocalPosition;
        }

        private static Transform MakeFeverSprite(string name, Sprite sprite, Transform parent, float z)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, -4f, z);
            // 临时整张图有很大的透明留白；纵向放大保证三层在同图调试时可分辨。
            go.transform.localScale = new Vector3(0.38f, 0.75f, 0.38f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "Materials/M_SceneProp.mat");
            renderer.sortingOrder = -50;
            return go.transform;
        }
    }
}
