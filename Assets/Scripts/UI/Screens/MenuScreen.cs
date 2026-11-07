using System.Collections;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Base for the code-built menu screens (Loading, Main Menu, Join List, Lobby, Pregame): makes the canvas,
/// the animated background and the fade-in, and offers toasts (short messages that slide in at the top).
/// Subclasses build their layout in <see cref="Build"/> into <see cref="Root"/>, a 1920×1080-safe area.
/// Attach a subclass to one GameObject in the scene; everything else is made at runtime.
/// </summary>
public abstract class MenuScreen : MonoBehaviourPunCallbacks
{
    protected Canvas        Canvas { get; private set; }
    /// <summary>The whole screen (grows past 1920×1080 on other aspect ratios).</summary>
    protected RectTransform Root   { get; private set; }
    protected UITheme       Theme  => UITheme.Current;

    private RectTransform toast;
    private TMP_Text      toastText;
    private Coroutine     toastRoutine;

    protected virtual void Awake()
    {
        // Cheats-tab key changes last one match: out of the level they're gone (Pregame showed keys a cheat
        // extra had taken in the last match as unbound)
        Keybinds.ResetMatchBindings();
        Canvas = UIKit.Canvas(GetType().Name + " Canvas");
        Root = (RectTransform)Canvas.transform;
        UIKit.Background(Root);
        Build();
        BuildToast();
        UIFade.In();
        RequestLayout();
    }

    // ─── Layout ───────────────────────────────────────────────────────────────

    private int layoutFrames;
    private Vector2 lastSize;

    /// <summary>Settles the whole screen's layout over the next frames (after building or changing lists).</summary>
    protected void RequestLayout() => layoutFrames = 2;

    protected virtual void LateUpdate()
    {
        Vector2 size = Root.rect.size;
        if (size != lastSize) { lastSize = size; layoutFrames = Mathf.Max(layoutFrames, 1); }
        if (layoutFrames <= 0) return;
        layoutFrames--;
        UIKit.Relayout(Root);
    }

    protected abstract void Build();

    // ─── Toasts ───────────────────────────────────────────────────────────────

    /// <summary>A short message that slides in from the top for a few seconds (errors, confirmations).</summary>
    public void Toast(string message, bool error = false)
    {
        toastText.text = message;
        UIKit.CardBg(toast).color = error ? Theme.danger : Theme.secondary;
        UISound.Play(error ? UISound.Kind.Error : UISound.Kind.Click);
        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(ShowToast());
    }

    private void BuildToast()
    {
        toast = UIKit.Card(Root, Theme.secondary, 22f, 0.4f, "Toast");
        UIKit.Place(toast, new Vector2(0.5f, 1f), new Vector2(0f, 120f), new Vector2(900f, 72f));
        toastText = UIKit.Text(toast, "", 26f, UIKit.Font.Bold, Theme.text, TextAlignmentOptions.Center);
        UIKit.Stretch(toastText.rectTransform, 24f, 24f, 0f, 0f);
        toast.gameObject.SetActive(false);
    }

    private IEnumerator ShowToast()
    {
        toast.gameObject.SetActive(true);
        toast.SetAsLastSibling();
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.25f)
        {
            toast.anchoredPosition = new Vector2(0f, Mathf.Lerp(120f, -32f, 1f - Mathf.Pow(1f - t, 3f)));
            yield return null;
        }
        toast.anchoredPosition = new Vector2(0f, -32f);
        yield return new WaitForSecondsRealtime(3.2f);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.2f)
        {
            toast.anchoredPosition = new Vector2(0f, Mathf.Lerp(-32f, 120f, t * t));
            yield return null;
        }
        toast.gameObject.SetActive(false);
    }

    // ─── Shared pieces ────────────────────────────────────────────────────────

    /// <summary>The screen header: a big title (and optional subtitle) at the top left.</summary>
    protected RectTransform Header(string title, string subtitle = null)
    {
        var header = UIKit.Rect("Header", Root);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.offsetMin = new Vector2(80f, -170f);
        header.offsetMax = new Vector2(-80f, -48f);
        UIKit.VStack(header, 0f, null, TextAnchor.UpperLeft);
        UIKit.Title(header, title, 84f);
        if (!string.IsNullOrEmpty(subtitle)) UIKit.Text(header, subtitle, 26f, UIKit.Font.Body, Theme.textMuted);
        return header;
    }

    /// <summary>A round avatar in a penguin colour with the player's initial.</summary>
    protected static Image Avatar(Transform parent, string name, Color color, float size)
    {
        var circle = UIKit.Rect("Avatar", parent).gameObject.AddComponent<Image>();
        circle.sprite = UISprites.Circle;
        circle.color = color;
        circle.preserveAspect = true; // Stays round when a layout stretches it
        UIKit.Size(circle, size, size);
        string initial = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
        bool light = color.grayscale > 0.6f;
        var t = UIKit.Text(circle.transform, initial, size * 0.48f, UIKit.Font.Display,
            light ? UITheme.Current.textDark : Color.white, TextAlignmentOptions.Center);
        UIKit.Stretch(t.rectTransform);
        return circle;
    }

    protected static string Nicify(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name ?? "", "(?<=[a-z])(?=[A-Z])", " ");
}
