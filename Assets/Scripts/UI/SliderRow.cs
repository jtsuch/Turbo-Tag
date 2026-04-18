using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single parameter row inside an AbilityCheatRow's expanded panel.
/// Drives a float field on a target MonoBehaviour via reflection — no switch statements needed.
/// Attach to: SliderRow prefab — expected layout: [Label | Slider | InputField] in a Horizontal Layout Group.
/// </summary>
public class SliderRow : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text       paramLabel;
    public Slider         paramSlider;
    public TMP_InputField paramInput;

    private object    target;
    private FieldInfo field;
    private bool      syncing;

    /// <param name="targetObj">The MonoBehaviour that owns the field (e.g. a Dash component).</param>
    /// <param name="fieldInfo">Reflected FieldInfo for the float field to drive.</param>
    public void Initialize(string displayName, float min, float max, float currentValue,
                           object targetObj, FieldInfo fieldInfo)
    {
        target = targetObj;
        field  = fieldInfo;

        paramLabel.text      = displayName;
        paramSlider.minValue = min;
        paramSlider.maxValue = max;
        paramSlider.value    = currentValue;
        paramInput.text      = currentValue.ToString("F2");

        paramSlider.onValueChanged.AddListener(OnSlider);
        paramInput.onEndEdit.AddListener(OnInput);
    }

    private void OnSlider(float value)
    {
        if (syncing) return;
        syncing = true;
        paramInput.text = value.ToString("F2");
        field?.SetValue(target, value);
        syncing = false;
    }

    private void OnInput(string text)
    {
        if (syncing || !float.TryParse(text, out float v)) return;
        syncing = true;
        v = Mathf.Clamp(v, paramSlider.minValue, paramSlider.maxValue);
        paramSlider.value = v;
        paramInput.text   = v.ToString("F2");
        field?.SetValue(target, v);
        syncing = false;
    }
}
