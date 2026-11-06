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

        tableCard = UIKit.Card(Root, UIKit.WithAlpha(Theme.panel, 0.94f), 26f, 0.45f, "Table");
        tableCard.anchorMin = new Vector2(0.5f, 0f);
        tableCard.anchorMax = new Vector2(0.5f, 1f);
        tableCard.pivot = new Vector2(0.5f, 1f);
        tableCard.offsetMin = new Vector2(-700f, 150f);
        tableCard.offsetMax = new Vector2(700f, -590f);
        tableList = UIKit.ScrollList(tableCard, 6f, UIKit.Pad(18, 14));

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

        BuildPodium(ranked, columns);

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
            preview.Dance = true;
            previews.Add(preview);

            var name = UIKit.OneLine(UIKit.Text(column, Name(p), place == 0 ? 30f : 26f, UIKit.Font.Display, place == 0 ? Gold : Theme.text, TextAlignmentOptions.Center));
            // The name under the step, clear of the title above
            UIKit.Place(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(300f, 38f));
            name.rectTransform.pivot = new Vector2(0.5f, 0f);
            name.rectTransform.anchoredPosition = Vector2.zero;
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
