using UnityEngine;

/// <summary>
/// ScriptableObject that fully describes one game mode: its display name, room property key,
/// and the ordered list of rules the host can configure. Create via
/// Assets → Create → TurboTag → Game Mode Definition, or use the
/// TurboTag → Create Game Mode Definitions editor menu item.
/// </summary>
[CreateAssetMenu(menuName = "TurboTag/Game Mode Definition")]
public class GameModeDefinition : ScriptableObject
{
    [Tooltip("Display name shown in the game mode dropdown.")]
    public string gameModeName;

    [Tooltip("Short key stored in the 'GameMode' room property (e.g. \"Tag\", \"Freeplay\").")]
    public string gameModeKey;

    [Tooltip("When true, Rule_CheatsEnabled is automatically set to true when this mode is selected.")]
    public bool cheatsAutoEnabled;

    [Tooltip("Ordered list of settings to display in the Rules panel. Leave empty for modes with no configurable rules.")]
    public RuleSetting[] settings;
}
