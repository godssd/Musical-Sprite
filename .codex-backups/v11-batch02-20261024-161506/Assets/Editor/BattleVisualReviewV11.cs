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
    }

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (animate && preview != null)
        {
            preview.Advance((float)Math.Min(0.05, now - lastTime));
            Repaint();
        }
        lastTime = now;
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("第一批：原相机与原地面上的四轨判定 UI。此窗口使用隔离预览场景，不保存或改动 SampleScene。实际运行也已接入同一套判定带。", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("载入 / 刷新小样")) Build();
            lane = EditorGUILayout.IntSlider("轨道（0–3）", lane, 0, 3);
            animate = GUILayout.Toggle(animate, "播放衰减", "Button", GUILayout.Width(95));
        }
        string[] states = { "常态", "按住", "单轨命中", "相邻命中", "四轨命中" };
        int next = GUILayout.Toolbar(state, states);
        if (next != state) { state = next; Apply(); }
        if (GUILayout.Button("重播当前状态")) Apply();
        if (failure != null) EditorGUILayout.HelpBox(failure, MessageType.Error);
        if (preview == null) return;
        Rect available = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        Rect frame = Fit(available, 16f / 9f);
        if (Event.current.type == EventType.Repaint)
        {
            preview.Render();
            GUI.DrawTexture(frame, preview.Target, ScaleMode.ScaleToFit, false);
        }
        EditorGUILayout.LabelField("审查：四段 / 五边；红蓝同形；按下有弱前向光；Hit 更亮；共享边不叠加；无中轴刻度。轨道 0 由实际 Spawner 定义。", EditorStyles.wordWrappedMiniLabel);
    }

    private void Build()
    {
        preview?.Dispose();
        preview = null;
        failure = null;
        try { preview = new RailPreview(); Apply(); }
        catch (Exception exception) { failure = exception.Message; Debug.LogException(exception); }
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
