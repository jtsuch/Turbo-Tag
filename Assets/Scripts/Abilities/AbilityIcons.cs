using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds an ability's icon in Resources/AbilityIcons, from sprite sheets or single images:
///   - Sprite sheets (Sprite Mode: Multiple): each slice named "{AbilityId}Icon" in the Sprite Editor. Any number of
///     sheets; if two sheets have the same name, the one whose file name sorts last wins (newer pages).
///   - Single images named "{AbilityId}Icon" (used only when no sheet has that ability).
/// Names match ignoring case and anything that isn't a letter or digit ("GravballIcon" finds GravBall), and a few
/// abilities also answer to another name (<see cref="Aliases"/>: "JumpPadIcon" is the BouncePadTrap icon).
/// </summary>
public static class AbilityIcons
{
    private const string Folder = "AbilityIcons";

    // Ability key → other icon names it may be drawn under
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["bouncepadtrap"] = new[] { "jumppad", "bouncepad" },
        ["infiniteclimb"] = new[] { "infiniteclimbing" },
    };

    private static Dictionary<string, Sprite> sheet;
    private static readonly Dictionary<string, Sprite> singles = new();

    /// <summary>The icon for <paramref name="abilityId"/>, or null if there isn't one.</summary>
    public static Sprite Get(string abilityId)
    {
        if (string.IsNullOrEmpty(abilityId)) return null;
        string key = Key(abilityId);
        LoadSheets();
        if (sheet.TryGetValue(key, out var sprite)) return sprite;
        if (Aliases.TryGetValue(key, out var others))
            foreach (string other in others)
                if (sheet.TryGetValue(other, out sprite)) return sprite;

        if (singles.TryGetValue(key, out sprite)) return sprite;
        var tex = Resources.Load<Texture2D>($"{Folder}/{abilityId}Icon");
        sprite = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)) : null;
        singles[key] = sprite;
        return sprite;
    }

    private static void LoadSheets()
    {
        if (sheet != null) return;
        sheet = new Dictionary<string, Sprite>();
        var sprites = new List<Sprite>(Resources.LoadAll<Sprite>(Folder));
        sprites.Sort((a, b) => string.CompareOrdinal(a.texture != null ? a.texture.name : "", b.texture != null ? b.texture.name : ""));
        foreach (var s in sprites) sheet[Key(s.name)] = s; // Later pages overwrite earlier ones
    }

    // "GravBall", "GravballIcon", "Grav Ball Icon" → "gravball"
    private static string Key(string name)
    {
        var chars = new System.Text.StringBuilder(name.Length);
        foreach (char c in name) if (char.IsLetterOrDigit(c)) chars.Append(char.ToLowerInvariant(c));
        string key = chars.ToString();
        return key.EndsWith("icon") ? key.Substring(0, key.Length - 4) : key;
    }
}
