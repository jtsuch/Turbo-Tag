using UnityEngine;

/// <summary>
/// Sprites drawn in code (no image files): an anti-aliased rounded rectangle (9-sliced, so any size keeps
/// its corners), a circle, a soft blurred blob and a soft shadow. Made once and shared.
/// </summary>
public static class UISprites
{
    public const int CornerRadius = 32; // Pixels in the rounded sprite; Image.pixelsPerUnitMultiplier scales it

    private static Sprite rounded, circle, blob, shadow, ring, play;

    /// <summary>A "play" triangle pointing right (the UI fonts have no ▶ glyph: it showed as a box).</summary>
    public static Sprite Play => play != null ? play : play = MakePlay(64);

    /// <summary>A play icon centred in a button (its label left empty).</summary>
    public static UnityEngine.UI.Image AddPlayIcon(Transform button, float size, Color color)
    {
        var go = new GameObject("Play Icon", typeof(RectTransform));
        go.transform.SetParent(button, false);
        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.sprite = Play;
        img.color = color;
        img.raycastTarget = false;
        var rect = img.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(size * 0.08f, 0f); // A triangle looks centred a touch right of centre
        rect.sizeDelta = new Vector2(size, size);
        return img;
    }

    // Anti-aliased triangle: points (0.2,0.12), (0.2,0.88), (0.88,0.5) of the square
    private static Sprite MakePlay(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "UIPlay",
        };
        var a = new Vector2(0.2f, 0.12f) * size; var b = new Vector2(0.2f, 0.88f) * size; var c = new Vector2(0.88f, 0.5f) * size;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            // Signed distance inside each edge (positive inside); the smallest decides coverage
            float d = Mathf.Min(EdgeDistance(p, a, b), Mathf.Min(EdgeDistance(p, b, c), EdgeDistance(p, c, a)));
            byte alpha = (byte)(Mathf.Clamp01(d + 0.5f) * 255f);
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // Distance of p to the line a→b, positive on the right-hand side (inside for a clockwise triangle)
    private static float EdgeDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 e = (b - a).normalized;
        Vector2 n = new(e.y, -e.x);
        return Vector2.Dot(p - a, n);
    }

    /// <summary>Rounded rectangle, 9-sliced. Radius on screen = 32 / pixelsPerUnitMultiplier.</summary>
    public static Sprite Rounded => rounded != null ? rounded : rounded = MakeRounded(128, CornerRadius, 0f, false);

    public static Sprite Circle => circle != null ? circle : circle = MakeRounded(128, 64, 0f, false, sliced: false);

    /// <summary>A circle outline (selection rings).</summary>
    public static Sprite Ring => ring != null ? ring : ring = MakeRounded(128, 64, 0f, true, sliced: false);

    /// <summary>Soft round glow, for background blobs.</summary>
    public static Sprite Blob => blob != null ? blob : blob = MakeBlob(128);

    /// <summary>Rounded rectangle with a soft edge, 9-sliced: drop shadows.</summary>
    public static Sprite Shadow => shadow != null ? shadow : shadow = MakeRounded(128, 48, 20f, false);

    private static Sprite MakeRounded(int size, int radius, float softness, bool ringOnly, bool sliced = true)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "UIRounded",
        };
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        float inner = half - radius;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Distance outside a rectangle shrunk by the radius, minus the radius = signed distance to the edge
            float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - inner, 0f);
            float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - inner, 0f);
            float d = Mathf.Sqrt(dx * dx + dy * dy) - radius + softness;
            float a = softness > 0f ? Mathf.Clamp01(-d / (softness * 2f)) : Mathf.Clamp01(0.5f - d);
            if (ringOnly) a *= Mathf.Clamp01(d + 7.5f); // A 7 px band at the edge
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        int border = sliced ? radius + Mathf.CeilToInt(softness) : 0;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }

    private static Sprite MakeBlob(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "UIBlob",
        };
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
            float a = Mathf.Clamp01(1f - d);
            a = a * a * (3f - 2f * a);
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    /// <summary>A vertical two-colour gradient texture (for backgrounds).</summary>
    public static Texture2D Gradient(Color top, Color bottom)
    {
        var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UIGradient" };
        for (int y = 0; y < 64; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
        tex.Apply(false, true);
        return tex;
    }
}
