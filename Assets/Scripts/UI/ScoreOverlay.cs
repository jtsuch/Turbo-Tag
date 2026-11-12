using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Hold the Scores key (default Tab) in a level to see the standings: every player ranked by the mode
/// (<see cref="GameMode.CompareForScoreboard"/>) with the mode's live columns (<see cref="GameMode.LiveScoreColumns"/>).
/// Built in code on its own overlay canvas by GameModeManager; works in every mode.
/// </summary>
public class ScoreOverlay : MonoBehaviour
{
    private static ScoreOverlay instance;

    private const float RowHeight   = 44f;
    private const float NameWidth   = 320f;
    private const float RankWidth   = 60f;
    private const float ColumnWidth = 170f;
    private const float Refresh     = 0.25f;


    private GameObject    panel;
    private RectTransform table;
    private TMP_Text      title;
    private TMP_FontAsset font;
    private float         nextRefresh;

    public static void Create()
    {
        if (instance != null) return;
        instance = new GameObject("ScoreOverlay").AddComponent<ScoreOverlay>();
        instance.Build();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5; // Over the HUD and combat HUD, under the pause menu
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        gameObject.AddComponent<UIScaler>();

        var card = UIKit.Card(transform, UIKit.WithAlpha(UITheme.Current.panel, 0.96f), 30f, 0.55f, "Panel");
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        panel = card.gameObject;

        title = UIKit.Title(panel.transform, "", 44f, TextAlignmentOptions.Center);
        title.color = UITheme.Current.primary;
        var titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot     = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -18f);
        titleRect.sizeDelta = new Vector2(0f, 56f);

        table = new GameObject("Table", typeof(RectTransform)).GetComponent<RectTransform>();
        table.SetParent(panel.transform, false);
        table.anchorMin = table.anchorMax = table.pivot = new Vector2(0.5f, 1f);
        table.anchoredPosition = new Vector2(0f, -86f);

        panel.SetActive(false);
    }

    private float nextPing;

    private void Update()
    {
        // Everyone publishes their own ping (ms) every few seconds for the Ping column
        if (PhotonNetwork.InRoom && Time.unscaledTime >= nextPing)
        {
            nextPing = Time.unscaledTime + 5f;
            NetProps.SetLocal(PlayerKeys.Ping, PhotonNetwork.GetPing());
        }

        bool show = !PauseMenuManager.IsPaused && Input.GetKey(Keybinds.Get(InputAction.Scores))
                    && GameModeManager.Instance != null && GameModeManager.Instance.Mode != null && PhotonNetwork.InRoom;
        if (panel.activeSelf != show) { panel.SetActive(show); nextRefresh = 0f; }
        if (!show || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + Refresh;
        Rebuild(GameModeManager.Instance.Mode);
    }

    private void Rebuild(GameMode mode)
    {
        if (font == null) font = UIKit.FontAsset(UIKit.Font.Bold);
        foreach (Transform child in table) Destroy(child.gameObject);

        var players = new List<PhotonPlayer>(PhotonNetwork.PlayerList);
        players.Sort(mode.CompareForScoreboard);
        var columns = new List<ScoreboardColumn>(mode.LiveScoreColumns);
        var teams = mode as TeamMode;
        if (teams != null) columns.RemoveAll(c => c.Header == "Team"); // The sections say it
        columns.Add(new ScoreboardColumn("Ping", p =>
        {
            int ms = NetProps.Of(p, PlayerKeys.Ping, -1);
            return ms < 0 ? "-" : ms.ToString();
        }));

        // Team modes: one section per team (its name and score), each ranked on its own
        var sections = new List<(string label, Color color, List<PhotonPlayer> rows)>();
        if (teams != null)
        {
            foreach (var role in new[] { PlayerRole.TeamRed, PlayerRole.TeamBlue })
            {
                var rows = players.FindAll(p => PlayerRoles.Get(p) == role);
                string name = role == PlayerRole.TeamRed ? "RED TEAM" : "BLUE TEAM";
                sections.Add(($"{name}   {teams.FinalScore(role)} {teams.FinalScoreUnit}", PlayerRoles.Color(role), rows));
            }
            var rest = players.FindAll(p => !PlayerRoles.IsTeam(PlayerRoles.Get(p)));
            if (rest.Count > 0) sections.Add(("NO TEAM YET", UITheme.Current.textMuted, rest));
        }
        else sections.Add((null, Color.white, players));

        int lines = 1;
        foreach (var s in sections) lines += s.rows.Count + (s.label != null ? 1 : 0);
        float width = RankWidth + NameWidth + columns.Count * ColumnWidth;
        float height = lines * RowHeight;
        ((RectTransform)panel.transform).sizeDelta = new Vector2(width + 64f, height + 120f);
        table.sizeDelta = new Vector2(width, height);
        title.text = mode.DisplayName;

        // Header
        var header = new List<string> { "#", "Player" };
        foreach (var column in columns) header.Add(column.Header);
        Row(header, 0, UITheme.Current.textMuted, null, header: true);

        int line = 1;
        foreach (var (label, color, rows) in sections)
        {
            if (label != null) Section(label, color, line++);
            for (int i = 0; i < rows.Count; i++)
            {
                var p = rows[i];
                var cells = new List<string> { (i + 1).ToString(), string.IsNullOrEmpty(p.NickName) ? $"Player {p.ActorNumber}" : p.NickName };
                foreach (var column in columns) cells.Add(column.Value(p));
                Row(cells, line++, p.IsLocal ? UITheme.Current.primary : Color.white,
                    p.IsLocal ? UIKit.WithAlpha(UITheme.Current.primary, 0.16f) : (i % 2 == 0 ? UIKit.WithAlpha(Color.white, 0.04f) : (Color?)null));
            }
        }
    }

    // A team's heading across the whole table: its name and score on a band in its colour
    private void Section(string label, Color color, int index)
    {
        float y = -index * RowHeight;
        var bg = new GameObject("Section", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(table, false);
        var r = (RectTransform)bg.transform;
        r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f); r.pivot = new Vector2(0.5f, 1f);
        r.anchoredPosition = new Vector2(0f, y);
        r.sizeDelta = new Vector2(0f, RowHeight);
        var image = bg.GetComponent<Image>();
        UIKit.Round(image, 12f);
        image.color = UIKit.WithAlpha(color, 0.28f);
        image.raycastTarget = false;

        var text = Text(table, label, 24f, TextAlignmentOptions.MidlineLeft);
        text.font = UIKit.FontAsset(UIKit.Font.Display);
        text.color = color;
        var t = text.rectTransform;
        t.anchorMin = new Vector2(0f, 1f); t.anchorMax = new Vector2(1f, 1f); t.pivot = new Vector2(0f, 1f);
        t.anchoredPosition = new Vector2(16f, y);
        t.sizeDelta = new Vector2(-32f, RowHeight);
    }

    private void Row(List<string> cells, int index, Color color, Color? highlight, bool header = false)
    {
        float y = -index * RowHeight;
        if (highlight.HasValue)
        {
            var bg = new GameObject("Highlight", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(table, false);
            var r = (RectTransform)bg.transform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f); r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(0f, RowHeight);
            var image = bg.GetComponent<Image>();
            UIKit.Round(image, 12f);
            image.color = highlight.Value;
            image.raycastTarget = false;
        }

        float x = 0f;
        for (int c = 0; c < cells.Count; c++)
        {
            float w = c == 0 ? RankWidth : c == 1 ? NameWidth : ColumnWidth;
            var text = Text(table, cells[c], header ? 18f : 26f, c == 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
            text.font = UIKit.FontAsset(header ? UIKit.Font.Bold : c <= 1 ? UIKit.Font.Display : UIKit.Font.Bold);
            if (header) text.text = cells[c].ToUpperInvariant();
            text.color = color;
            var r = text.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, RowHeight);
            x += w;
        }
    }

    private TMP_Text Text(Transform parent, string value, float size, TextAlignmentOptions align)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.text      = value;
        text.fontSize  = size;
        text.alignment = align;
        text.raycastTarget    = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode     = TextOverflowModes.Ellipsis;
        return text;
    }
}
