using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Typed, defaulted view of the host-configurable rules stored in room properties.
/// This is the single place that knows each rule's default and unit conversion — UI, game
/// modes and world applicators all read rules through here instead of raw keys.
/// </summary>
public static class MatchRules
{
    // ─── Defaults ─────────────────────────────────────────────────────────────
    public const float DefaultHideTime       = 120f;  // seconds
    public const float DefaultSeekTime       = 300f;  // seconds (only used when the limit is enabled)
    public const int   DefaultRoundCount     = 3;
    public const int   DefaultHunterCount    = 1;
    public const float DefaultHunterCooldown = 0f;    // % reduction
    public const float DefaultTagCooldown    = 2f;    // seconds a newly tagged hunter must wait
    public const float DefaultGlobalCooldown = 100f;  // %
    public const float DefaultGravity        = 100f;  // %
    public const float DefaultEffectsRespawn = 30f;   // seconds
    public const float DefaultTagBonus       = 0f;    // seconds
    public const float DefaultScoreLimit     = 600f;   // seconds

    // ─── Rules ────────────────────────────────────────────────────────────────

    public static string GameModeKey => NetProps.Room(RoomKeys.GameMode, GameModeRegistry.DefaultKey);
    public static string Map         => NetProps.Room(RoomKeys.Map, SceneNames.DefaultMap);

    public static bool  CheatsEnabled => NetProps.Room(RuleKeys.CheatsEnabled, false);
    public static bool  TimerPaused   => NetProps.Room(RuleKeys.TimerPaused, false);

    public static float HideTime => NetProps.Room(RuleKeys.HideTime, DefaultHideTime);

    public static bool  SeekTimeEnabled => NetProps.Room(RuleKeys.SeekTimeEnabled, false);
    /// <summary>Active-phase limit in seconds; 0 means unlimited.</summary>
    public static float SeekTimeLimit   => SeekTimeEnabled ? NetProps.Room(RuleKeys.SeekTime, DefaultSeekTime) : 0f;

    public static int   RoundCount  => System.Math.Max(1, NetProps.Room(RuleKeys.RoundCount, DefaultRoundCount));
    public static int   HunterCount => System.Math.Max(1, NetProps.Room(RuleKeys.HunterCount, DefaultHunterCount));

    /// <summary>Fraction (0–1) by which hunter cooldowns are reduced.</summary>
    public static float HunterCooldownReduction =>
        UnityEngine.Mathf.Clamp01(NetProps.Room(RuleKeys.HunterCooldown, DefaultHunterCooldown) / 100f);

    public static float TagCooldown => NetProps.Room(RuleKeys.TagCooldown, DefaultTagCooldown);

    /// <summary>Multiplier applied to every ability cooldown (1 = normal).</summary>
    public static float GlobalCooldownMultiplier =>
        UnityEngine.Mathf.Max(0f, NetProps.Room(RuleKeys.GlobalCooldown, DefaultGlobalCooldown) / 100f);

    /// <summary>Multiplier applied to world gravity (1 = normal).</summary>
    public static float GravityMultiplier => NetProps.Room(RuleKeys.Gravity, DefaultGravity) / 100f;

    public static float EffectsRespawn => NetProps.Room(RuleKeys.EffectsRespawn, DefaultEffectsRespawn);
    public static float TagBonus       => NetProps.Room(RuleKeys.TagBonus, DefaultTagBonus);

    public static bool  ScoreLimitEnabled => NetProps.Room(RuleKeys.ScoreLimitEnabled, false);
    /// <summary>Score (seconds) that ends the match early; 0 means no limit.</summary>
    public static float ScoreLimit => ScoreLimitEnabled ? NetProps.Room(RuleKeys.ScoreLimit, DefaultScoreLimit) : 0f;

    // ─── Host helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Host only: selects a game mode and writes its rule defaults. When
    /// <paramref name="overwrite"/> is false, rules the host already set are left alone.
    /// </summary>
    public static void ApplyGameMode(GameModeDefinition definition, bool overwrite)
    {
        if (definition == null || !PhotonNetwork.IsMasterClient) return;

        var props = new Hashtable { [RoomKeys.GameMode] = definition.gameModeKey };
        if (definition.cheatsAutoEnabled && (overwrite || !NetProps.RoomHas(RuleKeys.CheatsEnabled)))
            props[RuleKeys.CheatsEnabled] = true;

        if (definition.settings != null)
        {
            foreach (var setting in definition.settings)
            {
                if (string.IsNullOrEmpty(setting.roomPropertyKey)) continue;
                if (!overwrite && NetProps.RoomHas(setting.roomPropertyKey)) continue;
                props[setting.roomPropertyKey] = setting.DefaultObject;
            }
        }

        NetProps.SetRoom(props);
    }
}
