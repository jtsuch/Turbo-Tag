using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD: the emote menu. The Emote key (default T) opens a small card listing the emotes (<see cref="PlayerEmotes.All"/>);
/// a number key plays one (and closes the menu); T closes it (Escape opens the pause menu, which closes it too). Movement keeps working while it's open,
/// but the number keys belong to the menu (Free Spin waits). Your own emote shows as a line under the crosshair,
/// since your head is hidden from you. Part of the HUD canvas.
/// </summary>
public class EmoteMenu : MonoBehaviour
{
    /// <summary>True while the menu is open (number keys belong to it).</summary>
    public static bool IsOpen { get; private set; }

    private RectTransform card;
    private TMP_Text      echo;
    private float         echoAt = -10f;

    public static EmoteMenu Create(RectTransform hudRoot)
    {
        var rect = UIKit.Rect("EmoteMenu", hudRoot);
        UIKit.Stretch(rect);
        var menu = rect.gameObject.AddComponent<EmoteMenu>();
        menu.Build(rect);
        return menu;
    }

    private void Build(RectTransform rect)
    {
        var theme = UITheme.Current;
        card = UIKit.Card(rect, UIKit.WithAlpha(theme.panel, 0.94f), 22f, 0.45f, "Card");
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0f, 0.5f);
        card.anchoredPosition = new Vector2(32f, -40f);
        card.sizeDelta = new Vector2(300f, 70f + PlayerEmotes.All.Length * 44f);
        var title = UIKit.Text(card, "EMOTES", 22f, UIKit.Font.Display, theme.primary, TextAlignmentOptions.TopLeft);
        UIKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -14f), new Vector2(260f, 30f));
        for (int i = 0; i < PlayerEmotes.All.Length; i++)
        {
            var row = UIKit.OneLine(UIKit.Text(card, $"<color=#FFC93C>{i + 1}</color>   {PlayerEmotes.All[i].label}", 24f, UIKit.Font.Bold, theme.text, TextAlignmentOptions.MidlineLeft));
            UIKit.Place(row.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -52f - i * 44f), new Vector2(260f, 40f));
        }
        card.gameObject.SetActive(false);

        echo = UIKit.OneLine(UIKit.Text(rect, "", 26f, UIKit.Font.Bold, theme.text, TextAlignmentOptions.Center, "Echo"));
        UIKit.Place(echo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(800f, 36f));
        echo.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        echo.rectTransform.anchoredPosition = new Vector2(0f, -60f);
        echo.gameObject.SetActive(false);
        foreach (var g in GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
    }

    private void OnDestroy() => IsOpen = false;

    private void Update()
    {
        var local = Player.Local;
        bool blocked = local == null || local.IsCaught || PauseMenuManager.IsPaused || GunPicker.IsOpen;
        if (blocked) SetOpen(false);
        else if (Input.GetKeyDown(Keybinds.Get(InputAction.Emote))) SetOpen(!IsOpen);

        if (IsOpen && local != null && local.TryGetComponent(out PlayerEmotes emotes))
            for (int i = 0; i < PlayerEmotes.All.Length && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    if (emotes.Play(i))
                    {
                        echo.text = $"<color=#B9B2E0>You:</color> {PlayerEmotes.All[i].bubble}";
                        echoAt = Time.unscaledTime;
                    }
                    SetOpen(false);
                    break;
                }

        float age = Time.unscaledTime - echoAt;
        bool show = age < 1.8f;
        if (echo.gameObject.activeSelf != show) echo.gameObject.SetActive(show);
        if (show) echo.alpha = Mathf.Clamp01((1.8f - age) / 0.4f);
    }

    private void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;
        card.gameObject.SetActive(open);
        if (open) UISound.Play(UISound.Kind.Click);
    }
}
