using UnityEngine;

/// <summary>
/// One host-configurable rule in a <see cref="GameModeDefinition"/>: which room property it drives,
/// how it is edited, its limits and its default. The rules panel builds a row for each one, so a
/// mode's settings are pure data. Serialized as part of the ScriptableObject asset.
/// </summary>
[System.Serializable]
public class RuleSetting
{
    public enum FieldType
    {
        /// <summary>A number between min and max (optionally whole numbers only).</summary>
        Number = 0,
        /// <summary>On/off. Stored as a bool.</summary>
        Toggle = 1,
        /// <summary>A duration in seconds, shown and typed as m:ss.</summary>
        Time = 2,
    }

    [Tooltip("Label shown in the rules panel.")]
    public string displayName;
    [Tooltip("Room property it writes (a RuleKeys constant, \"Rule_\" prefix).")]
    public string roomPropertyKey;
    public FieldType fieldType;

    [Tooltip("Groups rules under a header in the lobby panel (e.g. \"Time\", \"Hunters\").")]
    public string category;

    [Header("Numbers and times")]
    public float minValue;
    public float maxValue = 100f;
    [Tooltip("For toggles: 1 = on.")]
    public float defaultValue;
    public bool  wholeNumbers = true;
    [Tooltip("Shown after the name, e.g. \"%\" or \"s\".")]
    public string unit;

    [Header("Limits")]
    [Tooltip("Never more than the player count minus one (e.g. hunters: someone must be left to hide).")]
    public bool atMostPlayersMinusOne;
    [Tooltip("Key of a toggle rule that switches this one on; while it is off this row is greyed out.")]
    public string enabledByKey;

    /// <summary>The default boxed as the type stored in room properties (bool for toggles).</summary>
    public object DefaultObject => fieldType == FieldType.Toggle ? defaultValue > 0f : (object)defaultValue;

    /// <summary>Highest allowed value right now (<paramref name="playerCount"/> applies the players-minus-one cap).</summary>
    public float MaxFor(int playerCount) =>
        atMostPlayersMinusOne ? Mathf.Max(minValue, Mathf.Min(maxValue, playerCount - 1)) : maxValue;

    /// <summary>Clamps (and rounds, for whole-number rules) a value the host typed.</summary>
    public float Clamp(float value, int playerCount)
    {
        if (wholeNumbers) value = Mathf.Round(value);
        return Mathf.Clamp(value, minValue, MaxFor(playerCount));
    }
}
