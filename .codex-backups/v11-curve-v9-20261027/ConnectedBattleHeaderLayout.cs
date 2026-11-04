using UnityEngine;
using UnityEngine.UI;

/// <summary>Mirrored V5 header. HP and scoring stay on their existing controllers.</summary>
public sealed class ConnectedBattleHeaderLayout : MonoBehaviour
{
    private ScoreManager owner;
    private ConnectedHeaderStyle style;
    private Canvas canvas;
    private RectTransform viewport;
    private readonly HPBarLiquid[] bars = new HPBarLiquid[2];
    private readonly RectTransform[] groups = new RectTransform[2];
    private readonly Image[] frames = new Image[2], outerLeaves = new Image[2], innerLeaves = new Image[2], marks = new Image[2];
    private readonly float[] widths = new float[2], velocities = new float[2];
    private bool initialized;
    private int revision = -1;
    public bool IsReady => initialized && canvas != null && canvas.isActiveAndEnabled;

    public void Configure(ScoreManager manager, ConnectedHeaderStyle profile)
    {
        owner = manager; style = profile;
        FindBars();
    }

    private void FindBars()
    {
        foreach (var bar in FindObjectsByType<HPBarLiquid>(FindObjectsSortMode.None))
            if (bar.side >= 0 && bar.side < 2 && bar.gameObject.scene == gameObject.scene &&
                bar.useHandpaintedArtwork && (bar.scoreManager == null || bar.scoreManager == owner)) bars[bar.side] = bar;
    }

    private ScoreDisplay Score(int side) => side == 0 ? owner.leftScoreDisplay : owner.rightScoreDisplay;

    private void LateUpdate()
    {
        if (owner == null || style == null || !style.HasArtwork || !HandpaintedHUD.EnsureLoaded()) return;
        if (bars[0] == null || bars[1] == null) FindBars();
        if (bars[0] == null || bars[1] == null || Score(0) == null || Score(1) == null) return;
        for (int side = 0; side < 2; side++)
            if (bars[side].fillImage == null || bars[side].hpNumberSprite == null) return;
        if (canvas == null) Build();
        Rect area = Screen.safeArea;
        if (Camera.main != null)
        {
            Rect cameraArea = Camera.main.pixelRect;
            float left = Mathf.Max(area.xMin, cameraArea.xMin), bottom = Mathf.Max(area.yMin, cameraArea.yMin);
            float right = Mathf.Min(area.xMax, cameraArea.xMax), top = Mathf.Min(area.yMax, cameraArea.yMax);
            if (right > left && top > bottom) area = Rect.MinMaxRect(left, bottom, right, top);
        }
        viewport.anchorMin = new Vector2(area.xMin / Mathf.Max(1, Screen.width), area.yMin / Mathf.Max(1, Screen.height));
        viewport.anchorMax = new Vector2(area.xMax / Mathf.Max(1, Screen.width), area.yMax / Mathf.Max(1, Screen.height));
        viewport.offsetMin = viewport.offsetMax = Vector2.zero;

        float widest = 1f;
        for (int side = 0; side < 2; side++)
        {
            float target = FrameTarget(side);
            // New digits are visible immediately: grow immediately too, so the leaf never crosses them.
            // Retraction can ease safely because the visible number is already narrower.
            if (!initialized || style.resizeTime <= 0f || target >= widths[side])
            {
                widths[side] = target; velocities[side] = 0f;
            }
            else widths[side] = Mathf.SmoothDamp(widths[side], target, ref velocities[side], style.resizeTime, Mathf.Infinity, Time.unscaledDeltaTime);
            widest = Mathf.Max(widest, Mathf.Max(target, widths[side]) + style.endPadding);
        }
        // Same scale on both teams; allocate using the larger current/target width, including score punch.
        float safeWidth = Mathf.Max(1f, ((RectTransform)canvas.transform).rect.width * area.width / Mathf.Max(1, Screen.width));
        float available = Mathf.Max(1f, safeWidth * .5f - style.screenInset.x - style.centerGap * .5f);
        float scale = Mathf.Min(Mathf.Max(.1f, style.scale), available / widest);
        bool refresh = !initialized || revision != style.Revision;
        for (int side = 0; side < 2; side++) Apply(side, scale, refresh);
        revision = style.Revision; initialized = true;
    }

    private float FrameTarget(int side)
    {
        float scoreWidth = Score(side).HandpaintedWidth(style.scoreDigitHeight) * (1f + style.scorePunchAllowance);
        // Endcaps always retain their native aspect; they cannot overlap even for a one-digit score.
        Sprite sprite = side == 0 ? style.redFrame : style.blueFrame;
        float capWidth = (sprite.border.x + sprite.border.z) * style.frameHeight / sprite.rect.height;
        return Mathf.Max(capWidth + 1f, style.scoreOffset.x + scoreWidth + style.scoreTailSpace);
    }

    private void Build()
    {
        var root = NewObject("ConnectedBattleHeader_V5", null);
        canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
        viewport = NewObject("BattleCameraSafeArea", root.transform).GetComponent<RectTransform>();
        for (int side = 0; side < 2; side++)
        {
            groups[side] = NewObject(side == 0 ? "Header_Red_V5" : "Header_Blue_V5", viewport).GetComponent<RectTransform>();
            frames[side] = HandpaintedHUD.NewImage(groups[side], "ConnectedCrescentAndLine", side == 0 ? style.redFrame : style.blueFrame, Color.white);
            frames[side].type = Image.Type.Sliced;
            bars[side].transform.SetParent(groups[side], false);
            Score(side).transform.SetParent(groups[side], false);
            Score(side).UseHandpaintedHUD(style.scoreDigitHeight);
            Score(side).SetHandpaintedAlignment(side == 1);
            Score(side).isScreenSpace = true;
            HideOldArtwork(bars[side]);
            outerLeaves[side] = HandpaintedHUD.NewImage(groups[side], "OuterLeaves", HandpaintedHUD.Leaves, Color.white);
            innerLeaves[side] = HandpaintedHUD.NewImage(groups[side], "ScoreEndLeaves", HandpaintedHUD.Leaves, Color.white);
            marks[side] = HandpaintedHUD.NewImage(groups[side], "BrushMarks", HandpaintedHUD.HPStrokes, HandpaintedHUD.TeamColor(side));
        }
    }

    private static void HideOldArtwork(HPBarLiquid bar)
    {
        var detail = bar.GetComponent<HandpaintedHPDecoration>();
        if (detail != null) detail.enabled = false;
        foreach (string name in new[] { "HandpaintedTail_V11", "HPOuterLeaves_V11", "HPBrushMarks_V11", "HPPaintDot_V11", "HPInnerLeaves_V11", "Frame", "Decoration" })
        {
            var image = bar.transform.Find(name)?.GetComponent<Image>();
            if (image != null) image.enabled = false;
        }
        if (bar.hpText != null) bar.hpText.enabled = false;
    }

    private void Apply(int side, float scale, bool refresh)
    {
        float width = widths[side] + style.endPadding;
        RectTransform group = groups[side];
        group.anchorMin = group.anchorMax = new Vector2(side == 0 ? 0f : 1f, 1f);
        group.pivot = new Vector2(side == 0 ? 0f : 1f, 1f);
        group.sizeDelta = new Vector2(width, style.height);
        group.anchoredPosition = new Vector2(side == 0 ? style.screenInset.x : -style.screenInset.x, -style.screenInset.y);
        group.localScale = Vector3.one * scale;
        var frame = frames[side];
        frame.sprite = side == 0 ? style.redFrame : style.blueFrame;
        frame.pixelsPerUnitMultiplier = frame.sprite.rect.height / Mathf.Max(1f, style.frameHeight);
        TopRect(frame.rectTransform, new Vector2(0f, style.frameTop), new Vector2(widths[side], style.frameHeight), side, width);

        var bar = bars[side];
        Sprite bodySprite = side == 1 && style.blueBody != null ? style.blueBody : style.body;
        TopRect((RectTransform)bar.transform, Vector2.zero, style.hpRootSize, side, width);
        foreach (var image in new[] { bar.fillImage, bar.ghostImage, bar.healingImage, bar.flashImage })
        {
            if (image == null) continue;
            if (image.sprite != bodySprite)
            {
                image.sprite = bodySprite; image.type = Image.Type.Simple; image.preserveAspect = false;
                image.color = new Color(1f, 1f, 1f, image.color.a);
            }
            TopRect(image.rectTransform, style.bodyOffset, style.bodySize, side, style.hpRootSize.x);
        }
        var background = bar.transform.Find("Background")?.GetComponent<Image>();
        if (background != null)
        {
            background.sprite = bodySprite; background.type = Image.Type.Simple; background.preserveAspect = false;
            Color team = HandpaintedHUD.TeamColor(side);
            background.color = new Color(team.r * .28f, team.g * .28f, team.b * .28f, .65f);
            TopRect(background.rectTransform, style.bodyOffset, style.bodySize, side, style.hpRootSize.x);
        }
        var number = bar.hpNumberSprite;
        TopRect((RectTransform)number.transform, style.numberOffset, style.numberSize, side, style.hpRootSize.x);
        if (refresh)
        {
            number.center = false; number.rightAlign = side == 1; number.leftPadding = number.rightPadding = 0f;
            number.digitHeight = style.hpDigitHeight; number.labelHeightRatio = style.hpLabelRatio;
            number.labelGap = style.hpLabelGap; number.RefreshLayout();
            Score(side).UseHandpaintedHUD(style.scoreDigitHeight);
            Score(side).SetHandpaintedAlignment(side == 1);
        }
        float scoreWidth = Score(side).HandpaintedWidth(style.scoreDigitHeight);
        TopRect((RectTransform)Score(side).transform, style.scoreOffset, new Vector2(Mathf.Max(1f, scoreWidth), style.scoreDigitHeight + 8f), side, width);
        PlaceDetail(outerLeaves[side], style.outerLeafOffset, style.outerLeafSize, side, width);
        PlaceDetail(innerLeaves[side], new Vector2(widths[side] + style.innerLeafOffset.x, style.innerLeafOffset.y), style.innerLeafSize, side, width);
        PlaceDetail(marks[side], style.marksOffset, style.marksSize, side, width);
    }

    private void PlaceDetail(Image image, Vector2 offset, Vector2 size, int side, float width)
    {
        image.enabled = style.showDetails && image.sprite != null;
        TopRect(image.rectTransform, offset, size, side, width);
        image.rectTransform.localScale = new Vector3(side == 0 ? 1f : -1f, 1f, 1f);
        image.preserveAspect = true;
    }

    private GameObject NewObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    private static void TopRect(RectTransform rect, Vector2 offset, Vector2 size, int side, float parentWidth)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(side == 0 ? offset.x : parentWidth - offset.x - size.x, -offset.y);
    }

    private void OnDestroy() { if (canvas != null) Destroy(canvas.gameObject); }
}
