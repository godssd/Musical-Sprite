using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using MusicalSprite.Terrain;

/// <summary>Isolated rail review using the battle camera and original arena mesh.</summary>
public sealed class BattleVisualReviewV11 : EditorWindow
{
    private RailPreview preview;
    private int lane = 1;
    private int state;
    private bool animate;
    private double lastTime;
    private string failure;
    private Editor styleEditor;
    private Vector2 parameterScroll;
    [SerializeField] private int viewMode;
    [SerializeField] private float verticalShare = 0.60f;
    [SerializeField] private float horizontalShare = 0.44f;
    private string failureDetails;

    [MenuItem("Tools/Musical-Sprite/V11/01 Judgement UI Review")]
    public static void Open()
    {
        var window = GetWindow<BattleVisualReviewV11>("V11 判定 UI");
        window.minSize = new Vector2(700, 480);
    }

    private void OnEnable()
    {
        lastTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        preview?.Dispose();
        preview = null;
        if (styleEditor != null) DestroyImmediate(styleEditor);
    }

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (animate && preview != null)
        {
            try
            {
                preview.Advance((float)Math.Min(0.05, now - lastTime));
                Repaint();
            }
            catch (Exception exception) { ReportFailure(exception); }
        }
        lastTime = now;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("参数实时作用于游戏与小样；保存后退出 Play 仍保留。", EditorStyles.wordWrappedMiniLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("载入 / 刷新小样")) Build();
            lane = EditorGUILayout.IntSlider("轨道（0–3）", lane, 0, 3);
            animate = GUILayout.Toggle(animate, "播放衰减", "Button", GUILayout.Width(95));
        }
        int next = GUILayout.Toolbar(state, new[] { "常态", "按住", "单轨命中", "相邻命中", "四轨命中" });
        if (next != state) { state = next; Apply(); }
        using (new EditorGUILayout.HorizontalScope())
        {
            viewMode = GUILayout.Toolbar(viewMode, new[] { "参数 + 预览", "只看参数", "只看预览" });
            if (GUILayout.Button("重播", GUILayout.Width(65))) Apply();
        }
        // An empty string after serialization/reload is not an error.
        if (!string.IsNullOrWhiteSpace(failure))
        {
            EditorGUILayout.HelpBox(failure, MessageType.Error);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("重试加载")) Build();
                if (GUILayout.Button("复制错误详情"))
                    EditorGUIUtility.systemCopyBuffer = string.IsNullOrWhiteSpace(failureDetails) ? failure : failureDetails;
            }
        }
        Rect body = GUILayoutUtility.GetRect(120f, 120f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (viewMode == 1) { DrawParameterPane(body); return; }
        if (viewMode == 2) { DrawPreviewPane(body); return; }
        bool horizontal = body.width >= 1050f;
        float share = horizontal ? horizontalShare : verticalShare;
        float offset = (horizontal ? body.width : body.height) * Mathf.Clamp(share, 0.22f, 0.80f);
        Rect parameters = horizontal ? new Rect(body.x, body.y, offset - 3f, body.height)
            : new Rect(body.x, body.y, body.width, offset - 3f);
        Rect divider = horizontal ? new Rect(body.x + offset - 3f, body.y, 6f, body.height)
            : new Rect(body.x, body.y + offset - 3f, body.width, 6f);
        Rect image = horizontal ? new Rect(divider.xMax, body.y, body.xMax - divider.xMax, body.height)
            : new Rect(body.x, divider.yMax, body.width, body.yMax - divider.yMax);
        DrawParameterPane(parameters);
        DrawPreviewPane(image);
        DrawDivider(divider, body, horizontal);
    }

    private void DrawParameterPane(Rect rect)
    {
        GUILayout.BeginArea(rect, EditorStyles.helpBox);
        try
        {
            var style = Resources.Load<JudgementRailStyle>(JudgementRailStyle.ResourcePath);
            if (style == null)
            {
                EditorGUILayout.HelpBox("参数资产尚未导入：Assets/Resources/BattleVisualV11/RailStyle.asset", MessageType.Warning);
                return;
            }
            Editor.CreateCachedEditor(style, typeof(JudgementRailStyleEditor), ref styleEditor);
            var inspector = (JudgementRailStyleEditor)styleEditor;
            inspector.DrawToolbar();
            parameterScroll = EditorGUILayout.BeginScrollView(parameterScroll, GUILayout.ExpandHeight(true));
            inspector.DrawParameterFields(rect.width - 24f);
            EditorGUILayout.EndScrollView();
            inspector.DrawHelp();
        }
        finally { GUILayout.EndArea(); }
    }

    private void DrawPreviewPane(Rect rect)
    {
        GUI.Box(rect, GUIContent.none);
        if (preview == null)
        {
            EditorGUI.HelpBox(new Rect(rect.x + 12f, rect.y + 12f, Mathf.Max(1f, rect.width - 24f), 64f),
                "尚未加载小样。可先调参，或点击顶部“载入 / 刷新小样”。参数编辑不依赖预览是否成功。", MessageType.Info);
            return;
        }
        if (Event.current.type != EventType.Repaint) return;
        try
        {
            preview.Render();
            GUI.DrawTexture(Fit(new Rect(rect.x, rect.y, rect.width, Mathf.Max(1f, rect.height - 32f)), 16f / 9f),
                preview.Target, ScaleMode.ScaleToFit, false);
            GUI.Label(new Rect(rect.x + 6f, rect.yMax - 30f, rect.width - 12f, 28f),
                "拖动中间分隔条改变比例；“只看参数”可占满窗口。", EditorStyles.wordWrappedMiniLabel);
        }
        catch (Exception exception) { ReportFailure(exception); }
    }

    private void DrawDivider(Rect divider, Rect body, bool horizontal)
    {
        EditorGUI.DrawRect(divider, new Color(0.28f, 0.28f, 0.28f));
        EditorGUIUtility.AddCursorRect(divider, horizontal ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);
        int control = GUIUtility.GetControlID(FocusType.Passive);
        Event current = Event.current;
        if (current.type == EventType.MouseDown && current.button == 0 && divider.Contains(current.mousePosition))
        {
            GUIUtility.hotControl = control;
            current.Use();
        }
        else if (current.type == EventType.MouseDrag && GUIUtility.hotControl == control)
        {
            if (horizontal) horizontalShare = Mathf.Clamp((current.mousePosition.x - body.x) / body.width, 0.22f, 0.80f);
            else verticalShare = Mathf.Clamp((current.mousePosition.y - body.y) / body.height, 0.22f, 0.80f);
            current.Use();
            Repaint();
        }
        else if (current.type == EventType.MouseUp && GUIUtility.hotControl == control)
        {
            GUIUtility.hotControl = 0;
            current.Use();
        }
    }

    private void OnInspectorUpdate()
    {
        if (preview != null && viewMode != 1) Repaint();
    }

    private void ReportFailure(Exception exception)
    {
        Exception cause = exception.GetBaseException();
        failure = "预览失败：" + (string.IsNullOrWhiteSpace(cause.Message) ? cause.GetType().Name : cause.Message);
        failureDetails = exception.ToString();
        preview?.Dispose();
        preview = null;
        Debug.LogException(exception);
        Repaint();
    }

    private void Build()
    {
        preview?.Dispose();
        preview = null;
        failure = null;
        try { preview = new RailPreview(); Apply(); }
        catch (Exception exception) { ReportFailure(exception); }
    }

    private void Apply()
    {
        if (preview == null) return;
        preview.SetState(state, lane);
        Repaint();
    }

    private static Rect Fit(Rect rect, float aspect)
    {
        float width = Mathf.Min(rect.width, rect.height * aspect);
        float height = width / aspect;
        return new Rect(rect.x + (rect.width - width) * 0.5f,
            rect.y + (rect.height - height) * 0.5f, width, height);
    }

    [MenuItem("Tools/Musical-Sprite/V11/02 Capture Rail Samples And Diagnostics")]
    public static void CaptureBatch()
    {
        string output = Path.GetFullPath("DesignPreviews/V11-Batch01");
        Directory.CreateDirectory(output);
        Scene loaded = default;
        try
        {
            if (UnityEngine.Object.FindFirstObjectByType<BattleVisualsController>() == null)
                loaded = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
            using (var context = new RailPreview())
            {
                string[] names = { "01-idle", "02-press", "03-hit", "04-adjacent-hit", "05-all-hit" };
                for (int i = 0; i < names.Length; i++)
                {
                    context.SetState(i, 1);
                    context.Render();
                    context.Save(Path.Combine(output, names[i] + ".png"));
                }
            }
            WriteDiagnostics(output);
            Debug.Log("[V11 Review] Captures and shader diagnostics written to " + output);
        }
        finally
        {
            if (loaded.IsValid() && loaded.isLoaded) EditorSceneManager.CloseScene(loaded, true);
        }
    }

    private static void WriteDiagnostics(string output)
    {
        var report = new StringBuilder("# V11 batch 01 — actual editor diagnostics\n\n");
        report.AppendLine("Generated UTC: " + DateTime.UtcNow.ToString("O"));
        report.AppendLine("Unity: " + Application.unityVersion);
        report.AppendLine("Graphics: " + SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName);
        report.AppendLine("Pipeline: " + GraphicsSettings.currentRenderPipeline);
        if (Camera.main != null)
        {
            var data = Camera.main.GetComponent<UniversalAdditionalCameraData>();
            int index = data != null ? new SerializedObject(data).FindProperty("m_RendererIndex").intValue : -1;
            report.AppendLine("Main camera renderer index: " + index);
        }
        foreach (var controller in UnityEngine.Object.FindObjectsByType<FeverSceneryController>(FindObjectsSortMode.None))
        {
            report.AppendLine("\n## Fever side " + controller.side);
            foreach (var layer in new[] { controller.background, controller.feverLayer, controller.superFeverLayer })
            {
                if (layer == null) { report.AppendLine("Missing layer reference"); continue; }
                var renderer = layer.GetComponent<SpriteRenderer>();
                var material = renderer != null ? renderer.sharedMaterial : null;
                var shader = material != null ? material.shader : null;
                report.AppendLine("- " + layer.name + ": sprite=" + (renderer != null ? renderer.sprite : null));
                report.AppendLine("  material=" + AssetDatabase.GetAssetPath(material) + "; shader=" +
                    (shader != null ? shader.name : "MISSING") + "; supported=" + (shader != null && shader.isSupported));
                if (shader != null)
                    foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        report.AppendLine("  " + message.severity + ": " + message.message);
            }
        }
        var newShader = Resources.Load<Shader>("Shaders/JudgementSoftLight");
        report.AppendLine("\n## New rail shader\nSupported: " + (newShader != null && newShader.isSupported));
        if (newShader != null)
            foreach (var message in ShaderUtil.GetShaderMessages(newShader))
                report.AppendLine(message.severity + ": " + message.message);
        report.AppendLine("\nThese are editor material / shader facts, not evidence that historical pink output was reproduced or repaired.");
        File.WriteAllText(Path.Combine(output, "editor-diagnostics.md"), report.ToString(), new UTF8Encoding(false));
    }

    private sealed class RailPreview : IDisposable
    {
        private Scene scene;
        private Scene borrowedScene;
        private Camera camera;
        private readonly UnifiedJudgementRail[] rails = new UnifiedJudgementRail[2];
        public RenderTexture Target { get; private set; }

        public RailPreview()
        {
            try { Build(); }
            catch { Dispose(); throw; }
        }

        private void Build()
        {
            var battle = UnityEngine.Object.FindFirstObjectByType<BattleVisualsController>();
            if (battle == null)
            {
                borrowedScene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
                battle = UnityEngine.Object.FindFirstObjectByType<BattleVisualsController>();
            }
            if (battle == null || battle.leftSpawner == null || battle.rightSpawner == null || Camera.main == null)
                throw new InvalidOperationException("找不到实际战斗相机或左右 Spawner；请先打开 SampleScene。");
            scene = EditorSceneManager.NewPreviewScene();
            var cameraObject = NewObject("V11_ReviewCamera");
            camera = cameraObject.AddComponent<Camera>();
            camera.CopyFrom(Camera.main);
            camera.transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation);
            camera.enabled = false;
            camera.cameraType = CameraType.Preview;
            camera.scene = scene;
            camera.aspect = 16f / 9f;
            camera.rect = new Rect(0, 0, 1, 1);
            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            var sourceData = Camera.main.GetComponent<UniversalAdditionalCameraData>();
            if (sourceData != null)
            {
                int index = new SerializedObject(sourceData).FindProperty("m_RendererIndex").intValue;
                data.SetRenderer(index);
            }
            data.renderPostProcessing = false;
            var center = battle.centerLine != null ? battle.centerLine : UnityEngine.Object.FindFirstObjectByType<BattleCenterLine>();
            if (center == null || center.ground == null)
                throw new InvalidOperationException("缺少原地面引用，停止预览以免用替代地面误导审查。");
            var sourceMesh = center.ground.GetComponent<MeshFilter>();
            var sourceRenderer = center.ground.GetComponent<MeshRenderer>();
            if (sourceMesh == null || sourceRenderer == null)
                throw new InvalidOperationException("原地面没有可预览的 MeshFilter / MeshRenderer。");
            var ground = NewObject("OriginalGround_ReadOnly");
            ground.AddComponent<MeshFilter>().sharedMesh = sourceMesh.sharedMesh;
            ground.AddComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
            ground.transform.SetPositionAndRotation(center.ground.position, center.ground.rotation);
            ground.transform.localScale = center.ground.lossyScale;
            for (int side = 0; side < 2; side++)
            {
                var source = side == 0 ? battle.leftSpawner : battle.rightSpawner;
                var root = NewObject(side == 0 ? "Red_FourLanes" : "Blue_FourLanes");
                rails[side] = root.AddComponent<UnifiedJudgementRail>();
                rails[side].Configure(side, source.hitPoint, source.laneSpacing, center);
                if (!rails[side].IsConfigured) throw new InvalidOperationException("新判定 Shader 不可用；请查看 Console。");
            }
            Target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { name = "V11_RailReview" };
            Target.Create();
            SetState(0, 1);
        }

        private GameObject NewObject(string label)
        {
            var go = new GameObject(label);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        public void SetState(int state, int lane)
        {
            lane = Mathf.Clamp(lane, 0, 3);
            foreach (var rail in rails)
            {
                rail.ClearPreviewState();
                if (state == 1) rail.SetHeld(lane, true);
                if (state == 2 || state == 3) rail.Hit(lane);
                if (state == 3) rail.Hit(lane < 3 ? lane + 1 : lane - 1);
                if (state == 4) for (int i = 0; i < 4; i++) rail.Hit(i);
            }
        }

        public void Advance(float delta)
        {
            foreach (var rail in rails) if (rail != null) rail.Advance(delta);
        }

        public void Render()
        {
            foreach (var rail in rails) rail.RefreshParameters();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = Target };
            RenderPipeline.SubmitRenderRequest(camera, request);
        }

        public void Save(string path)
        {
            var previous = RenderTexture.active;
            var texture = new Texture2D(Target.width, Target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = Target;
                texture.ReadPixels(new Rect(0, 0, Target.width, Target.height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
        }

        public void Dispose()
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            if (Target != null) { Target.Release(); UnityEngine.Object.DestroyImmediate(Target); Target = null; }
            if (borrowedScene.IsValid() && borrowedScene.isLoaded) EditorSceneManager.CloseScene(borrowedScene, true);
        }
    }
}
