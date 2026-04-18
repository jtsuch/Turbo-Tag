using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Reads raw Unity input and routes it to movement flags and AbilityHandler each frame.
/// Persists keybindings in PlayerPrefs and supports runtime rebinding. Ability keys are
/// stored by ability name so they survive hot-reloads and Photon property updates.
/// Attach to: ThePlayer prefab — disabled automatically on remote player instances.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class InputHandler : MonoBehaviourPunCallbacks
{
    // ─── Input State ──────────────────────────────────────────────────────────
    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }

    public bool Jump { get; private set; }
    public bool Sprint { get; private set; }
    public bool Crouch { get; private set; }
    public bool Down { get; private set; }
    public bool Prone { get; private set; }
    public bool Grab { get; private set; }
    public bool Pause { get; private set; }
    public bool AbilityZeroDown { get; private set; }
    public bool AbilityZeroUp { get; private set; }

    // ─── References ───────────────────────────────────────────────────────────
    [Header("References")]
    public AbilityHandler AbilityHandler;
    public Player player;

    private PhotonView view;

    // Key name → KeyCode; core actions and ability names share this dictionary
    private Dictionary<string, KeyCode> keybindings;

    // Cached key codes for ability slots, set at Start and reapplied when Photon properties arrive
    private KeyCode abilitySlotKey0;
    private KeyCode abilitySlotKey1;
    private KeyCode abilitySlotKey2;
    private KeyCode abilitySlotKey3;

    void Start()
    {
        view = GetComponent<PhotonView>();
        if (!view.IsMine)
        {
            enabled = false; // Skip Update on remote players — they don't drive local input
            return;
        }

        // Load saved keybindings from PlayerPrefs; defaults are used on first run
        string actionKey = PlayerPrefs.GetString("Keybind_Action", KeyCode.Mouse0.ToString());
        string jumpKey = PlayerPrefs.GetString("Keybind_Jump", KeyCode.Space.ToString());
        string sprintKey = PlayerPrefs.GetString("Keybind_Sprint", KeyCode.LeftShift.ToString());
        string crouchKey = PlayerPrefs.GetString("Keybind_Crouch", KeyCode.LeftControl.ToString());
        string proneKey = PlayerPrefs.GetString("Keybind_Prone", KeyCode.C.ToString());
        string pauseKey = PlayerPrefs.GetString("Keybind_Pause", KeyCode.Escape.ToString());
        string grabKey = PlayerPrefs.GetString("Keybind_Grab", KeyCode.F.ToString());
        string abilityZero = PlayerPrefs.GetString("Keybind_Ability0", KeyCode.Mouse1.ToString());
        string abilityOne = PlayerPrefs.GetString("Keybind_Ability1", KeyCode.E.ToString());
        string abilityTwo = PlayerPrefs.GetString("Keybind_Ability2", KeyCode.Q.ToString());
        string abilityThree = PlayerPrefs.GetString("Keybind_Ability3", KeyCode.X.ToString());

        // Cache parsed keycodes for ability slots so we can assign them later when ability names arrive
        abilitySlotKey0 = (KeyCode)System.Enum.Parse(typeof(KeyCode), abilityZero);
        abilitySlotKey1 = (KeyCode)System.Enum.Parse(typeof(KeyCode), abilityOne);
        abilitySlotKey2 = (KeyCode)System.Enum.Parse(typeof(KeyCode), abilityTwo);
        abilitySlotKey3 = (KeyCode)System.Enum.Parse(typeof(KeyCode), abilityThree);

        string abilityOneName = GetAbilityName("BasicAbility");
        string abilityTwoName = GetAbilityName("QuickAbility");
        string abilityThreeName = GetAbilityName("ThrowAbility");
        string abilityFourName = GetAbilityName("TrapAbility");

        // Build the master keybinding dictionary from the loaded/default values
        keybindings = new()
        {
            {"Action", (KeyCode)System.Enum.Parse(typeof(KeyCode), actionKey)},
            {"Jump", (KeyCode)System.Enum.Parse(typeof(KeyCode), jumpKey)},
            {"Sprint", (KeyCode)System.Enum.Parse(typeof(KeyCode), sprintKey)},
            {"Crouch", (KeyCode)System.Enum.Parse(typeof(KeyCode), crouchKey)},
            {"Prone", (KeyCode)System.Enum.Parse(typeof(KeyCode), proneKey)},
            {"Pause", (KeyCode)System.Enum.Parse(typeof(KeyCode), pauseKey)},
            {"Grab", (KeyCode)System.Enum.Parse(typeof(KeyCode), grabKey)},
        };

        Debug.Log("Ability names at start: " + abilityOneName + ", " + abilityTwoName + ", " + abilityThreeName + ", " + abilityFourName);

        // Add ability bindings only if the ability names are already present
        if (!string.IsNullOrEmpty(abilityOneName) && !keybindings.ContainsKey(abilityOneName)) keybindings.Add(abilityOneName, abilitySlotKey0);
        if (!string.IsNullOrEmpty(abilityTwoName) && !keybindings.ContainsKey(abilityTwoName)) keybindings.Add(abilityTwoName, abilitySlotKey1);
        if (!string.IsNullOrEmpty(abilityThreeName) && !keybindings.ContainsKey(abilityThreeName)) keybindings.Add(abilityThreeName, abilitySlotKey2);
        if (!string.IsNullOrEmpty(abilityFourName) && !keybindings.ContainsKey(abilityFourName)) keybindings.Add(abilityFourName, abilitySlotKey3);

    }

    // ─── Photon Callbacks ─────────────────────────────────────────────────────

    // Fires on all clients when any player's custom properties change.
    // Used to late-bind ability keys when ability names arrive after Start (common on reconnect).
    public override void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        base.OnPlayerPropertiesUpdate(targetPlayer, changedProps);
        if (!targetPlayer.IsLocal) return;

        // For each ability slot, if the property arrived, add/update keybinding
        TryApplyAbilityProperty("BasicAbility", abilitySlotKey0);
        TryApplyAbilityProperty("QuickAbility", abilitySlotKey1);
        TryApplyAbilityProperty("ThrowAbility", abilitySlotKey2);
        TryApplyAbilityProperty("TrapAbility", abilitySlotKey3);
    }

    private void TryApplyAbilityProperty(string propName, KeyCode slotKey)
    {
        var local = PhotonNetwork.LocalPlayer;
        if (local == null) return;
        var props = local.CustomProperties;
        if (props != null && props.TryGetValue(propName, out object raw))
        {
            if (raw is string abilityName && !string.IsNullOrEmpty(abilityName))
            {
                // Add or overwrite — ability name is the key, slot key code is the value
                if (keybindings.ContainsKey(abilityName))
                    keybindings[abilityName] = slotKey;
                else
                    keybindings.Add(abilityName, slotKey);
            }
        }
    }

    // Reads an ability name from Photon custom properties by property key (e.g. "BasicAbility")
    private string GetAbilityName(string abilityNumber)
    {
        var localPlayer = PhotonNetwork.LocalPlayer;
        if (localPlayer == null)
        {
            Debug.LogError("LocalPlayer is null. Rage quitting.");
            return "";
        }
        var props = localPlayer.CustomProperties;

        if (props != null && props.TryGetValue(abilityNumber, out object rawAbility))
        {
            if (rawAbility is string stringAbility)
                return stringAbility;
            else
                Debug.LogWarning("Can't find ability name in props");
        }
        else
        {
            Debug.LogWarning("Props is null");
        }
        return "";
    }

    // ─── Update ───────────────────────────────────────────────────────────────
    void Update()
    {
        if (!view.IsMine) return;

        // Pause key is always processed first; skip everything else while paused
        var menuManager = PauseMenuManager.Instance;
        if (menuManager != null)
        {
            if (Input.GetKeyDown(keybindings["Pause"]))
                menuManager.TogglePauseMenu();

            if (menuManager.Paused) return;
        }

        MoveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        LookInput = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

        Jump   = Input.GetKeyDown(keybindings["Jump"]);
        Sprint = Input.GetKey(keybindings["Sprint"]);
        Prone  = Input.GetKeyDown(keybindings["Prone"]);
        Crouch = Input.GetKeyDown(keybindings["Crouch"]);
        Grab   = Input.GetKeyDown(keybindings["Grab"]);

        // Held crouch key used by grapple logic (distinct from the tap-to-toggle Crouch bool)
        Down = Input.GetKey(keybindings["Crouch"]);

        // Re-evaluate Idle speed when sprint key changes (walk ↔ sprint transition)
        if (player.currentState == Player.MovementState.Idle)
        {
            if (Input.GetKeyDown(keybindings["Sprint"]) || Input.GetKeyUp(keybindings["Sprint"]))
                player.SetState(Player.MovementState.Idle);
        }

        if (AbilityHandler == null)
        {
            Debug.LogWarning("Cannot find AbilityHandler class");
            return;
        }

        // Action key (LMB by default) confirms/releases the currently active ability
        if (Input.GetKeyDown(keybindings["Action"]))
            AbilityHandler.TryConfirmAction();
        if (Input.GetKeyUp(keybindings["Action"]))
            AbilityHandler.TryConfirmActionUp();

        // Route all non-core keybinding presses to AbilityHandler by ability name
        foreach (var entry in keybindings)
        {
            var actionName = entry.Key;
            var key = entry.Value;

            if (IsCoreBinding(actionName)) continue;
            if (string.IsNullOrEmpty(actionName)) continue;

            if (Input.GetKeyDown(key))
                AbilityHandler.TryUseAbility(actionName, AbilityInputEvent.Down);
            else if (Input.GetKeyUp(key))
                AbilityHandler.TryUseAbility(actionName, AbilityInputEvent.Up);
            else if (Input.GetKey(key))
                AbilityHandler.TryUseAbility(actionName, AbilityInputEvent.Held);
        }
    }

    // ─── Helpers / Public API ─────────────────────────────────────────────────

    // Core bindings are handled in Update directly; all other keys belong to abilities
    private bool IsCoreBinding(string name)
    {
        return name == "Action" || name == "Jump" || name == "Sprint" || name == "Crouch" || name == "Prone" || name == "Pause" || name == "Grab";
    }

    public void RebindKey(string action, KeyCode newKey)
    {
        if (!view.IsMine) return;
        if (keybindings.ContainsKey(action))
        {
            keybindings[action] = newKey;
            Debug.Log($"Rebound {action} to {newKey}");
        }
        else
        {
            keybindings.Add(action, newKey);
            Debug.Log($"Added new binding: {action} to {newKey}");
        }
    }

    // --- Public API: Remove Binding ---
    public void RemoveKeyBinding(string action)
    {
        if (!view.IsMine) return;
        if (keybindings.ContainsKey(action))
        {
            keybindings.Remove(action);
            Debug.Log($"Removed binding for {action}");
        }
        else
        {
            Debug.LogWarning($"No binding found for {action} to remove.");
        }
    }

    // --- Public API: Reset to Default Bindings ---
    public void Resetkeybindings()
    {
        if (!view.IsMine) return;
        keybindings["Jump"] = KeyCode.Space;
        keybindings["Sprint"] = KeyCode.LeftShift;
        keybindings["Crouch"] = KeyCode.LeftControl;
        keybindings["Prone"] = KeyCode.C;
        keybindings["Pause"] = KeyCode.Escape;

        // Remove all other custom bindings
        // To figure out later

        Debug.Log("Key bindings reset to default.");
    }

    // --- Helper: Check if a Key is Already in Use ---
    public bool IsKeyInUse(KeyCode key)
    {
        return keybindings.ContainsValue(key);
    }

    // --- Helper: Get Current Key for an Action ---
    public KeyCode GetKeyForAction(string action)
    {
        if (keybindings.TryGetValue(action, out KeyCode key))
            return key;
        return KeyCode.None;
    }

}
