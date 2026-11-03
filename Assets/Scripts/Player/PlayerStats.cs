using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base (unmodified) tuning values for a player. Edited on the Player component in the prefab.
/// Runtime changes — cheats, effects, abilities — go through <see cref="StatModifiers"/> so
/// they stack cleanly and can be removed without remembering "original" values.
/// </summary>
[Serializable]
public class PlayerStats
{
    [Header("Body")]
    public float height = 1.8f;

    [Header("Speeds")]
    public float walkSpeed    = 8f;
    public float sprintSpeed  = 12f;
    public float crouchSpeed  = 5f;
    public float proneSpeed   = 3f;
    public float slideSpeed   = 15f;
    public float climbSpeed   = 30f;
    public float wallRunSpeed = 20f;
    public float shimmySpeed  = 4f;

    [Header("Physics")]
    public float acceleration     = 50f;
    public float jumpStrength     = 16f;
    public float wallSlideDownMax = 4f;
    [Tooltip("Steering strength while airborne, relative to grounded (0–1).")]
    public float airControl       = 0.1f;
    [Tooltip("Steering strength while swinging on a grapple, relative to grounded (0–1).")]
    public float swingControl     = 0.01f;
}

/// <summary>Stats that runtime systems can scale.</summary>
public enum Stat
{
    Speed,
    Acceleration,
    Jump,
    Size,
    Cooldown,
    AirControl,
    /// <summary>Multiplies movement input; -1 inverts the controls.</summary>
    ControlDirection,
    /// <summary>Scales gravity on the player (applied by PlayerMovement).</summary>
    Gravity,

}

/// <summary>
/// Multiplicative stat modifiers keyed by (stat, source). Each system owns its own source
/// object, so cheats, pickups and abilities can stack and later remove only their own changes.
/// </summary>
public class StatModifiers
{
    public event Action<Stat> Changed;

    private readonly Dictionary<(Stat stat, object source), float> multipliers = new();

    /// <summary>Product of every multiplier applied to <paramref name="stat"/> (1 when none).</summary>
    public float Get(Stat stat)
    {
        float value = 1f;
        foreach (var entry in multipliers)
            if (entry.Key.stat == stat) value *= entry.Value;
        return value;
    }

    public float Get(Stat stat, object source) =>
        multipliers.TryGetValue((stat, source), out float value) ? value : 1f;

    public void Set(Stat stat, object source, float multiplier)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (multipliers.TryGetValue((stat, source), out float current) && Mathf.Approximately(current, multiplier))
            return;

        multipliers[(stat, source)] = multiplier;
        Changed?.Invoke(stat);
    }

    public void Remove(Stat stat, object source)
    {
        if (multipliers.Remove((stat, source)))
            Changed?.Invoke(stat);
    }

    /// <summary>Removes every multiplier owned by <paramref name="source"/>.</summary>
    public void Clear(object source)
    {
        var removed = new List<Stat>();
        foreach (var key in new List<(Stat, object)>(multipliers.Keys))
        {
            if (!Equals(key.Item2, source)) continue;
            multipliers.Remove(key);
            removed.Add(key.Item1);
        }
        foreach (var stat in removed) Changed?.Invoke(stat);
    }
}
