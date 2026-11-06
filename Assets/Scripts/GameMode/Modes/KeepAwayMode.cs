using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Keep Away: Red against Blue over one disc (<see cref="KeepAwayDisc"/>). Touch the loose disc to grab it; touch an
/// enemy holder to steal it. While a team holds the disc it scores a point per second (nothing while it flies or
/// lies loose). Holders keep their abilities (except throws) and throw the disc with the Action key (hold to
/// throw further). First team to the point limit, or the higher score when the clock runs out, wins.
/// Points go to the holder's own score too (the scoreboard's "Held" column).
/// </summary>
public class KeepAwayMode : TeamMode
{
    private const float FlushEvery = 0.5f;

    // Master: hold time not yet written to the holder's score
    private float pending;
    private int   pendingHolder = -1;
    private float nextFlush;

    public override string DisplayName => "Keep Away";

    // ─── Master ───────────────────────────────────────────────────────────────

    public override void StartMatch()
    {
        base.StartMatch();
        // One disc per level, loose at a random spawn point
        if (KeepAwayDisc.Instance == null)
            PhotonNetwork.InstantiateRoomObject("Object/KeepAwayDisc", KeepAwayDisc.SpawnSpot(), Quaternion.identity);
        else KeepAwayDisc.Instance.ResetToSpawn();
    }

    protected override void ActiveUpdate()
    {
        var disc = KeepAwayDisc.Instance;
        int holder = disc != null ? disc.Holder : -1;
        var holderPlayer = holder >= 0 ? PhotonNetwork.CurrentRoom.GetPlayer(holder) : null;

        if (holderPlayer != null && PlayerRoles.IsTeam(PlayerRoles.Get(holderPlayer)))
        {
            if (pendingHolder != holder) Flush();
            pendingHolder = holder;
            var holding = Player.Find(holder);
            pending += Time.deltaTime * (holding != null && holding.Has(PlayerFlag.DoubleScore) ? 2f : 1f); // Point Frenzy donut
        }
        if (Time.time >= nextFlush || holderPlayer == null) Flush();
    }

    private void Flush()
    {
        nextFlush = Time.time + FlushEvery;
        if (pendingHolder >= 0 && pending > 0f)
        {
            var p = PhotonNetwork.CurrentRoom.GetPlayer(pendingHolder);
            if (p != null) Add(p, PlayerKeys.Score, pending);
        }
        pending = 0f;
        pendingHolder = -1;
    }

    // ─── All clients ──────────────────────────────────────────────────────────

    // An enemy holder touching you: steal (the host decides)
    public override void OnLocalPlayerContact(Player local, Player other)
    {
        if (Phase != MatchPhase.Active || KeepAwayDisc.Instance == null) return;
        if (KeepAwayDisc.Instance.Holder != other.ActorNumber || SameTeam(local.Role, other.Role)) return;
        KeepAwayDisc.Instance.RequestSteal(other);
    }

    public override bool CanUseAbilities(PlayerRole role, MatchPhase phase) => phase == MatchPhase.Active;

    protected override string FormatTeamScore(float score) => Mathf.FloorToInt(score).ToString();

    private static PlayerRole HolderTeam
    {
        get
        {
            var disc = KeepAwayDisc.Instance;
            int holder = disc != null ? disc.Holder : -1;
            return holder >= 0 ? PlayerRoles.Get(holder) : PlayerRole.None;
        }
    }

    public override string GetPhaseLabel(MatchPhase phase)
    {
        if (phase != MatchPhase.Active) return base.GetPhaseLabel(phase);
        return HolderTeam switch
        {
            PlayerRole.TeamRed  => "Red has the disc",
            PlayerRole.TeamBlue => "Blue has the disc",
            _                   => "The disc is loose!",
        };
    }

    public override string GetObjective(PlayerRole role, MatchPhase phase)
    {
        if (phase == MatchPhase.Countdown) return "Grab the disc and keep it from the other team.";
        if (phase != MatchPhase.Active) return "";
        var disc = KeepAwayDisc.Instance;
        if (disc != null && Player.Local != null && disc.Holder == Player.Local.ActorNumber)
            return "You have it! Run, or hold the Action key to throw it to a teammate.";
        var team = HolderTeam;
        float limit = MatchRules.PointLimit;
        string goal = limit > 0f ? $" First to {limit:0}." : "";
        return team == PlayerRole.None ? "Grab the loose disc!" + goal
             : team == role ? "Your team has it: keep it away!" + goal
             : "Touch their holder to steal it!" + goal;
    }

    // ─── Scoreboard ───────────────────────────────────────────────────────────

    public override IReadOnlyList<ScoreboardColumn> ScoreboardColumns => new[]
    {
        new ScoreboardColumn("Held",           p => $"{Mathf.FloorToInt(NetProps.Of(p, PlayerKeys.Score, 0f))}s"),
        new ScoreboardColumn("Team",           TeamLabel),
        new ScoreboardColumn("Steals",         p => MatchStats.Count(p, PlayerKeys.StatSteals)),
        new ScoreboardColumn("Grabs",          p => MatchStats.Count(p, PlayerKeys.StatCatches)),
        new ScoreboardColumn("Abilities Used", p => MatchStats.Count(p, PlayerKeys.StatAbilitiesUsed)),
    };
}
