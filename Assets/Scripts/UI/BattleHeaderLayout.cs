using UnityEngine;
using UnityEngine.UI;

/// <summary>One camera-safe header layout for the actual HP / score bindings. Other HUD stays on its original canvas.</summary>
public sealed class BattleHeaderLayout : MonoBehaviour
{
    private ScoreManager owner;
    private HPDecorationStyle style;
    private readonly HPBarLiquid[] hp = new HPBarLiquid[2];
    private readonly RectTransform[] groups = new RectTransform[2];
    private Canvas canvas;
    private RectTransform viewport;
    private Rect lastArea;
    private Vector2 lastSize;
    private int revision = -1;

    public void Configure(ScoreManager manager)
    {
        owner = manager;
        style = Resources.Load<HPDecorationStyle>("BattleVisualV11/HPDecorationStyle");
        foreach (var bar in FindObjectsByType<HPBarLiquid>(FindObjectsSortMode.None))
            if (bar.side >= 0 && bar.side < 2 && bar.gameObject.scene == gameObject.scene &&
                bar.useHandpaintedArtwork && (bar.scoreManager == null || bar.scoreManager == owner))
                hp[bar.side] = bar;
    }

    private void LateUpdate()
    {
        if (owner == null || style == null || !HandpaintedHUD.EnsureLoaded()) return;
        // Wait for HP Start to bind its artwork. Script Start order must not overwrite the layout.
        if (hp[0] == null || hp[1] == null || owner.leftScoreDisplay == null || owner.rightScoreDisplay == null ||
            hp[0].fillImage == null || hp[1].fillImage == null ||
            hp[0].fillImage.sprite != HandpaintedHUD.Body || hp[1].fillImage.sprite != HandpaintedHUD.Body) return;
        if (canvas == null) Build();
        Rect area = Screen.safeArea;
        if (Camera.main != null)
        {
            Rect cameraArea = Camera.main.pixelRect;
            float left = Mathf.Max(area.xMin, cameraArea.xMin), bottom = Mathf.Max(area.yMin, cameraArea.yMin);
            float right = Mathf.Min(area.xMax, cameraArea.xMax), top = Mathf.Min(area.yMax, cameraArea.yMax);
            if (right > left && top > bottom) area = Rect.MinMaxRect(left, bottom, right, top);
        }
        Vector2 size = ((RectTransform)canvas.transform).rect.size;
        if (area != lastArea || size != lastSize || revision != style.Revision)
        {
            lastArea = area; lastSize = size; revision = style.Revision;
            viewport.anchorMin = new Vector2(area.xMin / Mathf.Max(1f, Screen.width), area.yMin / Mathf.Max(1f, Screen.height));
            viewport.anchorMax = new Vector2(area.xMax / Mathf.Max(1f, Screen.width), area.yMax / Mathf.Max(1f, Screen.height));
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            Apply();
        }
    }

    private void Build()
    {
        var go = NewObject("HandpaintedBattleHeader_V11", null);
        canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
        viewport = NewObject("BattleCameraSafeArea", go.transform).GetComponent<RectTransform>();
        for (int side = 0; side < 2; side++)
        {
            groups[side] = NewObject(side == 0 ? "Header_Red" : "Header_Blue", viewport).GetComponent<RectTransform>();
            hp[side].transform.SetParent(groups[side], false);
            ScoreDisplay score = side == 0 ? owner.leftScoreDisplay : owner.rightScoreDisplay;
            score.transform.SetParent(groups[side], false);
            score.UseHandpaintedHUD(style.scoreDigitHeight);
        }
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private GameObject NewObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    private void Apply()
    {
        float width = Mathf.Max(1f, style.headerSize.x);
        float available = Mathf.Max(1f, viewport.rect.width * .5f - style.screenInset.x);
        float scale = Mathf.Min(Mathf.Max(.1f, style.headerScale), available / width);
        for (int side = 0; side < 2; side++)
        {
            var group = groups[side];
            group.anchorMin = group.anchorMax = new Vector2(side == 0 ? 0f : 1f, 1f);
            group.pivot = new Vector2(side == 0 ? 0f : 1f, 1f);
            group.sizeDelta = style.headerSize;
            group.anchoredPosition = new Vector2(side == 0 ? style.screenInset.x : -style.screenInset.x, -style.screenInset.y);
            group.localScale = Vector3.one * scale;
            var bar = hp[side];
            TopRect((RectTransform)bar.transform, style.hpRootOffset, style.hpRootSize, side, width);
            foreach (var image in new[] { bar.fillImage, bar.ghostImage, bar.healingImage, bar.flashImage })
                if (image != null) BodyRect(image.rectTransform, side);
            var background = bar.transform.Find("Background")?.GetComponent<Image>();
            if (background != null) BodyRect(background.rectTransform, side);
            var number = bar.hpNumberSprite;
            var rect = (RectTransform)number.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = Vector2.zero;
            rect.sizeDelta = style.numberSize;
            rect.anchoredPosition = new Vector2(side == 0 ? style.numberOffset.x : style.hpRootSize.x - style.numberOffset.x - style.numberSize.x, style.numberOffset.y);
            number.center = false; number.rightAlign = side == 1; number.leftPadding = number.rightPadding = 0f;
            number.digitHeight = style.hpDigitHeight; number.labelHeightRatio = style.hpLabelHeightRatio;
            number.labelGap = style.hpLabelGap; number.RefreshLayout();
            bar.GetComponent<HandpaintedHPDecoration>()?.Refresh();
            ScoreDisplay score = side == 0 ? owner.leftScoreDisplay : owner.rightScoreDisplay;
            TopRect((RectTransform)score.transform, style.scoreOffset, style.scoreSize, side, width);
            score.UseHandpaintedHUD(style.scoreDigitHeight);
        }
    }

    private void BodyRect(RectTransform rect, int side)
    {
        rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = Vector2.zero;
        rect.sizeDelta = style.bodySize;
        rect.anchoredPosition = new Vector2(side == 0 ? style.bodyOffset.x : style.hpRootSize.x - style.bodyOffset.x - style.bodySize.x, style.bodyOffset.y);
    }

    private static void TopRect(RectTransform rect, Vector2 offset, Vector2 size, int side, float parentWidth)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(side == 0 ? offset.x : parentWidth - offset.x - size.x, -offset.y);
    }
}
