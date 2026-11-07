using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Base for two-team modes (Team Deathmatch, Keep Away): balanced random Red and Blue teams (players who join
/// mid-match go to the smaller team), teams spawn on opposite halves of the map, enemies outlined red and allies
/// blue, team-only minimap, team scores on the HUD. A team's score is the sum of its players' scores.
///
/// Flow: Countdown (frozen) → Active (<see cref="MatchRules.MatchTime"/>, or until a team reaches
/// <see cref="MatchRules.PointLimit"/>) → MatchEnd (<see cref="RoomKeys.WinningTeam"/>: "Red", "Blue" or "Draw")
/// → Scoreboard.
/// </summary>
public abstract class TeamMode : GameMode
{
    public const string Red  = "Red";
    public const string Blue = "Blue";
    public const string Draw = "Draw";

    private const float MatchEndSeconds = 5f;

    // Master: running totals, so quick changes before Photon echoes the first property back all count
    private readonly Dictionary<(int actor, string key), float> tally = new();

    public override bool   UsesRoles       => true;
    public override bool   ShowsScores     => true;
    public override bool   UsesSpawnPoints => true;
    public override bool   AnnouncesTags   => false;
    public override string CaughtBanner    => "";
    public override string RespawnBanner   => "";

    // ─── Master: match flow ───────────────────────────────────────────────────

    public override void StartMatch()
    {
        tally.Clear();
        Scores.ResetAll();
        if (!TakeDealtRoles()) AssignTeams();
        Manager.SetPhase(MatchPhase.Countdown, MatchRules.CountdownTime, new Hashtable { [RoomKeys.Round] = 1 });
    }

    public override void MasterUpdate()
    {
        PlaceNewcomers();

        switch (Phase)
        {
            case MatchPhase.Countdown:
                if (Clock.IsExpired) Manager.SetPhase(MatchPhase.Active, MatchRules.MatchTime);
                break;

            case MatchPhase.Active:
                float limit = MatchRules.PointLimit;
                if (Clock.IsExpired || (limit > 0f && Mathf.Max(LiveTeamScore(PlayerRole.TeamRed), LiveTeamScore(PlayerRole.TeamBlue)) >= limit))
                    EndMatch();
                else ActiveUpdate();
                break;

            case MatchPhase.MatchEnd:
                if (Clock.IsExpired) Manager.GoToScoreboard();
                break;
        }
    }

    /// <summary>Master, every frame of the Active phase (Keep Away scores the holder here).</summary>
    protected virtual void ActiveUpdate() { }

    // Shuffled, then dealt out alternately: sizes differ by one at most
    protected override bool DealsRolesInPregame => true;
    protected override void AssignStartingRoles() => AssignTeams();

    private static void AssignTeams()
    {
        var players = new List<PhotonPlayer>(PhotonNetwork.PlayerList);
        for (int i = players.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (players[i], players[j]) = (players[j], players[i]);
        }
        PlayerRole first = Random.value < 0.5f ? PlayerRole.TeamRed : PlayerRole.TeamBlue;
        for (int i = 0; i < players.Count; i++)
            PlayerRoles.Set(players[i], i % 2 == 0 ? first : PlayerRoles.Opponent(first));
    }

    // Anyone without a team (joined mid-match) goes to the smaller one
    private static void PlaceNewcomers()
    {
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (PlayerRoles.IsTeam(PlayerRoles.Get(p))) continue;
            int red = PlayerRoles.Count(PlayerRole.TeamRed), blue = PlayerRoles.Count(PlayerRole.TeamBlue);
            PlayerRoles.Set(p, red <= blue ? PlayerRole.TeamRed : PlayerRole.TeamBlue);
        }
    }

    protected void EndMatch()
    {
        if (Phase == MatchPhase.MatchEnd) return;
        float red = LiveTeamScore(PlayerRole.TeamRed), blue = LiveTeamScore(PlayerRole.TeamBlue);
        string winner = Mathf.Approximately(red, blue) ? Draw : red > blue ? Red : Blue;
        Manager.SetPhase(MatchPhase.MatchEnd, MatchEndSeconds, new Hashtable { [RoomKeys.WinningTeam] = winner });
    }

    // ─── Master: scoring ──────────────────────────────────────────────────────

    protected float Get(PhotonPlayer player, string key) =>
        tally.TryGetValue((player.ActorNumber, key), out float value) ? value : NetProps.Of(player, key, 0f);

    /// <summary>Master: adds to a player's score or stat (tallied, so rapid changes all count).</summary>
    protected void Add(PhotonPlayer player, string key, float amount)
    {
        if (player == null || amount == 0f) return;
        float value = Get(player, key) + amount;
        tally[(player.ActorNumber, key)] = value;
        NetProps.SetPlayer(player, key, value);
    }

    // ─── Team scores ──────────────────────────────────────────────────────────

    /// <summary>In a level: the team's live score (the sum of its players').</summary>
    protected float LiveTeamScore(PlayerRole team)
    {
        float total = 0f;
        foreach (var p in PhotonNetwork.PlayerList)
            if (PlayerRoles.Get(p) == team) total += Manager != null ? Scores.GetLive(p) : NetProps.Of(p, PlayerKeys.Score, 0f);
        return total;
    }

    /// <summary>How a team's score reads (Keep Away: seconds held).</summary>
    protected virtual string FormatTeamScore(float score) => Mathf.FloorToInt(score).ToString();

    public override string TeamScoreText =>
        $"<size=80%><color=#FF4D6D>{FormatTeamScore(LiveTeamScore(PlayerRole.TeamRed))}</color><color=#B9B2E0> - </color>" +
        $"<color=#33A6FF>{FormatTeamScore(LiveTeamScore(PlayerRole.TeamBlue))}</color></size>";

    // ─── All clients ──────────────────────────────────────────────────────────

    public override void OnPhaseChanged(MatchPhase phase)
    {
        if (phase == MatchPhase.Countdown && Spawner.Instance != null)
            Spawner.Instance.RespawnLocalPlayer();
    }

    public override bool CanMove(PlayerRole role, MatchPhase phase) => phase != MatchPhase.Countdown;

    public override bool SameTeam(PlayerRole a, PlayerRole b) => PlayerRoles.IsTeam(a) && a == b;

    // Enemies red, allies blue
    public override int OutlineFor(PlayerRole viewer, PlayerRole target) =>
        !PlayerRoles.IsTeam(viewer) || !PlayerRoles.IsTeam(target) ? 0 : viewer == target ? 2 : 1;

    public override bool ShowsName(PlayerRole viewer, PlayerRole target) => true;

    public override bool ShowsOnMap(PlayerRole viewer, PlayerRole target) => SameTeam(viewer, target);

    public override string RoleName(PlayerRole role) => role switch
    {
        PlayerRole.TeamRed  => "RED TEAM",
        PlayerRole.TeamBlue => "BLUE TEAM",
        _                   => base.RoleName(role),
    };

    public override string FormatScore(float score) => Mathf.FloorToInt(score).ToString();

    protected static string Winner => NetProps.Room(RoomKeys.WinningTeam, "");

    public override string GetPhaseLabel(MatchPhase phase) => phase == MatchPhase.MatchEnd
        ? Winner switch { Red => "Red Team Wins!", Blue => "Blue Team Wins!", _ => "Draw!" }
        : base.GetPhaseLabel(phase);

    // ─── Scoreboard ───────────────────────────────────────────────────────────

    protected static string TeamLabel(PhotonPlayer p) => PlayerRoles.Get(p) switch
    {
        PlayerRole.TeamRed  => "<color=#FF4D6D>Red</color>",
        PlayerRole.TeamBlue => "<color=#33A6FF>Blue</color>",
        _                   => "-",
    };

    private static bool Won(PhotonPlayer p) =>
        (Winner == Red && PlayerRoles.Get(p) == PlayerRole.TeamRed) || (Winner == Blue && PlayerRoles.Get(p) == PlayerRole.TeamBlue);

    // Winning team first, then by score
    public override int CompareForScoreboard(PhotonPlayer a, PhotonPlayer b)
    {
        int byWin = Won(b).CompareTo(Won(a));
        if (byWin != 0) return byWin;
        int byTeam = PlayerRoles.Get(a).CompareTo(PlayerRoles.Get(b));
        if (byTeam != 0 && Winner == Draw) return byTeam;
        return NetProps.Of(b, PlayerKeys.Score, 0f).CompareTo(NetProps.Of(a, PlayerKeys.Score, 0f));
    }

    public override string ScoreboardTitle(IReadOnlyList<PhotonPlayer> ranked)
    {
        string score = $"{FormatTeamScore(LiveTeamScore(PlayerRole.TeamRed))} - {FormatTeamScore(LiveTeamScore(PlayerRole.TeamBlue))}";
        return Winner switch
        {
            Red  => $"Red Team wins!  {score}",
            Blue => $"Blue Team wins!  {score}",
            _    => $"Draw!  {score}",
        };
    }
}
