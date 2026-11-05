using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The first screen: the logo, a spinner and what's happening ("Connecting to servers…"), with rotating tips.
/// The status comes from <see cref="ConnectToServer.Status"/>. Attach to one GameObject in the Loading scene.
/// </summary>
public class LoadingScreen : MenuScreen
{
    private static readonly string[] Tips =
    {
        "Tip: let go of jump early for a short hop on narrow ledges.",
        "Tip: hold 4 and move the mouse to Free Spin. Backflips look great on the killcam.",
        "Tip: crouch, then jump, for a much higher leap.",
        "Tip: hold Tab in a match to see the scores.",
        "Tip: hunters glow red. Hiders, keep moving!",
        "Tip: falling off the map in Free for All costs you a point.",
        "Tip: combine abilities. A dash off a bounce pad goes a long way.",
    };

    private RectTransform logo;
    private RectTransform spinner;
    private TMP_Text status;
    private TMP_Text tip;
    private int tipIndex;
    private float nextTip;

    protected override void Build()
    {
        // Logo, centred and gently floating
        var logoImage = UIKit.Rect("Logo", Root).gameObject.AddComponent<RawImage>();
        logoImage.texture = Theme.logo;
        logoImage.raycastTarget = false;
        float aspect = Theme.logo != null ? Theme.logo.width / (float)Theme.logo.height : 1.45f;
        logo = UIKit.Place(logoImage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(820f * aspect, 820f));
        logo.pivot = new Vector2(0.5f, 0.5f);

        // Spinner: a ring of dots in party colours
        spinner = UIKit.Place(UIKit.Rect("Spinner", Root), new Vector2(0.5f, 0f), new Vector2(0f, 230f), new Vector2(72f, 72f));
        spinner.pivot = new Vector2(0.5f, 0.5f);
        var party = Theme.Party;
        for (int i = 0; i < 8; i++)
        {
            var dot = UIKit.Rect("Dot", spinner).gameObject.AddComponent<Image>();
            dot.sprite = UISprites.Circle;
            dot.color = UIKit.WithAlpha(party[i % party.Length], 0.25f + 0.75f * i / 7f);
            float a = i / 8f * Mathf.PI * 2f;
            float s = 10f + i * 1.2f;
            dot.rectTransform.sizeDelta = new Vector2(s, s);
            dot.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 30f;
        }

        status = UIKit.Text(Root, "Starting up…", 34f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Center, "Status");
        UIKit.Place(status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 130f), new Vector2(1200f, 50f));

        tip = UIKit.Text(Root, "", 24f, UIKit.Font.Body, Theme.textMuted, TextAlignmentOptions.Center, "Tip");
        UIKit.Place(tip.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(1400f, 40f));
        tipIndex = Random.Range(0, Tips.Length);
        tip.text = Tips[tipIndex];
        nextTip = Time.unscaledTime + 4.5f;
    }

    private void Update()
    {
        float t = Time.unscaledTime;
        logo.anchoredPosition = new Vector2(0f, 90f + Mathf.Sin(t * 1.6f) * 10f);
        logo.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.1f) * 1.5f);
        spinner.localRotation = Quaternion.Euler(0f, 0f, -t * 220f);

        string s = ConnectToServer.Status;
        if (!string.IsNullOrEmpty(s) && status.text != s) status.text = s;

        if (t >= nextTip)
        {
            tipIndex = (tipIndex + 1) % Tips.Length;
            tip.text = Tips[tipIndex];
            nextTip = t + 4.5f;
        }
    }
}
