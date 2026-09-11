using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 用图片拼出「HP + 数字」，替代 Text 组件，保证手绘风格 100% 还原。
///
/// 素材约定（放在 Assets/Art/UI/Numbers/）：
///   HP.png   → "HP" 字样
///   0.png ~ 9.png → 十个数字
///
/// 用法：
///   1. 把 11 张图放进 Assets/Art/UI/Numbers/
///   2. 运行 Tools → Musical-Sprite → Setup Liquid HP Bars，脚本会自动识别并挂上本组件
///   3. 或手动挂本组件，把 hpLabel / digits 拖进去
///
/// 没有素材时组件什么都不做，血条会继续用原来的 Text 显示。
/// </summary>
public class HPNumberSprite : MonoBehaviour
{
    [Header("素材")]
    [Tooltip("HP 字样图片")]
    public Sprite hpLabel;
    [Tooltip("0~9 十个数字，index 必须对应数字本身（digits[0] = 数字0）")]
    public Sprite[] digits = new Sprite[10];

    [Header("排版")]
    [Tooltip("数字显示高度（像素）")]
    public float digitHeight = 40f;
    [Tooltip("字符之间的间距，负数=更紧凑")]
    public float spacing = -2f;
    [Tooltip("HP 字样与数字之间的间距")]
    public float labelGap = 12f;
    [Tooltip("居中对齐：true=放在血条中间，false=靠左用 leftPadding")]
    public bool center = true;
    [Tooltip("靠左模式下的左边距（居中对齐时忽略）")]
    public float leftPadding = 22f;

    private readonly List<Image> _digitImages = new List<Image>();
    private Image _labelImage;
    private int _currentHP = -1;
    private bool _built;

    void Awake()
    {
        Build();
    }

    void OnEnable()
    {
        if (!_built) Build();
        Refresh();
    }

    /// <summary>外部调用：设置当前血量。</summary>
    public void SetHP(int hp)
    {
        if (hp == _currentHP) return;
        _currentHP = hp;
        Refresh();
    }

    private void Build()
    {
        if (_built) return;

        if (digits == null || digits.Length != 10)
        {
            Debug.LogWarning("[HPNumberSprite] 需要 10 张数字图（0~9），当前未配齐，跳过图片数字。");
            return;
        }

        for (int i = 0; i < 10; i++)
        {
            if (digits[i] == null)
            {
                Debug.LogWarning($"[HPNumberSprite] 缺少数字 {i} 的图片，跳过图片数字。");
                return;
            }
        }

        // HP 字样
        if (hpLabel != null)
        {
            _labelImage = CreateImage("HPLabel", hpLabel);
        }

        // 预建若干个数字位（最多 6 位足够）
        for (int i = 0; i < 6; i++)
        {
            var img = CreateImage("Digit" + i, digits[0]);
            _digitImages.Add(img);
        }

        _built = true;
        Refresh();
    }

    private Image CreateImage(string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);

        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        img.preserveAspect = true;
        img.SetNativeSize();

        return img;
    }

    private void Refresh()
    {
        if (!_built || digits == null || digits.Length != 10) return;

        string s = Mathf.Max(0, _currentHP).ToString();

        // ---------- 第一遍：计算每个字符的显示宽度 ----------
        float labelWidth = 0f;
        if (_labelImage != null)
        {
            ScaleToHeight(_labelImage, digitHeight);
            labelWidth = _labelImage.rectTransform.rect.width * _labelImage.rectTransform.localScale.x;
        }

        var digitInfos = new List<(Image img, int d, float width)>();
        for (int i = 0; i < _digitImages.Count; i++)
        {
            var img = _digitImages[i];
            bool show = i < s.Length;
            img.gameObject.SetActive(show);
            if (!show) continue;

            int d = s[i] - '0';
            if (d < 0 || d > 9) continue;

            img.sprite = digits[d];
            img.SetNativeSize();
            var rt = img.rectTransform;
            float h = rt.rect.height;
            float scale = h > 0f ? digitHeight / h : 1f;
            rt.localScale = Vector3.one * scale;

            digitInfos.Add((img, d, rt.rect.width * scale));
        }

        // ---------- 第二遍：计算整体宽度并定位 ----------
        float digitTotalWidth = 0f;
        for (int i = 0; i < digitInfos.Count; i++)
        {
            digitTotalWidth += digitInfos[i].width;
            if (i < digitInfos.Count - 1) digitTotalWidth += spacing;
        }

        float labelStartX = leftPadding;
        float digitStartX = labelStartX + labelWidth + (labelWidth > 0f ? labelGap : 0f);

        if (center)
        {
            RectTransform hostRt = transform as RectTransform;
            float hostWidth = hostRt != null ? hostRt.rect.width : 0f;
            if (hostWidth > 0f)
            {
                // 血量数值居中，HP 字样放在数值左边
                digitStartX = (hostWidth - digitTotalWidth) * 0.5f;
                labelStartX = digitStartX - labelWidth - (labelWidth > 0f ? labelGap : 0f);
            }
        }

        // HP 字样
        if (_labelImage != null)
        {
            var rt = _labelImage.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(labelStartX, 0f);
        }

        // 数字位
        float x = digitStartX;
        for (int i = 0; i < digitInfos.Count; i++)
        {
            var (img, d, width) = digitInfos[i];
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            x += width + spacing;
        }
    }

    private void ScaleToHeight(Image img, float targetHeight)
    {
        img.SetNativeSize();
        var rt = img.rectTransform;
        float h = rt.rect.height;
        rt.localScale = h > 0f ? Vector3.one * (targetHeight / h) : Vector3.one;
    }
}
