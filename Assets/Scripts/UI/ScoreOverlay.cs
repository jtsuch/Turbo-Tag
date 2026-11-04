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

    private static readonly Color Orange = new(1f, 0.55f, 0.1f);

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
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        panel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 0.85f);
        panel.GetComponent<Image>().raycastTarget = false;

        title = Text(panel.transform, "", 34f, TextAlignmentOptions.Center);
        title.color = Orange;
        var titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot     = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -12f);
        titleRect.sizeDelta = new Vector2(0f, 48f);

        table = new GameObject("Table", typeof(RectTransform)).GetComponent<RectTransform>();
        table.SetParent(panel.transform, false);
        table.anchorMin = table.anchorMax = table.pivot = new Vector2(0.5f, 1f);
        table.anchoredPosition = new Vector2(0f, -70f);

        panel.SetActive(false);
    }

    private void Update()
    {
        bool show = !PauseMenuManager.IsPaused && Input.GetKey(Keybinds.Get(InputAction.Scores))
                    && GameModeManager.Instance != null && GameModeManager.Instance.Mode != null && PhotonNetwork.InRoom;
        if (panel.activeSelf != show) { panel.SetActive(show); nextRefresh = 0f; }
        if (!show || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + Refresh;
        Rebuild(GameModeManager.Instance.Mode);
    }

    private void Rebuild(GameMode mode)
    {
        if (font == null) font = FindHudFont();
        foreach (Transform child in table) Destroy(child.gameObject);

        var players = new List<PhotonPlayer>(PhotonNetwork.PlayerList);
        players.Sort(mode.CompareForScoreboard);
        var columns = mode.LiveScoreColumns;

        float width = RankWidth + NameWidth + columns.Count * ColumnWidth;
        float height = (players.Count + 1) * RowHeight;
        ((RectTransform)panel.transform).sizeDelta = new Vector2(width + 40f, height + 100f);
        table.sizeDelta = new Vector2(width, height);
        title.text = mode.DisplayName;

        // Header
        var header = new List<string> { "#", "Player" };
        foreach (var column in columns) header.Add(column.Header);
        Row(header, 0, new Color(1f, 1f, 1f, 0.6f), null);

        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            var cells = new List<string> { (i + 1).ToString(), string.IsNullOrEmpty(p.NickName) ? $"Player {p.ActorNumber}" : p.NickName };
            foreach (var column in columns) cells.Add(column.Value(p));
            Row(cells, i + 1, p.IsLocal ? Orange : Color.white, p.IsLocal ? new Color(1f, 0.55f, 0.1f, 0.15f) : (Color?)null);
        }
    }

    private void Row(List<string> cells, int index, Color color, Color? highlight)
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
            image.color = highlight.Value;
            image.raycastTarget = false;
        }

        float x = 0f;
        for (int c = 0; c < cells.Count; c++)
        {
            float w = c == 0 ? RankWidth : c == 1 ? NameWidth : ColumnWidth;
            var text = Text(table, cells[c], 26f, c == 1 ? TextAlignmentOptions.Left : TextAlignmentOptions.Center);
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

    private static TMP_FontAsset FindHudFont()
    {
        var hud = HUDManager.Instance;
        if (hud == null) return null;
        var text = hud.GetComponentInChildren<TMP_Text>(true);
        return text != null ? text.font : null;
    }
}
