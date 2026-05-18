/// <summary>
/// Centralised string constants for all Photon custom property keys and PlayerPrefs keys.
/// Use these instead of raw strings everywhere to prevent typo-driven sync mismatches between
/// writers (PregameManager, InputHandler) and readers (InputHandler, Player, HUDManager, etc.).
///
/// Rule_ room property keys live in RulesUI as public consts (e.g. RulesUI.KEY_HIDE_TIME).
/// Reference them from other systems as RulesUI.KEY_HIDE_TIME rather than duplicating here.
/// </summary>
public static class NetworkKeys
{
    // ─── Player custom property keys ──────────────────────────────────────────
    // Ability slot selections — written by PregameManager, read by InputHandler and Player.
    public const string ABILITY_BASIC = "BasicAbility";
    public const string ABILITY_QUICK = "QuickAbility";
    public const string ABILITY_THROW = "ThrowAbility";
    public const string ABILITY_TRAP  = "TrapAbility";

    // ─── Keybind PlayerPrefs keys (core actions) ──────────────────────────────
    public const string KEYBIND_ACTION = "Keybind_Action";
    public const string KEYBIND_JUMP   = "Keybind_Jump";
    public const string KEYBIND_SPRINT = "Keybind_Sprint";
    public const string KEYBIND_CROUCH = "Keybind_Crouch";
    public const string KEYBIND_PRONE  = "Keybind_Prone";
    public const string KEYBIND_PAUSE  = "Keybind_Pause";
    public const string KEYBIND_GRAB   = "Keybind_Grab";

    // ─── Keybind PlayerPrefs keys (ability slots) ─────────────────────────────
    public const string KEYBIND_ABILITY_0 = "Keybind_Ability0";
    public const string KEYBIND_ABILITY_1 = "Keybind_Ability1";
    public const string KEYBIND_ABILITY_2 = "Keybind_Ability2";
    public const string KEYBIND_ABILITY_3 = "Keybind_Ability3";

    // ─── Keybind PlayerPrefs prefix (cheat / hot-swap bindings) ──────────────
    // Full key = KEYBIND_CUSTOM_PREFIX + abilityName  (e.g. "Keybind_Custom_BoomStick")
    public const string KEYBIND_CUSTOM_PREFIX = "Keybind_Custom_";
}
