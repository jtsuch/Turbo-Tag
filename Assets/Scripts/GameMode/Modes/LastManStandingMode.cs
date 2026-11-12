using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Last Man Standing: Free for All with one life a round. Everyone spawns spread out, picks a gun, and fights until
/// one penguin is left: they win the round (a point). Dead players stay out and spectate a survivor
/// (<see cref="DeadSpectator"/>) until the next round. <see cref="MatchRules.RoundCount"/> rounds (5); the most round
/// wins takes the match. A tie for first after the last round goes to <b>Sudden Death</b>: the same kind of round
/// with only the tied players (everyone else watches) and a random round modifier, repeated until someone wins one.
/// A round that runs out of time (<see cref="MatchRules.RoundTime"/>) goes to the survivor with the most kills that
/// round (a tie there: nobody). Alone in the room, rounds only end on the clock (practice).
///
/// Who's in: <see cref="RoomKeys.LmsIn"/> (everyone, or the finalists) and <see cref="RoomKeys.LmsOut"/> (knocked out
/// this round), both master-written. A player who isn't in the round (a late joiner, a Sudden Death bystander) sits
/// out: <see cref="KeepsDead(MatchPhase,int)"/> keeps them down in every phase, and their own client takes them out
/// if they're standing (a quiet "sit out" death, not scored).
///
/// Flow per round: Countdown (frozen, pick a gun) → Active → RoundEnd (5 s, who won) → next round, Sudden Death or
/// MatchEnd → Scoreboard. Scores are round wins; kills, deaths and headshots are stats.
/// </summary>
public class LastManStandingMode : FreeForAllMode
{
    private const float RoundEndSeconds = 5f, MatchEndSeconds = 6f, MinCountdown = 5f;
    private const string SitOut = "sitout";

    // Master
    private readonly Dictionary<int, int> roundKills = new();
    private readonly Dictionary<(int actor, string key), float> tally = new();
    private int round;

    public override string DisplayName       => "Last Man Standing";
    public override bool   HasRounds         => true;
    public override bool   SpectatesWhenDead => true;
    public override bool   CrownsLeader      => true;

    // ─── Who's in this round (every client) ───────────────────────────────────

    private static int[] InRound  => NetProps.Room(RoomKeys.LmsIn, System.Array.Empty<int>());
    private static int[] OutRound => NetProps.Room(RoomKeys.LmsOut, System.Array.Empty<int>());
    public static bool SuddenDeath => NetProps.Room(RoomKeys.LmsSudden, false);

    /// <summary>Playing this round (not knocked out, not sitting out).</summary>
    public static bool IsPlaying(int actor) =>
        System.Array.IndexOf(InRound, actor) >= 0 && System.Array.IndexOf(OutRound, actor) < 0;

    private static bool InTheRound(int actor) => System.Array.IndexOf(InRound, actor) >= 0;

    // Dead stay dead until the next round; anyone not in this round stays out whatever the phase
    public override bool KeepsDead(MatchPhase phase) =>
        phase == MatchPhase.Active || phase == MatchPhase.RoundEnd || phase == MatchPhase.MatchEnd;

    public override bool KeepsDead(MatchPhase phase, int actor) =>
        KeepsDead(phase) || (phase != MatchPhase.Waiting && InRound.Length > 0 && !InTheRound(actor));

    // ─── Master: match flow ───────────────────────────────────────────────────

    public override void StartMatch()
    {
        tally.Clear();
        foreach (var player in PhotonNetwork.PlayerList) PlayerRoles.Set(player, PlayerRole.None);
        Scores.ResetAll();
        StartRound(1, null);
    }

    // A new round: everyone (or just the finalists) in, nobody out; Sudden Death forces a random round modifier
    private void StartRound(int number, List<int> finalists)
    {
        round = number;
        roundKills.Clear();
        var everyone = new List<int>();
        foreach (var p in PhotonNetwork.PlayerList) everyone.Add(p.ActorNumber);
        bool sudden = finalists != null;
        var props = new Hashtable
        {
            [RoomKeys.Round]          = number,
            [RoomKeys.LmsIn]          = (sudden ? finalists : everyone).ToArray(),
            [RoomKeys.LmsOut]         = new int[0],
            [RoomKeys.LmsSudden]      = sudden,
            [RoomKeys.LmsRoundWinner] = -1,
        };
        if (sudden) props[RoomKeys.Modifier] = RoundModifiers.PickNext(NetProps.Room(RoomKeys.Modifier, ""));
        Manager.SetPhase(MatchPhase.Countdown, Mathf.Max(MatchRules.CountdownTime, MinCountdown), props);
    }

    public override void MasterUpdate()
    {
        switch (Phase)
        {
            case MatchPhase.Countdown:
                if (Clock.IsExpired) Manager.SetPhase(MatchPhase.Active, MatchRules.RoundTime);
                break;

            case MatchPhase.Active:
                ActiveUpdate();
                break;

            case MatchPhase.RoundEnd:
                if (Clock.IsExpired) NextRound();
                break;

            case MatchPhase.MatchEnd:
                if (Clock.IsExpired) Manager.GoToScoreboard();
                break;
        }
    }

    private void ActiveUpdate()
    {
        var alive = new List<PhotonPlayer>();
        int playing = 0;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (!InTheRound(p.ActorNumber)) continue;
            playing++;
            if (IsPlaying(p.ActorNumber)) alive.Add(p);
        }

        // Practice alone: only the clock ends the round
        bool contest = playing >= 2;
        if (contest && alive.Count <= 1) { EndRound(alive.Count == 1 ? alive[0] : null); return; }
        if (!Clock.IsExpired) return;

        // Out of time: the survivor with the most kills this round, if one stands out
        PhotonPlayer best = null; int bestKills = -1; bool tie = false;
        foreach (var p in alive)
        {
            int k = roundKills.TryGetValue(p.ActorNumber, out int n) ? n : 0;
            if (k > bestKills) { best = p; bestKills = k; tie = false; }
            else if (k == bestKills) tie = true;
        }
        EndRound(tie ? null : best);
    }

    private void EndRound(PhotonPlayer winner)
    {
        if (winner != null)
        {
            Add(winner, PlayerKeys.Score, 1f);
            MatchStats.Add(winner, PlayerKeys.StatRoundsSurvived);
        }
        Manager.SetPhase(MatchPhase.RoundEnd, RoundEndSeconds,
            new Hashtable { [RoomKeys.LmsRoundWinner] = winner != null ? winner.ActorNumber : -1 });
    }

    private void NextRound()
    {
        bool lastRound = round >= MatchRules.RoundCount;
        if (!lastRound && !SuddenDeath) { StartRound(round + 1, null); return; }

        // After the last round (or a Sudden Death round): one leader wins; a tie for first goes to Sudden Death
        float top = float.MinValue;
        foreach (var p in PhotonNetwork.PlayerList) top = Mathf.Max(top, Get(p, PlayerKeys.Score));
        var leaders = new List<int>();
        foreach (var p in PhotonNetwork.PlayerList) if (Mathf.Approximately(Get(p, PlayerKeys.Score), top)) leaders.Add(p.ActorNumber);

        if (leaders.Count == 1 || PhotonNetwork.PlayerList.Length < 2)
        {
            Manager.SetPhase(MatchPhase.MatchEnd, MatchEndSeconds,
                new Hashtable { [RoomKeys.Winner] = leaders.Count > 0 ? leaders[0] : -1, [RoomKeys.LmsIn] = new int[0] });
            return;
        }
        StartRound(round + 1, leaders);
    }

    // ─── Master: scoring ──────────────────────────────────────────────────────

    public override void OnPlayerKilled(PhotonPlayer victim, PhotonPlayer killer, string weapon, bool headshot)
    {
        if (victim == null || weapon == SitOut) return;
        // Out for the rest of the round
        var outList = new List<int>(OutRound);
        if (!outList.Contains(victim.ActorNumber))
        {
            outList.Add(victim.ActorNumber);
            NetProps.SetRoom(RoomKeys.LmsOut, outList.ToArray());
        }
        Add(victim, PlayerKeys.StatDeaths, 1f);
        if (killer == null) { Add(victim, PlayerKeys.StatSelfDestructs, 1f); return; }
        Add(killer, PlayerKeys.StatKills, 1f);
        if (headshot) Add(killer, PlayerKeys.StatHeadshots, 1f);
        roundKills[killer.ActorNumber] = (roundKills.TryGetValue(killer.ActorNumber, out int k) ? k : 0) + 1;
    }

    public override void OnPlayerLeft(PhotonPlayer player) { } // Leavers just stop counting as alive

    private float Get(PhotonPlayer player, string key) =>
        tally.TryGetValue((player.ActorNumber, key), out float value) ? value : NetProps.Of(player, key, 0f);

    private void Add(PhotonPlayer player, string key, float amount)
    {
        if (player == null || amount == 0f) return;
        float value = Get(player, key) + amount;
        tally[(player.ActorNumber, key)] = value;
        NetProps.SetPlayer(player, key, value);
    }

    // ─── All clients ──────────────────────────────────────────────────────────

    public override void OnPhaseChanged(MatchPhase phase)
    {
        base.OnPhaseChanged(phase); // Countdown: back to a (spread out) spawn point
        if (phase != MatchPhase.Countdown) return;
        string text = SuddenDeath
            ? $"SUDDEN DEATH{(RoundModifiers.Current != null ? $"\n<size=60%>{RoundModifiers.Current.Name}</size>" : "")}"
            : $"Round {NetProps.Room(RoomKeys.Round, 1)} of {MatchRules.RoundCount}";
        if (HUDManager.Instance != null) HUDManager.Instance.ShowBanner(text, 2.5f);
    }

    // Standing while not in this round (a late joiner, a Sudden Death bystander): out quietly to watch
    private float nextSitOutCheck;

    /// <summary>The last seconds of a round in which everyone shows on everyone's map (no camping it out).</summary>
    public const float RevealSeconds = 45f;
    private int revealedRound = -1;

    private bool Revealing => Phase == MatchPhase.Active && Clock != null && Clock.HasLimit && Clock.Remaining <= RevealSeconds;

    public override bool ShowsOnMap(PlayerRole viewer, PlayerRole target) => Revealing || base.ShowsOnMap(viewer, target);

    public override void ClientUpdate()
    {
        // Announce the reveal once a round, on every screen
        int round = NetProps.Room(RoomKeys.Round, 1);
        if (Revealing && revealedRound != round)
        {
            revealedRound = round;
            Minimap.Pulse(new Color(1f, 0.3f, 0.3f));
            if (HUDManager.Instance != null) HUDManager.Instance.ShowBanner($"Final {RevealSeconds:0} seconds: everyone is on the map!", 3f);
        }
        if (Time.time < nextSitOutCheck) return;
        nextSitOutCheck = Time.time + 0.5f;
        var local = Player.Local;
        if (local == null || local.IsCaught || Phase == MatchPhase.Waiting || Phase == MatchPhase.MatchEnd) return;
        if (InRound.Length == 0 || InTheRound(local.ActorNumber)) return;
        if (CombatManager.Instance != null) CombatManager.Instance.ReportLocalDeath(-1, SitOut, false);
    }

    public override bool CanMove(PlayerRole role, MatchPhase phase) => phase != MatchPhase.Countdown;

    public override bool CanUseAbilities(PlayerRole role, MatchPhase phase) =>
        phase == MatchPhase.Active && MatchRules.GunModeAbilities;

    public override string FormatScore(float score) => Mathf.RoundToInt(score).ToString();

    public override string GetPhaseLabel(MatchPhase phase) => phase switch
    {
        MatchPhase.Countdown when SuddenDeath => "Sudden Death",
        MatchPhase.Active when SuddenDeath    => "Sudden Death",
        MatchPhase.Active                     => $"Round {NetProps.Room(RoomKeys.Round, 1)} of {MatchRules.RoundCount}",
        MatchPhase.RoundEnd                   => RoundWinnerLabel(),
        MatchPhase.MatchEnd                   => MatchWinnerLabel(),
        _                                     => base.GetPhaseLabel(phase),
    };

    private static string NameOf(int actor)
    {
        var p = PhotonNetwork.CurrentRoom?.GetPlayer(actor);
        return p == null ? null : string.IsNullOrEmpty(p.NickName) ? $"Player {actor}" : p.NickName;
    }

    private static string RoundWinnerLabel()
    {
        string name = NameOf(NetProps.Room(RoomKeys.LmsRoundWinner, -1));
        return name != null ? $"{name} is the last one standing!" : "Nobody wins the round";
    }

    private static string MatchWinnerLabel()
    {
        string name = NameOf(NetProps.Room(RoomKeys.Winner, -1));
        return name != null ? $"{name} wins!" : "Match Over";
    }

    public override string GetObjective(PlayerRole role, MatchPhase phase)
    {
        if (phase != MatchPhase.Active && phase != MatchPhase.Countdown) return "";
        if (SuddenDeath) return "Sudden Death: the last one standing wins the match.";
        return $"One life. Be the last one standing. {MatchRules.RoundCount} rounds.";
    }

    // ─── Scoreboard ───────────────────────────────────────────────────────────

    public override IReadOnlyList<ScoreboardColumn> ScoreboardColumns => new[]
    {
        new ScoreboardColumn("Rounds Won",      p => Mathf.RoundToInt(NetProps.Of(p, PlayerKeys.Score, 0f)).ToString()),
        new ScoreboardColumn("Kills",           p => MatchStats.Count(p, PlayerKeys.StatKills)),
        new ScoreboardColumn("Deaths",          p => MatchStats.Count(p, PlayerKeys.StatDeaths)),
        new ScoreboardColumn("Assists",         p => MatchStats.Count(p, PlayerKeys.StatAssists)),
        new ScoreboardColumn("Damage",          p => MatchStats.Count(p, PlayerKeys.StatDamage)),
        new ScoreboardColumn("Headshots",       p => MatchStats.Count(p, PlayerKeys.StatHeadshots)),
        new ScoreboardColumn("Self Destructs",  p => MatchStats.Count(p, PlayerKeys.StatSelfDestructs)),
        new ScoreboardColumn("Abilities Used",  p => MatchStats.Count(p, PlayerKeys.StatAbilitiesUsed)),
    };
}
