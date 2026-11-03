using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Spawns a full-screen white Canvas overlay and fades it out over <paramref name="duration"/> seconds.
/// Call <see cref="Apply"/> from any context; no pre-existing UI setup required.
/// </summary>
public class ScreenFlash : MonoBehaviour
{
    /// <summary>
    /// Applies a flash blind effect to the local screen.
    /// </summary>
    /// <param name="intensity">Alpha of the white overlay at peak (0–1).</param>
    /// <param name="duration">Total seconds before the screen is fully clear.</param>
    public static void Apply(float intensity, float duration)
    {
        if (intensity <= 0f || duration <= 0f) return;

        GameObject go = new("ScreenFlash");
        DontDestroyOnLoad(go);
        go.AddComponent<ScreenFlash>().Init(Mathf.Clamp01(intensity), duration);
    }

    private void Init(float intensity, float duration)
    {
        // Canvas
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        gameObject.AddComponent<CanvasScaler>();
        gameObject.AddComponent<GraphicRaycaster>();

        // Full-screen white image
        GameObject imgGO = new("WhiteOverlay");
        imgGO.transform.SetParent(transform, false);
        Image img = imgGO.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, intensity);

        RectTransform rt = img.rectTransform;
        rt.anchorMin  = Vector2.zero;
        rt.anchorMax  = Vector2.one;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;

        StartCoroutine(FadeRoutine(img, intensity, duration));
    }

    private IEnumerator FadeRoutine(Image img, float peakAlpha, float duration)
    {
        // Hold at peak: a strong flash (full blind) holds for 40 % of the duration, a weak one barely at all
        float holdTime = duration * Mathf.Lerp(0.1f, 0.4f, peakAlpha);
        yield return new WaitForSeconds(holdTime);

        // Then fade, slowly at first (vision comes back gradually)
        float fadeTime = duration - holdTime;
        float elapsed  = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / fadeTime);
            float alpha = peakAlpha * (1f - t * t);
            img.color = new Color(1f, 1f, 1f, alpha);
            yield return null;
        }

        Destroy(gameObject);
    }
}
