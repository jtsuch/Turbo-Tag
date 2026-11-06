using System.Collections.Generic;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Team Deathmatch: Red against Blue, with guns. Every kill is a point for the killer's team; falling off the map
/// costs the faller's team <see cref="MatchRules.FallPenalty"/>. No friendly fire (bullets pass through teammates).
/// Pick a gun on every spawn, health drops and killcam as in Free for All; abilities on unless the host turns
/// them off. First team to the point limit, or the most points when the clock runs out, wins.
/// </summary>
public class TeamDeathmatchMode : TeamMode
{
    public override string DisplayName => "Team Deathmatch";
    public override bool   UsesGuns    => true;

    public override bool CanDamage(PlayerRole attacker, PlayerRole target) => !SameTeam(attacker, target);

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
        if (SameTeam(PlayerRoles.Get(killer), PlayerRoles.Get(victim))) return; // Can't happen (no friendly fire), but never a point
        Add(killer, PlayerKeys.Score, 1f);
        Add(killer, PlayerKeys.StatKills, 1f);
        if (headshot) Add(killer, PlayerKeys.StatHeadshots, 1f);
    }

    // ─── All clients ──────────────────────────────────────────────────────────

    public override bool CanUseAbilities(PlayerRole role, MatchPhase phase) =>
        phase == MatchPhase.Active && MatchRules.GunModeAbilities;

    public override float RespawnDelayAfter(PhotonPlayer tagger, PhotonPlayer victim) =>
        MatchRules.Killcam && tagger != null && tagger != victim ? Killcam.Length : MatchRules.RespawnDelay;

    public override string GetPhaseLabel(MatchPhase phase) =>
        phase == MatchPhase.Active ? "Team Deathmatch" : base.GetPhaseLabel(phase);

    public override string GetObjective(PlayerRole role, MatchPhase phase)
    {
        if (phase != MatchPhase.Active && phase != MatchPhase.Countdown) return "";
        float limit = MatchRules.PointLimit;
        return limit > 0f ? $"First team to {limit:0} kills wins." : "Most kills when time runs out wins.";
    }

    // ─── Scoreboard ───────────────────────────────────────────────────────────

    public override IReadOnlyList<ScoreboardColumn> ScoreboardColumns => new[]
    {
        new ScoreboardColumn("Points",         p => Mathf.RoundToInt(NetProps.Of(p, PlayerKeys.Score, 0f)).ToString()),
        new ScoreboardColumn("Team",           TeamLabel),
        new ScoreboardColumn("Kills",          p => MatchStats.Count(p, PlayerKeys.StatKills)),
        new ScoreboardColumn("Deaths",         p => MatchStats.Count(p, PlayerKeys.StatDeaths)),
        new ScoreboardColumn("K/D",            p => (MatchStats.Get(p, PlayerKeys.StatKills) / Mathf.Max(1f, MatchStats.Get(p, PlayerKeys.StatDeaths))).ToString("0.0")),
        new ScoreboardColumn("Headshots",      p => MatchStats.Count(p, PlayerKeys.StatHeadshots)),
        new ScoreboardColumn("Abilities Used", p => MatchStats.Count(p, PlayerKeys.StatAbilitiesUsed)),
    };
}
