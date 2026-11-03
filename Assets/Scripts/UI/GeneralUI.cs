using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// General tab of the pause menu: sensitivity, volume and FPS settings, the keybind list, and
/// leave/quit buttons. Settings go through SettingsManager; keybinds through Keybinds, which
/// the live InputHandler and HUD read directly — no manual syncing needed.
/// Attach to: the GeneralPage panel inside the PauseMenu.
/// </summary>
public class GeneralUI : MonoBehaviourPunCallbacks
{
    [Header("Sensitivity")]
    [SerializeField] private Slider         senseSlider;
    [SerializeField] private TMP_InputField senseInput;

    [Header("Music Volume (0–100)")]
    [SerializeField] private Slider         musicVolumeSlider;
    [SerializeField] private TMP_InputField musicVolumeInput;

    [Header("SFX Volume (0–100)")]
    [SerializeField] private Slider         sfxVolumeSlider;
    [SerializeField] private TMP_InputField sfxVolumeInput;

    [Header("FPS")]
    [SerializeField] private Slider         fpsSlider;
    [SerializeField] private TMP_InputField fpsInput;

    [Header("Keybinds")]
    [Tooltip("Prefab with a KeybindRow component.")]
    [SerializeField] private GameObject keybindRowPrefab;
    [SerializeField] private Transform  keybindContainer;
    [SerializeField] private Button     resetKeybindsButton;

    [Header("Session")]
    [SerializeField] private Button exitLevelButton;
    [SerializeField] private Button quitGameButton;

    private static readonly (string label, InputAction action)[] KeybindRows =
    {
        ("Action",    InputAction.Action),
        ("Jump",      InputAction.Jump),
        ("Sprint",    InputAction.Sprint),
        ("Crouch",    InputAction.Crouch),
        ("Prone",     InputAction.Prone),
        ("Grab",      InputAction.Grab),
        ("Ability 1", InputAction.AbilityBasic),
        ("Ability 2", InputAction.AbilityQuick),
        ("Ability 3", InputAction.AbilityThrow),
        ("Ability 4", InputAction.AbilityTrap),
    };

    private readonly List<KeybindRow> rows     = new();
    private readonly KeyListener      listener = new();
    private KeybindRow listeningRow;
    private bool       quittingApp;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        var settings = SettingsManager.Instance;
        if (settings != null)
        {
            new SliderInputPair(senseSlider, senseInput, "F0", settings.Sensitivity, v => settings.Sensitivity = v);
            new SliderInputPair(musicVolumeSlider, musicVolumeInput, "F0", settings.MusicVolume * 100f, v => settings.MusicVolume = v / 100f);
            new SliderInputPair(sfxVolumeSlider, sfxVolumeInput, "F0", settings.SfxVolume * 100f, v => settings.SfxVolume = v / 100f);
            new SliderInputPair(fpsSlider, fpsInput, "F0", settings.TargetFps, v => settings.TargetFps = Mathf.RoundToInt(v));
        }
        else
        {
            Debug.LogWarning("[GeneralUI] SettingsManager missing — start the game from the Loading scene to edit settings.");
        }

        BuildKeybindList();
        StartCoroutine(ResetScrollToTop());

        if (resetKeybindsButton != null) resetKeybindsButton.onClick.AddListener(ResetToDefaults);
        if (exitLevelButton     != null) exitLevelButton.onClick.AddListener(() => Leave(quitApp: false));
        if (quitGameButton      != null) quitGameButton.onClick.AddListener(() => Leave(quitApp: true));
    }

    private void Update()
    {
        if (listeningRow == null || !listener.Poll(out KeyCode key)) return;
        Commit(listeningRow, key);
        listeningRow = null;
    }

    // ─── Keybinds ─────────────────────────────────────────────────────────────

    private void BuildKeybindList()
    {
        if (keybindRowPrefab == null || keybindContainer == null) return;

        foreach (var (label, action) in KeybindRows)
        {
            var row = Instantiate(keybindRowPrefab, keybindContainer).GetComponent<KeybindRow>();
            row.Initialize(label, action, Keybinds.Get(action));
            row.ListenRequested += StartListening;
            row.RemoveRequested += r => Commit(r, KeyCode.None);
            rows.Add(row);
        }
    }

    private IEnumerator ResetScrollToTop()
    {
        yield return null;
        var scroll = GetComponentInParent<ScrollRect>();
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }

    private void StartListening(KeybindRow row)
    {
        listeningRow?.SetListening(false);
        listeningRow = row;
        row.SetListening(true);
        listener.Start();
    }

    // A key can only drive one action, so any other row using it is cleared
    private void Commit(KeybindRow row, KeyCode key)
    {
        if (key != KeyCode.None)
        {
            foreach (var other in rows)
            {
                if (other == row || other.CurrentKey != key) continue;
                Keybinds.Set(other.Action, KeyCode.None);
                other.SetKey(KeyCode.None);
            }
        }

        Keybinds.Set(row.Action, key);
        row.SetKey(key);
    }

    private void ResetToDefaults()
    {
        listener.Stop();
        listeningRow = null;
        Keybinds.ResetAll();
        foreach (var row in rows) row.SetKey(Keybinds.Get(row.Action));
    }

    // ─── Session ──────────────────────────────────────────────────────────────

    private void Leave(bool quitApp)
    {
        quittingApp = quitApp;
        if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
        else                      OnLeftRoom();
    }

    public override void OnLeftRoom()
    {
        if (!quittingApp)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
            return;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
