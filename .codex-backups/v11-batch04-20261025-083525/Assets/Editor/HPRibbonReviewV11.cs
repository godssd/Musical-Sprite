using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Isolated curved HP body and interval feedback sample.</summary>
public sealed class HPRibbonReviewV11 : EditorWindow
{
    private HPPreview preview;
    private float from = 0.35f, target = 0.75f;
    private double lastTime;
    private string failure;
    private readonly List<Vector2> queued = new List<Vector2>();
    private float elapsed;

    [MenuItem("Tools/Musical-Sprite/V11/03 HP Ribbon And Healing Review")]
    public static void Open()
    {
        var window = GetWindow<HPRibbonReviewV11>("V11 血条 / 回血");
        window.minSize = new Vector2(760, 480);
    }

    private void OnEnable()
    {
        lastTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        preview?.Dispose(); preview = null;
    }

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)Math.Min(0.05, now - lastTime);
        lastTime = now;
        if (preview == null) return;
        elapsed += dt;
        while (queued.Count > 0 && elapsed >= queued[0].x)
        {
            preview.SetTarget(queued[0].y);
            queued.RemoveAt(0);
        }
        preview.Advance(dt);
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("技术小样：曲带主体、红蓝镜像、绿色回血和黄色损血区间。程序曲带用于验证遮罩与动画，尚非完整手绘 HUD。原游戏血条已接入同一回血逻辑；隔离预览不修改原场景。", MessageType.Info);
        if (GUILayout.Button("载入 / 重建曲带小样")) Build();
        from = EditorGUILayout.Slider("起始血量", from, 0f, 1f);
        target = EditorGUILayout.Slider("目标血量", target, 0f, 1f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("设为起始（无动画）")) { queued.Clear(); preview?.Reset(from); }
            if (GUILayout.Button("变化到目标（可连续点）")) { queued.Clear(); preview?.SetTarget(target); }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("回血示例")) Scenario(0);
            if (GUILayout.Button("扣血示例")) Scenario(1);
            if (GUILayout.Button("连续回血")) Scenario(2);
            if (GUILayout.Button("回血被伤害打断")) Scenario(3);
            if (GUILayout.Button("伤害后立刻回血")) Scenario(4);
        }
        if (failure != null) EditorGUILayout.HelpBox(failure, MessageType.Error);
        if (preview == null) return;
        Rect area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (Event.current.type == EventType.Repaint)
        {
            preview.Render();
            GUI.DrawTexture(area, preview.Target, ScaleMode.ScaleToFit, false);
        }
        EditorGUILayout.LabelField("上：红方；下：蓝方镜像。绿色只显示新增 / 尚未追平区间，实条追上后消失。可在动画中反复改变目标检查接续。", EditorStyles.wordWrappedMiniLabel);
    }

    private void Build()
    {
        queued.Clear(); elapsed = 0f;
        preview?.Dispose(); preview = null; failure = null;
        try { preview = new HPPreview(); preview.Reset(from); }
        catch (Exception exception) { failure = exception.Message; Debug.LogException(exception); }
    }

    private void Scenario(int kind)
    {
        if (preview == null) Build();
        if (preview == null) return;
        queued.Clear(); elapsed = 0f;
        if (kind == 1) { preview.Reset(0.90f); preview.SetTarget(0.45f); return; }
        if (kind == 4)
        {
            preview.Reset(0.90f); preview.SetTarget(0.35f);
            queued.Add(new Vector2(0.14f, 0.65f)); return;
        }
        preview.Reset(0.35f);
        preview.SetTarget(kind == 2 ? 0.60f : 0.75f);
        if (kind == 2)
        {
            queued.Add(new Vector2(0.14f, 0.75f));
            queued.Add(new Vector2(0.28f, 0.90f));
        }
        if (kind == 3) queued.Add(new Vector2(0.16f, 0.50f));
    }

    private sealed class HPPreview : IDisposable
    {
        private Scene scene;
        private Camera camera;
        private readonly HPBarLiquid[] bars = new HPBarLiquid[2];
        public RenderTexture Target { get; private set; }

        public HPPreview()
        {
            try { Build(); } catch { Dispose(); throw; }
        }

        private GameObject NewObject(string name, params Type[] components)
        {
            var go = new GameObject(name, components);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private void Build()
        {
            scene = EditorSceneManager.NewPreviewScene();
            camera = NewObject("HP_ReviewCamera", typeof(Camera)).GetComponent<Camera>();
            camera.enabled = false;
            camera.cameraType = CameraType.Preview;
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = 3.1f;
            camera.aspect = 2f;
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 50f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.09f, 0.12f);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            var canvasObject = NewObject("HP_ReviewCanvas", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvasObject.transform.localScale = Vector3.one * 0.01f;
            for (int side = 0; side < 2; side++)
            {
                var root = NewObject(side == 0 ? "RedRibbon" : "BlueRibbon", typeof(RectTransform));
                root.SetActive(false);
                root.transform.SetParent(canvasObject.transform, false);
                var rt = root.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(930f, 170f);
                rt.anchoredPosition = new Vector2(0f, side == 0 ? 140f : -140f);
                var background = Layer(root.transform, "Background", side);
                background.color = new Color(0.15f, 0.20f, 0.24f);
                var ghost = Layer(root.transform, "Ghost", side);
                var fill = Layer(root.transform, "Fill", side);
                var flash = Layer(root.transform, "FlashOverlay", side);
                flash.color = new Color(1f, 1f, 1f, 0f);
                var hp = root.AddComponent<HPBarLiquid>();
                hp.enabled = false;
                hp.side = side; hp.logHP = false;
                hp.fillImage = fill; hp.ghostImage = ghost; hp.flashImage = flash;
                hp.topColor = hp.bottomColor = side == 0
                    ? new Color(1f, 0.30f, 0.28f) : new Color(0.43f, 0.61f, 1f);
                hp.crestIntensity = 0.10f; hp.surfaceGlow = 0f;
                hp.ambientWave = 0.001f;
                hp.fillDuration = 0.8f; hp.fillEasePower = 3f;
                hp.ghostHold = 0.25f; hp.ghostDrainDuration = 0.7f;
                hp.flashDuration = 0.5f; hp.punchDuration = 0.3f;
                hp.punchScaleMin = 1.02f; hp.punchScaleMax = 1.12f;
                hp.shakeDuration = 0.55f;
                root.SetActive(true);
                hp.PreviewSetFill(0.35f);
                hp.TickFX(0f);
                bars[side] = hp;
            }
            Target = new RenderTexture(1200, 600, 24, RenderTextureFormat.ARGB32);
            Target.Create();
        }

        private RibbonHPImage Layer(Transform parent, string name, int side)
        {
            var image = NewObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RibbonHPImage)).GetComponent<RibbonHPImage>();
            image.transform.SetParent(parent, false);
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
            image.raycastTarget = false;
            image.mirrorShape = side == 1;
            return image;
        }

        public void Reset(float ratio) { foreach (var hp in bars) hp.PreviewSetFill(ratio); }
        public void SetTarget(float ratio) { foreach (var hp in bars) hp.SetFill(ratio); }
        public void Advance(float dt) { foreach (var hp in bars) hp.TickFX(dt); }
        public void Render()
        {
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = Target });
        }

        public void Dispose()
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            if (Target != null) { Target.Release(); UnityEngine.Object.DestroyImmediate(Target); Target = null; }
        }
    }
}
