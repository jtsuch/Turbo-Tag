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
    public const int   DefaultHunterCount    = 1;
    public const float DefaultHunterCooldown = 0f;    // % reduction
    public const float DefaultTagCooldown    = 2f;    // seconds a newly tagged hunter must wait
    public const float DefaultGlobalCooldown = 100f;  // %
    public const float DefaultGravity        = 100f;  // %
    public const float DefaultEffectsRespawn = 25f;   // seconds between donuts
    public const float DefaultTagBonus       = 0f;    // seconds
    public const float DefaultScoreLimit     = 600f;   // seconds
    public const float DefaultCountdownTime  = 3f;     // seconds frozen before each round
    public const float DefaultRespawnDelay   = 3f;     // seconds a tagged player lies there
    public const float DefaultHunterSpeed    = 100f;   // %

    // ─── Rules ────────────────────────────────────────────────────────────────

    public static string GameModeKey => NetProps.Room(RoomKeys.GameMode, GameModeRegistry.DefaultKey);
    public static string Map         => NetProps.Room(RoomKeys.Map, SceneNames.DefaultMap);

    public static bool  CheatsEnabled => NetProps.Room(RuleKeys.CheatsEnabled, false);
    public static bool  TimerPaused   => NetProps.Room(RuleKeys.TimerPaused, false);

    public static float HideTime => NetProps.Room(RuleKeys.HideTime, DefaultHideTime);

    public static bool  SeekTimeEnabled => NetProps.Room(RuleKeys.SeekTimeEnabled, false);
    /// <summary>Active-phase limit in seconds; 0 means unlimited.</summary>
    public static float SeekTimeLimit   => SeekTimeEnabled ? NetProps.Room(RuleKeys.SeekTime, DefaultSeekTime) : 0f;

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

    /// <summary>Donuts (random-effect pickups) appear around the map. On by default (and in Freeplay).</summary>
    public static bool Donuts => NetProps.Room(RuleKeys.Donuts, true);
    public static float TagBonus       => NetProps.Room(RuleKeys.TagBonus, DefaultTagBonus);

    public static bool  ScoreLimitEnabled => NetProps.Room(RuleKeys.ScoreLimitEnabled, false);
    /// <summary>Score (seconds) that ends the match early; 0 means no limit.</summary>
    public static float ScoreLimit => ScoreLimitEnabled ? NetProps.Room(RuleKeys.ScoreLimit, DefaultScoreLimit) : 0f;

    public static float CountdownTime => UnityEngine.Mathf.Max(0f, NetProps.Room(RuleKeys.CountdownTime, DefaultCountdownTime));
    public static float RespawnDelay  => UnityEngine.Mathf.Max(0f, NetProps.Room(RuleKeys.RespawnDelay, DefaultRespawnDelay));

    /// <summary>Multiplier on hunters' movement speed (1 = normal).</summary>
    public static float HunterSpeedMultiplier =>
        UnityEngine.Mathf.Max(0.1f, NetProps.Room(RuleKeys.HunterSpeed, DefaultHunterSpeed) / 100f);

    public static bool HiderAbilities  => NetProps.Room(RuleKeys.HiderAbilities, true);

    // ─── Protect the President ────────────────────────────────────────────────
    public const float DefaultPresidentTime  = 180f;  // seconds the President must survive
    public const float DefaultPresidentSpeed = 100f;  // %

    public static float PresidentTime => UnityEngine.Mathf.Max(1f, NetProps.Room(RuleKeys.PresidentTime, DefaultPresidentTime));
    public static float PresidentSpeedMultiplier =>
        UnityEngine.Mathf.Max(0.1f, NetProps.Room(RuleKeys.PresidentSpeed, DefaultPresidentSpeed) / 100f);
    public static bool  PresidentAbilities => NetProps.Room(RuleKeys.PresidentAbilities, true);
    public static bool HunterAbilities => NetProps.Room(RuleKeys.HunterAbilities, true);

    // ─── Free for All (gun modes) ─────────────────────────────────────────────
    public const float DefaultMatchTime   = 300f;  // seconds
    public const float DefaultPointLimit  = 25f;
    public const float DefaultMaxHealth   = 100f;
    public const float DefaultDamageScale = 100f;  // %
    public const float DefaultFallPenalty = 1f;    // points lost for falling off
    public const float DefaultGunPickTime = 5f;    // seconds to choose a gun after spawning

    public static float MatchTime => UnityEngine.Mathf.Max(10f, NetProps.Room(RuleKeys.MatchTime, DefaultMatchTime));

    /// <summary>Points that end the match early; 0 means no limit.</summary>
    public static float PointLimit =>
        NetProps.Room(RuleKeys.PointLimitEnabled, false) ? UnityEngine.Mathf.Max(1f, NetProps.Room(RuleKeys.PointLimit, DefaultPointLimit)) : 0f;

    public static float MaxHealth   => UnityEngine.Mathf.Max(1f, NetProps.Room(RuleKeys.MaxHealth, DefaultMaxHealth));
    /// <summary>Multiplier on gun damage (1 = normal).</summary>
    public static float DamageScale => UnityEngine.Mathf.Max(0.05f, NetProps.Room(RuleKeys.DamageScale, DefaultDamageScale) / 100f);
    public static float FallPenalty => UnityEngine.Mathf.Max(0f, NetProps.Room(RuleKeys.FallPenalty, DefaultFallPenalty));
    public static bool  Killcam     => NetProps.Room(RuleKeys.Killcam, true);
    public static bool  HealthDrops => NetProps.Room(RuleKeys.HealthDrops, true);
    public static float GunPickTime => UnityEngine.Mathf.Max(1f, NetProps.Room(RuleKeys.GunPickTime, DefaultGunPickTime));
    public static bool  GunModeAbilities => NetProps.Room(RuleKeys.GunModeAbilities, true);
    /// <summary>Guns get less accurate in the air (off by default: flip and snipe with perfect aim).</summary>
    public static bool  MovementSpread => NetProps.Room(RuleKeys.MovementSpread, false);

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
