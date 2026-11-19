using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Profile window (Justin, Nov 18): the local player's level and XP bar, lifetime stats, their favourite mode
/// and favourite ability in each slot, and a row per game mode (games, wins, best score). Opened from the Profile
/// button on the main menu's player card and in the Lobby's "Your penguin" card. Reads <see cref="ProfileStats"/>;
/// rebuilt each time it opens, so it's always current. Create once per screen, on its canvas root (after the rest,
/// so it draws on top), like the Wardrobe.
/// </summary>
public class ProfilePanel : MonoBehaviour
{
    private static UITheme Theme => UIKit.Theme;

    private UIModal modal;
    private RectTransform content;

    public static ProfilePanel Create(Transform root)
    {
        var panel = root.gameObject.AddComponent<ProfilePanel>();
        panel.modal = UIKit.Modal(root, "Profile", new Vector2(1480f, 900f));
        panel.modal.NoFooter();
        panel.content = UIKit.Rect("Content", panel.modal.Body);
        UIKit.Stretch(panel.content);
        return panel;
    }

    /// <summary>A "Profile" button that opens it.</summary>
    public Button MakeButton(Transform parent, float height = 60f, float width = 260f)
    {
        var button = UIKit.Button(parent, "Profile", UIKit.Style.Secondary, Open, height, 26f);
        UIKit.Size(button, width, height);
        return button;
    }

    public void Open()
    {
        Rebuild();
        modal.transform.SetAsLastSibling();
        modal.Open();
    }

    // ─── Layout: identity and favourites on the left, stats and modes on the right ───

    private void Rebuild()
    {
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        var d = ProfileStats.Current;

        var left = UIKit.Panel(content, Theme.field, 24f, "Identity");
        var lr = left.rectTransform;
        lr.anchorMin = new Vector2(0f, 0f);
        lr.anchorMax = new Vector2(0f, 1f);
        lr.pivot = new Vector2(0f, 0.5f);
        lr.sizeDelta = new Vector2(470f, 0f);
        lr.anchoredPosition = Vector2.zero;
        UIKit.VStack(left.transform, 12f, UIKit.Pad(28, 26), TextAnchor.UpperLeft);
        BuildIdentity(left.transform, d);
        BuildFavourites(left.transform);

        var right = UIKit.Rect("Stats", content);
        UIKit.Stretch(right, 500f, 0f, 0f, 0f);
        var list = UIKit.ScrollList(right, 14f, UIKit.Pad(4));
        BuildStats(list, d);
        BuildModes(list, d);
    }

    private void BuildIdentity(Transform parent, ProfileStats.Data d)
    {
        int level = ProfileStats.LevelFor(d.xp, out int into, out int need);
        string name = string.IsNullOrEmpty(Photon.Pun.PhotonNetwork.NickName) ? "Penguin" : Photon.Pun.PhotonNetwork.NickName;

        var row = UIKit.Rect("Name Row", parent);
        UIKit.Size(row, height: 96f);
        UIKit.HStack(row, 18f, null, TextAnchor.MiddleLeft, expandHeight: false);
        var badge = UIKit.Panel(row, Theme.primary, 48f, "Level Badge");
        UIKit.Size(badge, 96f, 96f);
        var badgeText = UIKit.Text(badge.transform, $"<size=45%>LEVEL</size>\n{level}", 40f, UIKit.Font.Display, Theme.textDark, TextAlignmentOptions.Center);
        UIKit.Stretch(badgeText.rectTransform);
        badgeText.lineSpacing = -30f;
        var nameText = UIKit.Text(row, name, 40f, UIKit.Font.Display, Theme.text);
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.enableAutoSizing = true;
        nameText.fontSizeMin = 24f;
        nameText.fontSizeMax = 40f;
        UIKit.Size(nameText, flexWidth: 1f);

        // The XP bar to the next level
        var barBack = UIKit.Panel(parent, Theme.card, 12f, "XP Bar");
        UIKit.Size(barBack, height: 26f);
        var fill = UIKit.Panel(barBack.transform, Theme.primary, 12f, "Fill");
        var fr = fill.rectTransform;
        fr.anchorMin = Vector2.zero;
        fr.anchorMax = new Vector2(Mathf.Clamp01(need > 0 ? (float)into / need : 1f), 1f);
        fr.offsetMin = fr.offsetMax = Vector2.zero;
        UIKit.Text(parent, $"{into:N0} / {need:N0} XP to level {level + 1}   ·   {d.xp:N0} XP in all", 18f, UIKit.Font.Bold, Theme.textMuted);
    }

    private void BuildFavourites(Transform parent)
    {
        UIKit.Spacer(parent, 8f);
        UIKit.Text(parent, "FAVOURITES", 20f, UIKit.Font.Bold, Theme.textMuted);
        string mode = ProfileStats.FavoriteMode();
        FavouriteRow(parent, "Game mode", mode.Length > 0 ? GameModeRegistry.DisplayName(mode) : "Play a match!", null, Theme.secondary);
        foreach (AbilitySlot slot in System.Enum.GetValues(typeof(AbilitySlot)))
        {
            string id = ProfileStats.FavoriteAbility(slot, out int matches);
            FavouriteRow(parent, SlotName(slot), id.Length > 0 ? $"{AbilityCatalog.DisplayName(id)}  <size=75%><color=#{Hex(Theme.textMuted)}>{matches} match{(matches == 1 ? "" : "es")}</color></size>" : "—",
                id.Length > 0 ? AbilityIcons.Get(id) : null, SlotColor(slot));
        }
    }

    private void FavouriteRow(Transform parent, string label, string value, Sprite icon, Color accent)
    {
        var row = UIKit.Panel(parent, Theme.card, 16f, label);
        UIKit.Size(row, height: 78f);
        UIKit.HStack(row.transform, 14f, UIKit.Pad(12, 8), TextAnchor.MiddleLeft, expandHeight: false);
        var iconBack = UIKit.Panel(row.transform, UIKit.WithAlpha(accent, 0.9f), 14f, "Icon");
        UIKit.Size(iconBack, 58f, 58f);
        if (icon != null)
        {
            var image = UIKit.Rect("Image", iconBack.transform).gameObject.AddComponent<Image>();
            UIKit.Stretch(image.rectTransform, 5f, 5f, 5f, 5f);
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
        var texts = UIKit.Rect("Texts", row.transform);
        UIKit.Size(texts, flexWidth: 1f);
        UIKit.VStack(texts, 0f, null, TextAnchor.MiddleLeft);
        UIKit.Text(texts, label.ToUpperInvariant(), 15f, UIKit.Font.Bold, accent);
        UIKit.OneLine(UIKit.Text(texts, value, 24f, UIKit.Font.Display, Theme.text));
    }

    // ─── Stats ────────────────────────────────────────────────────────────────

    private void BuildStats(Transform list, ProfileStats.Data d)
    {
        Section(list, "OVERALL");
        Tiles(list, new (string, string)[]
        {
            ("Games", $"{d.games:N0}"),
            ("Wins", $"{d.wins:N0}"),
            ("Win rate", d.games > 0 ? $"{100f * d.wins / d.games:0}%" : "—"),
            ("Time played", Duration(d.seconds)),
        });
        Section(list, "GUN MODES");
        Tiles(list, new (string, string)[]
        {
            ("Kills", $"{d.kills:N0}"),
            ("Deaths", $"{d.deaths:N0}"),
            ("K/D", d.deaths > 0 ? $"{d.kills / d.deaths:0.00}" : d.kills > 0 ? $"{d.kills:0}" : "—"),
            ("Headshots", $"{d.headshots:N0}"),
            ("Assists", $"{d.assists:N0}"),
            ("Damage", $"{d.damage:N0}"),
            ("Knock-offs", $"{d.knockOffs:N0}"),
            ("Rounds survived", $"{d.roundsSurvived:N0}"),
        });
        Section(list, "CHASE AND OBJECTIVES");
        Tiles(list, new (string, string)[]
        {
            ("Tags", $"{d.tags:N0}"),
            ("Times tagged", $"{d.timesTagged:N0}"),
            ("Longest hide", d.longestHide > 0f ? MatchStats.FormatTime(d.longestHide) : "—"),
            ("Disc steals", $"{d.steals:N0}"),
            ("Bombs planted", $"{d.plants:N0}"),
            ("Bombs defused", $"{d.defuses:N0}"),
            ("Donuts eaten", $"{d.donuts:N0}"),
            ("Abilities used", $"{d.abilitiesUsed:N0}"),
        });
    }

    private void BuildModes(Transform list, ProfileStats.Data d)
    {
        Section(list, "GAME MODES");
        if (d.modes.Count == 0)
        {
            UIKit.Text(list, "Play a match and your modes show up here.", 20f, UIKit.Font.Body, Theme.textMuted);
            return;
        }
        var modes = new List<ProfileStats.ModeRecord>(d.modes);
        modes.Sort((a, b) => b.games.CompareTo(a.games));
        foreach (var m in modes)
        {
            var row = UIKit.Panel(list, Theme.card, 16f, m.key);
            UIKit.Size(row, height: 64f);
            UIKit.HStack(row.transform, 12f, UIKit.Pad(20, 8), TextAnchor.MiddleLeft, expandHeight: false);
            var name = UIKit.Text(row.transform, GameModeRegistry.DisplayName(m.key), 26f, UIKit.Font.Display, Theme.text);
            UIKit.OneLine(name);
            UIKit.Size(name, flexWidth: 1f);
            Cell(row.transform, $"{m.games} played");
            Cell(row.transform, $"{m.wins} won");
            Cell(row.transform, $"best {m.best:0}");
            Cell(row.transform, $"{m.xp:N0} XP");
        }
    }

    private static void Cell(Transform row, string text)
    {
        var t = UIKit.Text(row, text, 20f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Right);
        UIKit.OneLine(t);
        UIKit.Size(t, 150f, 40f);
    }

    private static void Section(Transform list, string title)
    {
        var t = UIKit.Text(list, title, 20f, UIKit.Font.Bold, Theme.textMuted);
        UIKit.Size(t, height: 30f);
    }

    // Stat tiles, four to a row
    private static void Tiles(Transform list, (string label, string value)[] items)
    {
        var grid = UIKit.Rect("Tiles", list);
        var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(220f, 104f);
        layout.spacing = new Vector2(14f, 14f);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 4;
        UIKit.Size(grid, height: Mathf.CeilToInt(items.Length / 4f) * 118f);
        foreach (var (label, value) in items)
        {
            var tile = UIKit.Panel(grid, Theme.card, 16f, label);
            var v = UIKit.Text(tile.transform, value, 38f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Center);
            UIKit.Place(v.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(210f, 48f));
            v.rectTransform.pivot = new Vector2(0.5f, 1f);
            UIKit.OneLine(v);
            var l = UIKit.Text(tile.transform, label.ToUpperInvariant(), 15f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Center);
            UIKit.Place(l.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(210f, 24f));
            l.rectTransform.pivot = new Vector2(0.5f, 0f);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string Duration(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        return minutes >= 60 ? $"{minutes / 60}h {minutes % 60:00}m" : $"{minutes}m";
    }

    private static string SlotName(AbilitySlot slot) => slot switch
    {
        AbilitySlot.Basic => "Basic ability",
        AbilitySlot.Quick => "Quick ability",
        AbilitySlot.Throw => "Throw ability",
        _                 => "Trap ability",
    };

    private static Color SlotColor(AbilitySlot slot) => slot switch
    {
        AbilitySlot.Basic => Theme.cyan,
        AbilitySlot.Quick => Theme.primary,
        AbilitySlot.Throw => Theme.pink,
        _                 => Theme.lime,
    };

    private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
}
