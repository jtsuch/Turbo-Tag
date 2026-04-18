using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Photon.Pun;
using ExitGames.Client.Photon;

/// <summary>
/// Drives the Rules settings page. All players can read the values, but only the master
/// client (host) can change them. Non-host players see a dimmed overlay that blocks interaction.
///
/// Settings are stored in Photon room custom properties so every client stays in sync via
/// OnRoomPropertiesUpdate. Other systems that need to react to rule changes should also
/// override OnRoomPropertiesUpdate and read from the same property keys (exposed as public
/// constants on this class).
///
/// Gravity is the one exception: it is a local physics-world value, so each client applies
/// it directly when room properties update.
///
/// Attach to: RulesPage panel inside the PauseMenu canvas — requires a PhotonView on the
/// same or parent GameObject (inherited via MonoBehaviourPunCallbacks).
/// </summary>
public class RulesUI : MonoBehaviourPunCallbacks
{
    // ─── Room property keys (read by other systems too) ───────────────────────
    public const string KEY_CHEATS        = "Rule_CheatsEnabled";
    public const string KEY_TIMER_PAUSED  = "Rule_TimerPaused";
    public const string KEY_HIDE_DURATION = "Rule_HideDuration";
    public const string KEY_SEEK_DURATION = "Rule_SeekDuration";
    public const string KEY_GRAVITY       = "Rule_Gravity";

    // ─── Inspector references ─────────────────────────────────────────────────
    [Header("Cheats Toggle")]
    public Button   cheatsButton;
    public TMP_Text cheatsButtonLabel;

    [Header("Timer Toggle")]
    public Button   timerButton;
    public TMP_Text timerButtonLabel;

    [Header("Hide Duration")]
    public Slider         hideDurationSlider;
    public TMP_InputField hideDurationInput;

    [Header("Seek Duration")]
    public Slider         seekDurationSlider;
    public TMP_InputField seekDurationInput;

    [Header("Gravity")]
    public Slider         gravitySlider;
    public TMP_InputField gravityInput;

    [Header("Host Lock")]
    [Tooltip("CanvasGroup covering the controls area. Set non-interactable + dimmed for non-hosts.")]
    public CanvasGroup controlsGroup;

    // ─── Defaults used until room properties are written ─────────────────────
    private const float DEFAULT_HIDE_DURATION =  60f;
    private const float DEFAULT_SEEK_DURATION = 300f;
    private const float DEFAULT_GRAVITY       = -9.81f;

    private bool updatingUI = false;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        RefreshAll();
    }

    // Re-read whenever the panel becomes visible (e.g. switching tabs)
    public override void OnEnable()
    {
        base.OnEnable();
        RefreshAll();
    }

    // ─── Photon callbacks ─────────────────────────────────────────────────────

    public override void OnRoomPropertiesUpdate(Hashtable changedProps)
    {
        RefreshAll();
    }

    // Host rights can transfer when the original host disconnects
    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        ApplyHostLock();
    }

    // ─── Host lock ────────────────────────────────────────────────────────────

    private void ApplyHostLock()
    {
        if (controlsGroup == null) return;
        bool isHost = PhotonNetwork.IsMasterClient;
        controlsGroup.interactable   = isHost;
        controlsGroup.alpha          = isHost ? 1f : 0.45f;
        controlsGroup.blocksRaycasts = isHost;
    }

    // ─── Read room properties → update UI ────────────────────────────────────

    private void RefreshAll()
    {
        ApplyHostLock();
        if (PhotonNetwork.CurrentRoom == null) return;

        updatingUI = true;
        var props = PhotonNetwork.CurrentRoom.CustomProperties;

        // Cheats
        bool cheats = props.TryGetValue(KEY_CHEATS, out object c) && (bool)c;
        cheatsButtonLabel.text = cheats ? "Cheats: ON" : "Cheats: OFF";

        // Timer
        bool timerPaused = props.TryGetValue(KEY_TIMER_PAUSED, out object t) && (bool)t;
        timerButtonLabel.text = timerPaused ? "Timer: Paused" : "Timer: Running";

        // Hide duration
        float hide = props.TryGetValue(KEY_HIDE_DURATION, out object h) ? (float)h : DEFAULT_HIDE_DURATION;
        hideDurationSlider.value = hide;
        hideDurationInput.text   = hide.ToString("F0");

        // Seek duration
        float seek = props.TryGetValue(KEY_SEEK_DURATION, out object s) ? (float)s : DEFAULT_SEEK_DURATION;
        seekDurationSlider.value = seek;
        seekDurationInput.text   = seek.ToString("F0");

        // Gravity — also apply locally so all clients keep physics in sync
        float grav = props.TryGetValue(KEY_GRAVITY, out object g) ? (float)g : DEFAULT_GRAVITY;
        gravitySlider.value  = grav;
        gravityInput.text    = grav.ToString("F2");
        Physics.gravity      = new Vector3(0f, grav, 0f);

        updatingUI = false;
    }

    // ─── Cheats toggle ────────────────────────────────────────────────────────

    public void ToggleCheats()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        var props  = PhotonNetwork.CurrentRoom.CustomProperties;
        bool current = props.TryGetValue(KEY_CHEATS, out object c) && (bool)c;
        WriteRoomProp(KEY_CHEATS, !current);
    }

    // ─── Timer pause / resume ─────────────────────────────────────────────────

    public void ToggleTimer()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        bool current = props.TryGetValue(KEY_TIMER_PAUSED, out object t) && (bool)t;
        WriteRoomProp(KEY_TIMER_PAUSED, !current);
        // TODO: call MatchTimerController.Instance?.Pause() / Resume() once those are exposed
    }

    // ─── Hide duration ────────────────────────────────────────────────────────

    public void SliderHideDuration(float value)
    {
        if (updatingUI || !PhotonNetwork.IsMasterClient) return;
        updatingUI               = true;
        hideDurationInput.text   = value.ToString("F0");
        updatingUI               = false;
        WriteRoomProp(KEY_HIDE_DURATION, value);
    }

    public void InputHideDuration()
    {
        if (updatingUI || !PhotonNetwork.IsMasterClient) return;
        if (!float.TryParse(hideDurationInput.text, out float v)) return;
        updatingUI               = true;
        hideDurationSlider.value = v;
        updatingUI               = false;
        WriteRoomProp(KEY_HIDE_DURATION, v);
    }

    // ─── Seek duration ────────────────────────────────────────────────────────

    public void SliderSeekDuration(float value)
    {
        if (updatingUI || !PhotonNetwork.IsMasterClient) return;
        updatingUI               = true;
        seekDurationInput.text   = value.ToString("F0");
        updatingUI               = false;
        WriteRoomProp(KEY_SEEK_DURATION, value);
    }

    public void InputSeekDuration()
    {
        if (updatingUI || !PhotonNetwork.IsMasterClient) return;
        if (!float.TryParse(seekDurationInput.text, out float v)) return;
        updatingUI               = true;
        seekDurationSlider.value = v;
        updatingUI               = false;
        WriteRoomProp(KEY_SEEK_DURATION, v);
    }

    // ─── Gravity ──────────────────────────────────────────────────────────────

    public void SliderGravity(float value)
    {
        if (updatingUI || !PhotonNetwork.IsMasterClient) return;
        updatingUI          = true;
        gravityInput.text   = value.ToString("F2");
        updatingUI          = false;
        WriteRoomProp(KEY_GRAVITY, value);
        // Other clients apply gravity inside RefreshAll when OnRoomPropertiesUpdate fires
        Physics.gravity = new Vector3(0f, value, 0f);
    }

    public void InputGravity()
    {
        if (updatingUI || !PhotonNetwork.IsMasterClient) return;
        if (!float.TryParse(gravityInput.text, out float v)) return;
        updatingUI          = true;
        gravitySlider.value = v;
        updatingUI          = false;
        WriteRoomProp(KEY_GRAVITY, v);
        Physics.gravity = new Vector3(0f, v, 0f);
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static void WriteRoomProp(string key, object value)
    {
        if (PhotonNetwork.CurrentRoom == null) return;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [key] = value });
    }
}
