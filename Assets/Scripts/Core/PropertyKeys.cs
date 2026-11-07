/// <summary>
/// Room custom-property keys that describe the room itself and the live match state.
/// Rule keys live in <see cref="RuleKeys"/>; per-player keys live in <see cref="PlayerKeys"/>.
/// </summary>
public static class RoomKeys
{
    // ─── Lobby selections ─────────────────────────────────────────────────────
    public const string Map      = "Map";
    public const string GameMode = "GameMode";


    // ─── Match state (written by the master client only) ──────────────────────
    public const string Phase         = "Match_Phase";
    public const string PhaseStart    = "Match_PhaseStart";
    public const string PhaseDuration = "Match_PhaseDuration";
    public const string PausedAt      = "Match_PausedAt";
    public const string Round         = "Match_Round";
    public const string Winner        = "Match_Winner";
    /// <summary>Team modes: which side won ("Hunters" / "Defenders"), for the scoreboard.</summary>
    public const string WinningTeam   = "Match_WinningTeam";
    public const string DiscHolder    = "Match_DiscHolder";    // Keep Away: actor holding the disc (-1: loose)
    public const string Modifier      = "Match_Modifier";      // This round's RoundModifiers id ("" none)
    /// <summary>Actor numbers that have already started a round as hunter (Tag rotation).</summary>
    public const string ServedAsHunter = "Match_Served";
    /// <summary>The starting roles were dealt in Pregame (so players chose abilities knowing them): the level keeps them.</summary>
    public const string RolesDealt = "Match_RolesDealt";

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
    public const string HunterCount       = "Rule_HunterCount";
    public const string HunterCooldown    = "Rule_HunterCooldown";
    public const string TagCooldown       = "Rule_TagCooldown";
    public const string Gravity           = "Rule_Gravity";
    public const string EffectsRespawn    = "Rule_EffectsRespawn"; // Seconds between donuts
    public const string Donuts            = "Rule_Donuts";
    public const string Modifiers         = "Rule_Modifiers";      // Round Modifiers: a random modifier each round
    public const string GlobalCooldown    = "Rule_GlobalCooldown";
    public const string TagBonus          = "Rule_HuntedBonus";
    public const string ScoreLimit        = "Rule_ScoreLimit";
    public const string ScoreLimitEnabled = "Rule_ScoreLimitEnabled";
    public const string CountdownTime     = "Rule_CountdownTime";
    public const string RespawnDelay      = "Rule_RespawnDelay";
    public const string HunterSpeed       = "Rule_HunterSpeed";
    public const string HiderAbilities    = "Rule_HiderAbilities";
    public const string HunterAbilities   = "Rule_HunterAbilities";
    public const string PresidentTime      = "Rule_PresidentTime";
    public const string PresidentSpeed     = "Rule_PresidentSpeed";
    public const string PresidentAbilities = "Rule_PresidentAbilities";

    // Free for All (and later gun modes)
    public const string MatchTime          = "Rule_MatchTime";
    public const string PointLimitEnabled  = "Rule_PointLimitEnabled";
    public const string PointLimit         = "Rule_PointLimit";
    public const string MaxHealth          = "Rule_MaxHealth";
    public const string DamageScale        = "Rule_DamageScale";
    public const string FallPenalty        = "Rule_FallPenalty";
    public const string Killcam            = "Rule_Killcam";
    public const string HealthDrops        = "Rule_HealthDrops";
    public const string GunPickTime        = "Rule_GunPickTime";
    public const string GunModeAbilities   = "Rule_GunModeAbilities";
    public const string MovementSpread     = "Rule_MovementSpread";
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

    /// <summary>Ready in Pregame (bool). Written by the player themself; cleared when Pregame opens.</summary>
    public const string Ready = "Ready";

    /// <summary>Penguin colour (<see cref="PenguinSkins"/> name). Written by the player themself.</summary>
    public const string Skin = "Skin";

    /// <summary>Banked score in seconds (float). Written by the master client.</summary>
    public const string Score = "Score";

    // Match stats for the scoreboard (floats; see MatchStats). Reset when a level loads.
    public const string StatTags           = "Stat_Tags";           // Master: players this one tagged
    public const string StatTimesTagged    = "Stat_TimesTagged";    // Master
    public const string StatLongestHide    = "Stat_LongestHide";    // Master: best single-round survival (s)
    public const string StatRoundsSurvived = "Stat_RoundsSurvived"; // Master
    public const string StatAbilitiesUsed  = "Stat_AbilitiesUsed";  // The player themself
    public const string StatKills          = "Stat_Kills";          // Master
    public const string StatDeaths         = "Stat_Deaths";         // Master: every death, falls included
    public const string StatSelfDestructs  = "Stat_SelfDestructs";  // Master: fell off the map
    public const string StatHeadshots      = "Stat_Headshots";      // Master: kills by a headshot
    public const string StatSteals         = "Stat_Steals";         // Master: Keep Away discs taken from an enemy
    public const string StatCatches        = "Stat_Catches";        // Master: Keep Away loose discs grabbed
    public const string StatKnockOffs      = "Stat_KnockOffs";      // Master: enemies who fell right after this player's push
    public const string StatDonuts         = "Stat_Donuts";         // Master: donuts eaten

    /// <summary>Gun the player is holding (<see cref="GunCatalog"/> id, "" for none). Written by the player themself.</summary>
    public const string Gun = "Gun";
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
    public const string Scoreboard = "Scoreboard";

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
