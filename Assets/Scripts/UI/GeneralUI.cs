using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Photon.Pun;

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
    [Header("Music Volume")]
    public Slider         musicVolumeSlider;
    public TMP_InputField musicVolumeInput;

    [Header("SFX Volume")]
    public Slider         sfxVolumeSlider;
    public TMP_InputField sfxVolumeInput;

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

    // ─── Session buttons ──────────────────────────────────────────────────────
    [Header("Session")]
    [Tooltip("Returns to the main menu without closing the application.")]
    public Button exitLevelButton;
    [Tooltip("Closes the application entirely.")]
    public Button quitGameButton;

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
        // Wire sliders and inputs programmatically — never breaks on method rename
        musicVolumeSlider.onValueChanged.AddListener(SliderMusicVolume);
        sfxVolumeSlider  .onValueChanged.AddListener(SliderSfxVolume);
        senseSlider      .onValueChanged.AddListener(SliderSense);
        fpsSlider        .onValueChanged.AddListener(SliderFPS);

        musicVolumeInput.onEndEdit.AddListener(_ => InputMusicVolume());
        sfxVolumeInput  .onEndEdit.AddListener(_ => InputSfxVolume());
        senseInput      .onEndEdit.AddListener(_ => InputSense());
        fpsInput        .onEndEdit.AddListener(_ => InputFPS());

        SettingsManager.OnSensitivityChanged += HandleSensitivityChanged;
        SettingsManager.OnMusicVolumeChanged += HandleMusicVolumeChanged;
        SettingsManager.OnSfxVolumeChanged   += HandleSfxVolumeChanged;
        SettingsManager.OnFPSChanged         += HandleFPSChanged;

        // Load saved values — guard updatingUI so listeners don't double-fire
        var sm = SettingsManager.Instance;
        updatingUI = true;
        senseSlider.value        = sm.Sensitivity;
        senseInput.text          = sm.Sensitivity.ToString("F2");
        musicVolumeSlider.value  = sm.MusicVolume * 100f;
        musicVolumeInput.text    = (sm.MusicVolume * 100f).ToString("F0");
        sfxVolumeSlider.value    = sm.SfxVolume * 100f;
        sfxVolumeInput.text      = (sm.SfxVolume * 100f).ToString("F0");
        fpsSlider.value          = sm.TargetFPS;
        fpsInput.text            = sm.TargetFPS.ToString();
        updatingUI = false;

        BuildKeybindList();
        StartCoroutine(ResetScrollToTop());

        if (resetKeybindsButton != null)
            resetKeybindsButton.onClick.AddListener(ResetToDefaults);

        if (exitLevelButton != null)
            exitLevelButton.onClick.AddListener(ExitLevelPressed);
        if (quitGameButton != null)
            quitGameButton.onClick.AddListener(QuitGamePressed);
    }

    void OnDestroy()
    {
        SettingsManager.OnSensitivityChanged -= HandleSensitivityChanged;
        SettingsManager.OnMusicVolumeChanged -= HandleMusicVolumeChanged;
        SettingsManager.OnSfxVolumeChanged   -= HandleSfxVolumeChanged;
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

    private IEnumerator ResetScrollToTop()
    {
        yield return null;
        var sr = GetComponentInParent<ScrollRect>();
        if (sr != null) sr.verticalNormalizedPosition = 1f;
    }

    private void StartListening(KeybindRow row)
    {
        if (listeningRow != null) listeningRow.SetListening(false);
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
    // Sliders and inputs both use 0–100. SettingsManager stores 0–1 internally.

    public void SliderMusicVolume(float value)
    {
        if (updatingUI) return;
        updatingUI = true;
        musicVolumeInput.text = value.ToString("F0");
        updatingUI = false;
        SettingsManager.Instance.MusicVolume = value / 100f;
    }

    public void InputMusicVolume()
    {
        if (updatingUI || !float.TryParse(musicVolumeInput.text, out float v)) return;
        updatingUI = true;
        musicVolumeSlider.value = v;
        updatingUI = false;
        SettingsManager.Instance.MusicVolume = v / 100f;
    }

    private void HandleMusicVolumeChanged(float v)
    {
        updatingUI = true;
        musicVolumeSlider.value = v * 100f;
        musicVolumeInput.text   = (v * 100f).ToString("F0");
        updatingUI = false;
    }

    public void SliderSfxVolume(float value)
    {
        if (updatingUI) return;
        updatingUI = true;
        sfxVolumeInput.text = value.ToString("F0");
        updatingUI = false;
        SettingsManager.Instance.SfxVolume = value / 100f;
    }

    public void InputSfxVolume()
    {
        if (updatingUI || !float.TryParse(sfxVolumeInput.text, out float v)) return;
        updatingUI = true;
        sfxVolumeSlider.value = v;
        updatingUI = false;
        SettingsManager.Instance.SfxVolume = v / 100f;
    }

    private void HandleSfxVolumeChanged(float v)
    {
        updatingUI = true;
        sfxVolumeSlider.value = v * 100f;
        sfxVolumeInput.text   = (v * 100f).ToString("F0");
        updatingUI = false;
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

    // ─── Session buttons ──────────────────────────────────────────────────────

    private bool quittingApp = false;

    private void ExitLevelPressed()
    {
        quittingApp = false;
        PhotonNetwork.LeaveRoom();
    }

    private void QuitGamePressed()
    {
        quittingApp = true;
        PhotonNetwork.LeaveRoom();
    }

    public override void OnLeftRoom()
    {
        if (quittingApp)
        {
#if UNITY_STANDALONE
            Application.Quit();
#endif
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
        else
        {
            PhotonNetwork.LoadLevel("MainMenu");
        }
    }
}
