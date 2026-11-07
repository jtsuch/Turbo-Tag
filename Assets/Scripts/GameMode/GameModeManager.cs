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
    private bool        leavingLevel; // A scene change was started: never start another (each call restarted it)
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

        ScoreOverlay.Create(); // Hold Tab for the standings, in every mode
        AdminConsole.Create(); // ` for admins (AdminConsole.Admins)
        if (!TryGetComponent(out AdminCommands _)) gameObject.AddComponent<AdminCommands>(); // Shares this scene PhotonView
        if (!TryGetComponent(out DonutManager _)) gameObject.AddComponent<DonutManager>();   // Random-effect donuts
        if (!TryGetComponent(out TrailHistory _)) gameObject.AddComponent<TrailHistory>();   // Bloodhound's footprints
        if (!TryGetComponent(out RoundModifiers _)) gameObject.AddComponent<RoundModifiers>(); // Round Modifiers rule
        if (!TryGetComponent(out MatchCinematics _)) gameObject.AddComponent<MatchCinematics>(); // Fly-over, final slow-mo
        Sfx.Prewarm();
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

        // Gun modes: health, guns, kill feed, health drops and killcam (RPCs share this scene PhotonView)
        if (Mode.UsesGuns && !TryGetComponent(out CombatManager _)) gameObject.AddComponent<CombatManager>();

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
    public float SpeedMultiplier(Player player) => Mode != null ? Mode.SpeedMultiplier(player.Role) : 1f;

    public float CooldownMultiplier(Player player) =>
        (Mode != null ? Mode.CooldownMultiplier(player.Role) : 1f) * MatchRules.GlobalCooldownMultiplier;

    public string PhaseLabel => Mode != null ? Mode.GetPhaseLabel(Phase) : "";

    /// <summary>True when the local player shouldn't be able to tell who <paramref name="target"/> is (silhouette, no name).</summary>
    public bool HidesIdentityFromLocal(Player target) =>
        Mode != null && Player.Local != null && target != Player.Local
        && Mode.HidesIdentity(Player.Local.Role, target.Role, Phase);

    /// <summary>True when <paramref name="target"/> should show a crown on this screen (the President, or the leader).</summary>
    public bool ShowsCrown(Player target) =>
        Mode != null && !HidesIdentityFromLocal(target)
        && (Mode.WearsCrown(target.Role, Phase) || (Mode.CrownsLeader && IsLeader(target.Owner)));

    /// <summary>Strictly ahead of everyone else, with points (modes with <see cref="GameMode.CrownsLeader"/>).</summary>
    public bool IsLeader(Photon.Realtime.Player who)
    {
        if (who == null || Phase == MatchPhase.Waiting || Phase == MatchPhase.Countdown) return false;
        float mine = Scores.GetLive(who);
        if (mine <= 0f) return false;
        foreach (var other in PhotonNetwork.PlayerList)
            if (other != who && Scores.GetLive(other) >= mine) return false;
        return true;
    }

    // ─── Master-client API ────────────────────────────────────────────────────

    /// <summary>Master: moves the match to <paramref name="phase"/> and restarts the clock.</summary>
    public void SetPhase(MatchPhase phase, float duration, Hashtable extra = null)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        Hashtable props = Clock.BuildRestart(duration);
        props[RoomKeys.Phase] = (int)phase;
        // Every round's countdown: a new random modifier when the rule is on
        if (phase == MatchPhase.Countdown)
            props[RoomKeys.Modifier] = MatchRules.Modifiers ? RoundModifiers.PickNext(NetProps.Room(RoomKeys.Modifier, "")) : "";
        if (extra != null)
            foreach (var entry in extra) props[entry.Key] = entry.Value;

        pendingPhase = phase;
        pendingSince = Time.time;
        NetProps.SetRoom(props);
    }

    /// <summary>
    /// Every client, when a round's countdown starts: each round starts fresh. Ragdolls go (they're local), and this
    /// client removes every ability object it owns (traps, ladders, stuck bombs, bait: anything spawned with
    /// <see cref="SpawnData"/>), whichever ability placed it, so nothing carries over from the last round.
    /// </summary>
    private static void ClearRoundLeftovers()
    {
        PlayerCorpse.ClearAll();
        foreach (var view in FindObjectsByType<PhotonView>(FindObjectsSortMode.None))
        {
            if (view == null || !view.IsMine || view.IsRoomView || view.GetComponent<Player>() != null) continue;
            var data = view.InstantiationData;
            if (data == null || data.Length < 3 || data[2] is not int) continue; // Not an ability object
            PhotonNetwork.Destroy(view.gameObject);
        }
    }

    /// <summary>Master: clears phase, round, roles and scores ready for a new match.</summary>
    public void ResetMatchState()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // Treat it as Waiting at once: until Photon echoes this back, the room still says the last match's phase
        // (MatchEnd, with its clock run out), and the mode would send everyone straight back to the Scoreboard
        pendingPhase = MatchPhase.Waiting;
        pendingSince = Time.time;

        NetProps.SetRoom(new Hashtable
        {
            [RoomKeys.Phase]          = (int)MatchPhase.Waiting,
            [RoomKeys.Round]          = 0,
            [RoomKeys.Winner]         = -1,
            [RoomKeys.WinningTeam]    = "",
            [RoomKeys.ServedAsHunter] = new int[0],
            [RoomKeys.PausedAt]       = -1d,
            [RoomKeys.DiscHolder]     = -1,
            [RoomKeys.Modifier]       = "",
        });

        PlayerRoles.ClearPending();
        bool keepRoles = NetProps.Room(RoomKeys.RolesDealt, false); // Dealt in Pregame: StartMatch keeps them
        foreach (var player in PhotonNetwork.PlayerList)
        {
            var props = new Hashtable { [PlayerKeys.Score] = 0f };
            if (!keepRoles) props[PlayerKeys.Role] = (int)PlayerRole.None;
            foreach (string stat in MatchStats.All) props[stat] = 0f;
            player.SetCustomProperties(props);
        }
    }

    /// <summary>
    /// Master: the match is over; everyone moves to the Scoreboard scene (stats stay in player properties).
    /// Modes call this every frame once their end timer runs out, so only the first call loads: calling
    /// LoadLevel again each frame kept restarting the host's own load (everyone else arrived, the host didn't).
    /// </summary>
    public void GoToScoreboard()
    {
        if (!PhotonNetwork.IsMasterClient || leavingLevel) return;
        leavingLevel = true;
        PhotonNetwork.LoadLevel(SceneNames.Scoreboard);
    }

    /// <summary>Master: reopens the room and takes everyone back to the lobby.</summary>
    public void ReturnToLobby()
    {
        if (!PhotonNetwork.IsMasterClient || leavingLevel) return;
        leavingLevel = true;

        ResetMatchState();
        MatchLauncher.ReturnToLobby();
    }

    /// <summary>Master: tells every client that a tag happened (feedback + caught state).</summary>
    public void BroadcastTag(PhotonPlayer tagger, PhotonPlayer target) =>
        photonView.RPC(nameof(RPC_PlayerTagged), RpcTarget.All, tagger != null ? tagger.ActorNumber : 0, target.ActorNumber);

    // ─── Client API ───────────────────────────────────────────────────────────

    /// <summary>Asks the master to validate a tag on <paramref name="target"/> by the local player.</summary>
    public void RequestTag(Player target) =>
        photonView.RPC(nameof(RPC_RequestTag), RpcTarget.MasterClient, target.ActorNumber);

    /// <summary>The local player fell off the map (non-gun modes): the mode may count it (Tag: a hider is caught).</summary>
    public void ReportFall() => photonView.RPC(nameof(RPC_ReportFall), RpcTarget.MasterClient);

    // ─── RPCs ─────────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_ReportFall(PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || Mode == null) return;
        Mode.HandleFall(info.Sender);
    }

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
        if (tagged != null)
        {
            Fx.Tagged(tagged.transform.position, Mode != null && Mode.UsesGuns);
            tagged.LeaveCorpse();
        }

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
        if (confirmed == MatchPhase.Countdown) ClearRoundLeftovers();
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
