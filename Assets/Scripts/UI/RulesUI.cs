using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Rules tab of the pause menu. The host edits match rules live; everyone else sees them
/// read-only under a dimmed overlay. Values are written to room properties and read back
/// through <see cref="MatchRules"/>, so the displayed values always match what the game uses.
///
/// Times are entered as minutes + seconds and stored as total seconds. Percent fields store
/// the integer percentage. Opt-in limits (seek time, score) have a toggle that greys out
/// their inputs.
/// Attach to: the RulesPage panel inside the PauseMenu.
/// </summary>
public class RulesUI : MonoBehaviourPunCallbacks
{
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
    [SerializeField] private CanvasGroup    seekTimeFieldsMask;

    [Header("Simple Values")]
    [SerializeField] private TMP_InputField roundCountInput;
    [SerializeField] private TMP_InputField hunterCountInput;
    [SerializeField] private TMP_InputField hunterCooldownInput;
    [SerializeField] private TMP_InputField globalCooldownInput;
    [SerializeField] private TMP_InputField gravityInput;
    [SerializeField] private TMP_InputField effectsRespawnInput;
    [SerializeField] private TMP_InputField taggingBonusInput;

    [Header("Score Limit (toggle + value)")]
    [SerializeField] private Toggle         scoreLimitToggle;
    [SerializeField] private TMP_InputField scoreLimitInput;
    [SerializeField] private CanvasGroup    scoreLimitFieldsMask;

    [Header("Host Lock Overlay")]
    [Tooltip("CanvasGroup covering the panel; blocks input for non-hosts.")]
    [SerializeField] private CanvasGroup hostLockOverlay;

    private bool refreshing;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        if (pauseTimerButton   != null) pauseTimerButton.onClick.AddListener(() => ToggleRule(RuleKeys.TimerPaused));
        if (enableCheatsButton != null) enableCheatsButton.onClick.AddListener(() => ToggleRule(RuleKeys.CheatsEnabled));

        OnEndEdit(hideTimeMinInput, CommitHideTime);
        OnEndEdit(hideTimeSecInput, CommitHideTime);
        OnEndEdit(seekTimeMinInput, CommitSeekTime);
        OnEndEdit(seekTimeSecInput, CommitSeekTime);

        OnEndEdit(roundCountInput,     () => CommitNumber(roundCountInput,     RuleKeys.RoundCount,     1));
        OnEndEdit(hunterCountInput,    () => CommitNumber(hunterCountInput,    RuleKeys.HunterCount,    1));
        OnEndEdit(hunterCooldownInput, () => CommitNumber(hunterCooldownInput, RuleKeys.HunterCooldown, 0, 100));
        OnEndEdit(globalCooldownInput, () => CommitNumber(globalCooldownInput, RuleKeys.GlobalCooldown, 0));
        OnEndEdit(gravityInput,        () => CommitNumber(gravityInput,        RuleKeys.Gravity,        1));
        OnEndEdit(effectsRespawnInput, () => CommitNumber(effectsRespawnInput, RuleKeys.EffectsRespawn, 0));
        OnEndEdit(taggingBonusInput,   () => CommitNumber(taggingBonusInput,   RuleKeys.TagBonus,       int.MinValue));
        OnEndEdit(scoreLimitInput,     () => CommitNumber(scoreLimitInput,     RuleKeys.ScoreLimit,     1));

        if (seekTimeToggle   != null) seekTimeToggle.onValueChanged.AddListener(on => CommitToggle(RuleKeys.SeekTimeEnabled, on));
        if (scoreLimitToggle != null) scoreLimitToggle.onValueChanged.AddListener(on => CommitToggle(RuleKeys.ScoreLimitEnabled, on));

        Refresh();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        if (PhotonNetwork.InRoom) Refresh();
    }

    public override void OnRoomPropertiesUpdate(Hashtable changed) => Refresh();
    public override void OnMasterClientSwitched(Photon.Realtime.Player newMaster) => Refresh();

    // ─── Display ──────────────────────────────────────────────────────────────

    private void Refresh()
    {
        ApplyHostLock();
        if (!PhotonNetwork.InRoom) return;

        refreshing = true;

        if (pauseTimerLabel   != null) pauseTimerLabel.text   = MatchRules.TimerPaused   ? "Resume Timer"   : "Pause Timer";
        if (enableCheatsLabel != null) enableCheatsLabel.text = MatchRules.CheatsEnabled ? "Disable Cheats" : "Enable Cheats";

        SetTime(hideTimeMinInput, hideTimeSecInput, MatchRules.HideTime);

        bool seekEnabled = MatchRules.SeekTimeEnabled;
        if (seekTimeToggle != null) seekTimeToggle.SetIsOnWithoutNotify(seekEnabled);
        SetMask(seekTimeFieldsMask, seekEnabled);
        SetTime(seekTimeMinInput, seekTimeSecInput, NetProps.Room(RuleKeys.SeekTime, MatchRules.DefaultSeekTime));

        SetNumber(roundCountInput,     MatchRules.RoundCount);
        SetNumber(hunterCountInput,    MatchRules.HunterCount);
        SetNumber(hunterCooldownInput, NetProps.Room(RuleKeys.HunterCooldown, MatchRules.DefaultHunterCooldown));
        SetNumber(globalCooldownInput, NetProps.Room(RuleKeys.GlobalCooldown, MatchRules.DefaultGlobalCooldown));
        SetNumber(gravityInput,        NetProps.Room(RuleKeys.Gravity,        MatchRules.DefaultGravity));
        SetNumber(effectsRespawnInput, MatchRules.EffectsRespawn);
        SetNumber(taggingBonusInput,   MatchRules.TagBonus);

        bool scoreEnabled = MatchRules.ScoreLimitEnabled;
        if (scoreLimitToggle != null) scoreLimitToggle.SetIsOnWithoutNotify(scoreEnabled);
        SetMask(scoreLimitFieldsMask, scoreEnabled);
        SetNumber(scoreLimitInput, NetProps.Room(RuleKeys.ScoreLimit, MatchRules.DefaultScoreLimit));

        refreshing = false;
    }

    private void ApplyHostLock()
    {
        if (hostLockOverlay == null) return;
        bool isHost = PhotonNetwork.IsMasterClient;
        hostLockOverlay.alpha          = isHost ? 0f : 0.5f;
        hostLockOverlay.interactable   = isHost;
        hostLockOverlay.blocksRaycasts = !isHost;
    }

    // ─── Commits (host only) ──────────────────────────────────────────────────

    private bool CanEdit => !refreshing && PhotonNetwork.IsMasterClient;

    private void ToggleRule(string key)
    {
        if (CanEdit) NetProps.SetRoom(key, !NetProps.Room(key, false));
    }

    private void CommitToggle(string key, bool value)
    {
        if (CanEdit) NetProps.SetRoom(key, value);
    }

    private void CommitHideTime()
    {
        if (CanEdit) NetProps.SetRoom(RuleKeys.HideTime, ReadTime(hideTimeMinInput, hideTimeSecInput));
    }

    private void CommitSeekTime()
    {
        if (CanEdit) NetProps.SetRoom(RuleKeys.SeekTime, Mathf.Max(1f, ReadTime(seekTimeMinInput, seekTimeSecInput)));
    }

    private void CommitNumber(TMP_InputField field, string key, int min, int max = int.MaxValue)
    {
        if (!CanEdit) return;
        if (!int.TryParse(field.text, out int value))
        {
            Refresh(); // Restore the current value
            return;
        }
        NetProps.SetRoom(key, (float)Mathf.Clamp(value, min, max));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static void OnEndEdit(TMP_InputField field, System.Action commit)
    {
        if (field != null) field.onEndEdit.AddListener(_ => commit());
    }

    private static float ReadTime(TMP_InputField minutes, TMP_InputField seconds)
    {
        int min = minutes != null && int.TryParse(minutes.text, out int m) ? Mathf.Max(0, m) : 0;
        int sec = seconds != null && int.TryParse(seconds.text, out int s) ? Mathf.Clamp(s, 0, 59) : 0;
        return min * 60 + sec;
    }

    private static void SetTime(TMP_InputField minutes, TMP_InputField seconds, float totalSeconds)
    {
        int total = Mathf.Max(0, Mathf.RoundToInt(totalSeconds));
        SetNumber(minutes, total / 60);
        SetNumber(seconds, total % 60);
    }

    private static void SetNumber(TMP_InputField field, float value)
    {
        if (field != null) field.SetTextWithoutNotify(Mathf.RoundToInt(value).ToString());
    }

    private static void SetMask(CanvasGroup group, bool active)
    {
        if (group == null) return;
        group.alpha          = active ? 1f : 0.3f;
        group.interactable   = active;
        group.blocksRaycasts = active;
    }
}
