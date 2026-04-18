using System;

/// <summary>
/// Marks a float field on an Ability MonoBehaviour as editable from CheatsUI.
/// The field's current value is read at expand-time and written back whenever the slider moves.
///
/// Usage:
///   [TunableParam("Dash Strength", 5f, 100f)]
///   public float dashStrength = 40f;
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class TunableParamAttribute : Attribute
{
    public readonly string DisplayName;
    public readonly float  Min;
    public readonly float  Max;

    public TunableParamAttribute(string displayName, float min, float max)
    {
        DisplayName = displayName;
        Min         = min;
        Max         = max;
    }
}
