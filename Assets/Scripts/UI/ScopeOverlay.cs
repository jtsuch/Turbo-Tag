using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The view through a scope (the Sniper, mouse wheel up): black all round a round lens filling the screen's
/// height, thin crosshair lines with a red dot in the middle. Fades in with <see cref="PlayerCam.ZoomBlend"/>;
/// the normal crosshair hides meanwhile. Its own canvas; added next to the GameModeManager.
/// </summary>
public class ScopeOverlay : MonoBehaviour
{
    private Canvas canvas;
    private CanvasGroup group;

    /// <summary>The local player is looking through a scope (the HUD crosshair hides).</summary>
    public static bool Showing { get; private set; }

    private void Start()
    {
        canvas = UIKit.Canvas("Scope Canvas", 0);
        canvas.transform.SetParent(transform, false);
        var root = UIKit.Stretch(UIKit.Rect("Scope", canvas.transform));
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;

        // The lens: a square as tall as the screen with a soft-edged round hole; black bars either side of it
        var lens = UIKit.Rect("Lens", root).gameObject.AddComponent<Image>();
        lens.sprite = LensSprite();
        lens.color = Color.black;
        lens.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        lens.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        var fit = lens.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
        fit.aspectRatio = 1f;
        foreach (float side in new[] { -1f, 1f })
        {
            var bar = UIKit.Rect("Bar", root).gameObject.AddComponent<Image>();
            bar.color = Color.black;
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(side < 0f ? 0f : 0.5f, 0f);
            rt.anchorMax = new Vector2(side < 0f ? 0.5f : 1f, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            // Each bar stops where the lens starts (half the screen height either side of the middle)
            var bound = bar.gameObject.AddComponent<LayoutElement>();
            bound.ignoreLayout = true;
            bar.gameObject.AddComponent<ScopeBar>().side = side;
        }

        // Crosshair: thin lines across the lens and a red dot
        Line(root, new Vector2(2f, 0f), new Vector2(0f, 1f)); // Vertical
        Line(root, new Vector2(0f, 2f), new Vector2(1f, 0f)); // Horizontal
        var dot = UIKit.Rect("Dot", root).gameObject.AddComponent<Image>();
        dot.sprite = UISprites.Circle;
        dot.color = new Color(1f, 0.15f, 0.15f);
        UIKit.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7f, 7f));

        foreach (var g in canvas.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        canvas.gameObject.SetActive(false);
    }

    private static void Line(RectTransform root, Vector2 size, Vector2 stretch)
    {
        var line = UIKit.Rect("Line", root).gameObject.AddComponent<Image>();
        line.color = new Color(0f, 0f, 0f, 0.85f);
        var rt = line.rectTransform;
        rt.anchorMin = new Vector2(stretch.x > 0f ? 0f : 0.5f, stretch.y > 0f ? 0f : 0.5f);
        rt.anchorMax = new Vector2(stretch.x > 0f ? 1f : 0.5f, stretch.y > 0f ? 1f : 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
    }

    private void Update()
    {
        var cam = PlayerCam.Local;
        float blend = cam != null && Player.Local != null && Player.Local.Gun != null && Player.Local.Gun.Scoped ? cam.ZoomBlend : 0f;
        Showing = blend > 0.5f;
        bool on = blend > 0.01f;
        if (canvas.gameObject.activeSelf != on) canvas.gameObject.SetActive(on);
        if (on) group.alpha = blend;
    }

    private void OnDestroy() => Showing = false;

    // A white square, transparent inside a circle with a soft rim (tinted black by the Image)
    private static Sprite lensSprite;
    private static Sprite LensSprite()
    {
        if (lensSprite != null) return lensSprite;
        const int n = 512;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        float r = n * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x + 0.5f - r, y + 0.5f - r).magnitude / r;
                byte a = (byte)(Mathf.Clamp01((d - 0.93f) / 0.05f) * 255f);
                px[y * n + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        lensSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        return lensSprite;
    }

    // Keeps a side bar from the screen's edge to where the lens begins
    private class ScopeBar : MonoBehaviour
    {
        public float side;
        private RectTransform rt;

        private void LateUpdate()
        {
            if (rt == null) rt = (RectTransform)transform;
            var parent = (RectTransform)rt.parent;
            float half = parent.rect.height * 0.5f; // The lens is as wide as the screen is tall
            if (side < 0f) { rt.offsetMin = Vector2.zero; rt.offsetMax = new Vector2(-half + 1f, 0f); }
            else { rt.offsetMin = new Vector2(half - 1f, 0f); rt.offsetMax = Vector2.zero; }
        }
    }
}
