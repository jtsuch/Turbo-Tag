using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Free for All: every penguin for themself, with guns. Everyone spawns on the level's SpawnPoints, picks a
/// gun on every spawn, and respawns after each death (killcam first when shot). A kill is worth one point;
/// falling off the map is a self destruction and costs <see cref="MatchRules.FallPenalty"/> points (scores can
/// go below zero). Health is <see cref="MatchRules.MaxHealth"/> with no regeneration: shot players drop a health
/// pack. The match ends when the clock (<see cref="MatchRules.MatchTime"/>) runs out or someone reaches the
/// optional point limit; the most points wins.
///
/// Flow: Countdown (frozen, pick a gun) → Active → MatchEnd → Scoreboard. No roles.
/// </summary>
public class FreeForAllMode : GameMode
{
    private const float MatchEndSeconds = 5f;

    // Master: running totals, so two kills before Photon echoes the first property back both count
    private readonly Dictionary<(int actor, string key), float> tally = new();

    public override string DisplayName     => "Free for All";
    public override bool   ShowsScores     => true;
    public override bool   UsesGuns        => true;
    public override bool   CrownsLeader    => true;
    public override bool   UsesSpawnPoints => true;
    public override bool   AnnouncesTags   => false;
    public override string CaughtBanner    => "";
    public override string RespawnBanner   => "";

    // ─── Master: match flow ───────────────────────────────────────────────────

    public override void StartMatch()
    {
        tally.Clear();
        foreach (var player in PhotonNetwork.PlayerList)
            PlayerRoles.Set(player, PlayerRole.None);
        Scores.ResetAll();
        Manager.SetPhase(MatchPhase.Countdown, MatchRules.CountdownTime, new Hashtable { [RoomKeys.Round] = 1 });
    }

    public override void MasterUpdate()
    {
        switch (Phase)
        {
            case MatchPhase.Countdown:
                if (Clock.IsExpired) Manager.SetPhase(MatchPhase.Active, MatchRules.MatchTime);
                break;

            case MatchPhase.Active:
                float limit = MatchRules.PointLimit;
                if (Clock.IsExpired || (limit > 0f && LeaderPoints() >= limit)) EndMatch();
                break;

            case MatchPhase.MatchEnd:
                if (Clock.IsExpired) Manager.GoToScoreboard();
                break;
        }
    }

    private void EndMatch()
    {
        PhotonPlayer leader = null;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (leader == null) { leader = p; continue; }
            int byPoints = Get(p, PlayerKeys.Score).CompareTo(Get(leader, PlayerKeys.Score));
            if (byPoints > 0 || (byPoints == 0 && CompareForScoreboard(p, leader) < 0)) leader = p;
        }
        Manager.SetPhase(MatchPhase.MatchEnd, MatchEndSeconds,
            new Hashtable { [RoomKeys.Winner] = leader != null ? leader.ActorNumber : -1 });
    }

    private float LeaderPoints()
    {
        float best = float.MinValue;
        foreach (var p in PhotonNetwork.PlayerList) best = Mathf.Max(best, Get(p, PlayerKeys.Score));
        return best;
    }

    // ─── Master: scoring ──────────────────────────────────────────────────────

    public override void OnPlayerKilled(PhotonPlayer victim, PhotonPlayer killer, string weapon, bool headshot)
    {
        Add(victim, PlayerKeys.StatDeaths, 1f);

        if (killer == null)
        {
            Add(victim, PlayerKeys.StatSelfDestructs, 1f);
            Add(victim, PlayerKeys.Score, -MatchRules.FallPenalty);
            return;
        }

        Add(killer, PlayerKeys.Score, 1f);
        Add(killer, PlayerKeys.StatKills, 1f);
        if (headshot) Add(killer, PlayerKeys.StatHeadshots, 1f);
    }

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
        if (phase == MatchPhase.Countdown && Spawner.Instance != null)
            Spawner.Instance.RespawnLocalPlayer();
    }

    public override bool CanMove(PlayerRole role, MatchPhase phase) => phase != MatchPhase.Countdown;

    // Abilities only while the match is live (none on the winner screen: no last-second knock-offs)
    public override bool CanUseAbilities(PlayerRole role, MatchPhase phase) =>
        phase == MatchPhase.Active && MatchRules.GunModeAbilities;

    // Shot dead: the killcam's length (skippable). Fell off: the normal respawn delay.
    public override float RespawnDelayAfter(PhotonPlayer tagger, PhotonPlayer victim) =>
        MatchRules.Killcam && tagger != null && tagger != victim ? Killcam.Length : MatchRules.RespawnDelay;

    // Everyone reads everyone's name (no teams)
    public override bool ShowsName(PlayerRole viewer, PlayerRole target) => true;

    public override string FormatScore(float score) => Mathf.RoundToInt(score).ToString();

    public override string GetPhaseLabel(MatchPhase phase) => phase switch
    {
        MatchPhase.Active   => "Free for All",
        MatchPhase.MatchEnd => WinnerLabel(),
        _                   => base.GetPhaseLabel(phase),
    };

    private static string WinnerLabel()
    {
        int actor = NetProps.Room(RoomKeys.Winner, -1);
        var winner = PhotonNetwork.CurrentRoom?.GetPlayer(actor);
        return winner != null ? $"{winner.NickName} wins!" : "Match Over";
    }

    public override string GetObjective(PlayerRole role, MatchPhase phase)
    {
        if (phase != MatchPhase.Active && phase != MatchPhase.Countdown) return "";
        float limit = MatchRules.PointLimit;
        string goal = limit > 0f ? $"First to {limit:0} points" : "Most points wins";
        float penalty = MatchRules.FallPenalty;
        return penalty > 0f ? $"{goal}. Falling off costs {penalty:0}." : goal + ".";
    }

    // ─── Scoreboard ───────────────────────────────────────────────────────────

    private static float Points(PhotonPlayer p) => NetProps.Of(p, PlayerKeys.Score, 0f);

    public override IReadOnlyList<ScoreboardColumn> ScoreboardColumns => new[]
    {
        new ScoreboardColumn("Points",          p => Mathf.RoundToInt(Points(p)).ToString()),
        new ScoreboardColumn("Kills",           p => MatchStats.Count(p, PlayerKeys.StatKills)),
        new ScoreboardColumn("Deaths",          p => MatchStats.Count(p, PlayerKeys.StatDeaths)),
        new ScoreboardColumn("K/D",             p => (MatchStats.Get(p, PlayerKeys.StatKills) / Mathf.Max(1f, MatchStats.Get(p, PlayerKeys.StatDeaths))).ToString("0.0")),
        new ScoreboardColumn("Self Destructs",  p => MatchStats.Count(p, PlayerKeys.StatSelfDestructs)),
        new ScoreboardColumn("Headshots",       p => MatchStats.Count(p, PlayerKeys.StatHeadshots)),
        new ScoreboardColumn("Abilities Used",  p => MatchStats.Count(p, PlayerKeys.StatAbilitiesUsed)),
    };

    // Most points, then most kills, then fewest deaths
    public override int CompareForScoreboard(PhotonPlayer a, PhotonPlayer b)
    {
        int byPoints = Points(b).CompareTo(Points(a));
        if (byPoints != 0) return byPoints;
        int byKills = MatchStats.Get(b, PlayerKeys.StatKills).CompareTo(MatchStats.Get(a, PlayerKeys.StatKills));
        if (byKills != 0) return byKills;
        return MatchStats.Get(a, PlayerKeys.StatDeaths).CompareTo(MatchStats.Get(b, PlayerKeys.StatDeaths));
    }

    public override string ScoreboardTitle(IReadOnlyList<PhotonPlayer> ranked) =>
        ranked.Count > 0 ? $"{ranked[0].NickName} wins!" : "Match Over";
}
