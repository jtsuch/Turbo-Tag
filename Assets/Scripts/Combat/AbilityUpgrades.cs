using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What each ability's Search &amp; Destroy upgrades do (<see cref="Economy.MaxUpgrades"/> = 2 a half): the value at
/// level 0 (what everyone starts the half with), 1 and 2. Justin's table (Nov 14), Basic abilities so far:
///   - Duration: the Basic bar in seconds (BasicAbility.DurationScale is set so the bar is exactly this).
///   - Power: a multiplier on the ability's main strength (<see cref="Ability.UpgradePower"/>; the ability reads it).
///   - Uses: uses a round (Quick/Throw/Trap).
/// Anything not listed: Basic bars 100% / 140% / 180% of the prefab's, other slots 1 / 2 / 3 uses a round.
/// </summary>
public static class AbilityUpgrades
{
    public enum Kind { Uses, Duration, Power }

    public sealed class Entry
    {
        public Kind Kind;
        public float[] Levels;                 // Level 0, 1, 2
        public string What;                    // "bar", "jump boost", "climb speed"
        public Func<float, string> Format;     // A level's value as players read it
    }

    private static Entry Bar(float a, float b, float c) =>
        new() { Kind = Kind.Duration, Levels = new[] { a, b, c }, What = "bar", Format = v => $"{v:0.#} s" };

    private static Entry Power(string what, float a, float b, float c, Func<float, string> format) =>
        new() { Kind = Kind.Power, Levels = new[] { a, b, c }, What = what, Format = format };

    private static readonly Dictionary<string, Entry> Table = new()
    {
        ["Chameleon"]     = Bar(6f, 10.5f, 15f),
        ["Flappy"]        = Power("jump boost", 0.5f, 0.75f, 1f, v => $"{v * 100f:0}%"),
        ["InfiniteClimb"] = Power("climb speed", 8f / 9f, 1f, 10f / 9f, v => $"{v * 9f:0.#} m/s"),
        ["LeafBlower"]    = Bar(1.75f, 2.5f, 3.25f),
        ["Shield"]        = Bar(2f, 3f, 4f),
        ["WallMagnet"]    = Bar(4f, 6f, 8f),
        ["ZeroGravity"]   = Bar(3.5f, 4f, 4.5f),
        ["ZipGrapple"]    = Bar(1f, 2f, 3f),

        // Quick (Nov 14): Power multiplies each one's main number; Dash, Ground Pound, Heal Orb and Teleport keep the
        // default (1 / 2 / 3 uses a round). Launch isn't offered in S&D.
        ["Adrenaline"]    = Power("speed", 2f / 3f, 1f, 4f / 3f, v => $"+{v * 75f:0}%"),
        ["BubbleShield"]  = Power("soaks", 0.5f, 1f, 1.5f, v => $"{v * 50f:0}"),
        ["Bulldozer"]     = Power("charge speed", 0.9f, 1f, 1.1f, v => $"{v * 20.8f:0.#} m/s"),
        ["Cloak"]         = Power("lasts", 0.8f, 1f, 1.2f, v => $"{v * 5f:0.#} s"),
        ["Drone"]         = Power("scan radius", 0.75f, 1f, 1.25f, v => $"{v * 15f:0.#} m"),
        ["EchoStomp"]     = Power("radius", 0.75f, 1f, 1.25f, v => $"{v * 17.25f:0.#} m"),
        ["GrowRay"]       = Power("grows to", 0.4f, 0.7f, 1f, v => $"×{1f + v:0.0}"),
        ["Honk"]          = Power("push", 0.75f, 1f, 1.25f, v => $"{v * 100f:0}%"),
        ["InkBlast"]      = Power("ink lasts", 2.5f / 3.5f, 1f, 4.5f / 3.5f, v => $"{v * 3.5f:0.#} s"),
        ["Shrink"]        = Power("shrinks to", 0.6f, 0.75f, 1f, v => $"{0.3f / v * 100f:0}%"),

        // Throw / Trap (Nov 14): sent to the object as SpawnData's upgrade; everything else 1 / 2 / 3 uses a round
        ["IceWall"]       = Power("wall health", 0.6f, 1f, 1.4f, v => $"{v * 250f:0}"),
        ["Suppressor"]    = Power("lasts", 0.75f, 1f, 1.25f, v => $"{v * 8f:0.#} s"),
    };

    /// <summary>The upgrade path for <paramref name="ability"/> (listed, or the default for its slot).</summary>
    public static Entry For(Ability ability)
    {
        if (Table.TryGetValue(ability.Id, out var entry)) return entry;
        if (ability is BasicAbility basic)
        {
            float d = basic.BaseMaxDuration;
            return Bar(d, d * 1.4f, d * 1.8f);
        }
        return new Entry { Kind = Kind.Uses, Levels = new[] { 1f, 2f, 3f }, What = "uses a round", Format = v => $"{v:0}" };
    }

    /// <summary>Sets the ability up for <paramref name="level"/> in a mode with an economy, or back to its prefab's
    /// own numbers (<paramref name="economy"/> false).</summary>
    public static void Apply(Ability ability, int level, bool economy)
    {
        ability.ChargesOverride = null;
        ability.UpgradePower = 1f;
        if (ability is BasicAbility b) b.DurationScale = 1f;
        if (!economy) return;

        var entry = For(ability);
        float value = entry.Levels[Mathf.Clamp(level, 0, entry.Levels.Length - 1)];
        switch (entry.Kind)
        {
            case Kind.Uses:
                if (ability is not BasicAbility) ability.ChargesOverride = Mathf.RoundToInt(value);
                break;
            case Kind.Duration:
                if (ability is BasicAbility basic && basic.BaseMaxDuration > 0f) basic.DurationScale = value / basic.BaseMaxDuration;
                break;
            case Kind.Power:
                ability.UpgradePower = value;
                break;
        }
        // Quick/Throw/Trap abilities with a non-Uses upgrade still get one use a round
        if (ability is not BasicAbility && entry.Kind != Kind.Uses) ability.ChargesOverride = 1;
    }

    /// <summary>The shop's line: "bar 6 s → 10.5 s", or "bar 15 s (max)".</summary>
    public static string Describe(Ability ability, int level)
    {
        var entry = For(ability);
        int last = entry.Levels.Length - 1;
        level = Mathf.Clamp(level, 0, last);
        return level >= last
            ? $"{entry.What} {entry.Format(entry.Levels[last])} (max)"
            : $"{entry.What} {entry.Format(entry.Levels[level])} → {entry.Format(entry.Levels[level + 1])}";
    }
}
