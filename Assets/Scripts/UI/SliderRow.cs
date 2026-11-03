using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One tunable-parameter row inside an AbilityCheatRow's expanded panel. Drives a float value
/// through a getter and setter: an ability's [TunableParam] field (by reflection) or a value the
/// ability passes to its spawned object (ObjectTunables), so new parameters need no UI code.
/// Attach to: SliderRow prefab — [Label | Slider | InputField].
/// </summary>
public class SliderRow : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text       paramLabel;
    [SerializeField] private Slider         paramSlider;
    [SerializeField] private TMP_InputField paramInput;

    private Func<float>     getter;
    private SliderInputPair pair;

    /// <summary>Drives a [TunableParam] float field on <paramref name="target"/>.</summary>
    public void Initialize(TunableParamAttribute attr, object target, FieldInfo field) =>
        Initialize(attr, () => (float)field.GetValue(target), v => field.SetValue(target, v));

    /// <param name="attr">Label and ranges: the slider covers Min–Max, the text box accepts anything from HardMin up.</param>
    public void Initialize(TunableParamAttribute attr, Func<float> get, Action<float> set)
    {
        getter = get;

        paramLabel.text          = attr.DisplayName;
        paramSlider.minValue     = attr.Min;
        paramSlider.maxValue     = attr.Max;
        paramSlider.wholeNumbers = false;

        pair = new SliderInputPair(paramSlider, paramInput, "F2", get(), set,
                                   inputMin: attr.HardMin, inputMax: float.MaxValue);
    }

    /// <summary>Re-reads the value (e.g. after cheats reset it).</summary>
    public void Refresh() => pair?.SetWithoutNotify(getter());
}
