using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Production PNG atlas artwork shared by live HUD and isolated review windows.</summary>
public static class HandpaintedHUD
{
    private const string Root = "BattleVisualV11/HudAssets/";
    [Serializable] private sealed class Region { public int x, y, width, height; }
    [Serializable] private sealed class Layout
    {
        public Region[] digits;
        public Region hp, combo, leaves, accents, body, tail;
    }
    private static Sprite[] digits;
    [Serializable] private sealed class HPDetails { public Region marks, dot; }
    public static Sprite HPStrokes { get; private set; }
    public static Sprite HPDot { get; private set; }
    public static Texture2D DigitTexture { get; private set; }
    public static Sprite HP { get; private set; }
    public static Sprite Combo { get; private set; }
    public static Sprite Leaves { get; private set; }
    public static Sprite Accents { get; private set; }
    public static Sprite Body { get; private set; }
    public static Sprite Tail { get; private set; }
    public static Color TeamColor(int side) => side == 0 ? new Color(1f, .32f, .30f) : new Color(.48f, .64f, 1f);

    public static bool EnsureLoaded()
    {
        if (digits != null && DigitTexture != null && Body != null) return true;
        var json = Resources.Load<TextAsset>(Root + "layout-v1");
        var digitTexture = Resources.Load<Texture2D>(Root + "digits-v1");
        var labels = Resources.Load<Texture2D>(Root + "labels-v1");
        var ribbon = Resources.Load<Texture2D>(Root + "ribbon-v1");
        if (json == null || digitTexture == null || labels == null || ribbon == null) return false;
        var layout = JsonUtility.FromJson<Layout>(json.text);
        if (layout == null || layout.digits == null || layout.digits.Length != 10) return false;
        digits = new Sprite[10]; DigitTexture = digitTexture;
        for (int i = 0; i < 10; i++) digits[i] = Make(digitTexture, layout.digits[i], "PaintedDigit_" + i);
        HP = Make(labels, layout.hp, "PaintedHP"); Combo = Make(labels, layout.combo, "PaintedCombo");
        Leaves = Make(labels, layout.leaves, "PaintedLeaves"); Accents = Make(labels, layout.accents, "PaintedAccents");
        Body = Make(ribbon, layout.body, "PaintedHPBody"); Tail = Make(ribbon, layout.tail, "PaintedHPTail");
        var detailJson = Resources.Load<TextAsset>(Root + "hp-details-v1");
        var detailTexture = Resources.Load<Texture2D>(Root + "hp-marks-v1");
        if (detailJson != null && detailTexture != null)
        {
            var details = JsonUtility.FromJson<HPDetails>(detailJson.text);
            HPStrokes = Make(detailTexture, details.marks, "PaintedHPStrokes");
            HPDot = Make(detailTexture, details.dot, "PaintedHPDot");
        }
        return true;
    }

    private static Sprite Make(Texture2D texture, Region region, string name)
    {
        var sprite = Sprite.Create(texture, new Rect(region.x, region.y, region.width, region.height),
            new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.name = name; sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    public static Sprite Digit(int value) => EnsureLoaded() && value >= 0 && value < 10 ? digits[value] : null;

    public static Image NewImage(Transform parent, string name, Sprite sprite, Color tint)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite; image.color = tint; image.raycastTarget = false;
        return image;
    }

    public static void ApplyHP(HPBarLiquid hp)
    {
        if (!EnsureLoaded() || hp.fillImage == null) return;
        Color team = TeamColor(hp.side);
        foreach (var image in new[] { hp.fillImage, hp.ghostImage, hp.healingImage, hp.flashImage })
        {
            if (image == null) continue;
            image.sprite = Body; image.type = Image.Type.Simple; image.preserveAspect = false;
            image.color = new Color(1f, 1f, 1f, image.color.a);
            PositionBar(image.rectTransform);
        }
        var background = hp.transform.Find("Background")?.GetComponent<Image>();
        if (background != null)
        {
            background.sprite = Body; background.type = Image.Type.Simple;
            background.color = new Color(team.r * .28f, team.g * .28f, team.b * .28f, .65f);
            PositionBar(background.rectTransform);
        }
        var frame = hp.transform.Find("Frame")?.GetComponent<Image>();
        if (frame != null) frame.enabled = false;
        if (hp.decorationImage != null) hp.decorationImage.enabled = false;
        hp.decorationImage = null;
        if (hp.transform.Find("HandpaintedTail_V11") == null)
        {
            var tail = NewImage(hp.transform, "HandpaintedTail_V11", Tail, team);
            tail.rectTransform.anchorMin = new Vector2(0f, 0f); tail.rectTransform.anchorMax = new Vector2(1f, 0f);
            tail.rectTransform.pivot = new Vector2(.5f, 0f);
            tail.rectTransform.sizeDelta = new Vector2(20f, 31f);
            tail.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            tail.rectTransform.localScale = new Vector3(hp.side == 0 ? 1f : -1f, 1f, 1f);
            tail.transform.SetAsFirstSibling();
        }
        var detail = hp.GetComponent<HandpaintedHPDecoration>();
        if (detail == null) detail = hp.gameObject.AddComponent<HandpaintedHPDecoration>();
        detail.Configure(hp.side, hp.transform.Find("HandpaintedTail_V11").GetComponent<Image>());
        if (hp.hpNumberSprite != null && hp.hpText != null && hp.hpNumberSprite.transform.IsChildOf(hp.hpText.transform))
        {
            hp.hpNumberSprite.gameObject.SetActive(false);
            hp.hpNumberSprite = null;
        }
        if (hp.hpNumberSprite == null)
        {
            var host = new GameObject("HandpaintedHPNumber_V11", typeof(RectTransform));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, hp.gameObject.scene);
            host.SetActive(false); host.transform.SetParent(hp.transform, false);
            hp.hpNumberSprite = host.AddComponent<HPNumberSprite>();
            hp.hpNumberSprite.ConfigureArtwork(HP, digits, team);
            host.SetActive(true);
        }
        else hp.hpNumberSprite.ConfigureArtwork(HP, digits, team);
        var numberRect = (RectTransform)hp.hpNumberSprite.transform;
        numberRect.anchorMin = new Vector2(0f, 0f); numberRect.anchorMax = new Vector2(1f, 0f);
        numberRect.pivot = new Vector2(.5f, 0f); numberRect.sizeDelta = new Vector2(0f, 38f);
        numberRect.anchoredPosition = new Vector2(0f, 29f);
        hp.hpNumberSprite.digitHeight = 34f; hp.hpNumberSprite.labelGap = 10f;
        hp.hpNumberSprite.center = true;
        hp.hpNumberSprite.ConfigureArtwork(HP, digits, team);
        hp.topColor = hp.bottomColor = team;
        hp.crestIntensity = .08f; hp.surfaceGlow = 0f; hp.ambientWave = .001f;
        hp.punchScaleMin = 1.02f; hp.punchScaleMax = 1.12f;
        hp.paperTextureStrength = .4f;
        if (hp.hpText != null) hp.hpText.enabled = false;
    }

    private static void PositionBar(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0f, 0f); rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(.5f, 0f); rect.sizeDelta = new Vector2(-16f, 27f);
        rect.anchoredPosition = new Vector2(0f, 4f);
    }
}
