using UnityEngine;

public static class RuntimeSpriteUtility
{
    private static Sprite _beam;
    public static Sprite Beam
    {
        get
        {
            if (_beam == null)
            {
                int w = 32, h = 128;
                Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                Color[] px = new Color[w * h];
                for (int y = 0; y < h; y++)
                {
                    float t = y / (float)(h - 1);
                    float alpha = Mathf.Pow(1f - t, 1.5f);
                    Color c = new Color(1f, 1f, 1f, alpha);
                    for (int x = 0; x < w; x++) px[x + y * w] = c;
                }
                tex.SetPixels(px);
                tex.Apply(false, true);
                _beam = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 64f);
            }
            return _beam;
        }
    }

    private static Sprite _sparkle;
    public static Sprite Sparkle
    {
        get
        {
            if (_sparkle == null)
            {
                int s = 32;
                Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                Color[] px = new Color[s * s];
                Vector2 center = new Vector2(s / 2f, s / 2f);
                for (int y = 0; y < s; y++)
                {
                    for (int x = 0; x < s; x++)
                    {
                        float d = Vector2.Distance(center, new Vector2(x + 0.5f, y + 0.5f)) / (s / 2f);
                        float a = Mathf.Clamp01(1f - d);
                        a = Mathf.Pow(a, 2f);
                        px[x + y * s] = new Color(1f, 1f, 1f, a);
                    }
                }
                tex.SetPixels(px);
                tex.Apply(false, true);
                _sparkle = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 64f);
            }
            return _sparkle;
        }
    }

    private static Material _additiveGlow;
    /// <summary>共享加法混合 Sprite 材质（MusicalSprite/AdditiveSprite）：音符光晕 / 命中光束 / 光碎 / 能量槽 glow / Slide 柔光带 统一复用。
    /// 找不到自定义 shader 时返回 null（调用方应判空，避免 NRE；为 null 时 SpriteRenderer 回退默认材质，不发光但仍可见）。</summary>
    public static Material AdditiveGlow
    {
        get
        {
            if (_additiveGlow == null)
            {
                var sh = Shader.Find("MusicalSprite/AdditiveSprite");
                if (sh != null) _additiveGlow = new Material(sh);
            }
            return _additiveGlow;
        }
    }

    /// <summary>
    /// 生成纯色圆角长条 Sprite（能量槽占位用）。运行时生成，不需要美术资源。
    /// </summary>
    public static Sprite CreateRoundedBarSprite(int width, int height, Color fill, Color border, int borderThickness = 2, int radius = 8)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[width * height];
        Vector2 center = new Vector2(width / 2f, height / 2f);
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - halfW);
                float dy = Mathf.Abs(y + 0.5f - halfH);
                float maxRx = halfW - radius;
                float maxRy = halfH - radius;
                float dist = 0f;
                if (dx > maxRx && dy > maxRy)
                    dist = Mathf.Sqrt((dx - maxRx) * (dx - maxRx) + (dy - maxRy) * (dy - maxRy));
                else
                    dist = Mathf.Max(dx - maxRx, dy - maxRy);

                Color c;
                if (dist > radius)
                    c = Color.clear;
                else if (dist > radius - borderThickness)
                    c = border;
                else
                    c = fill;
                px[x + y * width] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), height);
    }
}
