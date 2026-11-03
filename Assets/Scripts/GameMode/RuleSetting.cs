/// <summary>
/// Describes one configurable rule within a GameModeDefinition: which room property it drives,
/// how it is edited, and its default. Serialized as part of the ScriptableObject asset.
/// </summary>
[System.Serializable]
public class RuleSetting
{
    public string displayName;
    public string roomPropertyKey;

    public enum FieldType { Slider, Toggle, InputField, Dropdown }
    public FieldType fieldType;

    public float minValue;
    public float maxValue;
    public float defaultValue;

    /// <summary>Only used when fieldType == Dropdown.</summary>
    public string[] dropdownOptions;

    /// <summary>Groups settings under a header label in the UI (e.g. "Time", "World").</summary>
    public string category;

    /// <summary>The default boxed as the type stored in room properties (bool for toggles).</summary>
    public object DefaultObject => fieldType == FieldType.Toggle ? defaultValue > 0f : (object)defaultValue;
}
