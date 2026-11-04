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
        EditorGUILayout.LabelField("判定 UI 调节", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            viewMode = GUILayout.Toolbar(viewMode, new[] { "参数与画面", "只调参数", "只看画面" }, GUILayout.Height(24f));
            if (GUILayout.Button("打开独立调参窗口", GUILayout.Width(145f), GUILayout.Height(24f)))
                JudgementRailParametersWindow.Open();
        }

        if (viewMode != 1)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("载入 / 刷新画面", GUILayout.Width(145f))) Build();
                lane = EditorGUILayout.IntSlider(new GUIContent("预览轨道", "选择隔离小样的轨道，不改变游戏轨道数量。"), lane, 0, 3);
                animate = GUILayout.Toggle(animate, "播放衰减", "Button", GUILayout.Width(95f));
            }
            int next = GUILayout.Toolbar(state, new[] { "常态", "按住", "单轨命中", "相邻命中", "四轨命中" });
            if (next != state) { state = next; Apply(); }
            if (GUILayout.Button("重播当前预览")) Apply();
        }

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

        // Keep the entire control tree in GUILayout. BeginArea with a GetRect result is
        // unsafe on Layout events (GetRect may return a dummy rect), caching zero-width controls.
        if (viewMode == 1)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
                DrawParameterContent(position.width);
            return;
        }
        if (viewMode == 2) { DrawPreviewContent(); return; }

        if (position.width >= 1050f)
        {
            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * horizontalShare)))
                    DrawParameterContent(position.width * horizontalShare);
                GUILayout.Box(GUIContent.none, GUILayout.Width(6f), GUILayout.ExpandHeight(true));
                DrawDivider(GUILayoutUtility.GetLastRect(), true);
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                    DrawPreviewContent();
            }
        }
        else
        {
            float bodyHeight = Mathf.Max(180f, position.height - (string.IsNullOrWhiteSpace(failure) ? 155f : 240f));
            float parameterHeight = Mathf.Clamp(bodyHeight * verticalShare, 170f, Mathf.Max(170f, bodyHeight - 100f));
            using (new EditorGUILayout.VerticalScope(GUILayout.Height(parameterHeight)))
                DrawParameterContent(position.width);
            GUILayout.Box(GUIContent.none, GUILayout.Height(6f), GUILayout.ExpandWidth(true));
            DrawDivider(GUILayoutUtility.GetLastRect(), false);
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
                DrawPreviewContent();
        }
    }

    private void DrawParameterContent(float width)
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
        inspector.DrawParameterFields(width - 24f);
        EditorGUILayout.EndScrollView();
        inspector.DrawHelp();
    }

    private void DrawPreviewContent()
    {
        if (preview == null)
        {
            EditorGUILayout.HelpBox("尚未载入画面。点击“载入 / 刷新画面”；如只需调参，请选择“只调参数”。", MessageType.Info);
            GUILayout.FlexibleSpace();
            return;
        }
        Rect frame = GUILayoutUtility.GetRect(120f, 120f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (Event.current.type == EventType.Repaint)
        {
            try
            {
                preview.Render();
                GUI.DrawTexture(Fit(frame, 16f / 9f), preview.Target, ScaleMode.ScaleToFit, false);
            }
            catch (Exception exception) { ReportFailure(exception); }
        }
        EditorGUILayout.LabelField("拖动分隔条可调整面积；参数改动实时作用于游戏和画面。", EditorStyles.wordWrappedMiniLabel);
    }

    private void DrawDivider(Rect divider, bool horizontal)
    {
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
            // Update from movement rather than the temporary Layout-event rectangle.
            if (horizontal) horizontalShare = Mathf.Clamp(horizontalShare + current.delta.x / Mathf.Max(1f, position.width), 0.36f, 0.65f);
            else verticalShare = Mathf.Clamp(verticalShare + current.delta.y / Mathf.Max(1f, position.height - 155f), 0.30f, 0.85f);
            current.Use(); Repaint();
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
    { CaptureValidation(Path.GetFullPath("DesignPreviews/V11-Batch01")); }

    public static void CaptureValidation(string output)
    {
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
            camera.cameraType = CameraType.Game;
            camera.scene = scene;
            camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);
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
