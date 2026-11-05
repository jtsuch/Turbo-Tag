using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Smooth screen transitions: every kit screen fades in from the background colour when it opens, and
/// <see cref="LoadScene"/> fades out before switching scenes (for local scene changes; network loads just
/// fade in on arrival). A top-most overlay canvas that swallows clicks while it's covering the screen.
/// </summary>
public class UIFade : MonoBehaviour
{
    private const float Seconds = 0.3f;

    private static UIFade instance;
    private Image cover;

    private static UIFade Instance
    {
        get
        {
            if (instance != null) return instance;
            var go = new GameObject("UIFade");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<UIFade>();
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            go.AddComponent<GraphicRaycaster>();
            instance.cover = UIKit.Rect("Cover", go.transform).gameObject.AddComponent<Image>();
            UIKit.Stretch(instance.cover.rectTransform);
            instance.cover.color = UITheme.Current.backgroundTop;
            return instance;
        }
    }

    /// <summary>Starts covered and fades the new screen in.</summary>
    public static void In()
    {
        var fade = Instance;
        fade.StopAllCoroutines();
        fade.StartCoroutine(fade.Run(1f, 0f, null));
    }

    /// <summary>Fades out, then loads <paramref name="scene"/>.</summary>
    public static void LoadScene(string scene)
    {
        var fade = Instance;
        fade.StopAllCoroutines();
        fade.StartCoroutine(fade.Run(fade.cover.color.a, 1f, () => SceneManager.LoadScene(scene)));
    }

    private IEnumerator Run(float from, float to, System.Action done)
    {
        Color c = UITheme.Current.backgroundTop;
        cover.raycastTarget = true;
        for (float t = 0f; t < Seconds; t += Time.unscaledDeltaTime)
        {
            float k = t / Seconds;
            c.a = Mathf.Lerp(from, to, k * k * (3f - 2f * k));
            cover.color = c;
            yield return null;
        }
        c.a = to;
        cover.color = c;
        cover.raycastTarget = to > 0.5f;
        done?.Invoke();
    }
}
