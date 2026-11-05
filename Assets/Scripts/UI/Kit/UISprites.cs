using UnityEngine;

/// <summary>
/// Sprites drawn in code (no image files): an anti-aliased rounded rectangle (9-sliced, so any size keeps
/// its corners), a circle, a soft blurred blob and a soft shadow. Made once and shared.
/// </summary>
public static class UISprites
{
    public const int CornerRadius = 32; // Pixels in the rounded sprite; Image.pixelsPerUnitMultiplier scales it

    private static Sprite rounded, circle, blob, shadow, ring;

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
