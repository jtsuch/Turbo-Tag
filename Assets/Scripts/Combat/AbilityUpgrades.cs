using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What each ability's Search &amp; Destroy upgrades do (<see cref="Economy.MaxUpgrades"/> = 2 a half): the value at
/// level 0 (what everyone starts the half with), 1 and 2. Each ability declares its own path in its class
/// (<see cref="Ability.UpgradePath"/>, Justin's numbers, Nov 14; this was one table here until Nov 16):
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

    public static Entry Bar(float a, float b, float c) =>
        new() { Kind = Kind.Duration, Levels = new[] { a, b, c }, What = "bar", Format = v => $"{v:0.#} s" };

    public static Entry Power(string what, float a, float b, float c, Func<float, string> format) =>
        new() { Kind = Kind.Power, Levels = new[] { a, b, c }, What = what, Format = format };

    /// <summary>The upgrade path for <paramref name="ability"/> (listed, or the default for its slot).</summary>
    public static Entry For(Ability ability)
    {
        var entry = ability.UpgradePath;   // Each ability declares its own (Ability.UpgradePath)
        if (entry != null) return entry;
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
