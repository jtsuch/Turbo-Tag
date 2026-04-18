using UnityEngine;
using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using PhotonPlayer   = Photon.Realtime.Player;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Central game-mode orchestrator for the Hide-and-Seek match type.
///
/// Responsibilities:
///   - Reads match settings from room custom properties (HideTime, MaxHideTime,
///     AllowCheats, InitialHunterCount).
///   - Drives the round lifecycle: Countdown → Hiding → Active → RoundEnd → (repeat or MatchEnd).
///   - Delegates timer tracking to MatchTimerController and score tracking to ScoreController.
///   - Syncs phase changes and player roles to all clients via RPCs.
///   - Exposes TagPlayer() for hunter collision code to call.
///
/// Round rotation: every player serves as the initial hunter at least once before the
/// match can end. Highest cumulative hide time wins.
///
/// Attach to: a persistent scene GameObject — add MatchTimerController, ScoreController,
/// and PhotonView to the same object. The PhotonView must use a scene ViewID.
/// </summary>
[RequireComponent(typeof(MatchTimerController))]
[RequireComponent(typeof(ScoreController))]
[RequireComponent(typeof(PhotonView))]
public class GameModeManager : MonoBehaviourPunCallbacks
{
    private static GameModeManager _instance;
    public static GameModeManager Instance
    {
        get
        {
            if (_instance == null) _instance = FindFirstObjectByType<GameModeManager>();
            return _instance;
        }
        private set => _instance = value;
    }

    // -------------------------------------------------------------------------
    // Match settings (loaded from room custom properties)
    // -------------------------------------------------------------------------

    private float hideTime          = 60f;   // "HideTime"         — hide-phase duration
    private float maxHideTime       = 0f;    // "MaxHideTime"      — per-player score cap (0 = none)
    public bool   AllowCheats { get; private set; } = false; // "AllowCheats"
    private int   initialHunterCount = 1;    // "InitialHunterCount"

    // -------------------------------------------------------------------------
    // Match phase
    // -------------------------------------------------------------------------

    public enum MatchPhase { Countdown, Hiding, Active, RoundEnd, MatchEnd }
    public MatchPhase CurrentPhase { get; private set; }

    // DisplayTime forwarded from MatchTimerController for UI
    public float DisplayTime => timerController != null ? timerController.DisplayTime : 0f;

    // -------------------------------------------------------------------------
    // Round state (master client)
    // -------------------------------------------------------------------------

    private readonly HashSet<int> servedAsHunter = new();  // Actor numbers that have been initial hunter
    private readonly HashSet<int> currentHunters = new();
    private readonly HashSet<int> currentHiders  = new();
    public int RoundNumber { get; private set; } = 0;

    private static readonly WaitForSeconds WaitOneSecond  = new(1f);
    private static readonly WaitForSeconds WaitFiveSeconds = new(5f);

    // -------------------------------------------------------------------------
    // References
    // -------------------------------------------------------------------------

    private MatchTimerController timerController;
    private ScoreController      scoreController;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        Instance = this;

        timerController = GetComponent<MatchTimerController>();
        scoreController = GetComponent<ScoreController>();
    }

    private void Start()
    {
        LoadSettingsFromRoom();
        ApplySettingsToControllers();

        timerController.OnCountdownComplete += HandleCountdownComplete;
        timerController.OnHidePhaseComplete += HandleHidePhaseComplete;
        timerController.OnTimeLimitReached  += HandleTimeLimitReached;
        scoreController.OnScoreLimitReached += HandleScoreLimitReached;

        if (PhotonNetwork.IsMasterClient)
            StartCoroutine(StartFirstRound());
    }

    private void OnDestroy()
    {
        if (timerController != null)
        {
            timerController.OnCountdownComplete -= HandleCountdownComplete;
            timerController.OnHidePhaseComplete -= HandleHidePhaseComplete;
            timerController.OnTimeLimitReached  -= HandleTimeLimitReached;
        }
        if (scoreController != null)
            scoreController.OnScoreLimitReached -= HandleScoreLimitReached;
    }

    // -------------------------------------------------------------------------
    // Settings
    // -------------------------------------------------------------------------

    private void LoadSettingsFromRoom()
    {
        PhotonHashtable props = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.CustomProperties : null;
        if (props == null) return;

        if (props.TryGetValue("HideTime",          out object ht))  hideTime          = System.Convert.ToSingle(ht);
        if (props.TryGetValue("MaxHideTime",        out object mht)) maxHideTime       = System.Convert.ToSingle(mht);
        if (props.TryGetValue("AllowCheats",        out object ac))  AllowCheats       = (bool)ac;
        if (props.TryGetValue("InitialHunterCount", out object ihc)) initialHunterCount = System.Convert.ToInt32(ihc);
    }

    private void ApplySettingsToControllers()
    {
        timerController.HidePhaseDuration = hideTime;
        timerController.ActivePhaseLimit  = 0f;    // No hard active-phase time limit by default
        scoreController.MaxTotalTime      = maxHideTime;
    }

    // -------------------------------------------------------------------------
    // Round management (master client only)
    // -------------------------------------------------------------------------

    private IEnumerator StartFirstRound()
    {
        yield return WaitOneSecond;  // Brief pause for late-joining clients to settle
        StartRound();
    }

    private void StartRound()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        RoundNumber++;
        PickInitialHunters();
        BroadcastRoles();

        photonView.RPC(nameof(RPC_SetPhase), RpcTarget.All, (int)MatchPhase.Countdown);
        photonView.RPC(nameof(RPC_SetCanMove), RpcTarget.All, false, false); // nobody moves
        timerController.StartCountdown();
    }

    private void PickInitialHunters()
    {
        currentHunters.Clear();
        currentHiders.Clear();

        PhotonPlayer[] allPlayers = PhotonNetwork.PlayerList;

        // Collect players who haven't been the initial hunter yet
        List<PhotonPlayer> eligible = new();
        foreach (PhotonPlayer p in allPlayers)
        {
            if (!servedAsHunter.Contains(p.ActorNumber))
                eligible.Add(p);
        }

        // If everyone has served at least once, start a new rotation
        if (eligible.Count == 0)
        {
            servedAsHunter.Clear();
            foreach (PhotonPlayer p in allPlayers) eligible.Add(p);
        }

        Shuffle(eligible);
        int count = Mathf.Clamp(initialHunterCount, 1, eligible.Count - 1);  // Keep at least 1 hider
        for (int i = 0; i < count; i++)
        {
            currentHunters.Add(eligible[i].ActorNumber);
            servedAsHunter.Add(eligible[i].ActorNumber);
        }

        foreach (PhotonPlayer p in allPlayers)
        {
            if (!currentHunters.Contains(p.ActorNumber))
                currentHiders.Add(p.ActorNumber);
        }
    }

    private void BroadcastRoles()
    {
        foreach (int actor in currentHunters)
            photonView.RPC(nameof(RPC_SetPlayerRole), RpcTarget.All, actor, true);
        foreach (int actor in currentHiders)
            photonView.RPC(nameof(RPC_SetPlayerRole), RpcTarget.All, actor, false);
    }

    // -------------------------------------------------------------------------
    // Timer event handlers (master client)
    // -------------------------------------------------------------------------

    private void HandleCountdownComplete()
    {
        // Hiders scatter; hunters stay frozen
        photonView.RPC(nameof(RPC_SetPhase),   RpcTarget.All, (int)MatchPhase.Hiding);
        photonView.RPC(nameof(RPC_SetCanMove), RpcTarget.All, true, false); // hiders=true, hunters=false
        timerController.StartHidePhase();
    }

    private void HandleHidePhaseComplete()
    {
        // Everyone active; score counting begins
        photonView.RPC(nameof(RPC_SetPhase),   RpcTarget.All, (int)MatchPhase.Active);
        photonView.RPC(nameof(RPC_SetCanMove), RpcTarget.All, true, true); // everyone moves
        scoreController.BeginRound(currentHiders);
        timerController.StartActivePhase();
    }

    private void HandleTimeLimitReached() => EndRound();

    private void HandleScoreLimitReached() => CheckRoundEndCondition();

    // -------------------------------------------------------------------------
    // Tagging
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by hunter collision/trigger logic when they tag a hider.
    /// Only processed on the master client; ignored if the target is already a hunter
    /// or the match is not in the Active phase.
    /// </summary>
    public void TagPlayer(int hiderActorNumber)
    {
        if (!PhotonNetwork.IsMasterClient)             return;
        if (CurrentPhase != MatchPhase.Active)         return;
        if (!currentHiders.Contains(hiderActorNumber)) return;

        currentHiders.Remove(hiderActorNumber);
        currentHunters.Add(hiderActorNumber);

        photonView.RPC(nameof(RPC_SetPlayerRole), RpcTarget.All, hiderActorNumber, true);
        scoreController.FreezePlayerTimer(hiderActorNumber);

        CheckRoundEndCondition();
    }

    private void CheckRoundEndCondition()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (currentHiders.Count == 0)
            EndRound();
    }

    // -------------------------------------------------------------------------
    // Round / match end
    // -------------------------------------------------------------------------

    private void EndRound()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        timerController.Stop();
        scoreController.EndRound();

        photonView.RPC(nameof(RPC_SetPhase),   RpcTarget.All, (int)MatchPhase.RoundEnd);
        photonView.RPC(nameof(RPC_SetCanMove), RpcTarget.All, false, false);

        // Match ends when every player has served as the initial hunter at least once
        bool matchOver = true;
        foreach (PhotonPlayer p in PhotonNetwork.PlayerList)
        {
            if (!servedAsHunter.Contains(p.ActorNumber)) { matchOver = false; break; }
        }

        if (matchOver)
            StartCoroutine(EndMatch());
        else
            StartCoroutine(NextRoundDelay());
    }

    private IEnumerator NextRoundDelay()
    {
        yield return WaitFiveSeconds;
        StartRound();
    }

    private IEnumerator EndMatch()
    {
        yield return WaitFiveSeconds;
        int winner = scoreController.GetWinner();
        photonView.RPC(nameof(RPC_SetPhase),       RpcTarget.All, (int)MatchPhase.MatchEnd);
        photonView.RPC(nameof(RPC_AnnounceWinner), RpcTarget.All, winner);
    }

    // -------------------------------------------------------------------------
    // RPCs
    // -------------------------------------------------------------------------

    [PunRPC]
    private void RPC_SetPhase(int phaseInt)
    {
        CurrentPhase = (MatchPhase)phaseInt;
    }

    /// <summary>
    /// Sets CanMove for the local player based on their role.
    /// hidersCanMove applies to current hiders; huntersCanMove applies to current hunters.
    /// </summary>
    [PunRPC]
    private void RPC_SetCanMove(bool hidersCanMove, bool huntersCanMove)
    {
        if (Player.Instance == null) return;
        int local = PhotonNetwork.LocalPlayer.ActorNumber;
        if      (currentHiders.Contains(local))  Player.Instance.CanMove = hidersCanMove;
        else if (currentHunters.Contains(local)) Player.Instance.CanMove = huntersCanMove;
    }

    [PunRPC]
    private void RPC_SetPlayerRole(int actorNumber, bool isHunter)
    {
        if (isHunter) { currentHunters.Add(actorNumber); currentHiders.Remove(actorNumber); }
        else          { currentHiders.Add(actorNumber);  currentHunters.Remove(actorNumber); }

        if (PhotonNetwork.LocalPlayer.ActorNumber == actorNumber && Player.Instance != null)
            Player.Instance.isHunter = isHunter;
    }

    [PunRPC]
    private void RPC_AnnounceWinner(int winnerActorNumber)
    {
        PhotonPlayer winner = PhotonNetwork.CurrentRoom != null
            ? PhotonNetwork.CurrentRoom.GetPlayer(winnerActorNumber)
            : null;
        string name = winner != null ? winner.NickName : winnerActorNumber.ToString();
        Debug.Log($"[GameModeManager] Match over! Winner: {name}");
        // TODO: surface winner to the HUD
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
