using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD: a small list of the less obvious controls on the right of the screen (key chip + what it does): Free Spin,
/// Emote, Taunt and Ping always, plus situational ones while they apply (the Search &amp; Destroy bomb, dropping it,
/// throwing the Keep Away disc). Keys follow the player's bindings. Settings → Gameplay → Control Hints turns it off.
/// Its own canvas, added next to the GameModeManager.
/// </summary>
public class ControlsHint : MonoBehaviour
{
    private const string PrefsKey = "Settings_ControlHints";

    /// <summary>Whether the list shows (Settings → Gameplay; on by default).</summary>
    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(PrefsKey, 1) == 1;
        set { PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    private RectTransform list;
    private Canvas canvas;
    private string shownKey = "";
    private readonly List<(string key, string label, bool situational)> rows = new();

    private void Start()
    {
        canvas = UIKit.Canvas("Controls Hint Canvas", 0);
        canvas.transform.SetParent(transform, false);
        list = UIKit.Rect("Controls", canvas.transform);
        list.anchorMin = list.anchorMax = list.pivot = new Vector2(1f, 0.5f);
        list.anchoredPosition = new Vector2(-28f, 70f); // Above the effect cards (which grow down from the middle)
        list.sizeDelta = new Vector2(260f, 10f);
        var stack = UIKit.VStack(list, 4f, null, TextAnchor.LowerRight);
        stack.childControlHeight = true;
        stack.childForceExpandHeight = false;
        var fit = list.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        list.pivot = new Vector2(1f, 0f); // Grows upward from just above the middle
    }

    private void Update()
    {
        var local = Player.Local;
        bool show = Enabled && local != null && !local.IsCaught && !PauseMenuManager.IsPaused;
        if (canvas.gameObject.activeSelf != show) canvas.gameObject.SetActive(show);
        if (!show) return;

        rows.Clear();
        var gun = local.Gun != null ? local.Gun.Current : null;
        if (gun != null && gun.ScopeZoom > 1f && CombatManager.Instance != null)
            rows.Add(("WHEEL", local.Gun.Scoped ? "Scroll down: leave scope" : "Scroll up: scope in", true));
        if (SearchDestroyController.LocalBombOut) rows.Add((Key(InputAction.DropBomb), "Drop bomb", true));
        if (SearchDestroyController.LocalCarrying) rows.Add((Key(InputAction.Bomb), SearchDestroyController.LocalBombOut ? "Put bomb away · hold: plant" : "Bomb · hold: plant", true));
        if (KeepAwayDisc.LocalHolding) rows.Add((Key(InputAction.Action), "Throw disc · hold: further", true));
        rows.Add((Key(InputAction.FreeSpin), "Free Spin (hold)", false));
        rows.Add((Key(InputAction.Emote), "Emote (hold)", false));
        rows.Add((Key(InputAction.Taunt), "Taunt · hold: throw it", false));
        rows.Add((Key(InputAction.Ping), "Ping (hold)", false));

        // Rebuilt only when what's listed (or a key) changes
        var key = new System.Text.StringBuilder();
        foreach (var r in rows) key.Append(r.key).Append(r.label).Append('|');
        if (key.ToString() == shownKey) return;
        shownKey = key.ToString();
        Rebuild();
    }

    private static string Key(InputAction action) => Keybinds.Label(Keybinds.Get(action));

    private void Rebuild()
    {
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
        var theme = UITheme.Current;
        foreach (var (keyLabel, label, situational) in rows)
        {
            var row = UIKit.Rect(label, list);
            UIKit.Size(row, 260f, 26f);
            var text = UIKit.OneLine(UIKit.Text(row, label, 17f, UIKit.Font.Bold,
                situational ? theme.primary : UIKit.WithAlpha(theme.text, 0.75f), TextAlignmentOptions.MidlineRight));
            UIKit.Stretch(text.rectTransform, 0f, keyLabel.Length > 3 ? 78f : 50f, 0f, 0f);
            text.outlineWidth = 0.18f;
            text.outlineColor = new Color32(20, 12, 50, 200);
            var chip = UIKit.Panel(row, situational ? theme.primary : UIKit.WithAlpha(theme.panel, 0.85f), 7f, "Key");
            chip.rectTransform.anchorMin = chip.rectTransform.anchorMax = chip.rectTransform.pivot = new Vector2(1f, 0.5f);
            chip.rectTransform.sizeDelta = new Vector2(keyLabel.Length > 3 ? 72f : 44f, 24f);
            chip.rectTransform.anchoredPosition = Vector2.zero;
            var keyText = UIKit.Text(chip.transform, keyLabel, keyLabel.Length > 3 ? 13f : 15f, UIKit.Font.Display,
                situational ? theme.textDark : theme.text, TextAlignmentOptions.Center);
            UIKit.Stretch(keyText.rectTransform);
        }
        foreach (var g in list.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        LayoutRebuilder.ForceRebuildLayoutImmediate(list);
    }
}
