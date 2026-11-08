using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Every rebindable input. Ability slots are bound to slots, not specific abilities.</summary>
public enum InputAction
{
    Action, Jump, Sprint, Crouch, Prone, Grab, Pause,
    AbilityBasic, AbilityQuick, AbilityThrow, AbilityTrap,
    FreeSpin,
    Reload,
    Scores,
    Map,
    Emote,
    Taunt,
    Ping,
}

/// <summary>
/// PlayerPrefs-backed keybind store shared by InputHandler, the settings UI and the HUD.
/// Cheats-tab key changes last for the current match only (<see cref="ResetMatchBindings"/>), so every
/// match starts with the four starting-ability keys from Settings:
///   match overrides — a starting slot rebound or removed in Cheats (Settings keys stay saved)
///   extras          — cheat-mode bindings that map a key directly to an ability id
/// </summary>
public static class Keybinds
{
    private const string Prefix       = "Keybind_";
    private const string CustomPrefix = "Keybind_Custom_";

    public static event Action Changed;

    /// <summary>
    /// A key was just given to an ability slot. Anything else using it for an ability (cheat extra
    /// bindings) should let go, so one key never fires two abilities.
    /// </summary>
    public static event Action<KeyCode> AbilityKeyClaimed;

    private static readonly Dictionary<InputAction, KeyCode> defaults = new()
    {
        [InputAction.Action]       = KeyCode.Mouse0,
        [InputAction.Jump]         = KeyCode.Space,
        [InputAction.Sprint]       = KeyCode.LeftShift,
        [InputAction.Crouch]       = KeyCode.LeftControl,
        [InputAction.Prone]        = KeyCode.C,
        [InputAction.Grab]         = KeyCode.F,
        [InputAction.FreeSpin]     = KeyCode.Alpha4,
        [InputAction.Reload]       = KeyCode.R,
        [InputAction.Scores]       = KeyCode.Tab,
        [InputAction.Map]          = KeyCode.M,
        [InputAction.Emote]        = KeyCode.T,
        [InputAction.Taunt]        = KeyCode.G,
        [InputAction.Ping]         = KeyCode.Mouse2,
        [InputAction.Pause]        = KeyCode.Escape,
        [InputAction.AbilityBasic] = KeyCode.Mouse1,
        [InputAction.AbilityQuick] = KeyCode.E,
        [InputAction.AbilityThrow] = KeyCode.Q,
        [InputAction.AbilityTrap]  = KeyCode.X,
    };

    private static readonly Dictionary<InputAction, KeyCode> cache = new();

    /// <summary>Every KeyCode value, cached for "press any key" listening loops.</summary>
    public static readonly KeyCode[] AllKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));

    // ─── Core bindings ────────────────────────────────────────────────────────

    // Starting-slot keys changed in Cheats this match (not saved)
    private static readonly Dictionary<InputAction, KeyCode> matchOverrides = new();

    public static KeyCode Get(InputAction action)
    {
        if (matchOverrides.TryGetValue(action, out KeyCode key)) return key;
        if (cache.TryGetValue(action, out key)) return key;

        key = Parse(PlayerPrefs.GetString(PrefsKey(action), Default(action).ToString()), Default(action));
        cache[action] = key;
        return key;
    }

    /// <param name="forMatch">True for Cheats-tab changes: they last until the next match and aren't saved.</param>
    public static void Set(InputAction action, KeyCode key, bool forMatch = false)
    {
        if (action == InputAction.Pause) key = KeyCode.Escape; // Pause is never rebindable

        // An ability key belongs to one ability: other slots using it are unbound, cheat extras let go
        bool abilityKey = IsAbilitySlot(action) && key != KeyCode.None;
        if (abilityKey)
        {
            foreach (AbilitySlot slot in Enum.GetValues(typeof(AbilitySlot)))
            {
                var other = SlotAction(slot);
                if (other != action && Get(other) == key) Store(other, KeyCode.None, forMatch);
            }
        }

        Store(action, key, forMatch);
        if (abilityKey) AbilityKeyClaimed?.Invoke(key);
        Changed?.Invoke();
    }

    /// <summary>Unbinds every ability slot using <paramref name="key"/> (a cheat extra binding just took it).</summary>
    public static void ReleaseSlotsUsing(KeyCode key)
    {
        if (key == KeyCode.None) return;
        bool any = false;
        foreach (AbilitySlot slot in Enum.GetValues(typeof(AbilitySlot)))
        {
            if (Get(SlotAction(slot)) != key) continue;
            Store(SlotAction(slot), KeyCode.None, forMatch: true); // A cheat extra took it: this match only
            any = true;
        }
        if (any) Changed?.Invoke();
    }

    public static bool IsAbilitySlot(InputAction action) =>
        action == InputAction.AbilityBasic || action == InputAction.AbilityQuick
        || action == InputAction.AbilityThrow || action == InputAction.AbilityTrap;

    private static void Store(InputAction action, KeyCode key, bool forMatch)
    {
        if (forMatch)
        {
            matchOverrides[action] = key;
            return;
        }
        matchOverrides.Remove(action);
        cache[action] = key;
        PlayerPrefs.SetString(PrefsKey(action), key.ToString());
        PlayerPrefs.Save();
    }

    /// <summary>
    /// A new match: forget Cheats-tab key changes (starting-slot overrides and extras), so each starting
    /// ability is back on its Settings key and nothing else is bound.
    /// </summary>
    public static void ResetMatchBindings()
    {
        if (matchOverrides.Count == 0 && customs.Count == 0) return;
        matchOverrides.Clear();
        customs.Clear();
        Changed?.Invoke();
    }

    public static KeyCode Default(InputAction action) =>
        defaults.TryGetValue(action, out KeyCode key) ? key : KeyCode.None;

    public static void ResetAll()
    {
        foreach (var pair in defaults)
        {
            cache[pair.Key] = pair.Value;
            PlayerPrefs.SetString(PrefsKey(pair.Key), pair.Value.ToString());
        }
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static InputAction SlotAction(AbilitySlot slot) => slot switch
    {
        AbilitySlot.Basic => InputAction.AbilityBasic,
        AbilitySlot.Quick => InputAction.AbilityQuick,
        AbilitySlot.Throw => InputAction.AbilityThrow,
        _                 => InputAction.AbilityTrap,
    };

    public static KeyCode ForSlot(AbilitySlot slot) => Get(SlotAction(slot));

    // ─── Cheat bindings (ability id → key) ────────────────────────────────────

    private static readonly Dictionary<string, KeyCode> customs = new();

    public static KeyCode GetCustom(string abilityId) =>
        customs.TryGetValue(abilityId, out KeyCode key) ? key : KeyCode.None;

    public static void SetCustom(string abilityId, KeyCode key)
    {
        if (key == KeyCode.None) customs.Remove(abilityId);
        else                     customs[abilityId] = key;
        Changed?.Invoke();
    }

    // Older builds saved Cheats changes: extra bindings piled up across games, and a starting slot removed
    // in Cheats stayed unbound for good (a match could start with no usable abilities). Undo both.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CleanUpSavedBindings()
    {
        customs.Clear();
        matchOverrides.Clear();
        bool any = false;

        // Reload moved to R and Free Spin to 4: drop saved copies of the old defaults so the new ones apply
        any |= ForgetOldDefault(InputAction.FreeSpin, KeyCode.R);
        any |= ForgetOldDefault(InputAction.Reload, KeyCode.G);

        // Core controls must always have a key: older settings screens could clear one (an unbound Shoot meant a
        // player simply couldn't fire). Anything saved as "None" gets its default back.
        foreach (InputAction action in Enum.GetValues(typeof(InputAction)))
        {
            if (IsAbilitySlot(action) || Get(action) != KeyCode.None) continue;
            Store(action, Default(action), forMatch: false);
        }
        foreach (AbilitySlot slot in Enum.GetValues(typeof(AbilitySlot)))
        {
            var action = SlotAction(slot);
            if (Get(action) != KeyCode.None) continue;
            Store(action, Default(action), forMatch: false);
        }
        foreach (AbilitySlot slot in Enum.GetValues(typeof(AbilitySlot)))
            foreach (string id in AbilityCatalog.InSlot(slot))
            {
                if (!PlayerPrefs.HasKey(CustomPrefix + id)) continue;
                PlayerPrefs.DeleteKey(CustomPrefix + id);
                any = true;
            }
        if (any) PlayerPrefs.Save();
    }

    private static bool ForgetOldDefault(InputAction action, KeyCode oldDefault)
    {
        string key = PrefsKey(action);
        if (PlayerPrefs.GetString(key, "") != oldDefault.ToString()) return false;
        PlayerPrefs.DeleteKey(key);
        cache.Remove(action);
        return true;
    }

    // ─── Display ──────────────────────────────────────────────────────────────

    /// <summary>Short human-readable label, e.g. "LMB", "Shift", "E".</summary>
    public static string Label(KeyCode key) => key switch
    {
        KeyCode.None         => "—",
        KeyCode.Mouse0       => "LMB",
        KeyCode.Mouse1       => "RMB",
        KeyCode.Mouse2       => "MMB",
        KeyCode.LeftShift    => "Shift",
        KeyCode.RightShift   => "RShift",
        KeyCode.LeftControl  => "Ctrl",
        KeyCode.RightControl => "RCtrl",
        KeyCode.LeftAlt      => "Alt",
        KeyCode.Space        => "Space",
        KeyCode.Escape       => "Esc",
        >= KeyCode.Alpha0 and <= KeyCode.Alpha9 => ((int)key - (int)KeyCode.Alpha0).ToString(),
        _                    => key.ToString(),
    };

    // ─── Helpers ──────────────────────────────────────────────────────────────

    // Ability slots keep their original PlayerPrefs names so existing saves still load.
    private static string PrefsKey(InputAction action) => action switch
    {
        InputAction.AbilityBasic => Prefix + "Ability0",
        InputAction.AbilityQuick => Prefix + "Ability1",
        InputAction.AbilityThrow => Prefix + "Ability2",
        InputAction.AbilityTrap  => Prefix + "Ability3",
        _                        => Prefix + action,
    };

    private static KeyCode Parse(string value, KeyCode fallback) =>
        Enum.TryParse(value, out KeyCode key) ? key : fallback;
}
