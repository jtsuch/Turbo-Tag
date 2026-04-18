using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Photon.Pun;
using System.Collections.Generic;

/// <summary>
/// Drives the General settings page: sensitivity / volume / FPS sliders plus a runtime-built
/// keybind list for all core actions and the four ability slots.
///
/// Keybind logic:
///   - Clicking a row's key button enters "listening" mode; the next key pressed becomes the bind.
///   - If that key is already bound to another row, the conflicting row is cleared first.
///   - KeyCode.Escape is never rebindable (it is reserved for toggling the pause menu).
///   - Changes are immediately pushed to the live InputHandler and saved to PlayerPrefs.
///
/// Attach to: GeneralPage panel inside the PauseMenu canvas.
/// </summary>
public class GeneralUI : MonoBehaviourPunCallbacks
{
    // ─── Sensitivity ──────────────────────────────────────────────────────────
    [Header("Sensitivity")]
    public Slider         senseSlider;
    public TMP_InputField senseInput;

    // ─── Volume ───────────────────────────────────────────────────────────────
    [Header("Volume")]
    public Slider         volumeSlider;
    public TMP_InputField volumeInput;

    // ─── FPS ──────────────────────────────────────────────────────────────────
    [Header("FPS")]
    public Slider         fpsSlider;
    public TMP_InputField fpsInput;

    // ─── Keybinds ─────────────────────────────────────────────────────────────
    [Header("Keybinds")]
    [Tooltip("Prefab with a KeybindRow component — Label | KeyButton | RemoveButton layout.")]
    public GameObject keybindRowPrefab;
    [Tooltip("Parent transform (e.g. a Vertical Layout Group) where rows are spawned.")]
    public Transform  keybindContainer;
    [Tooltip("Resets all keybinds to their defaults.")]
    public Button     resetKeybindsButton;

    // ─── Internal state ───────────────────────────────────────────────────────
    private bool       updatingUI          = false;
    private KeybindRow listeningRow        = null;
    private bool       skipFrameAfterClick = false;   // Avoids capturing the mouse-click that started listening
    private readonly List<KeybindRow> rows = new();

    // Definition table: (display label, internal action name, PlayerPrefs key, default key)
    // Ability slots use "SLOT_X" as their action name — resolved to the actual ability name at rebind time.
    private static readonly (string display, string action, string prefsKey, KeyCode def)[] KeybindDefs =
    {
        ("Action",    "Action",  "Keybind_Action",   KeyCode.Mouse0      ),
        ("Jump",      "Jump",    "Keybind_Jump",     KeyCode.Space       ),
        ("Sprint",    "Sprint",  "Keybind_Sprint",   KeyCode.LeftShift   ),
        ("Crouch",    "Crouch",  "Keybind_Crouch",   KeyCode.LeftControl ),
        ("Prone",     "Prone",   "Keybind_Prone",    KeyCode.C           ),
        ("Grab",      "Grab",    "Keybind_Grab",     KeyCode.F           ),
        ("Ability 1", "SLOT_0",  "Keybind_Ability0", KeyCode.Mouse1      ),
        ("Ability 2", "SLOT_1",  "Keybind_Ability1", KeyCode.E           ),
        ("Ability 3", "SLOT_2",  "Keybind_Ability2", KeyCode.Q           ),
        ("Ability 4", "SLOT_3",  "Keybind_Ability3", KeyCode.X           ),
    };

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        var sm = SettingsManager.Instance;
        senseSlider.value  = sm.Sensitivity;
        senseInput.text    = sm.Sensitivity.ToString("F2");
        volumeSlider.value = sm.Volume;
        volumeInput.text   = sm.Volume.ToString("F2");
        fpsSlider.value    = sm.TargetFPS;
        fpsInput.text      = sm.TargetFPS.ToString();

        SettingsManager.OnSensitivityChanged += HandleSensitivityChanged;
        SettingsManager.OnVolumeChanged      += HandleVolumeChanged;
        SettingsManager.OnFPSChanged         += HandleFPSChanged;

        BuildKeybindList();

        if (resetKeybindsButton != null)
            resetKeybindsButton.onClick.AddListener(ResetToDefaults);
    }

    void OnDestroy()
    {
        SettingsManager.OnSensitivityChanged -= HandleSensitivityChanged;
        SettingsManager.OnVolumeChanged      -= HandleVolumeChanged;
        SettingsManager.OnFPSChanged         -= HandleFPSChanged;
    }

    void Update()
    {
        if (listeningRow == null) return;

        // Skip the frame that triggered listening so we don't capture the click itself
        if (skipFrameAfterClick)
        {
            skipFrameAfterClick = false;
            return;
        }

        foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
        {
            if (kc == KeyCode.Escape) continue;   // Escape is reserved for the pause toggle
            if (!Input.GetKeyDown(kc)) continue;

            CommitBinding(listeningRow, kc);
            listeningRow.SetListening(false);
            listeningRow = null;
            return;
        }
    }

    // ─── Keybind list ─────────────────────────────────────────────────────────

    private void BuildKeybindList()
    {
        if (keybindRowPrefab == null || keybindContainer == null) return;

        foreach (var (display, action, prefsKey, def) in KeybindDefs)
        {
            KeyCode loaded = TryParseKeyCode(PlayerPrefs.GetString(prefsKey, def.ToString()));

            var row = Instantiate(keybindRowPrefab, keybindContainer).GetComponent<KeybindRow>();
            row.Initialize(display, action, prefsKey, loaded);
            row.OnListenRequested += StartListening;
            row.OnRemoveRequested += RemoveBinding;
            rows.Add(row);
        }
    }

    private void StartListening(KeybindRow row)
    {
        listeningRow?.SetListening(false);
        listeningRow        = row;
        skipFrameAfterClick = true;
        row.SetListening(true);

        // Deselect current UI element so Space/Enter aren't intercepted by EventSystem
        UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
    }

    private void CommitBinding(KeybindRow row, KeyCode key)
    {
        // Clear any other row already using this key
        foreach (var other in rows)
        {
            if (other == row || other.CurrentKey != key) continue;
            other.ClearKey();
            PlayerPrefs.SetString(other.PlayerPrefsKey, KeyCode.None.ToString());
            PushToInputHandler(other.ActionName, KeyCode.None);
        }

        row.SetKey(key);
        PlayerPrefs.SetString(row.PlayerPrefsKey, key.ToString());
        PushToInputHandler(row.ActionName, key);
        PlayerPrefs.Save();
    }

    private void RemoveBinding(KeybindRow row)
    {
        row.ClearKey();
        PlayerPrefs.SetString(row.PlayerPrefsKey, KeyCode.None.ToString());
        PushToInputHandler(row.ActionName, KeyCode.None);
        PlayerPrefs.Save();
    }

    private void ResetToDefaults()
    {
        // Cancel any active listen
        if (listeningRow != null)
        {
            listeningRow.SetListening(false);
            listeningRow = null;
        }

        foreach (var (_, action, prefsKey, def) in KeybindDefs)
        {
            var row = rows.Find(r => r.PlayerPrefsKey == prefsKey);
            if (row == null) continue;

            // Clear any other row that currently holds the default key
            foreach (var other in rows)
            {
                if (other.PlayerPrefsKey == prefsKey || other.CurrentKey != def) continue;
                other.ClearKey();
                PlayerPrefs.SetString(other.PlayerPrefsKey, KeyCode.None.ToString());
                PushToInputHandler(other.ActionName, KeyCode.None);
            }

            row.SetKey(def);
            PlayerPrefs.SetString(prefsKey, def.ToString());
            PushToInputHandler(action, def);
        }

        PlayerPrefs.Save();
    }

    /// <summary>
    /// Resolves SLOT_X to the real ability name then calls InputHandler.RebindKey / RemoveKeyBinding.
    /// </summary>
    private void PushToInputHandler(string actionName, KeyCode key)
    {
        var ih = Player.Instance != null ? Player.Instance.GetComponent<InputHandler>() : null;
        if (ih == null) return;

        string resolved = actionName;
        if (actionName.StartsWith("SLOT_"))
        {
            int idx = int.Parse(actionName["SLOT_".Length..]);
            resolved = (Player.Instance.abilityList != null && idx < Player.Instance.abilityList.Length)
                ? Player.Instance.abilityList[idx]
                : null;
            if (string.IsNullOrEmpty(resolved)) return;
        }

        if (key == KeyCode.None)
            ih.RemoveKeyBinding(resolved);
        else
            ih.RebindKey(resolved, key);
    }

    private static KeyCode TryParseKeyCode(string s) =>
        System.Enum.TryParse(s, out KeyCode kc) ? kc : KeyCode.None;

    // ─── Sensitivity callbacks ────────────────────────────────────────────────

    public void SliderSense(float value)
    {
        if (updatingUI) return;
        updatingUI      = true;
        senseInput.text = value.ToString("F2");
        updatingUI      = false;
        SettingsManager.Instance.Sensitivity = value;
    }

    public void InputSense()
    {
        if (updatingUI || !float.TryParse(senseInput.text, out float v)) return;
        updatingUI        = true;
        senseSlider.value = v;
        updatingUI        = false;
        SettingsManager.Instance.Sensitivity = v;
    }

    private void HandleSensitivityChanged(float v)
    {
        updatingUI        = true;
        senseSlider.value = v;
        senseInput.text   = v.ToString("F2");
        updatingUI        = false;
    }

    // ─── Volume callbacks ─────────────────────────────────────────────────────

    public void SliderVolume(float value)
    {
        if (updatingUI) return;
        updatingUI         = true;
        volumeInput.text   = value.ToString("F2");
        updatingUI         = false;
        SettingsManager.Instance.Volume = value;
    }

    public void InputVolume()
    {
        if (updatingUI || !float.TryParse(volumeInput.text, out float v)) return;
        updatingUI         = true;
        volumeSlider.value = v;
        updatingUI         = false;
        SettingsManager.Instance.Volume = v;
    }

    private void HandleVolumeChanged(float v)
    {
        updatingUI         = true;
        volumeSlider.value = v;
        volumeInput.text   = v.ToString("F2");
        updatingUI         = false;
    }

    // ─── FPS callbacks ────────────────────────────────────────────────────────

    public void SliderFPS(float value)
    {
        if (updatingUI) return;
        updatingUI      = true;
        fpsInput.text   = value.ToString("F0");
        updatingUI      = false;
        SettingsManager.Instance.TargetFPS = (int)value;
    }

    public void InputFPS()
    {
        if (updatingUI || !int.TryParse(fpsInput.text, out int v)) return;
        updatingUI      = true;
        fpsSlider.value = v;
        updatingUI      = false;
        SettingsManager.Instance.TargetFPS = v;
    }

    private void HandleFPSChanged(int v)
    {
        updatingUI      = true;
        fpsSlider.value = v;
        fpsInput.text   = v.ToString();
        updatingUI      = false;
    }

    // ─── Quit ─────────────────────────────────────────────────────────────────

    public void QuitButtonPress()
    {
        Debug.Log("Quitting game...");
        PhotonNetwork.LeaveRoom();
    }

    public override void OnLeftRoom()
    {
#if UNITY_STANDALONE
        Application.Quit();
#endif
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
