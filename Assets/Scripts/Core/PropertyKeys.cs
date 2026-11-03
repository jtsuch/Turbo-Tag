/// <summary>
/// Room custom-property keys that describe the room itself and the live match state.
/// Rule keys live in <see cref="RuleKeys"/>; per-player keys live in <see cref="PlayerKeys"/>.
/// </summary>
public static class RoomKeys
{
    // ─── Lobby selections ─────────────────────────────────────────────────────
    public const string Map      = "Map";
    public const string GameMode = "GameMode";

    // ─── Pregame ──────────────────────────────────────────────────────────────
    /// <summary>PhotonNetwork.Time at which ability selection closes.</summary>
    public const string PregameEndsAt = "Pregame_EndsAt";

    // ─── Match state (written by the master client only) ──────────────────────
    public const string Phase         = "Match_Phase";
    public const string PhaseStart    = "Match_PhaseStart";
    public const string PhaseDuration = "Match_PhaseDuration";
    public const string PausedAt      = "Match_PausedAt";
    public const string Round         = "Match_Round";
    public const string Winner        = "Match_Winner";
    /// <summary>Actor numbers that have already started a round as hunter (Tag rotation).</summary>
    public const string ServedAsHunter = "Match_Served";

    /// <summary>Keys exposed in the room listing so the Join List can show them.</summary>
    public static readonly string[] LobbyVisible = { Map, GameMode };
}

/// <summary>
/// Room custom-property keys for host-configurable rules. All use the "Rule_" prefix so they can
/// be identified and reset as a group. Typed access with defaults lives in <see cref="MatchRules"/>.
/// </summary>
public static class RuleKeys
{
    public const string Prefix = "Rule_";

    public const string CheatsEnabled     = "Rule_CheatsEnabled";
    public const string TimerPaused       = "Rule_TimerPaused";
    public const string HideTime          = "Rule_HideTime";
    public const string SeekTime          = "Rule_SeekTime";
    public const string SeekTimeEnabled   = "Rule_SeekTimeEnabled";
    public const string RoundCount        = "Rule_RoundCount";
    public const string HunterCount       = "Rule_HunterCount";
    public const string HunterCooldown    = "Rule_HunterCooldown";
    public const string TagCooldown       = "Rule_TagCooldown";
    public const string Gravity           = "Rule_Gravity";
    public const string EffectsRespawn    = "Rule_EffectsRespawn";
    public const string GlobalCooldown    = "Rule_GlobalCooldown";
    public const string TagBonus          = "Rule_HuntedBonus";
    public const string ScoreLimit        = "Rule_ScoreLimit";
    public const string ScoreLimitEnabled = "Rule_ScoreLimitEnabled";
}

/// <summary>Player custom-property keys.</summary>
public static class PlayerKeys
{
    // Ability loadout — one key per slot (values are ability ids, e.g. "SwingGrapple")
    public const string AbilityBasic = "BasicAbility";
    public const string AbilityQuick = "QuickAbility";
    public const string AbilityThrow = "ThrowAbility";
    public const string AbilityTrap  = "TrapAbility";

    /// <summary>Extra abilities bound to keys through cheats (string[] of ability ids).</summary>
    public const string CheatAbilities = "CheatAbilities";

    /// <summary>Current <see cref="PlayerRole"/> (int). Written by the master client.</summary>
    public const string Role = "Role";

    /// <summary>Banked score in seconds (float). Written by the master client.</summary>
    public const string Score = "Score";
}

/// <summary>Scene names used for loading. Levels are named by the lobby's map selection.</summary>
public static class SceneNames
{
    /// <summary>Every scene in Build Settings under this folder is a selectable map.</summary>
    public const string LevelFolder = "Assets/Scenes/Levels/";

    public const string Loading  = "Loading";
    public const string MainMenu = "MainMenu";
    public const string JoinList = "JoinList";
    public const string Lobby    = "Lobby";
    public const string Pregame  = "Pregame";

    public const string DefaultMap = "Quads";

    /// <summary>
    /// Playable maps: scenes in Build Settings under <see cref="LevelFolder"/>, excluding names
    /// starting with "_" (templates). Build Settings is kept in sync with that folder by the editor.
    /// </summary>
    public static System.Collections.Generic.List<string> Maps()
    {
        var maps = new System.Collections.Generic.List<string>();
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
            if (!path.StartsWith(LevelFolder)) continue;
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith("_")) maps.Add(name);
        }
        maps.Sort(string.CompareOrdinal);
        return maps;
    }

    /// <summary>True for any scene that is not a menu, i.e. a playable level.</summary>
    public static bool IsLevel(string sceneName) =>
        sceneName != Loading && sceneName != MainMenu && sceneName != JoinList
        && sceneName != Lobby && sceneName != Pregame;
}
