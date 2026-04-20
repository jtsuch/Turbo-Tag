using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rule row for Toggle settings. Calls back with a bool value on change.
/// Attach to: RuleToggleRow prefab — expected layout: [Label | Toggle].
/// </summary>
public class RuleToggleRow : RuleRowBase
{
    [Header("UI References")]
    public TMP_Text label;
    public Toggle   toggle;

    private Action<string, object> onChanged;
    private bool syncing;

    public override void Initialize(RuleSetting setting, object currentValue,
                                    Action<string, object> callback)
    {
        RoomPropertyKey = setting.roomPropertyKey;
        onChanged       = callback;

        label.text = setting.displayName;

        bool v = currentValue is bool b ? b : setting.defaultValue > 0f;
        syncing    = true;
        toggle.isOn = v;
        syncing    = false;

        toggle.onValueChanged.AddListener(OnToggle);
    }

    public override void Refresh(object value)
    {
        if (value is not bool b) return;
        syncing     = true;
        toggle.isOn = b;
        syncing     = false;
    }

    private void OnToggle(bool v)
    {
        if (syncing) return;
        onChanged?.Invoke(RoomPropertyKey, v);
    }
}
