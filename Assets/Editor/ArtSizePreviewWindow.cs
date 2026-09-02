#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text;

/// <summary>
/// 美术素材尺寸预览工具。
///
/// 菜单：Tools → Musical-Sprite → Art Size Preview（美术尺寸预览）
///
/// 用途：在导入/替换美术素材后，快速确认「这张图在游戏里到底多大」。
///
/// 三种预览方式：
///   1) 窗口内 1:1 看图      —— 不进场景，直接在窗口里按原始像素（或指定倍数）显示，带透明棋盘格
///   2) 生成到 3D 场景        —— Sprite 面片 / Quad，按 PPU 换算成世界尺寸，可正对相机或平躺贴地
///   3) 生成到 UI Canvas      —— Screen Space Overlay + ConstantPixelSize，屏幕上 1 图片像素 = 1 屏幕像素
///
/// 关键概念 PPU（Pixels Per Unit）：
///   世界尺寸 = 图片像素宽 / PPU。PPU=100 时，877px 宽的图 = 8.77 世界单位。
///   想让图在游戏里变大 → 调小 PPU；想变小 → 调大 PPU。
///
/// 生成出来的对象名字都带 [ArtPreview] 前缀，用窗口里的「清除全部预览」一键删干净。
/// </summary>
public class ArtSizePreviewWindow : EditorWindow
{
    private const string Tag = "[ArtPreview]";

    // ------------------------------------------------------------------ 菜单
    [MenuItem("Tools/Musical-Sprite/Art Size Preview")]
    public static void Open()
    {
        var w = GetWindow<ArtSizePreviewWindow>("美术尺寸预览");
        w.minSize = new Vector2(430, 560);
        w.Show();
    }

    // ------------------------------------------------------------------ 枚举
    private enum SpawnMode
    {
        SpritePlane = 0,   // SpriteRenderer 面片（推荐：透明正确、不受光）
        Quad3D = 1,        // 3D Quad 面片
        CanvasUI = 2,      // 屏幕空间 UI，1:1 像素
    }

    private enum Orientation
    {
        FaceCamera = 0,    // 正对 Scene 相机
        Flat = 1,          // 平躺（贴地，XZ 平面）
        Upright = 2,       // 竖直朝 +Z
    }

    // ------------------------------------------------------------------ 字段
    private UnityEngine.Object targetAsset;
    private Texture2D tex;
    private Sprite sprite;

    private SpawnMode spawnMode = SpawnMode.SpritePlane;
    private Orientation orientation = Orientation.FaceCamera;

    private bool autoPPU = true;
    private float ppu = 100f;
    private float extraScale = 1f;
    private float distanceFromCamera = 10f;
    private float uiScale = 1f;
    private bool addOneUnitFrame = true;
    private GameObject compareTo;

    private bool fitToWindow = true;
    private float zoom = 1f;
    private string report = "";
    private Texture2D checker;

    // ------------------------------------------------------------------ 生命周期
    private void OnEnable()
    {
        // 打开窗口时，自动取 Project 里当前选中的图片
        if (targetAsset == null) TryTakeFromSelection();
    }

    private void OnDisable()
    {
        if (checker != null) { DestroyImmediate(checker); checker = null; }
    }

    private void OnSelectionChange()
    {
        // 没手动指定过目标时，跟随 Project 窗口的选择
        if (targetAsset == null) { TryTakeFromSelection(); Repaint(); }
    }

    private void TryTakeFromSelection()
    {
        var obj = Selection.activeObject;
        if (obj is Texture2D || obj is Sprite)
        {
            targetAsset = obj;
            RefreshTarget();
        }
    }

    // ------------------------------------------------------------------ GUI
    private void OnGUI()
    {
        DrawTargetArea();
        EditorGUILayout.Space(4);
        DrawPreviewArea();
        EditorGUILayout.Space(4);
        DrawInfoArea();
        EditorGUILayout.Space(4);
        DrawSpawnArea();
    }

    // -------------------------------------------------------------- 目标选择
    private void DrawTargetArea()
    {
        EditorGUILayout.LabelField("① 选择要确认的图片", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        var picked = EditorGUILayout.ObjectField("图片 / Sprite", targetAsset, typeof(UnityEngine.Object), false);
        if (EditorGUI.EndChangeCheck())
        {
            targetAsset = picked;
            RefreshTarget();
        }

        if (tex == null)
        {
            EditorGUILayout.HelpBox(
                "把 Project 里的图片拖到上面的框里（支持 Texture2D / Sprite / 场景里带 SpriteRenderer 的对象）。\n" +
                "也可以在 Project 窗口先选中图片，再打开本窗口，会自动带进来。",
                MessageType.Info);
        }
    }

    // -------------------------------------------------------------- 预览区
    private void DrawPreviewArea()
    {
        EditorGUILayout.LabelField("② 窗口内看原图", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            fitToWindow = EditorGUILayout.ToggleLeft("适应窗口", fitToWindow, GUILayout.Width(90));
            EditorGUI.BeginDisabledGroup(fitToWindow);
            zoom = EditorGUILayout.Slider("倍数", zoom, 0.05f, 4f);
            EditorGUI.EndDisabledGroup();
        }

        float viewH = 300f;
        Rect box = EditorGUILayout.GetControlRect(false, viewH, GUILayout.ExpandWidth(true));

        // 底色 + 棋盘格（方便看透明区域）
        EditorGUI.DrawRect(box, new Color(0.93f, 0.93f, 0.93f));
        if (tex != null)
        {
            GUI.DrawTextureWithTexCoords(box, GetChecker(),
                new Rect(0, 0, box.width / 16f, box.height / 16f), true);
        }

        if (tex == null)
        {
            GUI.Label(box, "（未选择图片）", CenteredLabel());
            return;
        }

        float scale = fitToWindow
            ? Mathf.Max(0.01f, Mathf.Min(box.width / tex.width, box.height / tex.height))
            : Mathf.Max(0.01f, zoom);

        float w = tex.width * scale;
        float h = tex.height * scale;
        Rect img = new Rect(
            box.x + (box.width - w) * 0.5f,
            box.y + (box.height - h) * 0.5f,
            w, h);

        GUI.DrawTexture(img, tex, ScaleMode.StretchToFill, true);
        DrawOutline(img, new Color(0.25f, 0.25f, 0.25f, 0.9f), 1f);

        GUI.Label(new Rect(box.x + 4, box.yMax - 18, box.width - 8, 16),
            string.Format("{0} × {1} px　显示 {2:P0}", tex.width, tex.height, scale),
            EditorStyles.miniLabel);
    }

    // -------------------------------------------------------------- 信息区
    private void DrawInfoArea()
    {
        EditorGUILayout.LabelField("③ 尺寸换算", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            autoPPU = EditorGUILayout.ToggleLeft("跟随导入设置", autoPPU, GUILayout.Width(100));
            EditorGUI.BeginDisabledGroup(autoPPU);
            ppu = EditorGUILayout.FloatField("PPU（每单位多少像素）", Mathf.Max(1f, ppu));
            EditorGUI.EndDisabledGroup();
        }
        ppu = Mathf.Max(1f, ppu);

        if (tex != null)
        {
            float w = tex.width / ppu;
            float h = tex.height / ppu;
            EditorGUILayout.HelpBox(
                string.Format(
                    "像素：{0} × {1}　比例 {2:0.##}:1\n" +
                    "PPU {3:0.##} → 世界尺寸 {4:0.###} × {5:0.###} 单位",
                    tex.width, tex.height, (float)tex.width / tex.height, ppu, w, h),
                MessageType.None);
        }

        compareTo = (GameObject)EditorGUILayout.ObjectField("和场景对象比大小", compareTo, typeof(GameObject), true);
        if (compareTo != null && tex != null)
        {
            float refH = MeasureHeight(compareTo);
            float myH = tex.height / ppu * extraScale;
            if (refH > 0.0001f)
            {
                EditorGUILayout.LabelField("对比结果",
                    string.Format("{0} 高 {1:0.###} 单位，本图是它的 {2:P1}",
                        compareTo.name, refH, myH / refH));
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("打印 / 复制尺寸报告", GUILayout.Height(24)))
            {
                report = BuildReport();
                EditorGUIUtility.systemCopyBuffer = report;
                Debug.Log("[ArtSizePreview]\n" + report);
            }
            if (GUILayout.Button("把 PPU 写回导入设置", GUILayout.Height(24)))
            {
                ApplyPPUToImporter();
            }
        }

        if (!string.IsNullOrEmpty(report))
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.HelpBox(report, MessageType.None);
        }
    }

    // -------------------------------------------------------------- 生成区
    private void DrawSpawnArea()
    {
        EditorGUILayout.LabelField("④ 生成面片", EditorStyles.boldLabel);

        spawnMode = (SpawnMode)EditorGUILayout.EnumPopup("生成方式", spawnMode);

        if (spawnMode == SpawnMode.CanvasUI)
        {
            uiScale = EditorGUILayout.Slider("屏幕倍数（1 = 原尺寸）", uiScale, 0.1f, 4f);
            EditorGUILayout.HelpBox(
                "会新建一个 Screen Space - Overlay 的 Canvas（ConstantPixelSize），\n" +
                "图片按原始像素直接铺在屏幕上 → 1 图片像素 = 1 屏幕像素。\n" +
                "判断素材分辨率够不够、在 1080p / 720p 上大不大，用这个最直观。",
                MessageType.Info);
        }
        else
        {
            orientation = (Orientation)EditorGUILayout.EnumPopup("朝向", orientation);
            if (orientation == Orientation.FaceCamera)
                distanceFromCamera = EditorGUILayout.FloatField("距相机距离", distanceFromCamera);
            extraScale = EditorGUILayout.FloatField("额外缩放", Mathf.Max(0.001f, extraScale));
            addOneUnitFrame = EditorGUILayout.Toggle("附带 1×1 单位参考框", addOneUnitFrame);
            EditorGUILayout.HelpBox(
                "世界尺寸 = 像素 ÷ PPU（再乘额外缩放）。\n" +
                "判断角色、血条在 3D 战斗场景里的大小是否合适，用这个。",
                MessageType.Info);
        }

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.backgroundColor = new Color(0.65f, 0.95f, 0.65f);
            if (GUILayout.Button("生成预览", GUILayout.Height(30))) Spawn();
            GUI.backgroundColor = Color.white;

            GUI.backgroundColor = new Color(1f, 0.75f, 0.7f);
            if (GUILayout.Button("清除全部预览", GUILayout.Height(30))) CleanAll();
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox(
            "确认完尺寸后，记得点「清除全部预览」删掉临时对象，再提交。\n" +
            "生成的对象都带 " + Tag + " 前缀，也可以在 Hierarchy 搜索 [ArtPreview] 手动删。",
            MessageType.Warning);
    }

    // ------------------------------------------------------------------ 逻辑
    private void RefreshTarget()
    {
        tex = null;
        sprite = null;

        if (targetAsset is Sprite sp)
        {
            sprite = sp;
            tex = sp.texture;
        }
        else if (targetAsset is Texture2D t)
        {
            tex = t;
            string p = AssetDatabase.GetAssetPath(t);
            if (!string.IsNullOrEmpty(p))
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
        }
        else if (targetAsset is GameObject go)
        {
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null) { sprite = sr.sprite; }
            else
            {
                var img = go.GetComponent<Image>();
                if (img != null) sprite = img.sprite;
            }
            if (sprite != null) tex = sprite.texture;
        }

        if (autoPPU) ppu = ReadPPU(tex);
        fitToWindow = true;
        report = "";
        Repaint();
    }

    private static float ReadPPU(Texture2D t)
    {
        if (t == null) return 100f;
        string path = AssetDatabase.GetAssetPath(t);
        if (string.IsNullOrEmpty(path)) return 100f;
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return 100f;
        return imp.spritePixelsPerUnit > 0.0001f ? imp.spritePixelsPerUnit : 100f;
    }

    private void ApplyPPUToImporter()
    {
        if (tex == null) { ShowNotification(new GUIContent("先选一张图片")); return; }
        string path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path)) return;
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;

        imp.spritePixelsPerUnit = ppu;
        imp.SaveAndReimport();
        autoPPU = true;
        Debug.Log(string.Format("[ArtSizePreview] 已把 {0} 的 PPU 设为 {1:0.##}", tex.name, ppu));
        ShowNotification(new GUIContent("PPU 已写入"));
    }

    /// <summary>把图片导入设置改成 Sprite（SpriteRenderer 模式需要），不改会加载不到。</summary>
    private Sprite EnsureSprite()
    {
        if (sprite != null) return sprite;
        if (tex == null) return null;

        string path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path)) return null;
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return null;

        if (imp.textureType != TextureImporterType.Sprite)
        {
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.SaveAndReimport();
            Debug.Log(string.Format("[ArtSizePreview] {0} 的 Texture Type 已改为 Sprite", tex.name));
        }

        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        return sprite;
    }

    private void Spawn()
    {
        if (tex == null) { ShowNotification(new GUIContent("先选一张图片")); return; }

        if (spawnMode == SpawnMode.CanvasUI) SpawnCanvasUI();
        else SpawnWorldPlane();

        Debug.Log(string.Format("[ArtSizePreview] 已生成：{0}　{1}×{2}px　PPU {3:0.##}",
            tex.name, tex.width, tex.height, ppu));
    }

    private void SpawnWorldPlane()
    {
        // 位置 & 朝向
        Camera cam = SceneView.lastActiveSceneView != null
            ? SceneView.lastActiveSceneView.camera
            : Camera.main;

        Vector3 pos;
        Quaternion rot;

        if (orientation == Orientation.FaceCamera && cam != null)
        {
            pos = cam.transform.position + cam.transform.forward * distanceFromCamera;
            rot = cam.transform.rotation;
        }
        else
        {
            pos = cam != null
                ? cam.transform.position + cam.transform.forward * distanceFromCamera
                : Vector3.zero;
            rot = orientation == Orientation.Flat
                ? Quaternion.Euler(90f, 0f, 0f)
                : Quaternion.identity;
        }

        float worldW = tex.width / ppu * extraScale;
        float worldH = tex.height / ppu * extraScale;
        string label = string.Format("{0} {1}　{2}x{3}px　→ {4:0.##}x{5:0.##}u",
            Tag, tex.name, tex.width, tex.height, worldW, worldH);

        GameObject go;

        if (spawnMode == SpawnMode.SpritePlane)
        {
            var sp = EnsureSprite();
            if (sp == null)
            {
                ShowNotification(new GUIContent("这张图转不成 Sprite，改用 Quad 模式"));
                return;
            }

            go = new GameObject(label);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = 999;                 // 压在场景其它东西上面，方便看
            go.transform.SetPositionAndRotation(pos, rot);

            // SpriteRenderer 用 sprite 自己的 PPU 算尺寸，这里按我们的 PPU 修正缩放
            float actualW = sp.rect.width / Mathf.Max(0.0001f, sp.pixelsPerUnit);
            float fix = actualW > 0.0001f ? (tex.width / ppu) / actualW : 1f;
            go.transform.localScale = Vector3.one * fix * extraScale;
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.mainTexture = tex;
            mat.color = Color.white;
            mat.name = Tag + "Mat_" + tex.name;
            mr.material = mat;

            go.name = label;
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = new Vector3(worldW, worldH, 1f);
        }

        Undo.RegisterCreatedObjectUndo(go, "Art Size Preview");
        Selection.activeGameObject = go;

        if (addOneUnitFrame)
        {
            // 参考框单独建对象，避免被面片的 scale 拉伸
            var frame = MakeOneUnitFrame();
            frame.transform.SetPositionAndRotation(pos - go.transform.forward * 0.01f, rot);
            Undo.RegisterCreatedObjectUndo(frame, "Art Size Preview");
        }

        EditorSceneManager.MarkSceneDirty(go.scene);
        ShowNotification(new GUIContent("已生成，看 Scene 视图"));
    }

    private void SpawnCanvasUI()
    {
        string label = string.Format("{0}Canvas {1}　{2}x{3}px",
            Tag, tex.name, tex.width, tex.height);

        var canvasGo = new GameObject(label);
        Undo.RegisterCreatedObjectUndo(canvasGo, "Art Size Preview");

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;

        // ConstantPixelSize = 不做任何分辨率适配，保证 1 图片像素 = 1 屏幕像素
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        canvasGo.AddComponent<GraphicRaycaster>();

        var imgGo = new GameObject("Image");
        imgGo.transform.SetParent(canvasGo.transform, false);

        var raw = imgGo.AddComponent<RawImage>();
        raw.texture = tex;
        raw.raycastTarget = false;
        raw.SetNativeSize();                       // 先按原图尺寸

        var rt = imgGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot    = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(tex.width, tex.height) * uiScale;

        Selection.activeGameObject = canvasGo;
        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
        ShowNotification(new GUIContent("已生成，切到 Game 视图看"));
    }

    private static GameObject MakeOneUnitFrame()
    {
        var go = new GameObject(Tag + "1x1单位参考框");
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = 5;
        lr.SetPositions(new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
            new Vector3(-0.5f, -0.5f, 0f),
        });
        lr.startWidth = 0.015f;
        lr.endWidth   = 0.015f;

        var m = new Material(Shader.Find("Sprites/Default"));
        m.color = new Color(1f, 0.85f, 0.15f, 0.95f);
        lr.material = m;
        return go;
    }

    private void CleanAll()
    {
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        int n = 0;
        for (int i = 0; i < all.Length; i++)
        {
            var go = all[i];
            if (go == null) continue;
            if (!go.scene.IsValid()) continue;          // 只处理场景里的，别动资源
            if (!go.name.StartsWith(Tag)) continue;
            Undo.DestroyObjectImmediate(go);
            n++;
        }
        Debug.Log(string.Format("[ArtSizePreview] 清除了 {0} 个预览对象", n));
        ShowNotification(new GUIContent("清除 " + n + " 个"));
    }

    // ------------------------------------------------------------------ 辅助
    private static float MeasureHeight(GameObject go)
    {
        if (go == null) return 0f;
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs == null || rs.Length == 0) return 0f;

        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b.size.y;
    }

    private string BuildReport()
    {
        if (tex == null) return "";
        var sb = new StringBuilder();
        sb.AppendFormat("图片：{0}\n", tex.name);
        sb.AppendFormat("像素：{0} × {1}（{2:0.###}:1）\n", tex.width, tex.height, (float)tex.width / tex.height);
        sb.AppendFormat("PPU：{0:0.##}　→　世界尺寸 {1:0.####} × {2:0.####} 单位\n",
            ppu, tex.width / ppu, tex.height / ppu);

        var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
        if (imp != null)
        {
            sb.AppendFormat("导入设置：{0} / Filter {1} / Mipmap {2} / 导入PPU {3:0.##}\n",
                imp.textureType, imp.filterMode, imp.mipmapEnabled, imp.spritePixelsPerUnit);
        }

        float worldH = tex.height / ppu * (spawnMode == SpawnMode.CanvasUI ? 1f : extraScale);
        sb.AppendFormat("UI 1:1 时占 1080p 高度：{0:P1}\n", (float)tex.height * uiScale / 1080f);

        if (compareTo != null)
        {
            float refH = MeasureHeight(compareTo);
            if (refH > 0.0001f)
                sb.AppendFormat("对比 {0}（高 {1:0.###}u）：本图 = {2:P1}\n",
                    compareTo.name, refH, worldH / refH);
        }
        return sb.ToString().TrimEnd();
    }

    private Texture2D GetChecker()
    {
        if (checker != null) return checker;
        checker = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        Color a = new Color(0.80f, 0.80f, 0.80f, 1f);
        Color b = new Color(0.97f, 0.97f, 0.97f, 1f);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                checker.SetPixel(x, y, ((x / 8 + y / 8) % 2 == 0) ? a : b);
        checker.wrapMode = TextureWrapMode.Repeat;
        checker.hideFlags = HideFlags.HideAndDontSave;
        checker.Apply();
        return checker;
    }

    private GUIStyle centeredLabel;

    private GUIStyle CenteredLabel()
    {
        if (centeredLabel == null)
        {
            centeredLabel = new GUIStyle(EditorStyles.label);
            centeredLabel.alignment = TextAnchor.MiddleCenter;
            centeredLabel.normal.textColor = new Color(0.35f, 0.35f, 0.35f);
        }
        return centeredLabel;
    }

    private static void DrawOutline(Rect r, Color c, float t)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
    }
}
#endif
