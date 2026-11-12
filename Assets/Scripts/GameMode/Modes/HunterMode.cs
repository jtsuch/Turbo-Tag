using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Hunter: one hunter (or more: Starting Hunters) with a pistol against everyone else, who only have abilities.
/// No health regeneration and no health drops. A hider who dies comes back as a ghost (<see cref="PlayerRole.Ghost"/>):
/// translucent to everyone, no hitbox (bullets and players pass through, nothing pushes them, their own blasts
/// included), but their abilities still hit the hunter. The hiders win if at least one of them is alive when
/// the clock runs out; the hunter wins the moment the last one falls.
///
/// Flow: Countdown (frozen) → Hiding (hiders scatter, the hunter waits) → Active (<see cref="MatchRules.MatchTime"/>)
/// → MatchEnd (<see cref="RoomKeys.WinningTeam"/>) → Scoreboard.
/// </summary>
public class HunterMode : GameMode
{
    public const string HunterTeam = "Hunter";
    public const string HiderTeam  = "Hiders";

    private const float MatchEndSeconds = 5f;

    private readonly HashSet<int> ghosted = new(); // Master: died this match (before the role echoes back)

    public override string DisplayName  => "Hunter";
    public override bool   UsesRoles    => true;
    public override bool   UsesGuns     => true;
    public override bool   AnnouncesTags => false;
    public override string CaughtBanner => "";
    public override string RespawnBanner =>
        Player.Local != null && Player.Local.Role == PlayerRole.Ghost ? "You're a ghost! Haunt the hunter." : "";
    public override bool   AllowsHealthDrops => false; // No healing at all

    // Only the hunter is armed (a pistol); hiders and ghosts have abilities only
    public override string FixedGun(PlayerRole role) => role == PlayerRole.Hunter ? "Pistol" : "";

    public override bool CanDamage(PlayerRole attacker, PlayerRole target) =>
        attacker == PlayerRole.Hunter && target == PlayerRole.Hider;

    // ─── Master: match flow ───────────────────────────────────────────────────

    public override void StartMatch()
    {
        ghosted.Clear();
        if (!TakeDealtRoles()) AssignRoles();
        Manager.SetPhase(MatchPhase.Countdown, MatchRules.CountdownTime, new Hashtable { [RoomKeys.Round] = 1 });
    }

    public override void MasterUpdate()
    {
        // Someone who joins once the hunt is on comes in as a ghost (no free hider, no second hunter)
        if (Phase == MatchPhase.Hiding || Phase == MatchPhase.Active)
            foreach (var p in PhotonNetwork.PlayerList)
                if (PlayerRoles.Get(p) == PlayerRole.None && ghosted.Add(p.ActorNumber))
                    PlayerRoles.Set(p, PlayerRole.Ghost);

        switch (Phase)
        {
            case MatchPhase.Countdown:
                if (Clock.IsExpired) Manager.SetPhase(MatchPhase.Hiding, MatchRules.HideTime);
                break;

            case MatchPhase.Hiding:
                if (Clock.IsExpired) Manager.SetPhase(MatchPhase.Active, MatchRules.MatchTime);
                break;

            case MatchPhase.Active:
                if (PhotonNetwork.PlayerList.Length >= 2 && LivingHiders().Count == 0) EndMatch(HunterTeam);
                else if (Clock.IsExpired) EndMatch(HiderTeam); // Someone made it
                break;

            case MatchPhase.MatchEnd:
                if (Clock.IsExpired) Manager.GoToScoreboard();
                break;
        }
    }

    protected override bool DealsRolesInPregame => true;
    protected override void AssignStartingRoles() => AssignRoles();

    private static void AssignRoles()
    {
        var players = new List<PhotonPlayer>(PhotonNetwork.PlayerList);
        for (int i = players.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (players[i], players[j]) = (players[j], players[i]);
        }
        int hunters = Mathf.Clamp(MatchRules.HunterCount, 1, Mathf.Max(0, players.Count - 1)); // Solo: a hider
        for (int i = 0; i < players.Count; i++)
            PlayerRoles.Set(players[i], i < hunters ? PlayerRole.Hunter : PlayerRole.Hider);
    }

    private List<PhotonPlayer> LivingHiders()
    {
        var hiders = PlayerRoles.With(PlayerRole.Hider);
        hiders.RemoveAll(p => ghosted.Contains(p.ActorNumber));
        return hiders;
    }

    private void EndMatch(string winner)
    {
        if (Phase == MatchPhase.MatchEnd) return;
        foreach (var hider in LivingHiders()) NetProps.SetPlayer(hider, PlayerKeys.StatTimeAlive, HuntSeconds);
        if (winner == HiderTeam)
            foreach (var hider in LivingHiders()) MatchStats.Add(hider, PlayerKeys.StatRoundsSurvived);
        Manager.SetPhase(MatchPhase.MatchEnd, MatchEndSeconds, new Hashtable { [RoomKeys.WinningTeam] = winner });
    }

    // Seconds into the hunt (0 before it starts)
    private float HuntSeconds => Phase == MatchPhase.Active && Clock != null ? Mathf.Max(0f, MatchRules.MatchTime - Clock.Remaining) : 0f;

    // ─── Master: deaths ───────────────────────────────────────────────────────

    public override void OnPlayerKilled(PhotonPlayer victim, PhotonPlayer killer, string weapon, bool headshot)
    {
        if (PlayerRoles.Get(victim) != PlayerRole.Hider || !ghosted.Add(victim.ActorNumber)) return; // Hunter falls: just respawn

        MatchStats.Add(victim, PlayerKeys.StatDeaths);
        NetProps.SetPlayer(victim, PlayerKeys.StatTimeAlive, HuntSeconds);
        PlayerRoles.Set(victim, PlayerRole.Ghost); // Comes back from the respawn as a ghost
        if (killer != null && PlayerRoles.Get(killer) == PlayerRole.Hunter)
        {
            MatchStats.Add(killer, PlayerKeys.StatKills);
            if (headshot) MatchStats.Add(killer, PlayerKeys.StatHeadshots);
            NetProps.SetPlayer(killer, PlayerKeys.Score, NetProps.Of(killer, PlayerKeys.Score, 0f) + 1f);
        }
    }

    public override void OnPlayerLeft(PhotonPlayer player)
    {
        if (Phase != MatchPhase.Hiding && Phase != MatchPhase.Active) return;
        if (PlayerRoles.Count(PlayerRole.Hunter) == 0) EndMatch(HiderTeam); // The hunter left
    }

    // ─── All clients ──────────────────────────────────────────────────────────

    public override void OnPhaseChanged(MatchPhase phase)
    {
        if (phase == MatchPhase.Countdown && Spawner.Instance != null)
            Spawner.Instance.RespawnLocalPlayer();
    }

    public override bool CanMove(PlayerRole role, MatchPhase phase) => phase switch
    {
        MatchPhase.Countdown => false,
        MatchPhase.Hiding    => role != PlayerRole.Hunter,
        _                    => true,
    };

    public override bool CanUseAbilities(PlayerRole role, MatchPhase phase) =>
        CanMove(role, phase) && (role != PlayerRole.Hunter || MatchRules.HunterAbilities);

    public override float SpeedMultiplier(PlayerRole role) =>
        role == PlayerRole.Hunter ? MatchRules.HunterSpeedMultiplier : 1f;

    public override float RespawnDelayAfter(PhotonPlayer tagger, PhotonPlayer victim) =>
        MatchRules.Killcam && tagger != null && tagger != victim ? Killcam.Length : MatchRules.RespawnDelay;

    // Hiders and ghosts see each other on the map; the hunter sees nobody
    public override bool ShowsOnMap(PlayerRole viewer, PlayerRole target) =>
        viewer != PlayerRole.Hunter && target != PlayerRole.Hunter;

    public override string RoleName(PlayerRole role) => role == PlayerRole.Ghost ? "GHOST" : base.RoleName(role);

    private static string Winner => NetProps.Room(RoomKeys.WinningTeam, "");

    private int HidersLeft => PlayerRoles.Count(PlayerRole.Hider);

    public override string GetPhaseLabel(MatchPhase phase) => phase switch
    {
        MatchPhase.Hiding   => "Hide!",
        MatchPhase.Active   => $"{HidersLeft} hider{(HidersLeft == 1 ? "" : "s")} left",
        MatchPhase.MatchEnd => Winner == HunterTeam ? "The Hunter Wins!" : "The Hiders Win!",
        _                   => base.GetPhaseLabel(phase),
    };

    public override string GetObjective(PlayerRole role, MatchPhase phase) => (role, phase) switch
    {
        (PlayerRole.Hunter, MatchPhase.Hiding) => "Wait... they're hiding. You'll have a pistol.",
        (PlayerRole.Hunter, _)                 => "Shoot every hider before the clock runs out!",
        (PlayerRole.Hider,  MatchPhase.Hiding) => "Hide! The hunter has a pistol.",
        (PlayerRole.Hider,  _)                 => "Stay alive! One survivor is a win for every hider.",
        (PlayerRole.Ghost,  _)                 => "You're a ghost: haunt the hunter with your abilities!",
        _                                      => "",
    };

    // ─── Scoreboard ───────────────────────────────────────────────────────────

    private static bool Won(PhotonPlayer p) =>
        (PlayerRoles.Get(p) == PlayerRole.Hunter) == (Winner == HunterTeam);

    public override IReadOnlyList<ScoreboardColumn> ScoreboardColumns => new[]
    {
        new ScoreboardColumn("Role", p => PlayerRoles.Get(p) switch
        {
            PlayerRole.Hunter => "Hunter",
            PlayerRole.Ghost  => "Ghost",
            _                 => "Survived",
        }),
        new ScoreboardColumn("Result",         p => Won(p) ? "Won" : "Lost"),
        new ScoreboardColumn("Time Alive",     p => PlayerRoles.Get(p) == PlayerRole.Hunter ? "-" : MatchStats.FormatTime(MatchStats.Get(p, PlayerKeys.StatTimeAlive))),
        new ScoreboardColumn("Kills",          p => MatchStats.Count(p, PlayerKeys.StatKills)),
        new ScoreboardColumn("Headshots",      p => MatchStats.Count(p, PlayerKeys.StatHeadshots)),
        new ScoreboardColumn("Abilities Used", p => MatchStats.Count(p, PlayerKeys.StatAbilitiesUsed)),
    };

    public override IReadOnlyList<ScoreboardColumn> LiveScoreColumns => new[]
    {
        new ScoreboardColumn("Role",           p => RoleName(PlayerRoles.Get(p))),
        new ScoreboardColumn("Kills",          p => MatchStats.Count(p, PlayerKeys.StatKills)),
        new ScoreboardColumn("Abilities Used", p => MatchStats.Count(p, PlayerKeys.StatAbilitiesUsed)),
    };

    // Winners first; survivors above ghosts; then kills
    public override int CompareForScoreboard(PhotonPlayer a, PhotonPlayer b)
    {
        int byWin = Won(b).CompareTo(Won(a));
        if (byWin != 0) return byWin;
        int byAlive = (PlayerRoles.Get(b) != PlayerRole.Ghost).CompareTo(PlayerRoles.Get(a) != PlayerRole.Ghost);
        if (byAlive != 0) return byAlive;
        return MatchStats.Get(b, PlayerKeys.StatKills).CompareTo(MatchStats.Get(a, PlayerKeys.StatKills));
    }

    public override string ScoreboardTitle(IReadOnlyList<PhotonPlayer> ranked) =>
        Winner == HunterTeam ? "The Hunter wins! Nobody survived." : "The Hiders win! Someone made it.";
}
