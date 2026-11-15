using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// End-of-match scoreboard, built in code with the UI kit. The match leaves its results in player and room
/// properties (score, MatchStats, roles, winning team); this screen reads them:
///   - a podium of the top three: live 3D penguins in their colours dancing on gold, silver and bronze steps,
///     with their names and headline stat (the mode's first scoreboard column);
///   - the full table below, with columns, order and headline from the game mode (<see cref="GameMode.ScoreboardColumns"/>).
/// Team modes (<see cref="TeamMode"/>: Team Deathmatch, Keep Away, Search &amp; Destroy) get a team layout instead: the
/// winning team in its colour, both teams' final scores in big cards either side of the MVP (the winning team's best
/// scorer, dancing on a gold step, with their numbers), and a table per team side by side so each side's players
/// can be compared.
/// The host picks Play Again (same mode, map and rules; through Pregame unless the mode skips it) or Back to Lobby;
/// if they do neither, everyone returns to the lobby after <see cref="AutoReturnSeconds"/>.
/// Attach to: one GameObject in the Scoreboard scene (the screen makes its own canvas).
/// </summary>
public class ScoreboardManager : MenuScreen
{
    private const float AutoReturnSeconds = 60f;
    private const float NameWidth   = 340f;
    private const float ColumnWidth = 170f;
    private const float NameRow     = 40f;  // Names sit under the podium steps

    private static readonly Color Gold   = new(1f, 0.79f, 0.24f);
    private static readonly Color Silver = new(0.8f, 0.84f, 0.92f);
    private static readonly Color Bronze = new(0.86f, 0.55f, 0.3f);

    private GameMode      mode;
    private TMP_Text      titleText;
    private RectTransform tableCard, tableList, podium;
    private TMP_Text      footerText;
    private Button        lobbyButton, playAgainButton;
    private float         openedAt;
    private bool          leaving;
    private string        podiumKey;
    private readonly List<PenguinPreview> previews = new();

    // Team layout
    private static readonly Color RedInk  = new(1f, 0.3f, 0.43f);
    private static readonly Color BlueInk = new(0.2f, 0.65f, 1f);
    private RectTransform redCard, blueCard, redList, blueList;
    private TMP_Text redHeader, blueHeader;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    protected override void Build()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        openedAt = Time.time;
        mode = GameModeRegistry.Create(MatchRules.GameModeKey);

        titleText = UIKit.Title(Root, "", 76f, TextAlignmentOptions.Center);
        UIKit.Place(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(1600f, 90f));
        titleText.color = Theme.primary;
        var subtitle = UIKit.Text(Root, GameModeRegistry.DisplayName(MatchRules.GameModeKey), 26f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Center);
        UIKit.Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -128f), new Vector2(1200f, 36f));

        podium = UIKit.Rect("Podium", Root);
        UIKit.Place(podium, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1100f, 400f));

        if (mode is TeamMode) BuildTeamTables();
        else BuildTable();

        // Buttons and the countdown along the bottom
        playAgainButton = UIKit.Button(Root, "Play Again", UIKit.Style.Primary, PlayAgain, 72f, 30f);
        UIKit.Place((RectTransform)playAgainButton.transform, new Vector2(0.5f, 0f), new Vector2(-190f, 56f), new Vector2(340f, 72f));
        lobbyButton = UIKit.Button(Root, "Back to Lobby", UIKit.Style.Secondary, ReturnToLobby, 72f, 30f);
        UIKit.Place((RectTransform)lobbyButton.transform, new Vector2(0.5f, 0f), new Vector2(190f, 56f), new Vector2(340f, 72f));
        footerText = UIKit.Text(Root, "", 22f, UIKit.Font.Body, Theme.textMuted, TextAlignmentOptions.Center);
        UIKit.Place(footerText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(1000f, 32f));

        Rebuild();
        Sfx.Here(SfxKind.Fanfare, 0.7f);
    }

    private void BuildTable()
    {
        tableCard = UIKit.Card(Root, UIKit.WithAlpha(Theme.panel, 0.94f), 26f, 0.45f, "Table");
        tableCard.anchorMin = new Vector2(0.5f, 0f);
        tableCard.anchorMax = new Vector2(0.5f, 1f);
        tableCard.pivot = new Vector2(0.5f, 1f);
        tableCard.offsetMin = new Vector2(-700f, 150f);
        tableCard.offsetMax = new Vector2(700f, -590f);
        tableList = UIKit.ScrollList(tableCard, 6f, UIKit.Pad(18, 14));
    }

    // Two tables side by side, one per team, each under a strip in the team's colour
    private void BuildTeamTables()
    {
        (RectTransform card, RectTransform list, TMP_Text header) Make(string name, float x, Color ink)
        {
            var card = UIKit.Card(Root, UIKit.WithAlpha(Theme.panel, 0.94f), 26f, 0.45f, name);
            card.anchorMin = new Vector2(0.5f, 0f);
            card.anchorMax = new Vector2(0.5f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.offsetMin = new Vector2(x - 460f, 150f);
            card.offsetMax = new Vector2(x + 460f, -540f);
            var strip = UIKit.Panel(card, UIKit.WithAlpha(ink, 0.9f), 20f, "Strip");
            strip.rectTransform.anchorMin = new Vector2(0f, 1f);
            strip.rectTransform.anchorMax = new Vector2(1f, 1f);
            strip.rectTransform.pivot = new Vector2(0.5f, 1f);
            strip.rectTransform.offsetMin = new Vector2(10f, -58f);
            strip.rectTransform.offsetMax = new Vector2(-10f, -10f);
            var header = UIKit.OneLine(UIKit.Text(strip.transform, "", 28f, UIKit.Font.Display, Theme.textDark, TextAlignmentOptions.Center));
            UIKit.Stretch(header.rectTransform);
            var holder = UIKit.Rect("Rows", card);
            UIKit.Stretch(holder, 0f, 0f, 62f, 0f);
            var list = UIKit.ScrollList(holder, 5f, UIKit.Pad(14, 8));
            return (card, list, header);
        }
        (redCard, redList, redHeader) = Make("Red Team", -470f, RedInk);
        (blueCard, blueList, blueHeader) = Make("Blue Team", 470f, BlueInk);
        tableList = redList; // Rebuild's "built yet" check
    }

    private void Update()
    {
        float left = AutoReturnSeconds - (Time.time - openedAt);
        footerText.text = (PhotonNetwork.IsMasterClient ? "" : "Waiting for the host…  ")
                        + $"Back to the lobby in {Mathf.CeilToInt(Mathf.Max(0f, left))}s";
        lobbyButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        playAgainButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        if (left <= 0f) ReturnToLobby();
    }

    // Stats can arrive a moment after the scene loads (abilities used is sent by each player)
    public override void OnPlayerPropertiesUpdate(PhotonPlayer target, Hashtable changed) => Rebuild();
    public override void OnPlayerLeftRoom(PhotonPlayer otherPlayer) => Rebuild();

    // ─── Flow ─────────────────────────────────────────────────────────────────

    // Stats reset when the next level loads, so neither needs to clear anything
    private void ReturnToLobby()
    {
        if (leaving || !PhotonNetwork.IsMasterClient) return;
        leaving = true;
        MatchLauncher.ReturnToLobby();
    }

    private void PlayAgain()
    {
        if (leaving || !PhotonNetwork.IsMasterClient) return;
        leaving = true;
        MatchLauncher.StartMatch();
    }

    // ─── Building ─────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        if (tableList == null || !PhotonNetwork.InRoom) return;

        var ranked = new List<PhotonPlayer>(PhotonNetwork.PlayerList);
        ranked.Sort(mode.CompareForScoreboard);
        titleText.text = mode.ScoreboardTitle(ranked);
        var columns = mode.ScoreboardColumns;
        if (mode is TeamMode teams) { RebuildTeams(teams, ranked, columns); return; }

        BuildPodium(ranked, columns);
        BuildAwards(ranked);

        for (int i = tableList.childCount - 1; i >= 0; i--) Destroy(tableList.GetChild(i).gameObject);
        var header = new List<string> { "#", "PLAYER" };
        foreach (var c in columns) header.Add(c.Header.ToUpperInvariant());
        AddRow(header, null, isHeader: true, rank: 0);
        for (int i = 0; i < ranked.Count; i++)
        {
            var p = ranked[i];
            var cells = new List<string> { (i + 1).ToString(), Name(p) };
            foreach (var c in columns) cells.Add(c.Value(p));
            AddRow(cells, p, isHeader: false, rank: i + 1);
        }
        RequestLayout();
    }

    // Rebuilt only when the top three (or their headline numbers) change, so the dancers don't restart
    private void BuildPodium(List<PhotonPlayer> ranked, IReadOnlyList<ScoreboardColumn> columns)
    {
        string key = "";
        for (int i = 0; i < Mathf.Min(3, ranked.Count); i++)
            key += ranked[i].ActorNumber + ":" + (columns.Count > 0 ? columns[0].Value(ranked[i]) : "") + ";";
        if (key == podiumKey) return;
        podiumKey = key;

        for (int i = podium.childCount - 1; i >= 0; i--) Destroy(podium.GetChild(i).gameObject);
        previews.Clear();

        // Second on the left, first in the middle (tallest), third on the right
        int[] order = { 1, 0, 2 };
        float[] x = { -360f, 0f, 360f };
        float[] step = { 105f, 150f, 70f };
        for (int slot = 0; slot < 3; slot++)
        {
            int place = order[slot];
            if (place >= ranked.Count) continue;
            var p = ranked[place];
            Color medal = place == 0 ? Gold : place == 1 ? Silver : Bronze;

            var column = UIKit.Rect($"Place {place + 1}", podium);
            column.anchorMin = column.anchorMax = new Vector2(0.5f, 0f);
            column.pivot = new Vector2(0.5f, 0f);
            column.anchoredPosition = new Vector2(x[slot], 0f);
            column.sizeDelta = new Vector2(300f, 400f);

            // The step
            var block = UIKit.Card(column, medal, 18f, 0.4f, "Step");
            block.anchorMin = block.anchorMax = new Vector2(0.5f, 0f);
            block.pivot = new Vector2(0.5f, 0f);
            block.anchoredPosition = new Vector2(0f, NameRow);
            block.sizeDelta = new Vector2(260f, step[slot]);
            var placeText = UIKit.Text(block, place == 0 ? "1ST" : place == 1 ? "2ND" : "3RD", 44f, UIKit.Font.Display, Theme.textDark, TextAlignmentOptions.Center);
            UIKit.Stretch(placeText.rectTransform, 0f, 0f, 6f, 30f);
            string stat = columns.Count > 0 ? $"{columns[0].Value(p)} {columns[0].Header.ToLowerInvariant()}" : "";
            var statText = UIKit.OneLine(UIKit.Text(block, stat, 18f, UIKit.Font.Bold, UIKit.WithAlpha(Theme.textDark, 0.8f), TextAlignmentOptions.Bottom));
            UIKit.Stretch(statText.rectTransform, 8f, 8f, 0f, 8f);

            // The penguin, dancing, on top of the step
            var holder = UIKit.Rect("Penguin", column);
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0f);
            holder.pivot = new Vector2(0.5f, 0f);
            holder.anchoredPosition = new Vector2(0f, NameRow + step[slot] - 18f);
            float size = place == 0 ? 200f : 165f;
            holder.sizeDelta = new Vector2(size, size);
            var preview = PenguinPreview.Create(holder, size);
            UIKit.Stretch((RectTransform)preview.transform);
            preview.SetSkin(PenguinSkins.Of(p));
            preview.SetCosmetics(p);
            preview.Dance = true;
            previews.Add(preview);

            var name = UIKit.OneLine(UIKit.Text(column, Name(p), place == 0 ? 30f : 26f, UIKit.Font.Display, place == 0 ? Gold : Theme.text, TextAlignmentOptions.Center));
            // The name under the step, clear of the title above
            UIKit.Place(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(300f, 38f));
            name.rectTransform.pivot = new Vector2(0.5f, 0f);
            name.rectTransform.anchoredPosition = Vector2.zero;
        }
    }

    // ─── Team layout ──────────────────────────────────────────────────────────

    private void RebuildTeams(TeamMode teams, List<PhotonPlayer> ranked, IReadOnlyList<ScoreboardColumn> columns)
    {
        string winner = TeamMode.WinningTeam;
        titleText.text = winner == TeamMode.Red ? "RED TEAM WINS!" : winner == TeamMode.Blue ? "BLUE TEAM WINS!" : "IT'S A DRAW!";
        titleText.color = winner == TeamMode.Red ? RedInk : winner == TeamMode.Blue ? BlueInk : Theme.primary;

        // Columns per team table: the mode's, minus the team (the table says it)
        var shown = new List<ScoreboardColumn>();
        foreach (var c in columns) if (c.Header != "Team") shown.Add(c);

        var mvp = teams.Mvp(ranked);
        BuildTeamTop(teams, winner, mvp, shown);
        FillTeam(redList, redHeader, PlayerRole.TeamRed, "RED TEAM", winner == TeamMode.Red, teams, ranked, shown, mvp);
        FillTeam(blueList, blueHeader, PlayerRole.TeamBlue, "BLUE TEAM", winner == TeamMode.Blue, teams, ranked, shown, mvp);
        RequestLayout();
    }

    // Both final scores either side of the MVP; rebuilt only when they change, so the MVP's dance doesn't restart
    private void BuildTeamTop(TeamMode teams, string winner, PhotonPlayer mvp, List<ScoreboardColumn> shown)
    {
        string red = teams.FinalScore(PlayerRole.TeamRed), blue = teams.FinalScore(PlayerRole.TeamBlue);
        string key = $"{winner}|{red}|{blue}|{(mvp != null ? mvp.ActorNumber : -1)}|{(mvp != null && shown.Count > 0 ? shown[0].Value(mvp) : "")}";
        if (key == podiumKey) return;
        podiumKey = key;
        for (int i = podium.childCount - 1; i >= 0; i--) Destroy(podium.GetChild(i).gameObject);
        previews.Clear();

        void ScoreCard(string label, string score, Color ink, float x, bool won)
        {
            var card = UIKit.Card(podium, won ? UIKit.WithAlpha(ink, 0.95f) : UIKit.WithAlpha(Theme.panel, 0.94f), 26f, 0.45f, label);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = new Vector2(x, 0f);
            card.sizeDelta = new Vector2(320f, 250f);
            Color text = won ? Theme.textDark : ink;
            var name = UIKit.OneLine(UIKit.Text(card, label, 30f, UIKit.Font.Display, text, TextAlignmentOptions.Center));
            UIKit.Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(300f, 40f));
            var number = UIKit.OneLine(UIKit.Text(card, score, 110f, UIKit.Font.Display, text, TextAlignmentOptions.Center));
            UIKit.Place(number.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(300f, 120f));
            number.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            number.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            var unit = UIKit.OneLine(UIKit.Text(card, (won ? "WINNERS · " : "") + teams.FinalScoreUnit.ToUpperInvariant(), 18f, UIKit.Font.Display,
                won ? UIKit.WithAlpha(Theme.textDark, 0.75f) : Theme.textMuted, TextAlignmentOptions.Center));
            UIKit.Place(unit.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(300f, 28f));
        }
        ScoreCard("RED TEAM", red, RedInk, -440f, winner == TeamMode.Red);
        ScoreCard("BLUE TEAM", blue, BlueInk, 440f, winner == TeamMode.Blue);

        if (mvp == null) return;
        Color mvpInk = PlayerRoles.Get(mvp) == PlayerRole.TeamBlue ? BlueInk : RedInk;
        var column = UIKit.Rect("MVP", podium);
        column.anchorMin = column.anchorMax = new Vector2(0.5f, 0f);
        column.pivot = new Vector2(0.5f, 0f);
        column.anchoredPosition = new Vector2(0f, 34f); // Its stat line clears the team tables below
        column.sizeDelta = new Vector2(440f, 380f);

        var step = UIKit.Card(column, Gold, 18f, 0.4f, "Step");
        step.anchorMin = step.anchorMax = new Vector2(0.5f, 0f);
        step.pivot = new Vector2(0.5f, 0f);
        step.anchoredPosition = new Vector2(0f, 64f);
        step.sizeDelta = new Vector2(240f, 70f);
        var mvpText = UIKit.Text(step, "MVP", 46f, UIKit.Font.Display, Theme.textDark, TextAlignmentOptions.Center);
        UIKit.Stretch(mvpText.rectTransform);

        var holder = UIKit.Rect("Penguin", column);
        holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0f);
        holder.pivot = new Vector2(0.5f, 0f);
        holder.anchoredPosition = new Vector2(0f, 120f);
        holder.sizeDelta = new Vector2(230f, 230f);
        var preview = PenguinPreview.Create(holder, 230f);
        UIKit.Stretch((RectTransform)preview.transform);
        preview.SetSkin(PenguinSkins.Of(mvp));
        preview.SetCosmetics(mvp);
        preview.Dance = true;
        previews.Add(preview);

        var name = UIKit.OneLine(UIKit.Text(column, Name(mvp), 32f, UIKit.Font.Display, mvpInk, TextAlignmentOptions.Center));
        UIKit.Place(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(440f, 40f));
        name.rectTransform.pivot = new Vector2(0.5f, 0f);
        name.rectTransform.anchoredPosition = new Vector2(0f, 26f);
        var parts = new List<string>();
        for (int i = 0; i < shown.Count && parts.Count < 3; i++) parts.Add($"{shown[i].Value(mvp)} {shown[i].Header.ToLowerInvariant()}");
        var line = UIKit.OneLine(UIKit.Text(column, string.Join("  ·  ", parts), 20f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Center));
        UIKit.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(440f, 28f));
        line.rectTransform.pivot = new Vector2(0.5f, 0f);
        line.rectTransform.anchoredPosition = Vector2.zero;
    }

    private void FillTeam(RectTransform list, TMP_Text header, PlayerRole team, string label, bool won, TeamMode teams,
                          List<PhotonPlayer> ranked, List<ScoreboardColumn> shown, PhotonPlayer mvp)
    {
        header.text = $"{label}  ·  {teams.FinalScore(team)}{(won ? "  ·  WINNERS" : "")}";
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);

        // Name gets what's left after the columns; columns share the rest of the 890 px row
        const float rowWidth = 860f, rankWidth = 44f, nameWidth = 230f;
        float colWidth = Mathf.Clamp((rowWidth - rankWidth - nameWidth) / Mathf.Max(1, shown.Count), 64f, 170f);
        var cells = new List<string> { "#", "PLAYER" };
        foreach (var c in shown) cells.Add(c.Header.ToUpperInvariant());
        TeamRow(list, cells, null, true, 0, rankWidth, nameWidth, colWidth, false);

        int place = 0;
        foreach (var p in ranked)
        {
            if (PlayerRoles.Get(p) != team) continue;
            place++;
            cells = new List<string> { place.ToString(), Name(p) + (p == mvp ? "  <size=70%><color=#FFC93C>MVP</color></size>" : "") };
            foreach (var c in shown) cells.Add(c.Value(p));
            TeamRow(list, cells, p, false, place, rankWidth, nameWidth, colWidth, p == mvp);
        }
        if (place == 0)
        {
            var empty = UIKit.Text(list, "Nobody", 22f, UIKit.Font.Body, Theme.textMuted, TextAlignmentOptions.Center);
            UIKit.Size(empty, height: 48f);
        }
    }

    private void TeamRow(RectTransform list, List<string> cells, PhotonPlayer player, bool isHeader, int rank,
                         float rankWidth, float nameWidth, float colWidth, bool mvp)
    {
        bool own = player != null && player.IsLocal;
        Color bg = isHeader ? Color.clear : own ? UIKit.WithAlpha(Theme.primary, 0.22f) : mvp ? UIKit.WithAlpha(Gold, 0.12f) : UIKit.WithAlpha(Theme.card, 0.8f);
        float height = isHeader ? 40f : 46f;
        var row = UIKit.Panel(list, bg, 12f, isHeader ? "Header" : "Row");
        UIKit.Size(row, height: height);
        var layout = UIKit.HStack(row.transform, 0f, new RectOffset(10, 10, 0, 0), TextAnchor.MiddleLeft);
        layout.childForceExpandWidth = false;
        layout.childControlWidth = true;
        for (int i = 0; i < cells.Count; i++)
        {
            float width = i == 0 ? rankWidth : i == 1 ? nameWidth : colWidth;
            var align = i == 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
            Color ink = isHeader ? Theme.textMuted : Theme.text;
            var text = UIKit.Text(row.transform, cells[i], isHeader ? 14f : 22f,
                isHeader || i == 0 ? UIKit.Font.Display : i == 1 ? UIKit.Font.Bold : UIKit.Font.Body, ink, align);
            if (!isHeader) UIKit.OneLine(text); // Headers may wrap to two lines ("ABILITIES / USED")
            text.enableAutoSizing = isHeader;
            if (isHeader) { text.fontSizeMin = 10f; text.fontSizeMax = 14f; }
            UIKit.Size(text, width, height);
        }
    }

    // ─── Awards ───────────────────────────────────────────────────────────────

    // Title, stat, how the number reads, and whether the most or the fewest wins; shown when one player leads alone
    private static readonly (string title, string key, System.Func<float, string> format, string color)[] AwardList =
    {
        ("Knock-off King",  PlayerKeys.StatKnockOffs,     v => $"{v:0} knock-offs",      "#FF8A3D"),
        ("Sharpshooter",    PlayerKeys.StatHeadshots,     v => $"{v:0} headshots",       "#FF4D6D"),
        ("Ability Addict",  PlayerKeys.StatAbilitiesUsed, v => $"{v:0} abilities used",  "#3DE0FF"),
        ("Master Hider",    PlayerKeys.StatLongestHide,   v => $"{MatchStats.FormatTime(v)} in one hide", "#9BFF5A"),
        ("Tag Machine",     PlayerKeys.StatTags,          v => $"{v:0} tags",            "#FF4D6D"),
        ("Pickpocket",      PlayerKeys.StatSteals,        v => $"{v:0} steals",          "#FFD23F"),
        ("Disc Magnet",     PlayerKeys.StatCatches,       v => $"{v:0} grabs",           "#FFD23F"),
        ("Sweet Tooth",     PlayerKeys.StatDonuts,        v => $"{v:0} donuts",          "#FF6FB5"),
        ("Gravity's Victim", PlayerKeys.StatSelfDestructs, v => $"fell off {v:0} times", "#B9B2E0"),
    };

    private RectTransform awardsLeft, awardsRight;

    // Up to four awards beside the podium, from the stats this mode tracked
    private void BuildAwards(List<PhotonPlayer> ranked)
    {
        if (awardsLeft == null)
        {
            awardsLeft = UIKit.Rect("Awards Left", Root);
            UIKit.Place(awardsLeft, new Vector2(0f, 1f), new Vector2(60f, -190f), new Vector2(340f, 360f));
            awardsLeft.pivot = new Vector2(0f, 1f);
            UIKit.VStack(awardsLeft, 14f, null, TextAnchor.UpperLeft);
            awardsRight = UIKit.Rect("Awards Right", Root);
            UIKit.Place(awardsRight, new Vector2(1f, 1f), new Vector2(-60f, -190f), new Vector2(340f, 360f));
            awardsRight.pivot = new Vector2(1f, 1f);
            UIKit.VStack(awardsRight, 14f, null, TextAnchor.UpperRight);
        }
        for (int i = awardsLeft.childCount - 1; i >= 0; i--) Destroy(awardsLeft.GetChild(i).gameObject);
        for (int i = awardsRight.childCount - 1; i >= 0; i--) Destroy(awardsRight.GetChild(i).gameObject);
        if (ranked.Count < 2) return;

        int shown = 0;
        foreach (var (title, key, format, color) in AwardList)
        {
            if (shown >= 4) break;
            PhotonPlayer best = null;
            float top = 0f;
            bool tie = false;
            foreach (var p in ranked)
            {
                float v = MatchStats.Get(p, key);
                if (v > top) { top = v; best = p; tie = false; }
                else if (v > 0f && Mathf.Approximately(v, top)) tie = true;
            }
            if (best == null || tie) continue;

            var parent = shown % 2 == 0 ? awardsLeft : awardsRight;
            ColorUtility.TryParseHtmlString(color, out Color accent);
            var card = UIKit.Card(parent, UIKit.WithAlpha(Theme.panel, 0.94f), 20f, 0.4f, title);
            UIKit.Size(card, 340f, 96f);
            UIKit.VStack(card, 2f, UIKit.Pad(18, 12), TextAnchor.UpperLeft);
            UIKit.OneLine(UIKit.Text(card, title.ToUpperInvariant(), 20f, UIKit.Font.Display, accent));
            UIKit.OneLine(UIKit.Text(card, Name(best), 28f, UIKit.Font.Display, Theme.text));
            UIKit.OneLine(UIKit.Text(card, format(top), 18f, UIKit.Font.Body, Theme.textMuted));
            shown++;
        }
    }

    private void AddRow(List<string> cells, PhotonPlayer player, bool isHeader, int rank)
    {
        bool own = player != null && player.IsLocal;
        Color bg = isHeader ? Color.clear : own ? UIKit.WithAlpha(Theme.primary, 0.22f) : UIKit.WithAlpha(Theme.card, 0.8f);
        var row = UIKit.Panel(tableList, bg, 12f, isHeader ? "Header" : "Row");
        UIKit.Size(row, height: isHeader ? 34f : 48f);
        var layout = UIKit.HStack(row.transform, 0f, new RectOffset(14, 14, 0, 0), TextAnchor.MiddleLeft);
        layout.childForceExpandWidth = false;
        layout.childControlWidth = true;

        for (int i = 0; i < cells.Count; i++)
        {
            float width = i == 0 ? 60f : i == 1 ? NameWidth : ColumnWidth;
            var align = i == 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
            Color ink = isHeader ? Theme.textMuted
                      : i == 0 && rank == 1 ? Gold : i == 0 && rank == 2 ? Silver : i == 0 && rank == 3 ? Bronze
                      : Theme.text;
            var text = UIKit.OneLine(UIKit.Text(row.transform, cells[i], isHeader ? 17f : 24f,
                isHeader || i == 0 ? UIKit.Font.Display : i == 1 ? UIKit.Font.Bold : UIKit.Font.Body, ink, align));
            UIKit.Size(text, width, isHeader ? 34f : 48f);
        }
    }

    private static string Name(PhotonPlayer p) => string.IsNullOrEmpty(p.NickName) ? $"Player {p.ActorNumber}" : p.NickName;
}
