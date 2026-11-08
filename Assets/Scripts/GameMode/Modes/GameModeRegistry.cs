using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maps game-mode keys (stored in the "GameMode" room property) to their logic classes and
/// finds the matching <see cref="GameModeDefinition"/> assets in Resources/GameModes.
/// Register new modes in <see cref="factories"/>.
/// </summary>
public static class GameModeRegistry
{
    public const string DefaultKey   = "Tag";
    private const string ResourcePath = "GameModes";

    private static readonly Dictionary<string, Func<GameMode>> factories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tag"]      = () => new TagMode(),
        ["Freeplay"] = () => new FreeplayMode(),
        ["Freeroam"] = () => new FreeplayMode(), // Legacy key
        ["President"] = () => new PresidentMode(),
        ["FFA"]       = () => new FreeForAllMode(),
        ["TDM"]       = () => new TeamDeathmatchMode(),
        ["Hunter"]    = () => new HunterMode(),
        ["KeepAway"]  = () => new KeepAwayMode(),
        ["Tutorial"]  = () => new TutorialMode(),
        ["SnD"]       = () => new SearchDestroyMode(),
    };

    private static GameModeDefinition[] definitions;

    public static GameMode Create(string key)
    {
        if (!string.IsNullOrEmpty(key) && factories.TryGetValue(key, out var factory))
            return factory();

        Debug.LogWarning($"[GameModeRegistry] Unknown game mode '{key}', falling back to {DefaultKey}.");
        return factories[DefaultKey]();
    }

    /// <summary>All mode assets that have registered logic, in a stable order.</summary>
    public static IReadOnlyList<GameModeDefinition> Definitions
    {
        get
        {
            if (definitions != null) return definitions;

            var found = new List<GameModeDefinition>();
            foreach (var def in Resources.LoadAll<GameModeDefinition>(ResourcePath))
                if (def != null && factories.ContainsKey(def.gameModeKey)) found.Add(def);

            // Default mode first, then alphabetical
            found.Sort((a, b) =>
                a.gameModeKey == DefaultKey ? -1 :
                b.gameModeKey == DefaultKey ?  1 :
                string.CompareOrdinal(a.gameModeName, b.gameModeName));

            definitions = found.ToArray();
            return definitions;
        }
    }

    public static GameModeDefinition Find(string key)
    {
        foreach (var def in Definitions)
            if (string.Equals(def.gameModeKey, key, StringComparison.OrdinalIgnoreCase)) return def;
        return null;
    }

    public static string DisplayName(string key) => Find(key)?.gameModeName ?? key;
}
