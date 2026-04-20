using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rule row for Slider settings. Keeps a Slider and InputField in sync and calls back on change.
/// Attach to: RuleSliderRow prefab — expected layout: [Label | Slider | InputField].
/// </summary>
public class RuleSliderRow : RuleRowBase
{
    [Header("UI References")]
    public TMP_Text       label;
    public Slider         slider;
    public TMP_InputField inputField;

    private Action<string, object> onChanged;
    private bool syncing;

    public override void Initialize(RuleSetting setting, object currentValue,
                                    Action<string, object> callback)
    {
        RoomPropertyKey = setting.roomPropertyKey;
        onChanged       = callback;

        label.text       = setting.displayName;
        slider.minValue  = setting.minValue;
        slider.maxValue  = setting.maxValue;

        float v = currentValue is float f ? f : setting.defaultValue;
        SetValue(v);

        slider.onValueChanged.AddListener(OnSlider);
        inputField.onEndEdit.AddListener(OnInput);
    }

    public override void Refresh(object value)
    {
        if (value is float v) SetValue(v);
    }

    private void SetValue(float v)
    {
        syncing          = true;
        slider.value     = v;
        inputField.text  = v.ToString("F0");
        syncing          = false;
    }

    private void OnSlider(float v)
    {
        if (syncing) return;
        syncing         = true;
        inputField.text = v.ToString("F0");
        syncing         = false;
        onChanged?.Invoke(RoomPropertyKey, v);
    }

    private void OnInput(string text)
    {
        if (syncing || !float.TryParse(text, out float v)) return;
        v = Mathf.Clamp(v, slider.minValue, slider.maxValue);
        SetValue(v);
        onChanged?.Invoke(RoomPropertyKey, v);
    }
}
