using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Race (Justin, Nov 18): everyone starts on the course's start line and races through its gates
/// (<see cref="RaceCheckpoint"/>) in order to the finish. A fall puts you back at the last gate passed. The first
/// finisher closes the race: everyone else has <see cref="FinishWindow"/> seconds left, or until all have finished,
/// or the clock (<see cref="MatchRules.MatchTime"/>) runs out. Places by finishing time; anyone who didn't finish
/// ranks by how many gates they passed. Plays on the Race maps (<c>Assets/Scenes/Levels/Race/</c>).
///
/// Rules: <see cref="MatchRules.RaceCollisions"/> (off: players pass through each other and can't touch each other
/// with abilities, but still see each other) and <see cref="MatchRules.AbilityVote"/> (Pregame is a vote: one
/// ability per slot is drawn for everyone, each weighted by its votes).
///
/// Flow: Countdown (frozen on the start line) → Active → MatchEnd → Scoreboard. No roles.
/// </summary>
public class RaceMode : GameMode
{
    public const float FinishWindow = 30f;
    private const float MatchEndSeconds = 6f;

    public override string       DisplayName     => "Race";
    public override ModeCategory Category        => ModeCategory.Movement;
    public override bool         ShowsScores     => true;
    public override bool         UsesSpawnPoints => true;   // The start line
    public override bool         AnnouncesTags   => false;
    public override string       CaughtBanner    => "";
    public override string       RespawnBanner   => "";
    public override bool         PlayersCollide  => MatchRules.RaceCollisions;
    public override bool         UsesAbilityVote => MatchRules.AbilityVote;

    // XP by finishing place (Score is placement points, written at the end)
    public override float  XpPerPoint   => 25f;
    public override string XpScoreLabel => "Finishing place";

    // Master
    private bool   closing;
    private int    announcedWinner = -1;

    // ─── Master: flow ─────────────────────────────────────────────────────────

    public override void StartMatch()
    {
        closing = false;
        announcedWinner = -1;
        foreach (var player in PhotonNetwork.PlayerList) PlayerRoles.Set(player, PlayerRole.None);
        Scores.ResetAll();
        Manager.SetPhase(MatchPhase.Countdown, MatchRules.CountdownTime,
            new Hashtable { [RoomKeys.Round] = 1, [RoomKeys.RaceCloseAt] = 0d });
    }

    public override void MasterUpdate()
    {
        switch (Phase)
        {
            case MatchPhase.Countdown:
                if (Clock.IsExpired) Manager.SetPhase(MatchPhase.Active, MatchRules.MatchTime);
                break;

            case MatchPhase.Active:
                ActiveUpdate();
                break;

            case MatchPhase.MatchEnd:
                if (Clock.IsExpired) Manager.GoToScoreboard();
                break;
        }
    }

    private void ActiveUpdate()
    {
        var players = PhotonNetwork.PlayerList;
        int finished = 0;
        PhotonPlayer first = null;
        foreach (var p in players)
        {
            if (FinishOf(p) < 0f) continue;
            finished++;
            if (first == null || FinishOf(p) < FinishOf(first)) first = p;
        }

        // The first finisher closes the race (once): a highlight, and the window for everyone else
        if (first != null && !closing)
        {
            closing = true;
            NetProps.SetRoom(RoomKeys.RaceCloseAt, PhotonNetwork.Time + FinishWindow);
        }
        if (first != null && announcedWinner != first.ActorNumber)
        {
            announcedWinner = first.ActorNumber;
            Highlights.Offer(first.ActorNumber, -1, 45f, $"{Name(first)} wins the race!");
        }

        double closeAt = NetProps.Room(RoomKeys.RaceCloseAt, 0d);
        bool allDone = players.Length > 0 && finished == players.Length;
        if (Clock.IsExpired || allDone || (closeAt > 0d && PhotonNetwork.Time >= closeAt)) EndRace();
    }

    // Placement points: last place 1, each place above one more; no finish, nothing
    private void EndRace()
    {
        if (Phase == MatchPhase.MatchEnd) return;
        var ranked = new List<PhotonPlayer>(PhotonNetwork.PlayerList);
        ranked.Sort(CompareForScoreboard);
        int n = ranked.Count;
        for (int i = 0; i < n; i++)
            NetProps.SetPlayer(ranked[i], PlayerKeys.Score, FinishOf(ranked[i]) >= 0f ? (float)(n - i) : 0f);
        Manager.SetPhase(MatchPhase.MatchEnd, MatchEndSeconds,
            new Hashtable { [RoomKeys.Winner] = n > 0 && FinishOf(ranked[0]) >= 0f ? ranked[0].ActorNumber : -1 });
    }

    // ─── Every client ─────────────────────────────────────────────────────────

    public override void OnPhaseChanged(MatchPhase phase)
    {
        if (phase != MatchPhase.Countdown) return;
        RaceProgress.Reset();
        if (Spawner.Instance != null) Spawner.Instance.RespawnLocalPlayer();   // Back on the start line
        AnnounceAbilities();
    }

    // The vote's result: everyone's abilities this race
    private static void AnnounceAbilities()
    {
        var forced = NetProps.Room(RoomKeys.ForcedLoadout, System.Array.Empty<string>());
        if (forced.Length != 4 || HUDManager.Instance == null) return;
        var names = new List<string>();
        foreach (var id in forced) if (!string.IsNullOrEmpty(id)) names.Add(AbilityCatalog.DisplayName(id));
        HUDManager.Instance.ShowBanner($"THIS RACE\n<size=50%>{string.Join("  ·  ", names)}</size>", 4f);
    }

    // A fall after a gate: back at that gate
    public override bool TryGetRespawn(PlayerRole role, MatchPhase phase, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (phase != MatchPhase.Active) return false;
        var gate = RaceProgress.Last;
        if (gate == null) return false;
        gate.RespawnPose(out position, out rotation);
        return true;
    }

    public override bool CanMove(PlayerRole role, MatchPhase phase) => phase != MatchPhase.Countdown;
    public override bool CanUseAbilities(PlayerRole role, MatchPhase phase) => phase == MatchPhase.Active;
    public override bool ShowsOnMap(PlayerRole viewer, PlayerRole target) => true;   // Everyone sees the field

    // ─── Ranking (synced state only: the Scoreboard uses it with no manager) ───

    private static float FinishOf(PhotonPlayer p) => NetProps.Of(p, PlayerKeys.RaceFinish, -1f);
    private static int   GatesOf(PhotonPlayer p)  => NetProps.Of(p, PlayerKeys.RaceCheckpoint, 0);

    public override int CompareForScoreboard(PhotonPlayer a, PhotonPlayer b)
    {
        float fa = FinishOf(a), fb = FinishOf(b);
        if (fa >= 0f && fb >= 0f) return fa.CompareTo(fb);     // Both finished: the faster first
        if (fa >= 0f != fb >= 0f) return fa >= 0f ? -1 : 1;    // A finisher above anyone who didn't
        int byGates = GatesOf(b).CompareTo(GatesOf(a));         // Neither: further along first
        return byGates != 0 ? byGates : a.ActorNumber.CompareTo(b.ActorNumber);
    }

    public override bool DidWin(PhotonPlayer player) => player != null && NetProps.Room(RoomKeys.Winner, -1) == player.ActorNumber;

    public override string ScoreboardTitle(IReadOnlyList<PhotonPlayer> ranked) =>
        ranked.Count > 0 && FinishOf(ranked[0]) >= 0f ? $"{Name(ranked[0])} wins the race!" : "Nobody finished!";

    public override IReadOnlyList<ScoreboardColumn> ScoreboardColumns => new[]
    {
        new ScoreboardColumn("Time", p => FinishOf(p) >= 0f ? FormatTime(FinishOf(p)) : "DNF"),
        new ScoreboardColumn("Gates", p => $"{GatesOf(p)} / {Mathf.Max(1, RaceCheckpoint.Count)}"),
        new ScoreboardColumn("Points", p => Mathf.RoundToInt(NetProps.Of(p, PlayerKeys.Score, 0f)).ToString()),
    };

    public override string FormatScore(float score) => Mathf.RoundToInt(score).ToString();

    /// <summary>"1:23.45".</summary>
    public static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int minutes = Mathf.FloorToInt(seconds / 60f);
        return $"{minutes}:{seconds - minutes * 60f:00.00}";
    }

    private static string Name(PhotonPlayer p) => string.IsNullOrEmpty(p?.NickName) ? $"Player {p?.ActorNumber}" : p.NickName;

    // ─── HUD text ─────────────────────────────────────────────────────────────

    public override string GetPhaseLabel(MatchPhase phase) => phase switch
    {
        MatchPhase.Countdown => "On your marks",
        MatchPhase.Active    => "Race!",
        MatchPhase.MatchEnd  => "Race over",
        _                    => base.GetPhaseLabel(phase),
    };

    public override string GetObjective(PlayerRole role, MatchPhase phase)
    {
        if (phase == MatchPhase.Countdown) return "Race to the finish through every gate. Fall and you're back at the last one.";
        if (phase != MatchPhase.Active) return "";
        int place = LocalPlace();
        string closes = "";
        double closeAt = NetProps.Room(RoomKeys.RaceCloseAt, 0d);
        if (closeAt > 0d) closes = $"  ·  race closes in {Mathf.Max(0, Mathf.CeilToInt((float)(closeAt - PhotonNetwork.Time)))}s";
        if (RaceProgress.Finished) return $"Finished {Ordinal(place)}  ·  {FormatTime(RaceProgress.FinishTime)}{closes}";
        return $"Gate {RaceProgress.Passed} / {Mathf.Max(1, RaceCheckpoint.Count)}  ·  {Ordinal(place)}{closes}";
    }

    // This player's place right now: finishers by time, then by gates passed, then (same gate) by distance to the next
    private static int LocalPlace()
    {
        var me = PhotonNetwork.LocalPlayer;
        if (me == null) return 1;
        int place = 1;
        foreach (var p in PhotonNetwork.PlayerList)
            if (p != me && Ahead(p, me)) place++;
        return place;
    }

    private static bool Ahead(PhotonPlayer a, PhotonPlayer b)
    {
        float fa = FinishOf(a), fb = FinishOf(b);
        if (fa >= 0f || fb >= 0f) return fa >= 0f && (fb < 0f || fa < fb);
        int ga = GatesOf(a), gb = GatesOf(b);
        if (ga != gb) return ga > gb;
        var next = RaceCheckpoint.Find(ga + 1);
        var pa = Player.Find(a.ActorNumber);
        var pb = Player.Find(b.ActorNumber);
        if (next == null || pa == null || pb == null) return false;
        return (pa.transform.position - next.transform.position).sqrMagnitude < (pb.transform.position - next.transform.position).sqrMagnitude;
    }

    private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}
