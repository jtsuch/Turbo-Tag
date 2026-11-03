using System;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// Marks a float field on an Ability as editable from the Cheats tab. The cheats UI builds a
/// slider for each tagged field (including inherited ones) and writes the value back live, so
/// abilities must read these fields at use time rather than caching them in Awake.
///
/// Min/Max only bound the slider. The text box accepts any value down to <see cref="HardMin"/>
/// with no upper limit — breaking the game with cheats is part of the fun.
///
/// Usage:
///   [TunableParam("Dash Strength", 5f, 100f)]
///   [SerializeField] private float dashStrength = 40f;
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class TunableParamAttribute : Attribute
{
    public readonly string DisplayName;
    public readonly float  Min;
    public readonly float  Max;

    /// <summary>Lowest value the text box accepts (default 0 — no negative durations or forces).</summary>
    public float HardMin { get; set; } = 0f;

    public TunableParamAttribute(string displayName, float min, float max)
    {
        DisplayName = displayName;
        Min         = min;
        Max         = max;
    }

    private static readonly Dictionary<Type, List<(FieldInfo, TunableParamAttribute)>> cache = new();

    /// <summary>Every [TunableParam] float field on <paramref name="type"/> and its base classes.</summary>
    public static IReadOnlyList<(FieldInfo field, TunableParamAttribute attribute)> FieldsOf(Type type)
    {
        if (cache.TryGetValue(type, out var cached)) return cached;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                 | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var result = new List<(FieldInfo, TunableParamAttribute)>();
        for (var t = type; t != null && t != typeof(UnityEngine.MonoBehaviour); t = t.BaseType)
        {
            var declared = new List<(FieldInfo, TunableParamAttribute)>();
            foreach (var field in t.GetFields(flags))
            {
                var attr = field.GetCustomAttribute<TunableParamAttribute>();
                if (attr != null && field.FieldType == typeof(float))
                    declared.Add((field, attr));
            }
            // Base-class parameters (cooldown, force…) appear before subclass-specific ones
            result.InsertRange(0, declared);
        }

        cache[type] = result;
        return result;
    }
}
