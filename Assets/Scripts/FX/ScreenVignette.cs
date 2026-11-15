using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A coloured glow around the edges of the local screen (clear in the middle), e.g. so a cloaked
/// player knows they're invisible. Call <see cref="Set"/> every frame with the strength you want
/// (0 hides it); it creates its own overlay canvas the first time. No scene setup needed.
/// </summary>
public class ScreenVignette : MonoBehaviour
{
    private static ScreenVignette instance;
    private static Texture2D      edgeTexture;

    private RawImage image;

    /// <summary>Shows the vignette at <paramref name="strength"/> (0–1) in <paramref name="color"/>.</summary>
    public static void Set(float strength, Color color)
    {
        if (instance == null)
        {
            if (strength <= 0.001f) return;
            var go = new GameObject("ScreenVignette");
            instance = go.AddComponent<ScreenVignette>();
            instance.Build();
        }

        color.a *= Mathf.Clamp01(strength);
        instance.image.color   = color;
        instance.image.enabled = color.a > 0.001f;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900; // Under ScreenFlash (999)

        var imgGO = new GameObject("Edges");
        imgGO.transform.SetParent(transform, false);
        image = imgGO.AddComponent<RawImage>();
        image.texture       = EdgeTexture();
        image.raycastTarget = false;

        var rt = image.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // White with alpha rising from 0 in the middle to 1 at the edges (soft, slightly oval)
    private static Texture2D EdgeTexture()
    {
        if (edgeTexture != null) return edgeTexture;

        const int size = 128;
        edgeTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x / (size - 1f) * 2f - 1f;
            float v = y / (size - 1f) * 2f - 1f;
            float d = Mathf.Sqrt(u * u + v * v) / Mathf.Sqrt(2f); // 0 centre → 1 corner
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d));
            pixels[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        edgeTexture.SetPixels(pixels);
        edgeTexture.Apply();
        return edgeTexture;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
