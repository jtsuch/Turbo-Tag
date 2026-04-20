using System;
using UnityEngine;

/// <summary>
/// Abstract base for all dynamically-spawned rule setting rows in RulesUI.
/// Subclasses handle a specific FieldType and own their own UI wiring.
/// </summary>
public abstract class RuleRowBase : MonoBehaviour
{
    /// <summary>Photon room property key this row drives (set during Initialize).</summary>
    public string RoomPropertyKey { get; protected set; }

    /// <summary>
    /// Populate UI controls from <paramref name="setting"/> and <paramref name="currentValue"/>.
    /// Call <paramref name="onChanged"/> with (key, newValue) when the user edits the control.
    /// </summary>
    public abstract void Initialize(RuleSetting setting, object currentValue,
                                    Action<string, object> onChanged);

    /// <summary>Push a new value from room properties into the UI without firing onChanged.</summary>
    public abstract void Refresh(object value);
}
