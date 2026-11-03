using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// End-of-match scoreboard. The match leaves its results in player and room properties
/// (score, MatchStats, roles, winning team); this screen reads them and builds a table whose
/// columns, order and headline come from the game mode (<see cref="GameMode.ScoreboardColumns"/>).
/// The host picks "Play Again?" (same mode, map and rules; through Pregame unless the mode skips it) or
/// "Back to Lobby"; if they do neither, everyone returns to the lobby after <see cref="autoReturnSeconds"/>.
/// The whole UI is built in code under this Canvas, so the scene only needs the Canvas, an EventSystem
/// and a camera.
/// Attach to: the Canvas in the Scoreboard scene.
/// </summary>
public class ScoreboardManager : MonoBehaviourPunCallbacks
{
    [Header("Look")]
    [SerializeField] private TMP_FontAsset titleFont;
    [SerializeField] private TMP_FontAsset bodyFont;
    [SerializeField] private Color background  = new(0.07f, 0.09f, 0.14f, 1f);
    [SerializeField] private Color rowColor    = new(1f, 1f, 1f, 0.06f);
    [SerializeField] private Color ownRowColor = new(1f, 0.8f, 0.2f, 0.25f);
    [SerializeField] private Color accent      = new(1f, 0.8f, 0.2f, 1f);

    [Header("Layout")]
    [SerializeField] private float nameWidth   = 380f;
    [SerializeField] private float columnWidth = 210f;
    [SerializeField] private float rowHeight   = 58f;

    [Header("Flow")]
    [SerializeField] private float autoReturnSeconds = 60f;

    private GameMode      mode;
    private RectTransform table;
    private TMP_Text      titleText;
    private TMP_Text      footerText;
    private Button        lobbyButton;
    private Button        playAgainButton;
    private float         openedAt;
    private bool          leaving;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        openedAt = Time.time;

        mode = GameModeRegistry.Create(MatchRules.GameModeKey);
        BuildFrame();
        Rebuild();
    }

    private void Update()
    {
        float left = autoReturnSeconds - (Time.time - openedAt);
        if (footerText != null)
            footerText.text = (PhotonNetwork.IsMasterClient ? "" : "Waiting for the host... ")
                            + $"Back to the lobby in {Mathf.CeilToInt(Mathf.Max(0f, left))}s";
        if (lobbyButton != null)     lobbyButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        if (playAgainButton != null) playAgainButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);

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

    private void BuildFrame()
    {
        var bg = NewChild("Background", transform);
        Stretch(bg);
        bg.gameObject.AddComponent<Image>().color = background;

        titleText = NewText("Title", transform, "", 64f, titleFont, TextAlignmentOptions.Center, accent);
        Place(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1700f, 90f));

        var subtitle = NewText("Mode", transform, GameModeRegistry.DisplayName(MatchRules.GameModeKey), 34f, bodyFont,
                               TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.7f));
        Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(1200f, 50f));

        table = NewChild("Table", transform);
        Place(table, new Vector2(0.5f, 1f), new Vector2(0f, -210f), new Vector2(1600f, 0f)); // Width set per mode in Rebuild
        table.pivot = new Vector2(0.5f, 1f);
        var list = table.gameObject.AddComponent<VerticalLayoutGroup>();
        list.spacing = 6f;
        list.childControlWidth = list.childControlHeight = true;
        list.childForceExpandWidth = true;
        list.childForceExpandHeight = false;
        table.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        footerText = NewText("Footer", transform, "", 28f, bodyFont, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.6f));
        Place(footerText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1000f, 40f));

        playAgainButton = NewButton("PlayAgain", "Play Again?");
        Place((RectTransform)playAgainButton.transform, new Vector2(0.5f, 0f), new Vector2(-190f, 130f), new Vector2(340f, 72f));
        playAgainButton.onClick.AddListener(PlayAgain);

        lobbyButton = NewButton("BackToLobby", "Back to Lobby");
        Place((RectTransform)lobbyButton.transform, new Vector2(0.5f, 0f), new Vector2(190f, 130f), new Vector2(340f, 72f));
        lobbyButton.onClick.AddListener(ReturnToLobby);
    }

    private void Rebuild()
    {
        if (table == null || !PhotonNetwork.InRoom) return;
        for (int i = table.childCount - 1; i >= 0; i--) Destroy(table.GetChild(i).gameObject);

        var ranked = new List<PhotonPlayer>(PhotonNetwork.PlayerList);
        ranked.Sort(mode.CompareForScoreboard);
        titleText.text = mode.ScoreboardTitle(ranked);

        var columns = mode.ScoreboardColumns;
        table.sizeDelta = new Vector2(60f + nameWidth + columns.Count * columnWidth + 32f, table.sizeDelta.y);
        var header = new List<string> { "#", "Player" };
        foreach (var c in columns) header.Add(c.Header);
        AddRow(header, isHeader: true, isOwn: false);

        for (int i = 0; i < ranked.Count; i++)
        {
            var p = ranked[i];
            var cells = new List<string> { (i + 1).ToString(), string.IsNullOrEmpty(p.NickName) ? $"Player {p.ActorNumber}" : p.NickName };
            foreach (var c in columns) cells.Add(c.Value(p));
            AddRow(cells, isHeader: false, isOwn: p.IsLocal);
        }
    }

    private void AddRow(List<string> cells, bool isHeader, bool isOwn)
    {
        var row = NewChild(isHeader ? "Header" : "Row", table);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = rowHeight;
        if (!isHeader) row.gameObject.AddComponent<Image>().color = isOwn ? ownRowColor : rowColor;

        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 0, 0);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        for (int i = 0; i < cells.Count; i++)
        {
            float width = i == 0 ? 60f : i == 1 ? nameWidth : columnWidth;
            var align   = i == 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
            var text = NewText($"Cell{i}", row, cells[i], isHeader ? 26f : 32f, isHeader ? titleFont : bodyFont, align,
                               isHeader ? accent : Color.white);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode     = TextOverflowModes.Ellipsis;
            text.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
        }
    }

    // ─── UI helpers ───────────────────────────────────────────────────────────

    private static RectTransform NewChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static TMP_Text NewText(string name, Transform parent, string text, float size, TMP_FontAsset font,
                                    TextAlignmentOptions align, Color color)
    {
        var tmp = NewChild(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.alignment = align;
        tmp.color     = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private Button NewButton(string name, string label)
    {
        var rect = NewChild(name, transform);
        rect.gameObject.AddComponent<Image>().color = accent;
        var button = rect.gameObject.AddComponent<Button>();
        var text = NewText("Label", rect, label, 34f, titleFont, TextAlignmentOptions.Center, background);
        Stretch(text.rectTransform);
        return button;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, anchor.y);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
