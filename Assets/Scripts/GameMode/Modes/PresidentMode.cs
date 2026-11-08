using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Protect the President: one or more hunters, one President, and everyone else defends the
/// President with their abilities. Only the President can be tagged; that ends the match and the
/// hunters win. If the President survives <see cref="MatchRules.PresidentTime"/>, the defenders win.
///
/// Flow (one round): Countdown (everyone frozen) → Hiding (setup: defenders and President move,
/// hunters frozen) → Active (hunt, survival clock) → MatchEnd → Scoreboard.
/// Roles: Hunter, President, and Hider (shown as "Defender"). During setup hunters see every other player
/// as a black silhouette (no names), so they can't tell who the President is; after that the President
/// wears a crown. Defenders see the crown from the start, so they know who to guard.
/// </summary>
public class PresidentMode : GameMode
{
    public const string HuntersTeam   = "Hunters";
    public const string DefendersTeam = "Defenders";

    private const float MatchEndSeconds     = 5f;
    private const float ContactRetrySeconds = 0.5f;

    private readonly Dictionary<int, float> lastContactRequest = new(); // Local: throttles tag requests

    public override string DisplayName => "Protect the President";
    public override bool HunterRadar => true;
    public override bool HasHunterClasses => true;
    public override bool   UsesRoles   => true;

    // Tagging the President ends the match: they stay down, watching, until the scoreboard
    public override bool   RespawnsAfterTag => false;

    // ─── Master: match flow ───────────────────────────────────────────────────

    public override void StartMatch()
    {
        if (!TakeDealtRoles()) AssignRoles();
        Manager.SetPhase(MatchPhase.Countdown, MatchRules.CountdownTime, new Hashtable { [RoomKeys.Round] = 1 });
    }

    public override void MasterUpdate()
    {
        switch (Phase)
        {
            case MatchPhase.Countdown:
                if (Clock.IsExpired) Manager.SetPhase(MatchPhase.Hiding, MatchRules.HideTime);
                break;

            case MatchPhase.Hiding:
                if (Clock.IsExpired) Manager.SetPhase(MatchPhase.Active, MatchRules.PresidentTime);
                break;

            case MatchPhase.Active:
                if (Clock.IsExpired) EndMatch(DefendersTeam); // The President made it
                break;

            case MatchPhase.MatchEnd:
                if (Clock.IsExpired) Manager.GoToScoreboard();
                break;
        }
    }

    protected override bool DealsRolesInPregame => true;
    protected override void AssignStartingRoles() => AssignRoles();

    /// <summary>Random hunters (at least one President is always left), a random President, the rest defend.</summary>
    private static void AssignRoles()
    {
        var players = new List<PhotonPlayer>(PhotonNetwork.PlayerList);
        Shuffle(players);

        int hunterCount = Mathf.Clamp(MatchRules.HunterCount, 1, Mathf.Max(0, players.Count - 1));
        for (int i = 0; i < players.Count; i++)
        {
            PlayerRole role = i < hunterCount ? PlayerRole.Hunter
                            : i == hunterCount ? PlayerRole.President
                            : PlayerRole.Hider;
            PlayerRoles.Set(players[i], role);
        }
    }

    private void EndMatch(string winningTeam)
    {
        if (Phase == MatchPhase.MatchEnd) return;
        Manager.SetPhase(MatchPhase.MatchEnd, MatchEndSeconds, new Hashtable { [RoomKeys.WinningTeam] = winningTeam });
    }

    // ─── Master: tagging ──────────────────────────────────────────────────────

    public override void HandleTagRequest(PhotonPlayer tagger, PhotonPlayer target)
    {
        if (Phase != MatchPhase.Active || tagger == null || target == null) return;
        if (PlayerRoles.Get(tagger) != PlayerRole.Hunter)    return;
        if (PlayerRoles.Get(target) != PlayerRole.President) return; // Defenders can't be tagged

        MatchStats.Add(tagger, PlayerKeys.StatTags);
        MatchStats.Add(target, PlayerKeys.StatTimesTagged);
        HunterStats.RecordCatch(tagger, Clock.Elapsed);
        Manager.BroadcastTag(tagger, target);
        EndMatch(HuntersTeam);
    }

    public override void OnPlayerLeft(PhotonPlayer player)
    {
        bool running = Phase == MatchPhase.Countdown || Phase == MatchPhase.Hiding || Phase == MatchPhase.Active;
        if (!running) return;

        if (PlayerRoles.Count(PlayerRole.Hunter) == 0) { EndMatch(DefendersTeam); return; }

        // The President left: a defender steps up, or the hunters win if nobody is left to protect
        if (PlayerRoles.Count(PlayerRole.President) == 0)
        {
            var defenders = PlayerRoles.With(PlayerRole.Hider);
            if (defenders.Count == 0) EndMatch(HuntersTeam);
            else PlayerRoles.Set(defenders[Random.Range(0, defenders.Count)], PlayerRole.President);
        }
    }

    // ─── All clients ──────────────────────────────────────────────────────────

    public override void OnPhaseChanged(MatchPhase phase)
    {
        if (phase == MatchPhase.Countdown && Spawner.Instance != null)
            Spawner.Instance.RespawnLocalPlayer();
    }

    public override void OnLocalPlayerContact(Player local, Player other)
    {
        if (Phase != MatchPhase.Active) return;
        if (local.Role != PlayerRole.Hunter || other.Role != PlayerRole.President) return;

        int actor = other.ActorNumber;
        if (lastContactRequest.TryGetValue(actor, out float last) && Time.time - last < ContactRetrySeconds) return;
        lastContactRequest[actor] = Time.time;
        Manager.RequestTag(other);
    }

    public override bool CanMove(PlayerRole role, MatchPhase phase) => phase switch
    {
        MatchPhase.Countdown => false,
        MatchPhase.Hiding    => role != PlayerRole.Hunter,
        _                    => true,
    };

    public override bool CanUseAbilities(PlayerRole role, MatchPhase phase) =>
        CanMove(role, phase) && role switch
        {
            PlayerRole.Hunter    => MatchRules.HunterAbilities,
            PlayerRole.President => MatchRules.PresidentAbilities,
            _                    => MatchRules.HiderAbilities,
        };

    public override float SpeedMultiplier(PlayerRole role) => role switch
    {
        PlayerRole.Hunter    => MatchRules.HunterSpeedMultiplier,
        PlayerRole.President => MatchRules.PresidentSpeedMultiplier,
        _                    => 1f,
    };

    public override float CooldownMultiplier(PlayerRole role) =>
        role == PlayerRole.Hunter ? 1f - MatchRules.HunterCooldownReduction : 1f;

    // Hunters can't tell anyone apart until the hunt starts (decoys and bluffing during setup)
    public override bool HidesIdentity(PlayerRole viewer, PlayerRole target, MatchPhase phase) =>
        viewer == PlayerRole.Hunter && target != PlayerRole.Hunter
        && (phase == MatchPhase.Countdown || phase == MatchPhase.Hiding);

    // Two teams: hunters, and the President with the defenders; the defenders see the hunters too
    public override bool ShowsOnMap(PlayerRole viewer, PlayerRole target) =>
        viewer != PlayerRole.None && (viewer != PlayerRole.Hunter || target == PlayerRole.Hunter);

    // Teammates through walls (the President yellow), and the hunters (red) for the President and defenders
    public override int SeesThroughWalls(PlayerRole viewer, PlayerRole target) =>
        viewer != PlayerRole.None && viewer != PlayerRole.Hunter && target == PlayerRole.Hunter ? 2 : base.SeesThroughWalls(viewer, target);

    public override bool WearsCrown(PlayerRole role, MatchPhase phase) =>
        role == PlayerRole.President && phase != MatchPhase.Waiting;

    public override string RoleName(PlayerRole role) =>
        role == PlayerRole.Hider ? "DEFENDER" : base.RoleName(role);

    public override string GetPhaseLabel(MatchPhase phase) => phase switch
    {
        MatchPhase.Hiding   => "Set Up!",
        MatchPhase.Active   => "Protect the President!",
        MatchPhase.MatchEnd => NetProps.Room(RoomKeys.WinningTeam, "") == HuntersTeam ? "Hunters Win!" : "Defenders Win!",
        _                   => base.GetPhaseLabel(phase),
    };

    public override string GetObjective(PlayerRole role, MatchPhase phase) => (role, phase) switch
    {
        (PlayerRole.Hunter,    MatchPhase.Hiding) => "Wait... they're setting up their defences.",
        (PlayerRole.Hunter,    _)                 => "Tag the President (the one with the crown)!",
        (PlayerRole.President, MatchPhase.Hiding) => "Find a safe spot with your defenders.",
        (PlayerRole.President, _)                 => "Survive! Only you can be tagged.",
        (PlayerRole.Hider,     MatchPhase.Hiding) => "Set up defences around the President.",
        (PlayerRole.Hider,     _)                 => "Protect the President (crown) with your abilities!",
        _                                         => "",
    };

    // ─── Scoreboard ───────────────────────────────────────────────────────────

    private static string Team => NetProps.Room(RoomKeys.WinningTeam, "");

    private static bool Won(PhotonPlayer p)
    {
        bool hunter = PlayerRoles.Get(p) == PlayerRole.Hunter;
        return Team == (hunter ? HuntersTeam : DefendersTeam);
    }

    public override IReadOnlyList<ScoreboardColumn> ScoreboardColumns => new[]
    {
        new ScoreboardColumn("Role",           p => PlayerRoles.Get(p) switch
        {
            PlayerRole.Hunter    => "Hunter",
            PlayerRole.President => "President",
            _                    => "Defender",
        }),
        new ScoreboardColumn("Result",         p => Won(p) ? "Won" : "Lost"),
        new ScoreboardColumn("Tags",           p => MatchStats.Count(p, PlayerKeys.StatTags)),
        new ScoreboardColumn("Abilities Used", p => MatchStats.Count(p, PlayerKeys.StatAbilitiesUsed)),
    };

    // Mid-match: no result yet; roles are hidden from hunters during setup like everything else
    public override IReadOnlyList<ScoreboardColumn> LiveScoreColumns => new[]
    {
        new ScoreboardColumn("Role", p =>
        {
            var role = PlayerRoles.Get(p);
            bool hidden = Manager != null && Player.Local != null && !p.IsLocal
                && HidesIdentity(Player.Local.Role, role, Phase);
            return hidden ? "?" : RoleName(role);
        }),
        new ScoreboardColumn("Tags",           p => MatchStats.Count(p, PlayerKeys.StatTags)),
        new ScoreboardColumn("Abilities Used", p => MatchStats.Count(p, PlayerKeys.StatAbilitiesUsed)),
    };

    // Winners first, the President at the top of their team, then the busiest ability users
    public override int CompareForScoreboard(PhotonPlayer a, PhotonPlayer b)
    {
        int byWin = Won(b).CompareTo(Won(a));
        if (byWin != 0) return byWin;
        int byPresident = (PlayerRoles.Get(b) == PlayerRole.President).CompareTo(PlayerRoles.Get(a) == PlayerRole.President);
        if (byPresident != 0) return byPresident;
        return MatchStats.Get(b, PlayerKeys.StatAbilitiesUsed).CompareTo(MatchStats.Get(a, PlayerKeys.StatAbilitiesUsed));
    }

    public override string ScoreboardTitle(IReadOnlyList<PhotonPlayer> ranked) => Team switch
    {
        HuntersTeam   => "Hunters win! The President was tagged.",
        DefendersTeam => "Defenders win! The President survived.",
        _             => "Match Over",
    };

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
