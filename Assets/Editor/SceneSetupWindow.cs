using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using UnityEngine.UI;

namespace MusicalSprite.Editor
{
    /// <summary>
    /// 一键搭建对战音游测试场景。
    /// 顶部菜单：Tools > Musical Sprite > Setup Demo Scene
    /// </summary>
    public class SceneSetupWindow : EditorWindow
    {
        private static readonly string ScriptsFolder = "Assets/Scripts";
        private static readonly string MaterialsFolder = "Assets/Resources/Materials";
        private static readonly string PrefabsFolder = "Assets/Resources/Prefabs";
        private static readonly string BeatmapsFolder = "Assets/ScriptableObjects";

        private static readonly Color ArenaRed = new Color(0.9f, 0.3f, 0.3f, 1f);
        private static readonly Color ArenaBlue = new Color(0.3f, 0.4f, 0.9f, 1f);
        private static readonly Color CenterLinePink = new Color(1f, 0.4f, 0.7f, 1f);
        private static readonly Color NoteYellow = new Color(1f, 0.9f, 0.2f, 1f);
        private static readonly Color IndicatorBlue = new Color(0.2f, 0.6f, 1f, 1f);
        private static readonly Color IndicatorDim = new Color(0.1f, 0.2f, 0.4f, 1f);
        private static readonly Color MemberYellow = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color ProtagonistYellow = new Color(1f, 0.75f, 0.1f, 1f);
        private static readonly Color ComboGreen = new Color(0.2f, 1f, 0.2f, 1f);

        [MenuItem("Tools/Musical Sprite/Setup Demo Scene")]
        public static void ShowWindow()
        {
            GetWindow<SceneSetupWindow>("Setup Demo Scene");
        }

        private void OnGUI()
        {
            GUILayout.Label("Musical Sprite 一键场景搭建", EditorStyles.boldLabel);
            GUILayout.Space(10);

            if (GUILayout.Button("1. 创建材质与文件夹", GUILayout.Height(30)))
            {
                CreateFoldersAndMaterials();
            }

            if (GUILayout.Button("2. 生成测试谱面", GUILayout.Height(30)))
            {
                DemoBeatmapGenerator.CreateDemoBeatmap();
                AssetDatabase.Refresh();
            }

            if (GUILayout.Button("3. 搭建完整场景", GUILayout.Height(40)))
            {
                SetupScene();
            }

            GUILayout.Space(10);
            EditorGUILayout.HelpBox("步骤：\n1. 先点创建材质\n2. 再点生成测试谱面\n3. 最后点搭建完整场景\n4. 按 Play 即可测试", MessageType.Info);
        }

        private static void CreateFoldersAndMaterials()
        {
            EnsureFolder(MaterialsFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(BeatmapsFolder);

            CreateMaterial("M_ArenaRed", ArenaRed);
            CreateMaterial("M_ArenaBlue", ArenaBlue);
            CreateMaterial("M_CenterLine", CenterLinePink);
            CreateMaterial("M_Note", NoteYellow);
            CreateMaterial("M_TouchDebug", new Color(1f, 1f, 1f, 0.25f), true);
            CreateMaterial("M_IndicatorActive", IndicatorBlue, false, true);
            CreateMaterial("M_IndicatorIdle", IndicatorDim);
            CreateMaterial("M_ScoreLeft", ComboGreen);
            CreateMaterial("M_ScoreRight", ProtagonistYellow);
            CreateMaterial("M_BandMember", MemberYellow);
            CreateMaterial("M_Protagonist", ProtagonistYellow);
            CreateMaterial("M_ComboText", ComboGreen);

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("完成", "材质与文件夹已创建", "确定");
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = Path.GetDirectoryName(path).Replace('\\', '/');
                string name = Path.GetFileName(path);
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private static Material CreateMaterial(string name, Color color, bool transparent = false, bool emission = false)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {
                // 如果已存在，同步 emission 设置
                if (emission)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", color * 3f);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    EditorUtility.SetDirty(mat);
                }
                return mat;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            mat = new Material(shader);
            mat.color = color;
            mat.SetColor("_BaseColor", color);

            if (emission)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 3f);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            if (transparent)
            {
                mat.SetFloat("_Surface", 1); // Transparent
                mat.SetFloat("_Blend", 0);   // Alpha
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void SetupScene()
        {
            // 清理旧场景对象（避免重复生成）
            string[] oldNames = new[]
            {
                "GameManager", "CenterLine", "LeftSpawner", "RightSpawner",
                "LeftSpawnPoint", "LeftHitPoint", "RightSpawnPoint", "RightHitPoint",
                "LeftHitLine", "RightHitLine", "JudgeFeedbackManager", "TouchZoneBuilder",
                "TouchInputManager", "OpponentInput", "LeftBand", "RightBand", "ComboDisplay",
                "ScoreManager", "ScoreLeft", "ScoreRight"
            };
            foreach (string n in oldNames)
            {
                GameObject old = GameObject.Find(n);
                if (old != null) DestroyImmediate(old);
            }

            // 确保 Layer 6 叫 TouchZone
            SetupTouchLayer();

            // 创建材质
            Material redMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_ArenaRed.mat");
            Material blueMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_ArenaBlue.mat");
            Material pinkMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_CenterLine.mat");
            Material noteMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_Note.mat");
            Material touchDebugMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_TouchDebug.mat");
            Material indicatorActiveMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_IndicatorActive.mat");
            Material indicatorIdleMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_IndicatorIdle.mat");
            Material bandMemberMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_BandMember.mat");
            Material protagonistMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_Protagonist.mat");

            if (redMat == null || blueMat == null || pinkMat == null || noteMat == null ||
                indicatorActiveMat == null || indicatorIdleMat == null || bandMemberMat == null || protagonistMat == null)
            {
                EditorUtility.DisplayDialog("错误", "请先点击「创建材质与文件夹」", "确定");
                return;
            }

            // 获取或创建相机
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                GameObject camGo = new GameObject("Main Camera");
                mainCam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
            }
            mainCam.transform.position = new Vector3(0f, 13f, -8f);
            mainCam.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
            mainCam.orthographic = false;
            mainCam.fieldOfView = 60f;
            mainCam.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);

            // 创建/更新 Arena
            GameObject arenaLeft = GameObject.Find("ArenaLeft");
            if (arenaLeft == null)
            {
                arenaLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arenaLeft.name = "ArenaLeft";
            }
            arenaLeft.transform.position = new Vector3(-4f, -0.05f, 0f);
            arenaLeft.transform.localScale = new Vector3(8f, 0.1f, 9f);
            SetMaterial(arenaLeft, redMat);

            GameObject arenaRight = GameObject.Find("ArenaRight");
            if (arenaRight == null)
            {
                arenaRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arenaRight.name = "ArenaRight";
            }
            arenaRight.transform.position = new Vector3(4f, -0.05f, 0f);
            arenaRight.transform.localScale = new Vector3(8f, 0.1f, 9f);
            SetMaterial(arenaRight, blueMat);

            // 删除旧的 ArenaBase 避免重叠
            GameObject oldArena = GameObject.Find("ArenaBase");
            if (oldArena != null) DestroyImmediate(oldArena);

            // 创建 GameManager
            GameObject gm = new GameObject("GameManager");
            gm.transform.position = Vector3.zero;

            AudioSource audioSource = gm.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;

            Conductor conductor = gm.AddComponent<Conductor>();
            conductor.musicSource = audioSource;
            conductor.secPerBeat = 0.5f;
            conductor.songOffset = 0f;

            GameManager gameManager = gm.AddComponent<GameManager>();
            gameManager.conductor = conductor;

            // 创建 CenterLine
            GameObject centerLineGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            centerLineGo.name = "CenterLine";
            centerLineGo.transform.position = new Vector3(0f, 0.5f, 0f);
            centerLineGo.transform.localScale = new Vector3(0.2f, 1f, 9f);
            SetMaterial(centerLineGo, pinkMat);
            BattleCenterLine centerLine = centerLineGo.AddComponent<BattleCenterLine>();
            centerLine.leftGround = arenaLeft.transform;
            centerLine.rightGround = arenaRight.transform;
            centerLine.arenaTotalWidth = 16f;
            centerLine.minX = -5f;
            centerLine.maxX = 5f;
            centerLine.pushPerHit = 0.001f;
            centerLine.smoothSpeed = 5f;

            // 创建发射点 / 判定线
            GameObject leftSpawn = CreateEmpty("LeftSpawnPoint", new Vector3(7f, 0.5f, 0f));
            GameObject leftHit = CreateEmpty("LeftHitPoint", new Vector3(-6f, 0.5f, 0f));
            GameObject rightSpawn = CreateEmpty("RightSpawnPoint", new Vector3(-7f, 0.5f, 0f));
            GameObject rightHit = CreateEmpty("RightHitPoint", new Vector3(6f, 0.5f, 0f));

            // 创建判定线可视化
            CreateHitLineVisualizer("LeftHitLine", leftHit.transform.position, redMat);
            CreateHitLineVisualizer("RightHitLine", rightHit.transform.position, blueMat);

            // 创建 Note 预制体
            GameObject notePrefab = CreateNotePrefab(noteMat);

            // 创建左右发射器
            BeatmapSO beatmap = AssetDatabase.LoadAssetAtPath<BeatmapSO>($"{BeatmapsFolder}/DemoBeatmap.asset");
            if (beatmap == null)
            {
                EditorUtility.DisplayDialog("错误", "请先点击「生成测试谱面」", "确定");
                return;
            }

            GameObject leftSpawnerGo = new GameObject("LeftSpawner");
            NoteSpawner leftSpawner = leftSpawnerGo.AddComponent<NoteSpawner>();
            SetupSpawner(leftSpawner, 0, conductor, beatmap, centerLine, leftSpawn.transform, leftHit.transform, notePrefab);

            GameObject rightSpawnerGo = new GameObject("RightSpawner");
            NoteSpawner rightSpawner = rightSpawnerGo.AddComponent<NoteSpawner>();
            SetupSpawner(rightSpawner, 1, conductor, beatmap, centerLine, rightSpawn.transform, rightHit.transform, notePrefab);

            // 配置 CenterLine
            centerLine.minX = -5f;
            centerLine.maxX = 5f;
            centerLine.pushPerHit = 0.001f;
            centerLine.smoothSpeed = 5f;

            // 关联 GameManager
            gameManager.leftSpawner = leftSpawner;
            gameManager.rightSpawner = rightSpawner;
            gameManager.centerLine = centerLine;

            // 创建 JudgeFeedbackManager
            GameObject feedbackGo = new GameObject("JudgeFeedbackManager");
            JudgeFeedbackManager feedbackManager = feedbackGo.AddComponent<JudgeFeedbackManager>();
            gameManager.judgeFeedback = feedbackManager;

            // 创建计分板
            GameObject scoreManagerGo = new GameObject("ScoreManager");
            ScoreManager scoreManager = scoreManagerGo.AddComponent<ScoreManager>();
            scoreManager.leftSpawner = leftSpawner;
            scoreManager.rightSpawner = rightSpawner;

            ScoreDisplay leftScore = CreateScoreDisplay("ScoreLeft", new Vector3(-6f, 5.5f, 0f), ComboGreen);
            ScoreDisplay rightScore = CreateScoreDisplay("ScoreRight", new Vector3(6f, 5.5f, 0f), ProtagonistYellow);
            scoreManager.leftScoreDisplay = leftScore;
            scoreManager.rightScoreDisplay = rightScore;
            gameManager.scoreManager = scoreManager;

            // 让中线直接读取 ScoreManager 真实分差
            centerLine.scoreManager = scoreManager;

            // 创建触控区构建器
            GameObject touchBuilderGo = new GameObject("TouchZoneBuilder");
            TouchZoneBuilder touchBuilder = touchBuilderGo.AddComponent<TouchZoneBuilder>();
            touchBuilder.leftSpawner = leftSpawner;
            touchBuilder.rightSpawner = rightSpawner;
            touchBuilder.leftEdgeX = -8f;
            touchBuilder.rightEdgeX = 8f;
            touchBuilder.touchHeight = 1f;
            touchBuilder.showDebugVisual = true;
            touchBuilder.debugMaterial = touchDebugMat;
            touchBuilder.touchLayer = 6;

            // 创建触摸输入管理器
            GameObject touchInputGo = new GameObject("TouchInputManager");
            TouchInputManager touchInput = touchInputGo.AddComponent<TouchInputManager>();
            touchInput.gameCamera = mainCam;
            touchInput.leftSpawner = leftSpawner;
            touchInput.rightSpawner = rightSpawner;
            touchInput.touchLayer = 1 << 6;
            touchInput.logTouches = false;
            gameManager.touchInput = touchInput;

            // 创建 AI 对手输入
            GameObject opponentGo = new GameObject("OpponentInput");
            OpponentInput opponent = opponentGo.AddComponent<OpponentInput>();
            opponent.spawner = rightSpawner;
            opponent.conductor = conductor;
            opponent.beatmap = beatmap;
            opponent.aimOffset = 0f;
            opponent.missChance = 0.05f;
            opponent.showVisualFeedback = true;
            gameManager.opponentInput = opponent;

            // 视觉反馈事件：让对手按键时右轨道闪一下
            OpponentVisualFeedback oppFeedback = rightSpawnerGo.AddComponent<OpponentVisualFeedback>();
            oppFeedback.opponentInput = opponent;
            oppFeedback.rightSpawner = rightSpawner;
            oppFeedback.touchZoneBuilder = touchBuilder;
            oppFeedback.feedbackMaterial = touchDebugMat;
            oppFeedback.flashDuration = 0.15f;

            // 创建乐队阵容、轨道指示灯、连击显示
            BattleVisualsController visuals = gm.AddComponent<BattleVisualsController>();
            visuals.leftSpawner = leftSpawner;
            visuals.rightSpawner = rightSpawner;
            visuals.leftComboDisplay = CreateComboDisplay("ComboLeft", new Vector3(-4f, 2.5f, 0f), ArenaRed);
            visuals.rightComboDisplay = CreateComboDisplay("ComboRight", new Vector3(4f, 2.5f, 0f), ArenaBlue);

            // 左侧乐队
            Transform leftBand = CreateBandFormation("LeftBand", -1f, leftSpawner, bandMemberMat, protagonistMat, indicatorActiveMat, indicatorIdleMat);
            visuals.leftBandRoot = leftBand;

            // 右侧乐队
            Transform rightBand = CreateBandFormation("RightBand", 1f, rightSpawner, bandMemberMat, protagonistMat, indicatorActiveMat, indicatorIdleMat);
            visuals.rightBandRoot = rightBand;

            // 收集指示灯引用
            CollectIndicators(visuals, leftBand, rightBand);

            EditorUtility.DisplayDialog("完成", "场景已搭建完成。\n\n提示：\n• 没有音频 clip 也能运行（判定系统按时间走）。\n• 想听音乐的话，给 GameManager 的 AudioSource 拖一首 AudioClip。\n• 然后按 Play 即可测试。", "确定");
        }

        private static GameObject CreateEmpty(string name, Vector3 pos)
        {
            GameObject go = new GameObject(name);
            go.transform.position = pos;
            return go;
        }

        /// <summary>
        /// 生成一个扁平半圆柱体 Mesh：直径沿 Z 轴，圆弧朝 +X 方向。
        /// 可通过旋转 GameObject 改变朝向。
        /// </summary>
        private static Mesh CreateHalfCylinderMesh(float radius, float height, int segments)
        {
            Mesh mesh = new Mesh();
            mesh.name = "HalfCylinder";

            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();
            List<Vector2> uvs = new List<Vector2>();

            // 底部中心 + 顶部中心
            verts.Add(new Vector3(0, 0, 0));
            verts.Add(new Vector3(0, height, 0));
            uvs.Add(new Vector2(0.5f, 0.5f));
            uvs.Add(new Vector2(0.5f, 0.5f));

            // 弧形顶点，角度 -90° ~ 90°
            for (int i = 0; i <= segments; i++)
            {
                float angle = -Mathf.PI / 2f + Mathf.PI * i / segments;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                verts.Add(new Vector3(x, 0, z));
                verts.Add(new Vector3(x, height, z));
                uvs.Add(new Vector2(x / radius * 0.5f + 0.5f, z / radius * 0.5f + 0.5f));
                uvs.Add(new Vector2(x / radius * 0.5f + 0.5f, z / radius * 0.5f + 0.5f));
            }

            int bottomCenter = 0;
            int topCenter = 1;
            int arcStart = 2;

            // 底面
            for (int i = 0; i < segments; i++)
            {
                tris.Add(bottomCenter);
                tris.Add(arcStart + i * 2);
                tris.Add(arcStart + i * 2 + 2);
            }

            // 顶面
            for (int i = 0; i < segments; i++)
            {
                tris.Add(topCenter);
                tris.Add(arcStart + i * 2 + 3);
                tris.Add(arcStart + i * 2 + 1);
            }

            // 弧形侧面
            for (int i = 0; i < segments; i++)
            {
                int bl = arcStart + i * 2;
                int tl = arcStart + i * 2 + 1;
                int br = arcStart + i * 2 + 2;
                int tr = arcStart + i * 2 + 3;

                tris.Add(bl);
                tris.Add(tl);
                tris.Add(tr);

                tris.Add(bl);
                tris.Add(tr);
                tris.Add(br);
            }

            // 直径侧面（封闭切面）
            int frontBottom = arcStart;
            int frontTop = arcStart + 1;
            int backBottom = arcStart + segments * 2;
            int backTop = arcStart + segments * 2 + 1;

            tris.Add(frontBottom);
            tris.Add(backBottom);
            tris.Add(backTop);

            tris.Add(frontBottom);
            tris.Add(backTop);
            tris.Add(frontTop);

            mesh.vertices = verts.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false); // 保持 CPU 可读，供 MeshCollider 使用
            return mesh;
        }

        private static void CreateHitLineVisualizer(string name, Vector3 pos, Material mat)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
            }
            go.transform.position = pos + new Vector3(0f, -0.05f, 0f);
            go.transform.localScale = new Vector3(0.1f, 0.05f, 9f);
            SetMaterial(go, mat);
        }

        private static GameObject CreateNotePrefab(Material mat)
        {
            string prefabPath = $"{PrefabsFolder}/Note.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null) return prefab;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Note";
            go.transform.localScale = new Vector3(0.6f, 0.12f, 0.6f);
            SetMaterial(go, mat);

            // 确保有 Note 组件
            Note note = go.GetComponent<Note>();
            if (note == null) go.AddComponent<Note>();

            // 移除碰撞体，不需要物理
            Collider col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            DestroyImmediate(go);
            return prefab;
        }

        private static void SetupSpawner(NoteSpawner spawner, int side, Conductor conductor, BeatmapSO beatmap,
            BattleCenterLine centerLine, Transform spawnPoint, Transform hitPoint, GameObject notePrefab)
        {
            spawner.side = side;
            spawner.conductor = conductor;
            spawner.beatmap = beatmap;
            spawner.centerLine = centerLine;
            spawner.spawnPoint = spawnPoint;
            spawner.hitPoint = hitPoint;
            spawner.notePrefab = notePrefab;
            spawner.leadTime = 2f;
            spawner.laneCount = 4;
            spawner.laneSpacing = 1.5f;

            if (side == 0)
            {
                spawner.keys = new KeyCode[] { KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F };
            }
            else
            {
                spawner.keys = new KeyCode[] { KeyCode.H, KeyCode.J, KeyCode.K, KeyCode.L };
            }
        }

        public static Font GetUIFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 16);
            return font;
        }

        private static void SetMaterial(GameObject go, Material mat)
        {
            MeshRenderer rend = go.GetComponent<MeshRenderer>();
            if (rend == null) rend = go.GetComponentInChildren<MeshRenderer>();
            if (rend != null) rend.material = mat;
        }

        private static ComboDisplay CreateComboDisplay(string name, Vector3 position, Color color)
        {
            GameObject root = new GameObject(name);
            root.transform.position = position;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (Camera.main != null)
                root.transform.rotation = Quaternion.LookRotation(root.transform.position - Camera.main.transform.position);

            root.transform.localScale = Vector3.one * 0.03f;

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(root.transform);
            textGo.transform.localPosition = Vector3.zero;
            textGo.transform.localScale = Vector3.one;

            RectTransform rect = textGo.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(300f, 160f);

            Text uiText = textGo.AddComponent<Text>();
            uiText.text = "";
            uiText.fontSize = 120;
            uiText.alignment = TextAnchor.MiddleCenter;
            uiText.color = color;
            uiText.font = GetUIFont();

            Outline outline = textGo.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(4f, 4f);

            ComboDisplay display = root.AddComponent<ComboDisplay>();
            display.text = uiText;
            display.comboColor = color;
            return display;
        }

        private static ScoreDisplay CreateScoreDisplay(string name, Vector3 position, Color color)
        {
            GameObject root = new GameObject(name);
            root.transform.position = position;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            // 让 Canvas 面向相机：法线指向相机，文字才能正对镜头
            if (Camera.main != null)
                root.transform.rotation = Quaternion.LookRotation(root.transform.position - Camera.main.transform.position);

            // 世界空间 Canvas 整体缩放，使 UI 像素映射到合适的世界尺寸
            root.transform.localScale = Vector3.one * 0.02f;

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(root.transform);
            textGo.transform.localPosition = Vector3.zero;
            textGo.transform.localScale = Vector3.one;

            RectTransform rect = textGo.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(400f, 120f);

            Text uiText = textGo.AddComponent<Text>();
            uiText.text = "0";
            uiText.fontSize = 100;
            uiText.alignment = TextAnchor.MiddleCenter;
            uiText.color = color;
            uiText.font = GetUIFont();

            // 加粗描边提升可读性
            Outline outline = textGo.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(4f, 4f);

            ScoreDisplay display = root.AddComponent<ScoreDisplay>();
            display.scoreText = uiText;
            return display;
        }

        private static Transform CreateBandFormation(string rootName, float direction, NoteSpawner spawner,
            Material memberMat, Material protagonistMat, Material indicatorActiveMat, Material indicatorIdleMat)
        {
            // direction: -1 = 左侧乐队，+1 = 右侧乐队
            // 场地内侧为 X=0，外侧为 X 绝对值更大的方向
            // 圆台圆弧朝向场地中心（里侧），直径朝外（靠近场地边缘）
            float stageRadius = 1.2f;
            float hitX = spawner.hitPoint.position.x;
            float indicatorX = hitX - direction * 0.15f; // 略靠场地内侧

            // 半圆圆台直径贴着场地外侧边缘，圆弧朝场地中心，不越过判定线
            float platformX = direction * 8f;

            GameObject root = new GameObject(rootName);
            root.transform.position = Vector3.zero;

            // 半圆圆台：圆弧朝向场地中心（里侧），直径朝外（靠近场地边缘）
            // 这样乐队站在圆弧上，面向场地中心演奏
            GameObject stage = new GameObject($"{rootName}_Stage");
            stage.transform.SetParent(root.transform);
            stage.transform.position = new Vector3(platformX, 0f, 0f);
            // CreateHalfCylinderMesh 默认圆弧朝 +X；左侧 direction=-1 需要圆弧朝里（+X），所以不旋转
            // 右侧 direction=+1 需要圆弧朝里（-X），所以旋转 180°
            stage.transform.rotation = Quaternion.Euler(0f, direction < 0 ? 0f : 180f, 0f);

            MeshFilter mf = stage.AddComponent<MeshFilter>();
            mf.sharedMesh = CreateHalfCylinderMesh(stageRadius, 0.15f, 24);
            MeshRenderer mr = stage.AddComponent<MeshRenderer>();
            mr.material = direction < 0 ? AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_ArenaRed.mat") :
                                           AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_ArenaBlue.mat");
            MeshCollider mc = stage.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;

            // 成员与主角的 X 偏移：正数表示向场地边缘（后），负数表示向判定线（前）
            float memberForward = 0.5f;   // 中间两个微微靠前
            float memberBack = 1.15f;     // 最上/最下两个靠后
            float protagonistOffset = 0.85f;

            // 主角大方块：缩小后放在中间两个小方块后方
            GameObject protagonist = GameObject.CreatePrimitive(PrimitiveType.Cube);
            protagonist.name = $"{rootName}_Protagonist";
            protagonist.transform.SetParent(root.transform);
            protagonist.transform.position = new Vector3(hitX + direction * protagonistOffset, 0.45f, 0f);
            protagonist.transform.localScale = new Vector3(0.65f, 0.9f, 0.65f);
            SetMaterial(protagonist, protagonistMat);

            // 4 个成员小方块：Z 坐标严格与 4 条判定线对齐
            for (int lane = 0; lane < spawner.laneCount; lane++)
            {
                // 判定线 Z 坐标
                float z = (lane - (spawner.laneCount - 1) * 0.5f) * spawner.laneSpacing;

                // 最上方和最下方靠后，中间两个微微靠前
                float xOffset = (lane == 0 || lane == spawner.laneCount - 1) ? memberBack : memberForward;
                float x = hitX + direction * xOffset;

                GameObject member = GameObject.CreatePrimitive(PrimitiveType.Cube);
                member.name = $"{rootName}_Member_Lane{lane}";
                member.transform.SetParent(root.transform);
                member.transform.position = new Vector3(x, 0.45f, z);
                member.transform.localScale = new Vector3(0.45f, 0.6f, 0.45f);
                SetMaterial(member, memberMat);

                // 指示灯：判定线处的横向短条（横躺在地面上方）
                GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
                indicator.name = $"{rootName}_Indicator_Lane{lane}";
                indicator.transform.SetParent(root.transform);
                // 横躺：沿 Z 轴延伸，Y 轴很薄，X 轴略宽于线
                indicator.transform.position = new Vector3(indicatorX, 0.04f, z);
                indicator.transform.localScale = new Vector3(0.12f, 0.04f, spawner.laneSpacing * 0.85f);
                SetMaterial(indicator, indicatorIdleMat);

                LaneIndicator li = indicator.AddComponent<LaneIndicator>();
                li.targetRenderer = indicator.GetComponent<MeshRenderer>();
                li.activeMaterial = indicatorActiveMat;
                li.idleMaterial = indicatorIdleMat;
                li.flashDuration = 0.12f;
                indicator.tag = "LaneIndicator";
            }

            return root.transform;
        }

        private static void CollectIndicators(BattleVisualsController visuals, Transform leftBand, Transform rightBand)
        {
            visuals.indicators = new LaneIndicator[8];

            foreach (Transform child in leftBand)
            {
                if (child.CompareTag("LaneIndicator"))
                {
                    LaneIndicator li = child.GetComponent<LaneIndicator>();
                    if (li != null)
                    {
                        int lane = ExtractLaneIndex(child.name);
                        if (lane >= 0 && lane < 4)
                            visuals.indicators[0 * 4 + lane] = li;
                    }
                }
            }

            foreach (Transform child in rightBand)
            {
                if (child.CompareTag("LaneIndicator"))
                {
                    LaneIndicator li = child.GetComponent<LaneIndicator>();
                    if (li != null)
                    {
                        int lane = ExtractLaneIndex(child.name);
                        if (lane >= 0 && lane < 4)
                            visuals.indicators[1 * 4 + lane] = li;
                    }
                }
            }
        }

        private static int ExtractLaneIndex(string name)
        {
            // 从名字如 "LeftBand_Indicator_Lane2" 提取数字
            int idx = name.LastIndexOf("Lane");
            if (idx < 0) return -1;
            if (int.TryParse(name.Substring(idx + 4), out int lane))
                return lane;
            return -1;
        }

        private static void SetupTouchLayer()
        {
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            if (layers != null && layers.isArray)
            {
                SerializedProperty layer6 = layers.GetArrayElementAtIndex(6);
                if (layer6 != null && string.IsNullOrEmpty(layer6.stringValue))
                {
                    layer6.stringValue = "TouchZone";
                    tagManager.ApplyModifiedProperties();
                    Debug.Log("[Setup] Layer 6 已设置为 TouchZone");
                }
            }

            // 确保有 LaneIndicator tag
            SerializedProperty tags = tagManager.FindProperty("tags");
            if (tags != null && tags.isArray)
            {
                bool found = false;
                for (int i = 0; i < tags.arraySize; i++)
                {
                    if (tags.GetArrayElementAtIndex(i).stringValue == "LaneIndicator")
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    tags.InsertArrayElementAtIndex(tags.arraySize);
                    tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = "LaneIndicator";
                    tagManager.ApplyModifiedProperties();
                    Debug.Log("[Setup] Tag LaneIndicator 已创建");
                }
            }
        }
    }
}
