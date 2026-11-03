using UnityEngine;

/// <summary>
/// Data asset describing one game mode: its display name, the key that selects its logic in
/// <see cref="GameModeRegistry"/>, and the rules (with defaults) the host can configure.
/// Assets live in Resources/GameModes/ and are discovered automatically by the lobby.
///
/// To add a new mode: create a GameMode subclass, register its key in GameModeRegistry,
/// then create one of these assets with the same key.
/// </summary>
[CreateAssetMenu(menuName = "TurboTag/Game Mode Definition")]
public class GameModeDefinition : ScriptableObject
{
    [Tooltip("Display name shown in the game mode dropdown.")]
    public string gameModeName;

    [Tooltip("Key stored in the 'GameMode' room property; must match a key in GameModeRegistry.")]
    public string gameModeKey;

    [TextArea]
    [Tooltip("Short description shown in the lobby and pregame screens.")]
    public string description;

    [Tooltip("When true, cheats are switched on automatically when this mode is selected.")]
    public bool cheatsAutoEnabled;

    [Tooltip("When true, starting from the lobby skips Pregame and loads the map directly with each player's last loadout.")]
    public bool skipAbilitySelection;

    [Tooltip("Rules for this mode and their defaults. Written to room properties when the mode is selected.")]
    public RuleSetting[] settings;
}
