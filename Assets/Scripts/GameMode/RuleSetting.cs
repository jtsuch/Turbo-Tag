/// <summary>
/// Describes one configurable rule setting within a GameModeDefinition.
/// Serialized and stored as part of a ScriptableObject asset — not a MonoBehaviour.
/// </summary>
[System.Serializable]
public class RuleSetting
{
    public string displayName;
    public string roomPropertyKey;

    public enum FieldType { Slider, Toggle, InputField, Dropdown }
    public FieldType fieldType;

    public float  minValue;
    public float  maxValue;
    public float  defaultValue;

    /// <summary>Only used when fieldType == Dropdown.</summary>
    public string[] dropdownOptions;

    /// <summary>Groups settings under a header label in the UI (e.g. "Time", "World").</summary>
    public string category;
}
