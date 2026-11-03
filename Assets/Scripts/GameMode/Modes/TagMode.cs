using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Tag: one or more players start as hunters ("it"). Tagged hiders become hunters too.
///
/// Round flow:
///   Countdown (everyone frozen) → Hiding (hiders move, hunters frozen) → Active (everyone)
///   → RoundEnd once every hider is caught or the seek limit expires → next round or MatchEnd.
///
/// Scoring: hiders bank the time they survive during the Active phase. Every player takes a
/// turn as starting hunter before anyone repeats. Highest total after the last round wins.
/// </summary>
public class TagMode : GameMode
{
    private const float CountdownSeconds = 3f;
    private const float RoundEndSeconds  = 6f;
    private const float MatchEndSeconds  = 10f;
    private const float ContactRetrySeconds = 0.5f;

    // Master-only bookkeeping (rebuilt from props if the master changes mid-round)
    private readonly HashSet<int>            caughtThisRound = new();
    private readonly Dictionary<int, double> becameHunterAt  = new();

    // Local-only: throttles tag requests while bodies stay in contact
    private readonly Dictionary<int, float> lastContactRequest = new();

    public override string DisplayName => "Tag";
    public override bool   UsesRoles   => true;
    public override bool   ShowsScores => true;

    // ─── Master: match flow ───────────────────────────────────────────────────

    public override void StartMatch()
    {
        Scores.ResetAll();
        NetProps.SetRoom(RoomKeys.ServedAsHunter, new int[0]);
        StartRound(1);
    }

    public override void MasterUpdate()
    {
        switch (Phase)
        {
            case MatchPhase.Countdown:
                if (Clock.IsExpired) Manager.SetPhase(MatchPhase.Hiding, MatchRules.HideTime);
                break;

            case MatchPhase.Hiding:
                if (Clock.IsExpired) Manager.SetPhase(MatchPhase.Active, MatchRules.SeekTimeLimit);
                break;

            case MatchPhase.Active:
                if (Clock.IsExpired || IsRoundDecided())
                    EndRound();
                else if (MatchRules.ScoreLimit > 0f && Scores.GetLeaderScore() >= MatchRules.ScoreLimit)
                    EndMatch();
                break;

            case MatchPhase.RoundEnd:
                if (!Clock.IsExpired) break;
                if (Manager.Round >= MatchRules.RoundCount) EndMatch();
                else                                        StartRound(Manager.Round + 1);
                break;

            case MatchPhase.MatchEnd:
                if (Clock.IsExpired) Manager.ReturnToLobby();
                break;
        }
    }

    private void StartRound(int round)
    {
        caughtThisRound.Clear();
        becameHunterAt.Clear();
        AssignRoles();
        Manager.SetPhase(MatchPhase.Countdown, CountdownSeconds, new Hashtable { [RoomKeys.Round] = round });
    }

    /// <summary>Picks starting hunters, preferring players who have not been hunter yet.</summary>
    private void AssignRoles()
    {
        PhotonPlayer[] players = PhotonNetwork.PlayerList;

        // A solo player stays a hider so the mode is still playable for testing
        int hunterCount = Mathf.Min(MatchRules.HunterCount, players.Length - 1);

        var served   = new HashSet<int>(NetProps.Room(RoomKeys.ServedAsHunter, new int[0]));
        var eligible = new List<PhotonPlayer>();
        foreach (var p in players)
            if (!served.Contains(p.ActorNumber)) eligible.Add(p);

        // Start a new rotation once everyone has had a turn
        if (eligible.Count < hunterCount)
        {
            served.Clear();
            eligible.Clear();
            eligible.AddRange(players);
        }

        Shuffle(eligible);
        var hunters = new HashSet<int>();
        for (int i = 0; i < hunterCount; i++)
        {
            hunters.Add(eligible[i].ActorNumber);
            served.Add(eligible[i].ActorNumber);
        }

        foreach (var p in players)
            PlayerRoles.Set(p, hunters.Contains(p.ActorNumber) ? PlayerRole.Hunter : PlayerRole.Hider);

        var servedArray = new int[served.Count];
        served.CopyTo(servedArray);
        NetProps.SetRoom(RoomKeys.ServedAsHunter, servedArray);
    }

    private void EndRound()
    {
        BankRemainingHiders();
        Manager.SetPhase(MatchPhase.RoundEnd, RoundEndSeconds);
    }

    private void EndMatch()
    {
        if (Phase == MatchPhase.Active) BankRemainingHiders();

        PhotonPlayer leader = Scores.GetLeader();
        Manager.SetPhase(MatchPhase.MatchEnd, MatchEndSeconds,
            new Hashtable { [RoomKeys.Winner] = leader != null ? leader.ActorNumber : -1 });
    }

    private void BankRemainingHiders()
    {
        foreach (var hider in RemainingHiders())
            Scores.Bank(hider);
    }

    /// <summary>The round is over when no hiders remain (or no hunters are left to chase them).</summary>
    private bool IsRoundDecided()
    {
        if (PhotonNetwork.PlayerList.Length < 2) return false; // Solo: play until the seek limit
        return RemainingHiders().Count == 0 || PlayerRoles.Count(PlayerRole.Hunter) == 0;
    }

    // Excludes players caught this round whose role change has not echoed back yet
    private List<PhotonPlayer> RemainingHiders()
    {
        var hiders = PlayerRoles.With(PlayerRole.Hider);
        hiders.RemoveAll(p => caughtThisRound.Contains(p.ActorNumber));
        return hiders;
    }

    // ─── Master: tagging ──────────────────────────────────────────────────────

    public override void HandleTagRequest(PhotonPlayer tagger, PhotonPlayer target)
    {
        if (Phase != MatchPhase.Active || tagger == null || target == null) return;
        if (PlayerRoles.Get(tagger) != PlayerRole.Hunter)                    return;
        if (PlayerRoles.Get(target) != PlayerRole.Hider)                     return;
        if (caughtThisRound.Contains(target.ActorNumber))                     return;
        if (caughtThisRound.Contains(tagger.ActorNumber))                     return;

        // Freshly tagged hunters must wait before they can tag
        if (becameHunterAt.TryGetValue(tagger.ActorNumber, out double since)
            && PhotonNetwork.Time - since < MatchRules.TagCooldown)
            return;

        Scores.Bank(target);
        if (!Mathf.Approximately(MatchRules.TagBonus, 0f))
            Scores.Add(tagger, MatchRules.TagBonus);

        caughtThisRound.Add(target.ActorNumber);
        becameHunterAt[target.ActorNumber] = PhotonNetwork.Time;
        PlayerRoles.Set(target, PlayerRole.Hunter);
        Manager.BroadcastTag(tagger, target);
    }

    public override void OnPlayerLeft(PhotonPlayer player)
    {
        caughtThisRound.Remove(player.ActorNumber);

        // If the last starting hunter leaves mid-round, promote a random hider
        bool running = Phase == MatchPhase.Hiding || Phase == MatchPhase.Active;
        if (!running || PlayerRoles.Count(PlayerRole.Hunter) > 0) return;

        var hiders = RemainingHiders();
        if (hiders.Count < 2) return; // IsRoundDecided ends the round instead
        var promoted = hiders[Random.Range(0, hiders.Count)];
        if (Phase == MatchPhase.Active) Scores.Bank(promoted);
        PlayerRoles.Set(promoted, PlayerRole.Hunter);
    }

    // ─── All clients ──────────────────────────────────────────────────────────

    public override void OnPhaseChanged(MatchPhase phase)
    {
        // Each client puts its own player back at the right spawn for the new round
        if (phase == MatchPhase.Countdown && Spawner.Instance != null)
            Spawner.Instance.RespawnLocalPlayer();
    }

    public override void OnLocalPlayerContact(Player local, Player other)
    {
        if (Phase != MatchPhase.Active)                                   return;
        if (local.Role != PlayerRole.Hunter || other.Role != PlayerRole.Hider) return;

        int actor = other.ActorNumber;
        if (lastContactRequest.TryGetValue(actor, out float last) && Time.time - last < ContactRetrySeconds)
            return;

        lastContactRequest[actor] = Time.time;
        Manager.RequestTag(other);
    }

    public override bool CanMove(PlayerRole role, MatchPhase phase) => phase switch
    {
        MatchPhase.Countdown => false,
        MatchPhase.Hiding    => role != PlayerRole.Hunter,
        _                    => true,
    };

    public override float CooldownMultiplier(PlayerRole role) =>
        role == PlayerRole.Hunter ? 1f - MatchRules.HunterCooldownReduction : 1f;

    public override bool IsScoring(PlayerRole role, MatchPhase phase) =>
        role == PlayerRole.Hider && phase == MatchPhase.Active;

    public override string GetPhaseLabel(MatchPhase phase) => phase switch
    {
        MatchPhase.Hiding => "Hide!",
        MatchPhase.Active => "Hunt!",
        _                 => base.GetPhaseLabel(phase),
    };

    public override string GetObjective(PlayerRole role, MatchPhase phase) => (role, phase) switch
    {
        (PlayerRole.Hunter, MatchPhase.Countdown) => "You're IT!",
        (PlayerRole.Hunter, MatchPhase.Hiding)    => "You're IT! Wait for the hiders to hide...",
        (PlayerRole.Hunter, MatchPhase.Active)    => "Tag the hiders!",
        (PlayerRole.Hider,  MatchPhase.Countdown) => "Get ready to run!",
        (PlayerRole.Hider,  MatchPhase.Hiding)    => "Hide!",
        (PlayerRole.Hider,  MatchPhase.Active)    => "Don't get tagged!",
        _                                         => "",
    };

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
