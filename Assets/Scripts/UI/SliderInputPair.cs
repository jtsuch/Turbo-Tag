using System;
using System.Globalization;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Keeps a Slider and a TMP_InputField showing the same number and reports changes from
/// either one. Removes the repeated "updating" guard boilerplate from every settings screen.
///
/// By default typed values are clamped to the slider's range. Pass explicit input bounds to let
/// the text box go beyond the slider (the slider then just pins at its end) — used by cheats,
/// where the slider covers sensible values but any number can be typed.
/// </summary>
public class SliderInputPair
{
    private readonly Slider         slider;
    private readonly TMP_InputField input;
    private readonly string         format;
    private readonly Action<float>  onChanged;
    private readonly float          inputMin;
    private readonly float          inputMax;
    private float value;
    private bool  syncing;

    /// <param name="inputMin">Lowest typeable value; NaN = slider minimum.</param>
    /// <param name="inputMax">Highest typeable value; NaN = slider maximum.</param>
    public SliderInputPair(Slider slider, TMP_InputField input, string format, float initial,
                           Action<float> onChanged, float inputMin = float.NaN, float inputMax = float.NaN)
    {
        this.slider    = slider;
        this.input     = input;
        this.format    = format;
        this.onChanged = onChanged;
        this.inputMin  = inputMin;
        this.inputMax  = inputMax;

        SetWithoutNotify(initial);
        if (slider != null) slider.onValueChanged.AddListener(OnSlider);
        if (input  != null) input.onEndEdit.AddListener(OnInput);
    }

    /// <summary>The real value, which may lie outside the slider's range.</summary>
    public float Value => value;

    /// <summary>Updates both controls without raising the change callback.</summary>
    public void SetWithoutNotify(float newValue)
    {
        syncing = true;
        value = newValue;
        if (slider != null) slider.SetValueWithoutNotify(newValue); // Pins at the slider's ends
        if (input  != null) input.SetTextWithoutNotify(newValue.ToString(format));
        syncing = false;
    }

    private void OnSlider(float newValue)
    {
        if (syncing) return;
        value = newValue;
        if (input != null) input.SetTextWithoutNotify(newValue.ToString(format));
        onChanged?.Invoke(newValue);
    }

    private void OnInput(string text)
    {
        if (syncing) return;
        if (!TryParse(text, out float typed))
        {
            SetWithoutNotify(value); // Restore the last valid value
            return;
        }

        float min = float.IsNaN(inputMin) ? (slider != null ? slider.minValue : float.MinValue) : inputMin;
        float max = float.IsNaN(inputMax) ? (slider != null ? slider.maxValue : float.MaxValue) : inputMax;
        SetWithoutNotify(Math.Clamp(typed, min, max));
        onChanged?.Invoke(value);
    }

    public static bool TryParse(string text, out float result) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out result)
        || float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
