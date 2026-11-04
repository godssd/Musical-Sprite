using UnityEngine;
using UnityEngine.UI;

/// <summary>Small fixed brush accents around the HP ribbon; no HP or score state is calculated here.</summary>
public sealed class HandpaintedHPDecoration : MonoBehaviour
{
    public HPDecorationStyle style;

    private RectTransform root;
    private Image tail, outerLeaf, strokes, dot, innerLeaf;
    private int side;
    private float lastWidth = -1f;
    private bool needsRefresh;
    private int lastRevision = -1;

    public void Configure(int teamSide, Image ribbonTail)
    {
        side = teamSide; tail = ribbonTail;
        root = GetComponent<RectTransform>();
        if (style == null) style = Resources.Load<HPDecorationStyle>("BattleVisualV11/HPDecorationStyle");
        if (outerLeaf == null) outerLeaf = HandpaintedHUD.NewImage(transform, "HPOuterLeaves_V11", HandpaintedHUD.Leaves, Color.white);
        if (strokes == null) strokes = HandpaintedHUD.NewImage(transform, "HPBrushMarks_V11", HandpaintedHUD.HPStrokes, Color.white);
        if (dot == null) dot = HandpaintedHUD.NewImage(transform, "HPPaintDot_V11", HandpaintedHUD.HPDot, Color.white);
        if (innerLeaf == null) innerLeaf = HandpaintedHUD.NewImage(transform, "HPInnerLeaves_V11", HandpaintedHUD.Leaves, Color.white);
        Refresh();
    }

    private void OnValidate() { needsRefresh = true; }
    private void LateUpdate()
    {
        if (root != null && style != null && (needsRefresh || style.Revision != lastRevision || Mathf.Abs(root.rect.width - lastWidth) > .01f)) Refresh();
    }

    public void Refresh()
    {
        if (root == null || tail == null || style == null) return;
        needsRefresh = false; lastWidth = root.rect.width;
        lastRevision = style.Revision;
        float direction = side == 0 ? 1f : -1f;
        Color team = HandpaintedHUD.TeamColor(side);
        float extension = style.showDetails ? Mathf.Max(0f, style.tailExtension) : 0f;
        float width = Mathf.Max(1f, root.rect.width + 20f + extension);
        var rect = tail.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(side == 0 ? 0f : 1f, 0f);
        rect.pivot = new Vector2(.5f, 0f);
        rect.sizeDelta = new Vector2(width, Mathf.Max(1f, style.tailHeight));
        rect.anchoredPosition = new Vector2(direction * (width * .5f - 10f), style.tailYOffset);
        rect.localScale = new Vector3(direction, 1f, 1f);
        tail.color = new Color(team.r, team.g, team.b, style.tailOpacity);

        Place(outerLeaf, true, style.outerLeafOffset, style.outerLeafSize, false);
        Place(strokes, false, style.strokesOffset, style.strokesSize, true);
        Place(dot, false, style.dotOffset, Vector2.one * style.dotSize, true);
        Place(innerLeaf, false, new Vector2(extension + style.innerLeafOffset.x, style.innerLeafOffset.y), style.innerLeafSize, false);
        strokes.color = dot.color = team;
    }

    private void Place(Image image, bool outer, Vector2 offset, Vector2 size, bool tint)
    {
        bool visible = style.showDetails && image.sprite != null;
        image.enabled = visible;
        if (!visible) return;
        float direction = side == 0 ? 1f : -1f;
        var rect = image.rectTransform;
        float anchor = outer ? (side == 0 ? 0f : 1f) : (side == 0 ? 1f : 0f);
        rect.anchorMin = rect.anchorMax = new Vector2(anchor, 0f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));
        rect.anchoredPosition = new Vector2(offset.x * direction, offset.y);
        rect.localScale = new Vector3(direction, 1f, 1f);
        image.preserveAspect = true;
        image.color = tint ? HandpaintedHUD.TeamColor(side) : Color.white;
    }
}
