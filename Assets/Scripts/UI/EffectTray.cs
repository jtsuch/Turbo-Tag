using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD: the local player's running effects (<see cref="PlayerEffect.Active"/>), as cards stacked on the right side
/// of the screen: a green (boon) or red (curse) stripe, the name and what it does, the seconds left and a bar
/// that runs down. Cards pop in when an effect starts and shrink away when it ends. Part of the HUD canvas.
/// </summary>
public class EffectTray : MonoBehaviour
{
    private const float CardWidth  = 360f;
    private const float CardHeight = 78f;
    private const float Gap        = 10f;

    private sealed class Card
    {
        public PlayerEffect  Effect;
        public RectTransform Root;
        public TMP_Text      TimeText;
        public RectTransform Bar;
        public float         Born;
        public float         Y;
    }

    private RectTransform stack;
    private readonly List<Card> cards = new();
    private readonly List<Card> leaving = new();

    private static UITheme Theme => UITheme.Current;

    public static EffectTray Create(RectTransform hudRoot)
    {
        var rect = UIKit.Rect("EffectTray", hudRoot);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-32f, 40f); // Top of the stack (under the kill feed); it grows down toward the ammo card
        rect.sizeDelta = new Vector2(CardWidth, 10f);
        var tray = rect.gameObject.AddComponent<EffectTray>();
        tray.stack = rect;
        return tray;
    }

    private void Update()
    {
        // New effects get a card
        foreach (var effect in PlayerEffect.Active)
            if (effect != null && cards.Find(c => c.Effect == effect) == null) cards.Add(Build(effect));

        // Ended ones leave
        for (int i = cards.Count - 1; i >= 0; i--)
        {
            var card = cards[i];
            if (card.Effect != null && PlayerEffect.Active.Contains(card.Effect)) continue;
            cards.RemoveAt(i);
            card.Born = Time.unscaledTime;
            leaving.Add(card);
        }

        // Lots at once: the whole stack shrinks so it still fits on screen (about five cards' height)
        float fit = Mathf.Min(1f, 5f / Mathf.Max(1, cards.Count));
        float s = Mathf.Lerp(stack.localScale.x, fit, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        stack.localScale = new Vector3(s, s, 1f);

        // Lay out top to bottom, easing into place
        float y = 0f;
        foreach (var card in cards)
        {
            card.Y = Mathf.Lerp(card.Y, y, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            card.Root.anchoredPosition = new Vector2(0f, -card.Y);
            float age = Time.unscaledTime - card.Born;
            float pop = age < 0.25f ? 1f + 0.12f * Mathf.Sin(age / 0.25f * Mathf.PI) : 1f;
            float slide = Mathf.Clamp01(age / 0.18f);
            card.Root.localScale = Vector3.one * pop;
            card.Root.anchoredPosition += new Vector2((1f - slide) * 120f, 0f);

            float left = card.Effect.Remaining;
            card.TimeText.text = left >= 10f ? $"{left:0}s" : $"{left:0.0}s";
            float t = card.Effect.Duration > 0f ? left / card.Effect.Duration : 0f;
            card.Bar.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
            card.TimeText.color = left <= 3f ? Color.Lerp(Theme.text, Theme.danger, Mathf.PingPong(Time.unscaledTime * 3f, 1f)) : Theme.text;
            y += CardHeight + Gap;
        }

        for (int i = leaving.Count - 1; i >= 0; i--)
        {
            var card = leaving[i];
            float k = Mathf.Clamp01((Time.unscaledTime - card.Born) / 0.2f);
            card.Root.localScale = Vector3.one * (1f - k);
            if (k >= 1f) { Destroy(card.Root.gameObject); leaving.RemoveAt(i); }
        }
    }

    private Card Build(PlayerEffect effect)
    {
        Color accent = effect.IsChaos ? Theme.primary : effect.IsGood ? Theme.lime : Theme.danger;
        var root = UIKit.Card(stack, UIKit.WithAlpha(Theme.panel, 0.92f), 18f, 0.4f, effect.DisplayName);
        root.anchorMin = root.anchorMax = new Vector2(1f, 1f);
        root.pivot = new Vector2(1f, 1f);
        root.sizeDelta = new Vector2(CardWidth, CardHeight);

        var stripe = UIKit.Panel(root, accent, 6f, "Stripe");
        stripe.rectTransform.anchorMin = new Vector2(0f, 0f);
        stripe.rectTransform.anchorMax = new Vector2(0f, 1f);
        stripe.rectTransform.pivot = new Vector2(0f, 0.5f);
        stripe.rectTransform.sizeDelta = new Vector2(10f, -16f);
        stripe.rectTransform.anchoredPosition = new Vector2(10f, 0f);

        var name = UIKit.OneLine(UIKit.Text(root, effect.DisplayName, 26f, UIKit.Font.Display, accent, TextAlignmentOptions.TopLeft));
        UIKit.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(32f, -10f), new Vector2(240f, 32f));
        var what = UIKit.OneLine(UIKit.Text(root, effect.Description, 17f, UIKit.Font.Body, Theme.textMuted, TextAlignmentOptions.TopLeft));
        UIKit.Place(what.rectTransform, new Vector2(0f, 1f), new Vector2(32f, -42f), new Vector2(240f, 22f));

        var time = UIKit.Text(root, "", 30f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.MidlineRight);
        UIKit.Place(time.rectTransform, new Vector2(1f, 0.5f), new Vector2(-18f, 4f), new Vector2(90f, 40f));
        time.rectTransform.pivot = new Vector2(1f, 0.5f);
        time.rectTransform.anchoredPosition = new Vector2(-18f, 4f);

        var back = UIKit.Panel(root, Theme.field, 3f, "Bar");
        back.rectTransform.anchorMin = new Vector2(0f, 0f);
        back.rectTransform.anchorMax = new Vector2(1f, 0f);
        back.rectTransform.pivot = new Vector2(0.5f, 0f);
        back.rectTransform.sizeDelta = new Vector2(-52f, 5f);
        back.rectTransform.anchoredPosition = new Vector2(10f, 8f);
        var fill = UIKit.Panel(back.transform, accent, 3f, "Fill").rectTransform;
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.sizeDelta = Vector2.zero;

        foreach (var g in root.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
        float startY = cards.Count * (CardHeight + Gap);
        return new Card { Effect = effect, Root = root, TimeText = time, Bar = fill, Born = Time.unscaledTime, Y = startY };
    }
}
