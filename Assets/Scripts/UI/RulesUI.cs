using ExitGames.Client.Photon;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the Rules settings page. Only the host can edit; non-hosts see a dimmed overlay.
///
/// Time fields (Hide Time, Seek Time) split into separate minute and second integer inputs
/// and are stored as total seconds in room properties.
///
/// Seek Time Limit and Score Limit are opt-in: their toggle enables or disables the rule
/// and unmasks the input fields. When disabled the room property is set to 0 (no limit).
///
/// Percent fields (Hunter Cooldown, Global Cooldown, Gravity) store the integer percentage;
/// GameModeApplicator divides by 100 when applying (e.g. 50 % → × 0.5 of base value).
///
/// Attach to: RulesPage panel inside the PauseMenu canvas.
/// </summary>
public class RulesUI : MonoBehaviourPunCallbacks
{
    // ─── Room property keys ───────────────────────────────────────────────────
    public const string KEY_CHEATS              = "Rule_CheatsEnabled";
    public const string KEY_TIMER_PAUSED        = "Rule_TimerPaused";
    public const string KEY_GAME_MODE           = "GameMode";
    public const string KEY_HIDE_TIME           = "Rule_HideTime";
    public const string KEY_SEEK_TIME           = "Rule_SeekTime";
    public const string KEY_SEEK_TIME_ENABLED   = "Rule_SeekTimeEnabled";
    public const string KEY_MAX_SEEK_TIME       = "Rule_MaxSeekTime";
    public const string KEY_SEEKER_DELAY        = "Rule_SeekerDelay";
    public const string KEY_HUNTER_COUNT        = "Rule_HunterCount";
    public const string KEY_HUNTER_COOLDOWN     = "Rule_HunterCooldown";
    public const string KEY_TAG_COOLDOWN        = "Rule_TagCooldown";
    public const string KEY_GRAVITY             = "Rule_Gravity";
    public const string KEY_EFFECTS_RESPAWN     = "Rule_EffectsRespawn";
    public const string KEY_GLOBAL_COOLDOWN     = "Rule_GlobalCooldown";
    public const string KEY_HUNTED_BONUS        = "Rule_HuntedBonus";
    public const string KEY_SCORE_LIMIT         = "Rule_ScoreLimit";
    public const string KEY_SCORE_LIMIT_ENABLED = "Rule_ScoreLimitEnabled";
    public const string KEY_TEAMS               = "Rule_Teams";
    public const string KEY_ROUND_COUNT         = "Rule_RoundCount";
    public const string KEY_ALLOW_KILLS         = "Rule_AllowKills";
    public const string KEY_PLAYER_HEALTH       = "Rule_PlayerHealth";
    public const string KEY_FALL_DAMAGE         = "Rule_FallDamage";

    // ─── Inspector references ─────────────────────────────────────────────────
    [Header("Action Buttons")]
    [SerializeField] private Button   pauseTimerButton;
    [SerializeField] private TMP_Text pauseTimerLabel;
    [SerializeField] private Button   enableCheatsButton;
    [SerializeField] private TMP_Text enableCheatsLabel;

    [Header("Hide Time (min + sec)")]
    [SerializeField] private TMP_InputField hideTimeMinInput;
    [SerializeField] private TMP_InputField hideTimeSecInput;

    [Header("Seek Time Limit (toggle + min + sec)")]
    [SerializeField] private Toggle         seekTimeToggle;
    [SerializeField] private TMP_InputField seekTimeMinInput;
    [SerializeField] private TMP_InputField seekTimeSecInput;
    [Tooltip("CanvasGroup wrapping only the seek-time input fields — dimmed when toggle is off.")]
    [SerializeField] private CanvasGroup    seekTimeFieldsMask;

    [Header("Round Count")]
    [SerializeField] private TMP_InputField roundCountInput;

    [Header("Starting Hunters")]
    [SerializeField] private TMP_InputField hunterCountInput;

    [Header("Hunter Cooldown (%)")]
    [SerializeField] private TMP_InputField hunterCooldownInput;

    [Header("Global Cooldown (%)")]
    [SerializeField] private TMP_InputField globalCooldownInput;

    [Header("Gravity (%)")]
    [SerializeField] private TMP_InputField gravityInput;

    [Header("Effects Respawn Rate (sec)")]
    [SerializeField] private TMP_InputField effectsRespawnInput;

    [Header("Tag Bonus (sec, can be negative)")]
    [SerializeField] private TMP_InputField taggingBonusInput;

    [Header("Score Limit (toggle + value)")]
    [SerializeField] private Toggle         scoreLimitToggle;
    [SerializeField] private TMP_InputField scoreLimitInput;
    [Tooltip("CanvasGroup wrapping only the score-limit input field — dimmed when toggle is off.")]
    [SerializeField] private CanvasGroup    scoreLimitFieldsMask;

    [Header("Host Lock Overlay")]
    [Tooltip("CanvasGroup covering the whole panel. Add a LayoutElement (Ignore Layout) so it sits outside the layout group.")]
    [SerializeField] private CanvasGroup hostLockOverlay;

    // ─── Defaults ─────────────────────────────────────────────────────────────
    private const int DEF_HIDE_TIME_SEC    = 120;   // 2 min
    private const int DEF_SEEK_TIME_SEC    = 900;   // 15 min
    private const int DEF_ROUND_COUNT      =   3;
    private const int DEF_HUNTER_COUNT     =   1;
    private const int DEF_HUNTER_COOLDOWN  =   0;   // %
    private const int DEF_GLOBAL_COOLDOWN  = 100;   // %
    private const int DEF_GRAVITY          = 100;   // %
    private const int DEF_EFFECTS_RESPAWN  =  30;   // sec
    private const int DEF_TAG_BONUS        =   0;   // sec (can be negative)
    private const int DEF_SCORE_LIMIT      =   0;

    private bool updatingUI;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        // Action buttons
        if (pauseTimerButton   != null) pauseTimerButton  .onClick.AddListener(TogglePauseTimer);
        if (enableCheatsButton != null) enableCheatsButton.onClick.AddListener(ToggleCheats);

        // Hide Time
        if (hideTimeMinInput != null) hideTimeMinInput.onEndEdit.AddListener(_ => CommitHideTime());
        if (hideTimeSecInput != null) hideTimeSecInput.onEndEdit.AddListener(_ => CommitHideTime());

        // Seek Time (toggle + fields)
        if (seekTimeToggle   != null) seekTimeToggle  .onValueChanged.AddListener(OnSeekTimeToggle);
        if (seekTimeMinInput != null) seekTimeMinInput.onEndEdit.AddListener(_ => CommitSeekTime());
        if (seekTimeSecInput != null) seekTimeSecInput.onEndEdit.AddListener(_ => CommitSeekTime());

        // Simple integer inputs
        if (roundCountInput     != null) roundCountInput    .onEndEdit.AddListener(_ => CommitInt(roundCountInput,     KEY_ROUND_COUNT));
        if (hunterCountInput    != null) hunterCountInput   .onEndEdit.AddListener(_ => CommitInt(hunterCountInput,    KEY_HUNTER_COUNT));
        if (hunterCooldownInput != null) hunterCooldownInput.onEndEdit.AddListener(_ => CommitInt(hunterCooldownInput, KEY_HUNTER_COOLDOWN));
        if (globalCooldownInput != null) globalCooldownInput.onEndEdit.AddListener(_ => CommitInt(globalCooldownInput, KEY_GLOBAL_COOLDOWN));
        if (gravityInput        != null) gravityInput       .onEndEdit.AddListener(_ => CommitInt(gravityInput,        KEY_GRAVITY));
        if (effectsRespawnInput != null) effectsRespawnInput.onEndEdit.AddListener(_ => CommitInt(effectsRespawnInput, KEY_EFFECTS_RESPAWN));
        if (taggingBonusInput   != null) taggingBonusInput  .onEndEdit.AddListener(_ => CommitInt(taggingBonusInput,   KEY_HUNTED_BONUS));

        // Score Limit (toggle + field)
        if (scoreLimitToggle != null) scoreLimitToggle.onValueChanged.AddListener(OnScoreLimitToggle);
        if (scoreLimitInput  != null) scoreLimitInput .onEndEdit.AddListener(_ => CommitInt(scoreLimitInput, KEY_SCORE_LIMIT));

        RefreshAll();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        if (PhotonNetwork.CurrentRoom != null)
            RefreshAll();
    }

    // ─── Photon callbacks ─────────────────────────────────────────────────────

    public override void OnRoomPropertiesUpdate(Hashtable changedProps)
    {
        RefreshAll();
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        ApplyHostLock();
    }

    // ─── Host lock ────────────────────────────────────────────────────────────

    private void ApplyHostLock()
    {
        if (hostLockOverlay == null) return;
        bool isHost = PhotonNetwork.IsMasterClient;
        hostLockOverlay.interactable   = isHost;
        hostLockOverlay.alpha          = isHost ? 0f : 0.5f;
        hostLockOverlay.blocksRaycasts = !isHost;
    }

    // ─── Full refresh ─────────────────────────────────────────────────────────

    private void RefreshAll()
    {
        ApplyHostLock();
        if (PhotonNetwork.CurrentRoom == null) return;

        updatingUI = true;
        var p = PhotonNetwork.CurrentRoom.CustomProperties;

        // Action button labels
        bool timerPaused = BoolProp(p, KEY_TIMER_PAUSED);
        if (pauseTimerLabel   != null) pauseTimerLabel  .text = timerPaused ? "Resume Timer" : "Pause Timer";
        bool cheatsOn = BoolProp(p, KEY_CHEATS);
        if (enableCheatsLabel != null) enableCheatsLabel.text = cheatsOn ? "Disable Cheats" : "Enable Cheats";

        // Hide Time (stored as total seconds)
        SplitSeconds((int)FloatProp(p, KEY_HIDE_TIME, DEF_HIDE_TIME_SEC),
                     out int hideMin, out int hideSec);
        SetIntInput(hideTimeMinInput, hideMin);
        SetIntInput(hideTimeSecInput, hideSec);

        // Seek Time Limit
        bool seekEnabled = BoolProp(p, KEY_SEEK_TIME_ENABLED);
        if (seekTimeToggle != null) seekTimeToggle.isOn = seekEnabled;
        ApplyFieldMask(seekTimeFieldsMask, seekEnabled);
        SplitSeconds((int)FloatProp(p, KEY_SEEK_TIME, DEF_SEEK_TIME_SEC),
                     out int seekMin, out int seekSec);
        SetIntInput(seekTimeMinInput, seekMin);
        SetIntInput(seekTimeSecInput, seekSec);

        // Integers
        SetIntInput(roundCountInput,     (int)FloatProp(p, KEY_ROUND_COUNT,     DEF_ROUND_COUNT));
        SetIntInput(hunterCountInput,    (int)FloatProp(p, KEY_HUNTER_COUNT,    DEF_HUNTER_COUNT));
        SetIntInput(hunterCooldownInput, (int)FloatProp(p, KEY_HUNTER_COOLDOWN, DEF_HUNTER_COOLDOWN));
        SetIntInput(globalCooldownInput, (int)FloatProp(p, KEY_GLOBAL_COOLDOWN, DEF_GLOBAL_COOLDOWN));
        SetIntInput(gravityInput,        (int)FloatProp(p, KEY_GRAVITY,         DEF_GRAVITY));
        SetIntInput(effectsRespawnInput, (int)FloatProp(p, KEY_EFFECTS_RESPAWN, DEF_EFFECTS_RESPAWN));
        SetIntInput(taggingBonusInput,   (int)FloatProp(p, KEY_HUNTED_BONUS,    DEF_TAG_BONUS));

        // Score Limit
        bool scoreEnabled = BoolProp(p, KEY_SCORE_LIMIT_ENABLED);
        if (scoreLimitToggle != null) scoreLimitToggle.isOn = scoreEnabled;
        ApplyFieldMask(scoreLimitFieldsMask, scoreEnabled);
        SetIntInput(scoreLimitInput, (int)FloatProp(p, KEY_SCORE_LIMIT, DEF_SCORE_LIMIT));

        updatingUI = false;
    }

    // ─── Action button callbacks ──────────────────────────────────────────────

    private void TogglePauseTimer()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        bool current = BoolPropFromRoom(KEY_TIMER_PAUSED);
        WriteRoomProp(KEY_TIMER_PAUSED, !current);
        // MatchTimerController should subscribe to OnRoomPropertiesUpdate and read KEY_TIMER_PAUSED
    }

    private void ToggleCheats()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        bool current = BoolPropFromRoom(KEY_CHEATS);
        WriteRoomProp(KEY_CHEATS, !current);
    }

    // ─── Toggle callbacks ─────────────────────────────────────────────────────

    private void OnSeekTimeToggle(bool enabled)
    {
        if (updatingUI) return;
        ApplyFieldMask(seekTimeFieldsMask, enabled);
        WriteRoomProp(KEY_SEEK_TIME_ENABLED, enabled);
        // Also write 0 when disabling so other systems read "no limit"
        if (!enabled) WriteRoomProp(KEY_SEEK_TIME, 0f);
    }

    private void OnScoreLimitToggle(bool enabled)
    {
        if (updatingUI) return;
        ApplyFieldMask(scoreLimitFieldsMask, enabled);
        WriteRoomProp(KEY_SCORE_LIMIT_ENABLED, enabled);
        if (!enabled) WriteRoomProp(KEY_SCORE_LIMIT, 0f);
    }

    // ─── Time field commits ───────────────────────────────────────────────────

    private void CommitHideTime()
    {
        if (updatingUI || !PhotonNetwork.IsMasterClient) return;
        int min = ParseInt(hideTimeMinInput, 0);
        int sec = Mathf.Clamp(ParseInt(hideTimeSecInput, 0), 0, 59);
        SetIntInput(hideTimeSecInput, sec);          // clamp-correct the display
        WriteRoomProp(KEY_HIDE_TIME, (float)(min * 60 + sec));
    }

    private void CommitSeekTime()
    {
        if (updatingUI || !PhotonNetwork.IsMasterClient) return;
        if (seekTimeToggle != null && !seekTimeToggle.isOn) return;
        int min = ParseInt(seekTimeMinInput, 0);
        int sec = Mathf.Clamp(ParseInt(seekTimeSecInput, 0), 0, 59);
        SetIntInput(seekTimeSecInput, sec);
        WriteRoomProp(KEY_SEEK_TIME, (float)(min * 60 + sec));
    }

    // ─── Generic integer commit ───────────────────────────────────────────────

    private void CommitInt(TMP_InputField field, string key)
    {
        if (updatingUI || !PhotonNetwork.IsMasterClient) return;
        if (!int.TryParse(field.text, out int v)) return;
        WriteRoomProp(key, (float)v);
    }

    // ─── Utilities ────────────────────────────────────────────────────────────

    private static void ApplyFieldMask(CanvasGroup group, bool active)
    {
        if (group == null) return;
        group.alpha          = active ? 1f : 0.3f;
        group.interactable   = active;
        group.blocksRaycasts = active;
    }

    private static void SplitSeconds(int totalSeconds, out int minutes, out int seconds)
    {
        totalSeconds = Mathf.Max(0, totalSeconds);
        minutes = totalSeconds / 60;
        seconds = totalSeconds % 60;
    }

    private static void SetIntInput(TMP_InputField field, int value)
    {
        if (field != null) field.text = value.ToString();
    }

    private static int ParseInt(TMP_InputField field, int fallback) =>
        field != null && int.TryParse(field.text, out int v) ? v : fallback;

    private static float FloatProp(Hashtable p, string key, float def) =>
        p.TryGetValue(key, out object v) && v is float f ? f : def;

    private static bool BoolProp(Hashtable p, string key) =>
        p.TryGetValue(key, out object v) && v is bool b && b;

    private static bool BoolPropFromRoom(string key)
    {
        if (PhotonNetwork.CurrentRoom == null) return false;
        return BoolProp(PhotonNetwork.CurrentRoom.CustomProperties, key);
    }

    private static void WriteRoomProp(string key, object value)
    {
        if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.IsMasterClient) return;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [key] = value });
    }
}
