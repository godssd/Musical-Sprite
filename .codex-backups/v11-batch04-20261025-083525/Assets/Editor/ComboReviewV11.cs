using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Isolated review of the same ComboDisplay and glyph-fracture mesh used in battle.</summary>
public sealed class ComboReviewV11 : EditorWindow
{
    private ComboPreview preview;
    private int number = 55;
    private float duration = .30f, spread = .16f, time;
    private bool playing;
    private double lastTime;
    private float pendingHit = -1f;
    private string failure;
    private Font font;

    [MenuItem("Tools/Musical-Sprite/V11/04 Combo Growth And Tear Review")]
    public static void Open()
    {
        var window = GetWindow<ComboReviewV11>("V11 连击 / 撕碎");
        window.minSize = new Vector2(760f, 520f);
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
        float dt = (float)Math.Min(.05, now - lastTime);
        lastTime = now;
        if (preview == null || !playing) return;
        preview.Advance(dt);
        time += dt;
        if (pendingHit >= 0f && time >= pendingHit)
        {
            foreach (var display in preview.Displays) display.AddCombo();
            pendingHit = -1f;
        }
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("第三批技术小样：真实数字分片、红蓝同形、中心大块到边缘小块、灰化淡出。字体默认沿用当前战斗绑定；完整手绘 HUD、标题与装饰尚未替换。本窗口不修改场景和判定参数。", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("载入 / 重建小样", GUILayout.Width(150f))) Build();
            number = Mathf.Max(1, EditorGUILayout.IntField("示例连击数", number));
        }
        font = (Font)EditorGUILayout.ObjectField("预览字体（重建生效）", font, typeof(Font), false);
        duration = EditorGUILayout.Slider("撕碎总时长（秒）", duration, .15f, .6f);
        spread = EditorGUILayout.Slider("分离距离 / 字高", spread, 0f, .5f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("显示示例（无动画）")) ResetNumber();
            if (GUILayout.Button("连击 +1")) { EnsurePreview(); preview?.Add(); playing = true; }
            if (GUILayout.Button("MISS / BREAK 断连")) StartTear(false);
            if (GUILayout.Button("断连后 0.1 秒命中")) StartTear(true);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("削减 3（非零轻收束）")) { EnsurePreview(); preview?.Reduce(); playing = true; }
            if (GUILayout.Button("零连击时再次失败"))
            {
                EnsurePreview(); preview?.Reset(0); preview?.Break(duration, spread); playing = true;
            }
            playing = GUILayout.Toggle(playing, "播放", "Button", GUILayout.Width(80f));
        }
        EditorGUI.BeginChangeCheck();
        float next = EditorGUILayout.Slider("撕碎定格（秒）", Mathf.Min(time, duration), 0f, duration);
        if (EditorGUI.EndChangeCheck())
        {
            EnsurePreview(); preview?.Reset(number); preview?.Break(duration, spread); preview?.Advance(next);
            time = next; pendingHit = -1f; playing = false;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            foreach (int value in new[] { 1, 8, 55, 100, 12345 })
                if (GUILayout.Button(value.ToString())) { number = value; ResetNumber(); }
        }
        if (!string.IsNullOrWhiteSpace(failure)) EditorGUILayout.HelpBox(failure, MessageType.Error);
        if (preview == null) return;
        Rect frame = GUILayoutUtility.GetRect(100f, 100f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (Event.current.type == EventType.Repaint)
        {
            try { preview.Render(); GUI.DrawTexture(frame, preview.Target, ScaleMode.ScaleToFit, false); }
            catch (Exception exception) { failure = exception.Message; playing = false; }
        }
        EditorGUILayout.LabelField("左红右蓝。改变此处系数仅影响小样；游戏参数在实际 ComboDisplay 的“V11 连击反馈”中调整。", EditorStyles.wordWrappedMiniLabel);
    }

    private void EnsurePreview() { if (preview == null) Build(); }

    private void Build()
    {
        preview?.Dispose(); preview = null; failure = null; playing = false;
        pendingHit = -1f; time = 0f;
        try
        {
            if (font == null)
            {
                var battle = FindFirstObjectByType<BattleVisualsController>();
                if (battle != null && battle.leftComboDisplay != null && battle.leftComboDisplay.text != null)
                    font = battle.leftComboDisplay.text.font;
            }
            preview = new ComboPreview(font);
            preview.Reset(number);
        }
        catch (Exception exception) { failure = exception.ToString(); Debug.LogException(exception); }
    }

    private void ResetNumber()
    {
        EnsurePreview(); preview?.Reset(number);
        playing = false; time = 0f; pendingHit = -1f;
    }

    private void StartTear(bool newHit)
    {
        EnsurePreview(); preview?.Reset(number); preview?.Break(duration, spread);
        playing = true; time = 0f; pendingHit = newHit ? .1f : -1f;
    }

    private sealed class ComboPreview : IDisposable
    {
        private Scene scene;
        private Camera camera;
        public readonly ComboDisplay[] Displays = new ComboDisplay[2];
        public RenderTexture Target { get; private set; }

        public ComboPreview(Font font)
        {
            try { Build(font); } catch { Dispose(); throw; }
        }

        private GameObject NewObject(string name, params Type[] types)
        {
            var go = new GameObject(name, types);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private void Build(Font font)
        {
            scene = EditorSceneManager.NewPreviewScene();
            camera = NewObject("Combo_ReviewCamera", typeof(Camera)).GetComponent<Camera>();
            camera.enabled = false; camera.cameraType = CameraType.Preview; camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = 2.1f; camera.aspect = 2f;
            camera.nearClipPlane = .1f; camera.farClipPlane = 50f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.055f, .09f, .12f);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            var canvasObject = NewObject("Combo_ReviewCanvas", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            canvasObject.transform.localScale = Vector3.one * .01f;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int side = 0; side < 2; side++)
            {
                var root = NewObject(side == 0 ? "Combo_Red" : "Combo_Blue", typeof(RectTransform));
                root.SetActive(false); root.transform.SetParent(canvasObject.transform, false);
                var rect = root.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(side == 0 ? -205f : 205f, 0f);
                rect.sizeDelta = new Vector2(390f, 220f);
                var digits = NewObject("Digits", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                digits.transform.SetParent(root.transform, false);
                var text = digits.GetComponent<Text>();
                text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
                text.font = font; text.fontSize = 110; text.alignment = TextAnchor.MiddleCenter;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow; text.raycastTarget = false;
                var display = root.AddComponent<ComboDisplay>();
                display.enabled = false; display.text = text; display.isDynamic = false; display.side = side;
                display.comboColor = side == 0 ? new Color(1f, .31f, .29f) : new Color(.47f, .63f, 1f);
                root.SetActive(true); display.SetComboImmediate(55);
                Displays[side] = display;
            }
            Target = new RenderTexture(1400, 700, 24, RenderTextureFormat.ARGB32);
            Target.Create();
        }

        public void Reset(int value) { foreach (var display in Displays) display.SetComboImmediate(value); }
        public void Add() { foreach (var display in Displays) display.AddCombo(); }
        public void Reduce() { foreach (var display in Displays) display.ReduceBy(3); }
        public void Break(float duration, float spread)
        {
            foreach (var display in Displays)
            { display.tearDuration = duration; display.tearSpread = spread; display.ResetCombo(); }
        }
        public void Advance(float deltaTime) { foreach (var display in Displays) display.AdvanceVisuals(deltaTime); }
        public void Render() { Canvas.ForceUpdateCanvases(); camera.targetTexture = Target; camera.Render(); }
        public void Dispose()
        {
            if (camera != null) camera.targetTexture = null;
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            if (Target != null) { Target.Release(); DestroyImmediate(Target); Target = null; }
        }
    }
}
