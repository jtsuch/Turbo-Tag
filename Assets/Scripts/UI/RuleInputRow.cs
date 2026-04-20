using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Rule row for free-text InputField settings. Calls back with a string value on end-edit.
/// Attach to: RuleInputRow prefab — expected layout: [Label | InputField].
/// </summary>
public class RuleInputRow : RuleRowBase
{
    [Header("UI References")]
    public TMP_Text       label;
    public TMP_InputField inputField;

    private Action<string, object> onChanged;

    public override void Initialize(RuleSetting setting, object currentValue,
                                    Action<string, object> callback)
    {
        RoomPropertyKey = setting.roomPropertyKey;
        onChanged       = callback;

        label.text      = setting.displayName;
        inputField.text = currentValue != null
            ? currentValue.ToString()
            : setting.defaultValue.ToString("F0");

        inputField.onEndEdit.AddListener(OnInput);
    }

    public override void Refresh(object value)
    {
        inputField.text = value?.ToString() ?? string.Empty;
    }

    private void OnInput(string text)
    {
        onChanged?.Invoke(RoomPropertyKey, text);
    }
}
