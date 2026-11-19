using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Scoreboard's XP card (bottom left): what the match just earned, line by line, the level and a bar that fills
/// up to the new total, with a LEVEL UP flash when it crossed a level (<see cref="ProfileStats.RecentMatch"/>). Nothing
/// shows after a practice match or when nothing was recorded (left before the end).
/// </summary>
public class MatchXpCard : MonoBehaviour
{
    private static UITheme Theme => UIKit.Theme;

    private RectTransform fill;
    private TextMeshProUGUI levelText;
    private float fromShare, toShare, shown;
    private int levelBefore, levelAfter;
    private float startedAt;

    public static void Create(Transform root)
    {
        var match = ProfileStats.RecentMatch();
        if (match == null) return;

        var card = UIKit.Card(root, Theme.panel, 24f, 0.45f, "Match XP");
        UIKit.Place(card, new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(420f, 120f + match.lines.Count * 30f));
        card.pivot = Vector2.zero;
        var xp = card.gameObject.AddComponent<MatchXpCard>();
        UIKit.VStack(card, 6f, UIKit.Pad(24, 18), TextAnchor.UpperLeft);

        UIKit.OneLine(UIKit.Text(card, $"+{match.xp} XP", 36f, UIKit.Font.Display, Theme.primary));
        foreach (var line in match.lines)
            UIKit.OneLine(UIKit.Text(card, $"{line.label}  <color=#{ColorUtility.ToHtmlStringRGB(Theme.text)}>+{line.amount}</color>", 19f, UIKit.Font.Bold, Theme.textMuted));

        var row = UIKit.Rect("Level", card);
        UIKit.Size(row, height: 30f);
        UIKit.HStack(row, 10f, null, TextAnchor.MiddleLeft, expandHeight: false);
        xp.levelText = UIKit.Text(row, "", 20f, UIKit.Font.Display, Theme.text);
        UIKit.OneLine(xp.levelText);
        UIKit.Size(xp.levelText, 140f, 30f);
        var barBack = UIKit.Panel(row, Theme.card, 8f, "Bar");
        UIKit.Size(barBack, flexWidth: 1f, height: 16f);
        var fillImage = UIKit.Panel(barBack.transform, Theme.primary, 8f, "Fill");
        xp.fill = fillImage.rectTransform;
        xp.fill.anchorMin = Vector2.zero;
        xp.fill.offsetMin = xp.fill.offsetMax = Vector2.zero;

        // The bar fills from where it was before the match to where it is now (wrapping through any level ups)
        int total = ProfileStats.Current.xp;
        ProfileStats.LevelFor(total - match.xp, out int beforeInto, out int beforeNeed);
        ProfileStats.LevelFor(total, out int afterInto, out int afterNeed);
        xp.levelBefore = match.levelBefore;
        xp.levelAfter  = match.levelAfter;
        xp.fromShare   = beforeNeed > 0 ? (float)beforeInto / beforeNeed : 0f;
        xp.toShare     = afterNeed > 0 ? (float)afterInto / afterNeed : 0f;
        xp.startedAt   = Time.unscaledTime + 0.8f;
        xp.Apply(0f);
    }

    private void Update()
    {
        float t = Mathf.Clamp01((Time.unscaledTime - startedAt) / 1.6f);
        if (t <= 0f || shown >= 1f) return;
        Apply(Mathf.SmoothStep(0f, 1f, t));
        if (t >= 1f && levelAfter > levelBefore) { Sfx.Here(SfxKind.Fanfare, 0.5f); UISound.Play(UISound.Kind.Confirm); }
    }

    // 0 → 1 over the fill: with a level up, it runs to the end of the old bar, then the new one fills from empty
    private void Apply(float t)
    {
        shown = t;
        bool levelled = levelAfter > levelBefore;
        float share;
        int level;
        if (!levelled) { share = Mathf.Lerp(fromShare, toShare, t); level = levelAfter; }
        else if (t < 0.5f) { share = Mathf.Lerp(fromShare, 1f, t * 2f); level = levelBefore; }
        else { share = Mathf.Lerp(0f, toShare, (t - 0.5f) * 2f); level = levelAfter; }
        fill.anchorMax = new Vector2(Mathf.Clamp01(share), 1f);
        levelText.text = levelled && t >= 0.5f ? $"<color=#{ColorUtility.ToHtmlStringRGB(Theme.lime)}>LEVEL {level}!</color>" : $"Level {level}";
    }
}
