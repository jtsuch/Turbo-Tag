using System;
using Photon.Pun;
using UnityEngine;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Scene-level match driver. Creates the active <see cref="GameMode"/> from the room's
/// "GameMode" property, forwards scene/network events to it, and owns the networked match
/// state (phase, round, roles) stored in room and player properties.
///
/// Every client reads the same properties, so movement locks, timers, roles and scores agree
/// without per-frame RPCs. RPCs are only used for discrete moments (tag requests/announcements).
///
/// Attach to: one GameObject per level, alongside MatchTimerController, ScoreController and a
/// scene PhotonView.
/// </summary>
[RequireComponent(typeof(MatchTimerController))]
[RequireComponent(typeof(ScoreController))]
[RequireComponent(typeof(PhotonView))]
public class GameModeManager : MonoBehaviourPunCallbacks
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    private static GameModeManager _instance;
    public static GameModeManager Instance => _instance;

    // ─── Events ───────────────────────────────────────────────────────────────
    public event Action<MatchPhase>               PhaseChanged;
    public event Action<PhotonPlayer, PlayerRole> RoleChanged;
    /// <summary>(tagger, target)</summary>
    public event Action<PhotonPlayer, PhotonPlayer> PlayerTagged;

    // ─── State ────────────────────────────────────────────────────────────────
    public GameMode             Mode       { get; private set; }
    public GameModeDefinition   Definition { get; private set; }
    public MatchTimerController Clock      { get; private set; }
    public ScoreController      Scores     { get; private set; }

    /// <summary>
    /// Current phase. On the master client a just-written phase is returned immediately, before
    /// Photon echoes the property back, so master logic never acts twice on a stale phase.
    /// </summary>
    public MatchPhase Phase => pendingPhase ?? (MatchPhase)NetProps.Room(RoomKeys.Phase, (int)MatchPhase.Waiting);
    public int        Round => NetProps.Room(RoomKeys.Round, 0);
    public int        WinnerActorNumber => NetProps.Room(RoomKeys.Winner, -1);

    [Tooltip("Longest the master waits for every player to spawn before starting anyway.")]
    [SerializeField] private float maxWaitForPlayers = 6f;

    private MatchPhase? pendingPhase;
    private float       pendingSince;
    private MatchPhase  lastAnnouncedPhase;
    private float       sceneStartTime;

    private const float PendingTimeout = 3f;

    // The same on every client, and above the ids handed out to other scene objects.
    private const int FallbackViewId = 999;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;

        // A level whose scene PhotonView never got an id would fail every RPC ("Illegal view ID:0").
        // Turbo Tag > Levels > Assign Scene PhotonView IDs fixes the scene; this keeps play working meanwhile.
        if (photonView.ViewID == 0)
        {
            Debug.LogWarning($"[GameModeManager] Scene PhotonView has no id in '{gameObject.scene.name}'. " +
                             "Run Turbo Tag > Levels > Assign Scene PhotonView IDs. Using a fallback id.");
            photonView.ViewID = FallbackViewId;
        }

        Clock  = GetComponent<MatchTimerController>();
        Scores = GetComponent<ScoreController>();
        lastAnnouncedPhase = MatchPhase.Waiting;
        sceneStartTime     = Time.time;

        TryInitialize();
    }

    // The room may not exist yet when a level is opened directly in the editor (offline debug
    // mode), so the mode is created as soon as it does.
    private bool TryInitialize()
    {
        if (Mode != null) return true;
        if (PhotonNetwork.CurrentRoom == null) return false;

        Definition = GameModeRegistry.Find(MatchRules.GameModeKey);
        Mode       = GameModeRegistry.Create(MatchRules.GameModeKey);
        Mode.Attach(this);

        // A freshly loaded level always starts from a clean match state
        if (PhotonNetwork.IsMasterClient) ResetMatchState();
        return true;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (!TryInitialize() || !PhotonNetwork.IsMasterClient) return;

        // Wait for our own phase write to round-trip before making further decisions
        if (pendingPhase.HasValue)
        {
            if (Time.time - pendingSince < PendingTimeout) return;
            pendingPhase = null;
        }

        if (Phase == MatchPhase.Waiting)
        {
            if (AllPlayersSpawned() || Time.time - sceneStartTime > maxWaitForPlayers)
                Mode.StartMatch();
            return;
        }

        Mode.MasterUpdate();
    }

    private static bool AllPlayersSpawned() =>
        PhotonNetwork.CurrentRoom != null && Player.All.Count >= PhotonNetwork.CurrentRoom.PlayerCount;

    // ─── Queries used by gameplay systems ─────────────────────────────────────

    public bool CanMove(Player player) => Mode == null || Mode.CanMove(player.Role, Phase);

    public bool CanUseAbilities(Player player) => Mode == null || Mode.CanUseAbilities(player.Role, Phase);

    /// <summary>Combined mode and rule multiplier applied to the player's ability cooldowns.</summary>
    public float CooldownMultiplier(Player player) =>
        (Mode != null ? Mode.CooldownMultiplier(player.Role) : 1f) * MatchRules.GlobalCooldownMultiplier;

    public string PhaseLabel => Mode != null ? Mode.GetPhaseLabel(Phase) : "";

    // ─── Master-client API ────────────────────────────────────────────────────

    /// <summary>Master: moves the match to <paramref name="phase"/> and restarts the clock.</summary>
    public void SetPhase(MatchPhase phase, float duration, Hashtable extra = null)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        Hashtable props = Clock.BuildRestart(duration);
        props[RoomKeys.Phase] = (int)phase;
        if (extra != null)
            foreach (var entry in extra) props[entry.Key] = entry.Value;

        pendingPhase = phase;
        pendingSince = Time.time;
        NetProps.SetRoom(props);
    }

    /// <summary>Master: clears phase, round, roles and scores ready for a new match.</summary>
    public void ResetMatchState()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        NetProps.SetRoom(new Hashtable
        {
            [RoomKeys.Phase]          = (int)MatchPhase.Waiting,
            [RoomKeys.Round]          = 0,
            [RoomKeys.Winner]         = -1,
            [RoomKeys.ServedAsHunter] = new int[0],
            [RoomKeys.PausedAt]       = -1d,
        });

        foreach (var player in PhotonNetwork.PlayerList)
        {
            player.SetCustomProperties(new Hashtable
            {
                [PlayerKeys.Role]  = (int)PlayerRole.None,
                [PlayerKeys.Score] = 0f,
            });
        }
    }

    /// <summary>Master: reopens the room and takes everyone back to the lobby.</summary>
    public void ReturnToLobby()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        ResetMatchState();
        PhotonNetwork.CurrentRoom.IsOpen    = true;
        PhotonNetwork.CurrentRoom.IsVisible = true;
        PhotonNetwork.LoadLevel(SceneNames.Lobby);
    }

    /// <summary>Master: tells every client that a tag happened (feedback + caught state).</summary>
    public void BroadcastTag(PhotonPlayer tagger, PhotonPlayer target) =>
        photonView.RPC(nameof(RPC_PlayerTagged), RpcTarget.All, tagger.ActorNumber, target.ActorNumber);

    // ─── Client API ───────────────────────────────────────────────────────────

    /// <summary>Asks the master to validate a tag on <paramref name="target"/> by the local player.</summary>
    public void RequestTag(Player target) =>
        photonView.RPC(nameof(RPC_RequestTag), RpcTarget.MasterClient, target.ActorNumber);

    // ─── RPCs ─────────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_RequestTag(int targetActor, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || Mode == null) return;
        Mode.HandleTagRequest(info.Sender, PhotonNetwork.CurrentRoom.GetPlayer(targetActor));
    }

    [PunRPC]
    private void RPC_PlayerTagged(int taggerActor, int targetActor)
    {
        var room   = PhotonNetwork.CurrentRoom;
        var tagger = room?.GetPlayer(taggerActor);
        var target = room?.GetPlayer(targetActor);

        // Everyone leaves a ragdoll where the tagged player was; the tagged player then runs the caught flow
        var tagged = Player.Find(targetActor);
        if (tagged != null) tagged.LeaveCorpse();

        if (targetActor == PhotonNetwork.LocalPlayer.ActorNumber && Player.Local != null)
            Player.Local.OnTagged(tagger);

        PlayerTagged?.Invoke(tagger, target);
    }

    // ─── Photon Callbacks ─────────────────────────────────────────────────────

    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        if (!changed.ContainsKey(RoomKeys.Phase)) return;

        var confirmed = (MatchPhase)NetProps.Get(changed, RoomKeys.Phase, (int)MatchPhase.Waiting);
        if (pendingPhase == confirmed) pendingPhase = null;

        if (confirmed == lastAnnouncedPhase) return;
        lastAnnouncedPhase = confirmed;
        Mode?.OnPhaseChanged(confirmed);
        PhaseChanged?.Invoke(confirmed);
    }

    public override void OnPlayerPropertiesUpdate(PhotonPlayer target, Hashtable changed)
    {
        if (changed.ContainsKey(PlayerKeys.Role))
            RoleChanged?.Invoke(target, PlayerRoles.Get(target));
    }

    public override void OnPlayerLeftRoom(PhotonPlayer otherPlayer)
    {
        if (PhotonNetwork.IsMasterClient) Mode?.OnPlayerLeft(otherPlayer);
    }
}
