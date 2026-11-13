using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The round modifier on screen. When a round's countdown starts with a modifier, the countdown opens with
/// <see cref="RoundModifiers.AnnounceSeconds"/> of announcement (a wide card under the clock: "ROUND MODIFIER", its
/// name and what it does, with a horn) before the numbers start; then, for the whole round, an orange card beside
/// the clock keeps naming it. Its own canvas; added next to the GameModeManager.
/// </summary>
public class ModifierHud : MonoBehaviour
{
    private static readonly Color Orange = new(1f, 0.54f, 0.24f);

    private Canvas canvas;
    private RectTransform announce, chip;
    private CanvasGroup announceGroup, chipGroup;
    private TMP_Text announceName, announceDesc, chipName;
    private string announcedFor; // Modifier id + phase start, so each round's announcement plays once

    private void Start()
    {
        canvas = UIKit.Canvas("Modifier Canvas", 1);
        canvas.transform.SetParent(transform, false);
        var theme = UITheme.Current;

        // The announcement: a wide card under the clock (clear of the gun picker in the middle)
        announce = UIKit.Card(canvas.transform, UIKit.WithAlpha(theme.panel, 0.94f), 28f, 0.5f, "Modifier Announcement");
        UIKit.Place(announce, new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(760f, 190f));
        var stripe = UIKit.Panel(announce, Orange, 6f, "Stripe");
        stripe.rectTransform.anchorMin = new Vector2(0f, 1f);
        stripe.rectTransform.anchorMax = new Vector2(1f, 1f);
        stripe.rectTransform.pivot = new Vector2(0.5f, 1f);
        stripe.rectTransform.sizeDelta = new Vector2(-56f, 8f);
        stripe.rectTransform.anchoredPosition = new Vector2(0f, -14f);
        var title = UIKit.OneLine(UIKit.Text(announce, "ROUND MODIFIER", 24f, UIKit.Font.Bold, Orange, TextAlignmentOptions.Center));
        UIKit.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(700f, 32f));
        announceName = UIKit.OneLine(UIKit.Text(announce, "", 68f, UIKit.Font.Display, theme.text, TextAlignmentOptions.Center));
        UIKit.Place(announceName.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(720f, 76f));
        announceDesc = UIKit.Text(announce, "", 24f, UIKit.Font.Body, theme.textMuted, TextAlignmentOptions.Center);
        UIKit.Place(announceDesc.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(700f, 40f));
        announceGroup = announce.gameObject.AddComponent<CanvasGroup>();

        // The reminder: beside the clock card (300 wide at the top middle), the whole round
        chip = UIKit.Card(canvas.transform, UIKit.WithAlpha(theme.panel, 0.88f), 20f, 0.4f, "Modifier Chip");
        UIKit.Place(chip, new Vector2(0.5f, 1f), new Vector2(286f, -24f), new Vector2(240f, 72f));
        var chipBar = UIKit.Panel(chip, Orange, 5f, "Bar");
        chipBar.rectTransform.anchorMin = new Vector2(0f, 0f);
        chipBar.rectTransform.anchorMax = new Vector2(0f, 1f);
        chipBar.rectTransform.pivot = new Vector2(0f, 0.5f);
        chipBar.rectTransform.sizeDelta = new Vector2(8f, -20f);
        chipBar.rectTransform.anchoredPosition = new Vector2(12f, 0f);
        var chipTitle = UIKit.OneLine(UIKit.Text(chip, "MODIFIER", 15f, UIKit.Font.Bold, Orange));
        UIKit.Place(chipTitle.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -10f), new Vector2(200f, 20f));
        chipName = UIKit.OneLine(UIKit.Text(chip, "", 28f, UIKit.Font.Display, theme.text));
        UIKit.Place(chipName.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(200f, 34f));
        chipGroup = chip.gameObject.AddComponent<CanvasGroup>();

        foreach (var g in canvas.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        announce.gameObject.SetActive(false);
        chip.gameObject.SetActive(false);
    }

    private void Update()
    {
        var manager = GameModeManager.Instance;
        var modifier = RoundModifiers.Current;
        var phase = manager != null ? manager.Phase : MatchPhase.Waiting;
        bool live = modifier != null && manager != null && manager.Clock != null
                    && (phase == MatchPhase.Countdown || phase == MatchPhase.Hiding || phase == MatchPhase.Active);

        // The announcement: the opening seconds of the countdown
        float shown = live && phase == MatchPhase.Countdown ? manager.Clock.Elapsed : float.MaxValue;
        bool announcing = shown < RoundModifiers.AnnounceSeconds;
        if (announcing)
        {
            string key = modifier.Id + manager.Clock.PhaseStart;
            if (announcedFor != key)
            {
                announcedFor = key;
                announceName.text = modifier.Name.ToUpperInvariant();
                announceDesc.text = modifier.Description;
                Sfx.Here(SfxKind.Horn, 0.55f, 1.15f);
            }
            float pop = Mathf.Clamp01(shown / 0.3f);
            announce.localScale = Vector3.one * Mathf.LerpUnclamped(0.7f, 1f, 1f + 2.2f * Mathf.Pow(pop - 1f, 3f) + 1.2f * Mathf.Pow(pop - 1f, 2f));
            announceGroup.alpha = Mathf.Clamp01(shown / 0.15f) * Mathf.Clamp01((RoundModifiers.AnnounceSeconds - shown) / 0.35f);
        }
        if (announce.gameObject.activeSelf != announcing) announce.gameObject.SetActive(announcing);

        // The reminder: once the announcement is done, until the round ends
        bool showChip = live && !announcing;
        if (showChip)
        {
            chipName.text = modifier.Name.ToUpperInvariant();
            if (!chip.gameObject.activeSelf) chipGroup.alpha = 0f;
            chipGroup.alpha = Mathf.MoveTowards(chipGroup.alpha, 1f, Time.unscaledDeltaTime / 0.25f);
        }
        if (chip.gameObject.activeSelf != showChip) chip.gameObject.SetActive(showChip);
    }
}
