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
/// Rounds: as many as it takes for everyone to start as hunter once. When too few players are left
/// who haven't hunted to fill every hunter spot, random players hunt again. Then MatchEnd → Scoreboard.
/// Scoring: hiders bank the time they survive during the Active phase; highest total wins. Stats for
/// the scoreboard (tags, times tagged, longest hide, rounds survived) go through MatchStats.
/// </summary>
public class TagMode : GameMode
{
    private const float RoundEndSeconds  = 6f;
    private const float MatchEndSeconds  = 5f;
    private const float ContactRetrySeconds = 0.5f;

    // Master-only bookkeeping (rebuilt from props if the master changes mid-round)
    private readonly HashSet<int>            caughtThisRound = new();
    private readonly Dictionary<int, double> becameHunterAt  = new();

    // Local-only: throttles tag requests while bodies stay in contact
    private readonly Dictionary<int, float> lastContactRequest = new();

    public override string DisplayName => "Tag";
    public override bool   UsesRoles   => true;
    public override bool   ShowsScores => true;
    public override bool   HasRounds   => true;
    public override bool   HunterRadar => true;

    // ─── Master: match flow ───────────────────────────────────────────────────

    public override void StartMatch()
    {
        Scores.ResetAll();
        if (TakeDealtRoles())
        {
            var hunters = new List<int>();
            foreach (var p in PhotonNetwork.PlayerList)
                if (PlayerRoles.Get(p) == PlayerRole.Hunter) hunters.Add(p.ActorNumber);
            NetProps.SetRoom(RoomKeys.ServedAsHunter, hunters.ToArray());
            StartRound(1, keepRoles: true);
            return;
        }
        NetProps.SetRoom(RoomKeys.ServedAsHunter, new int[0]);
        StartRound(1);
    }

    protected override bool DealsRolesInPregame => true;

    protected override void AssignStartingRoles()
    {
        NetProps.SetRoom(RoomKeys.ServedAsHunter, new int[0]); // A fresh match: nobody has hunted yet
        AssignRoles();
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
                if (EveryoneHasHunted()) EndMatch();
                else                     StartRound(Manager.Round + 1);
                break;

            case MatchPhase.MatchEnd:
                if (Clock.IsExpired) Manager.GoToScoreboard();
                break;
        }
    }

    private void StartRound(int round, bool keepRoles = false)
    {
        caughtThisRound.Clear();
        becameHunterAt.Clear();
        if (!keepRoles) AssignRoles();
        Manager.SetPhase(MatchPhase.Countdown, MatchRules.CountdownTime, new Hashtable { [RoomKeys.Round] = round });
    }

    /// <summary>
    /// Picks starting hunters from players who haven't hunted yet. If there aren't enough of them left
    /// (e.g. an odd player out), random players who already hunted fill the remaining spots.
    /// </summary>
    private void AssignRoles()
    {
        PhotonPlayer[] players = PhotonNetwork.PlayerList;

        // At least one hider and one hunter (the lobby caps the rule too, but players may have left).
        // A solo player stays a hider so the mode is still playable for testing.
        int hunterCount = Mathf.Clamp(MatchRules.HunterCount, 1, Mathf.Max(0, players.Length - 1));

        var served = new HashSet<int>(NetProps.Room(RoomKeys.ServedAsHunter, new int[0]));
        var fresh  = new List<PhotonPlayer>();
        var repeat = new List<PhotonPlayer>();
        foreach (var p in players) (served.Contains(p.ActorNumber) ? repeat : fresh).Add(p);
        Shuffle(fresh);
        Shuffle(repeat);

        var hunters = new HashSet<int>();
        foreach (var p in fresh)  { if (hunters.Count >= hunterCount) break; hunters.Add(p.ActorNumber); }
        foreach (var p in repeat) { if (hunters.Count >= hunterCount) break; hunters.Add(p.ActorNumber); }

        foreach (var p in players)
            PlayerRoles.Set(p, hunters.Contains(p.ActorNumber) ? PlayerRole.Hunter : PlayerRole.Hider);

        served.UnionWith(hunters);
        var servedArray = new int[served.Count];
        served.CopyTo(servedArray);
        NetProps.SetRoom(RoomKeys.ServedAsHunter, servedArray);
    }

    // Every player still here has started a round as hunter (a solo test match is one round)
    private static bool EveryoneHasHunted()
    {
        if (PhotonNetwork.PlayerList.Length < 2) return true;
        var served = new HashSet<int>(NetProps.Room(RoomKeys.ServedAsHunter, new int[0]));
        foreach (var p in PhotonNetwork.PlayerList)
            if (!served.Contains(p.ActorNumber)) return false;
        return true;
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

    // Hiders still free at the end of a round: lock in their time and count the round as survived
    private void BankRemainingHiders()
    {
        foreach (var hider in RemainingHiders())
        {
            BankHider(hider);
            MatchStats.Add(hider, PlayerKeys.StatRoundsSurvived);
        }
    }

    // Locks in a hider's time, remembering their best single round
    private void BankHider(PhotonPlayer hider)
    {
        MatchStats.Max(hider, PlayerKeys.StatLongestHide, Scores.GetLive(hider) - Scores.GetBanked(hider));
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

    // Hiders always know where the hunters are: through walls (red) and on the map
    public override int SeesThroughWalls(PlayerRole viewer, PlayerRole target) =>
        viewer == PlayerRole.Hider && target == PlayerRole.Hunter ? 2 : base.SeesThroughWalls(viewer, target);

    public override bool ShowsOnMap(PlayerRole viewer, PlayerRole target) =>
        base.ShowsOnMap(viewer, target) || (viewer == PlayerRole.Hider && target == PlayerRole.Hunter);

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

        BankHider(target);
        MatchStats.Add(tagger, PlayerKeys.StatTags);
        MatchStats.Add(target, PlayerKeys.StatTimesTagged);
        if (!Mathf.Approximately(MatchRules.TagBonus, 0f))
            Scores.Add(tagger, MatchRules.TagBonus);

        caughtThisRound.Add(target.ActorNumber);
        becameHunterAt[target.ActorNumber] = PhotonNetwork.Time;
        PlayerRoles.Set(target, PlayerRole.Hunter);
        Manager.BroadcastTag(tagger, target);
    }

    // A hider who falls off the edge during the hunt is caught, as if tagged by nobody (in the hide phase they
    // just respawn: the Spawner has already put them back)
    public override void HandleFall(PhotonPlayer player)
    {
        if (Phase != MatchPhase.Active || player == null) return;
        if (PlayerRoles.Get(player) != PlayerRole.Hider || caughtThisRound.Contains(player.ActorNumber)) return;

        BankHider(player);
        MatchStats.Add(player, PlayerKeys.StatTimesTagged);
        caughtThisRound.Add(player.ActorNumber);
        becameHunterAt[player.ActorNumber] = PhotonNetwork.Time;
        PlayerRoles.Set(player, PlayerRole.Hunter);
        Manager.BroadcastTag(null, player);
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

    // Rules can switch abilities off for one side
    public override bool CanUseAbilities(PlayerRole role, MatchPhase phase) =>
        CanMove(role, phase) && (role == PlayerRole.Hunter ? MatchRules.HunterAbilities : MatchRules.HiderAbilities);

    public override float SpeedMultiplier(PlayerRole role) =>
        role == PlayerRole.Hunter ? MatchRules.HunterSpeedMultiplier : 1f;

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

    // ─── Scoreboard ───────────────────────────────────────────────────────────

    public override IReadOnlyList<ScoreboardColumn> ScoreboardColumns => new[]
    {
        new ScoreboardColumn("Survival Time",   p => MatchStats.FormatTime(NetProps.Of(p, PlayerKeys.Score, 0f))),
        new ScoreboardColumn("Tags",            p => MatchStats.Count(p, PlayerKeys.StatTags)),
        new ScoreboardColumn("Times Tagged",    p => MatchStats.Count(p, PlayerKeys.StatTimesTagged)),
        new ScoreboardColumn("Longest Hide",    p => MatchStats.FormatTime(MatchStats.Get(p, PlayerKeys.StatLongestHide))),
        new ScoreboardColumn("Rounds Survived", p => MatchStats.Count(p, PlayerKeys.StatRoundsSurvived)),
        new ScoreboardColumn("Abilities Used",  p => MatchStats.Count(p, PlayerKeys.StatAbilitiesUsed)),
    };

    // Mid-match: survival time includes the round in progress
    public override IReadOnlyList<ScoreboardColumn> LiveScoreColumns => new[]
    {
        new ScoreboardColumn("Role",          p => RoleName(PlayerRoles.Get(p))),
        new ScoreboardColumn("Survival Time", p => MatchStats.FormatTime(Manager != null ? Scores.GetLive(p) : NetProps.Of(p, PlayerKeys.Score, 0f))),
        new ScoreboardColumn("Tags",          p => MatchStats.Count(p, PlayerKeys.StatTags)),
        new ScoreboardColumn("Times Tagged",  p => MatchStats.Count(p, PlayerKeys.StatTimesTagged)),
    };

    // Most survival time first; ties go to more tags
    public override int CompareForScoreboard(PhotonPlayer a, PhotonPlayer b)
    {
        int byScore = NetProps.Of(b, PlayerKeys.Score, 0f).CompareTo(NetProps.Of(a, PlayerKeys.Score, 0f));
        return byScore != 0 ? byScore : MatchStats.Get(b, PlayerKeys.StatTags).CompareTo(MatchStats.Get(a, PlayerKeys.StatTags));
    }

    public override string ScoreboardTitle(IReadOnlyList<PhotonPlayer> ranked) =>
        ranked.Count > 0 ? $"{ranked[0].NickName} wins!" : "Match Over";

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
