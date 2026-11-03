using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Rules tab of the pause menu: the Pause Timer and Cheats buttons, and a dimmed overlay that makes
/// the tab read-only for everyone but the host. The rule rows themselves are built from the current
/// game mode by the <see cref="GameModeRulesPanel"/> on this page (no mode dropdown mid-match).
/// Attach to: the RulesPage panel inside the PauseMenu.
/// </summary>
public class RulesUI : MonoBehaviourPunCallbacks
{
    [Header("Action Buttons")]
    [SerializeField] private Button   pauseTimerButton;
    [SerializeField] private TMP_Text pauseTimerLabel;
    [SerializeField] private Button   enableCheatsButton;
    [SerializeField] private TMP_Text enableCheatsLabel;

    [Header("Host Lock Overlay")]
    [Tooltip("CanvasGroup covering the panel; blocks input for non-hosts.")]
    [SerializeField] private CanvasGroup hostLockOverlay;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        if (pauseTimerButton   != null) pauseTimerButton.onClick.AddListener(() => ToggleRule(RuleKeys.TimerPaused));
        if (enableCheatsButton != null) enableCheatsButton.onClick.AddListener(() => ToggleRule(RuleKeys.CheatsEnabled));
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

        if (pauseTimerLabel   != null) pauseTimerLabel.text   = MatchRules.TimerPaused   ? "Resume Timer"   : "Pause Timer";
        if (enableCheatsLabel != null) enableCheatsLabel.text = MatchRules.CheatsEnabled ? "Disable Cheats" : "Enable Cheats";
    }

    private void ApplyHostLock()
    {
        if (hostLockOverlay == null) return;
        bool isHost = PhotonNetwork.IsMasterClient;
        hostLockOverlay.alpha          = isHost ? 0f : 0.5f;
        hostLockOverlay.interactable   = isHost;
        hostLockOverlay.blocksRaycasts = !isHost;
    }

    // ─── Host actions ─────────────────────────────────────────────────────────

    private void ToggleRule(string key)
    {
        if (PhotonNetwork.IsMasterClient) NetProps.SetRoom(key, !NetProps.Room(key, false));
    }
}
