using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// The room before a match. Left: everyone in the room as cards (penguin colour avatar, name, HOST and YOU tags,
/// a kick button for the host). Right: your penguin colour, and the match card (map thumbnail, game mode and its
/// description, Play for the host or "Waiting for the host" for everyone else). The host changes the map and the
/// mode's rules in modals; everyone sees them update live (room properties). Leave goes back to the main menu.
/// Attach to one GameObject in the Lobby scene.
/// </summary>
public class LobbyScreen : MenuScreen
{
    private RectTransform playerGrid;
    private TMP_Text playerCount, roomTitle;
    private readonly List<(Image ring, string skin)> swatches = new();

    private RawImage mapThumb;
    private TMP_Text mapName, modeName, modeDesc, waiting;
    private Button playButton, mapButton, rulesButton;

    private UIModal mapModal, rulesModal;
    private RectTransform mapGrid;
    private readonly List<(string map, Image bg)> mapCards = new();
    private RulesEditor rules;
    private bool starting;

    private static bool IsHost => PhotonNetwork.IsMasterClient;

    // ─── Build ────────────────────────────────────────────────────────────────

    protected override void Build()
    {
        var header = Header("Lobby", " ");
        roomTitle = header.GetChild(1).GetComponent<TMP_Text>();

        var leave = UIKit.Button(Root, "Leave", UIKit.Style.Ghost, () => PhotonNetwork.LeaveRoom(), 64f, 26f);
        UIKit.Place((RectTransform)leave.transform, new Vector2(1f, 1f), new Vector2(-80f, -60f), new Vector2(180f, 64f));
        leave.GetComponent<UIButtonFx>().ClickSound = UISound.Kind.Back;

        BuildPlayers();
        BuildSide();
        BuildMapModal();

        rulesModal = UIKit.Modal(Root, "Game Settings", new Vector2(1180f, 900f));
        rulesModal.NoFooter();
        rules = RulesEditor.Create(rulesModal.Body);
    }

    private void BuildPlayers()
    {
        var card = UIKit.Card(Root, Theme.panel, 32f, 0.45f, "Players");
        card.anchorMin = new Vector2(0f, 0f);
        card.anchorMax = new Vector2(1f, 1f);
        card.offsetMin = new Vector2(80f, 70f);
        card.offsetMax = new Vector2(-760f, -200f);

        var title = UIKit.Title(card, "Players", 44f);
        UIKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -24f), new Vector2(400f, 60f));
        playerCount = UIKit.Text(card, "", 30f, UIKit.Font.Display, Theme.primary, TextAlignmentOptions.Right);
        UIKit.Place(playerCount.rectTransform, new Vector2(1f, 1f), new Vector2(-36f, -28f), new Vector2(200f, 52f));

        var area = UIKit.Rect("Area", card);
        UIKit.Stretch(area, 28f, 16f, 100f, 28f);
        var content = UIKit.ScrollList(area, 0f);
        DestroyImmediate(content.GetComponent<VerticalLayoutGroup>()); // Now: a second layout group can't be added while one remains
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(500f, 104f);
        grid.spacing = new Vector2(16f, 16f);
        grid.constraint = GridLayoutGroup.Constraint.Flexible;
        grid.padding = UIKit.Pad(4);
        playerGrid = content;
    }

    private void BuildSide()
    {
        var side = UIKit.Rect("Side", Root);
        side.anchorMin = new Vector2(1f, 0f);
        side.anchorMax = new Vector2(1f, 1f);
        side.pivot = new Vector2(1f, 0.5f);
        side.offsetMin = new Vector2(-720f, 70f);
        side.offsetMax = new Vector2(-80f, -200f);
        UIKit.VStack(side, 24f, null, TextAnchor.UpperLeft);

        // Your penguin
        var you = UIKit.Card(side, Theme.panel, 28f, 0.45f, "You");
        UIKit.Size(you, height: 170f);
        UIKit.VStack(you, 10f, UIKit.Pad(28, 20), TextAnchor.UpperLeft);
        UIKit.Text(you, "YOUR PENGUIN", 20f, UIKit.Font.Bold, Theme.textMuted);
        var row = UIKit.Rect("Swatches", you);
        UIKit.HStack(row, 12f, null, TextAnchor.MiddleLeft, expandHeight: false);
        UIKit.Size(row, height: 64f);
        foreach (var skin in PenguinSkins.All)
        {
            var swatch = UIKit.Rect(skin.Name, row).gameObject.AddComponent<Image>();
            swatch.sprite = UISprites.Circle;
            swatch.color = skin.Color;
            UIKit.Size(swatch, 52f, 52f);
            var ring = UIKit.Rect("Ring", swatch.transform).gameObject.AddComponent<Image>();
            ring.sprite = UISprites.Ring;
            ring.raycastTarget = false;
            UIKit.Stretch(ring.rectTransform, -7f, -7f, -7f, -7f);
            swatch.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            swatch.gameObject.AddComponent<UIButtonFx>().HoverScale = 1.15f;
            string name = skin.Name;
            swatch.GetComponent<Button>().onClick.AddListener(() => { PenguinSkins.SetLocal(name); RefreshSwatches(); });
            swatches.Add((ring, name));
        }

        // The match
        var match = UIKit.Card(side, Theme.panel, 28f, 0.45f, "Match");
        UIKit.Size(match, flexHeight: 1f);
        UIKit.VStack(match, 14f, UIKit.Pad(28, 24), TextAnchor.UpperLeft);

        var thumbFrame = UIKit.Panel(match, Theme.field, 20f, "Thumb");
        UIKit.Size(thumbFrame, height: 250f);
        thumbFrame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        mapThumb = UIKit.Rect("Image", thumbFrame.transform).gameObject.AddComponent<RawImage>();
        UIKit.Stretch(mapThumb.rectTransform);
        mapThumb.raycastTarget = false;
        mapThumb.color = UIKit.WithAlpha(Theme.secondary, 0.4f);
        var mapLabelBg = UIKit.Panel(thumbFrame.transform, new Color(0f, 0f, 0f, 0.55f), 0f, "Label");
        mapLabelBg.rectTransform.anchorMin = new Vector2(0f, 0f);
        mapLabelBg.rectTransform.anchorMax = new Vector2(1f, 0f);
        mapLabelBg.rectTransform.pivot = new Vector2(0.5f, 0f);
        mapLabelBg.rectTransform.sizeDelta = new Vector2(0f, 64f);
        mapName = UIKit.Text(mapLabelBg.transform, "", 34f, UIKit.Font.Display);
        UIKit.Stretch(mapName.rectTransform, 20f, 150f, 0f, 0f);
        mapName.alignment = TextAlignmentOptions.MidlineLeft;
        mapButton = UIKit.Button(mapLabelBg.transform, "Change", UIKit.Style.Ghost, () => mapModal.Open(), 48f, 22f);
        UIKit.Place((RectTransform)mapButton.transform, new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(130f, 48f));

        var modeRow = UIKit.Rect("Mode", match);
        UIKit.HStack(modeRow, 12f, null, TextAnchor.MiddleLeft, expandHeight: false);
        UIKit.Size(modeRow, height: 60f);
        modeName = UIKit.Text(modeRow, "", 40f, UIKit.Font.Display, Theme.primary);
        modeName.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Size(modeName, flexWidth: 1f);
        rulesButton = UIKit.Button(modeRow, "Settings", UIKit.Style.Ghost, () => rulesModal.Open(), 52f, 22f);
        UIKit.Size(rulesButton, 160f, 52f);

        modeDesc = UIKit.Text(match, "", 22f, UIKit.Font.Body, Theme.textMuted);
        UIKit.Size(modeDesc, flexHeight: 1f);

        waiting = UIKit.Text(match, "Waiting for the host to start…", 26f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Center);
        UIKit.Size(waiting, height: 40f);
        playButton = UIKit.Button(match, "Play!", UIKit.Style.Primary, Play, 104f, 48f);
        playButton.gameObject.SetActive(false); // Shown to the host once the room is known
    }

    private void BuildMapModal()
    {
        mapModal = UIKit.Modal(Root, "Choose a Map", new Vector2(1400f, 860f));
        mapModal.NoFooter();
        var content = UIKit.ScrollList(mapModal.Body, 0f);
        DestroyImmediate(content.GetComponent<VerticalLayoutGroup>()); // Now: a second layout group can't be added while one remains
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(400f, 300f);
        grid.spacing = new Vector2(24f, 24f);
        grid.padding = UIKit.Pad(8);
        mapGrid = content;

        foreach (string map in SceneNames.Maps())
        {
            if (!MatchLauncher.CanLoad(map)) continue;
            var card = UIKit.Panel(mapGrid, Theme.card, 22f, map);
            var thumbFrame = UIKit.Panel(card.transform, Theme.field, 16f, "Thumb");
            UIKit.Stretch(thumbFrame.rectTransform, 12f, 12f, 12f, 72f);
            thumbFrame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var image = UIKit.Rect("Image", thumbFrame.transform).gameObject.AddComponent<RawImage>();
            UIKit.Stretch(image.rectTransform);
            image.texture = Resources.Load<Texture2D>("MapThumbnails/" + map);
            image.color = image.texture != null ? Color.white : Theme.field;
            image.raycastTarget = false;
            var label = UIKit.Text(card.transform, Nicify(map), 30f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 0f);
            label.rectTransform.pivot = new Vector2(0.5f, 0f);
            label.rectTransform.sizeDelta = new Vector2(0f, 72f);

            var button = card.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var fx = card.gameObject.AddComponent<UIButtonFx>();
            fx.HoverScale = 1.03f;
            fx.Face = card;           // The card itself carries the colour (selected = violet)
            fx.FaceColor = Theme.card;
            fx.LipDepth = 0f;         // Layout places the card, so it doesn't move when pressed
            string chosen = map;
            button.onClick.AddListener(() =>
            {
                if (!IsHost) return;
                NetProps.SetRoom(RoomKeys.Map, chosen);
                mapModal.Close();
            });
            mapCards.Add((map, card));
        }
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        PenguinSkins.EnsureLocal();
        if (IsHost) EnsureRoomDefaults();
        RefreshAll();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && !mapModal.IsOpen && !rulesModal.IsOpen && !rules.IsTyping) PhotonNetwork.LeaveRoom();
    }

    public override void OnJoinedRoom()                               { PenguinSkins.EnsureLocal(); RefreshAll(); }
    public override void OnRoomPropertiesUpdate(Hashtable changed)    => RefreshMatch();
    public override void OnPlayerEnteredRoom(PhotonPlayer newPlayer)  { RefreshPlayers(); Toast($"{newPlayer.NickName} joined!"); }
    public override void OnPlayerLeftRoom(PhotonPlayer otherPlayer)   => RefreshPlayers();
    public override void OnPlayerPropertiesUpdate(PhotonPlayer target, Hashtable changed)
    {
        if (changed.ContainsKey(PlayerKeys.Skin)) { RefreshPlayers(); RefreshSwatches(); }
    }

    public override void OnMasterClientSwitched(PhotonPlayer newMaster)
    {
        if (IsHost) { EnsureRoomDefaults(); Toast("You're the host now."); }
        RefreshAll();
    }

    public override void OnLeftRoom() => SceneManager.LoadScene(SceneNames.MainMenu);

    private void Play()
    {
        if (!IsHost || starting) return;
        starting = true;
        playButton.interactable = false;
        MatchLauncher.StartMatch();
    }

    // The map must exist in this build and the mode's rules must all be set
    private static void EnsureRoomDefaults()
    {
        if (!NetProps.RoomHas(RoomKeys.Map) || !MatchLauncher.CanLoad(MatchRules.Map))
        {
            string fallback = SceneNames.DefaultMap;
            foreach (string map in SceneNames.Maps())
                if (MatchLauncher.CanLoad(map)) { fallback = MatchLauncher.CanLoad(SceneNames.DefaultMap) ? SceneNames.DefaultMap : map; break; }
            NetProps.SetRoom(RoomKeys.Map, fallback);
        }

        var mode = GameModeRegistry.Find(MatchRules.GameModeKey);
        if (mode == null) foreach (var m in GameModeRegistry.Definitions) { mode = m; break; }
        MatchRules.ApplyGameMode(mode, overwrite: false);
    }

    // ─── Refresh ──────────────────────────────────────────────────────────────

    private void RefreshAll()
    {
        RefreshPlayers();
        RefreshSwatches();
        RefreshMatch();
    }

    private void RefreshPlayers()
    {
        if (!PhotonNetwork.InRoom) return;
        var room = PhotonNetwork.CurrentRoom;
        roomTitle.text = room.Name;
        playerCount.text = $"{room.PlayerCount}/{room.MaxPlayers}";

        foreach (Transform child in playerGrid) Destroy(child.gameObject);
        var players = new List<PhotonPlayer>(PhotonNetwork.PlayerList);
        players.Sort((a, b) => a.IsMasterClient != b.IsMasterClient ? (a.IsMasterClient ? -1 : 1) : a.ActorNumber.CompareTo(b.ActorNumber));
        foreach (var p in players) PlayerCard(p);
        RequestLayout();
    }

    private void PlayerCard(PhotonPlayer p)
    {
        var card = UIKit.Panel(playerGrid, p.IsLocal ? Theme.cardHover : Theme.card, 22f, p.NickName);
        UIKit.HStack(card.transform, 18f, UIKit.Pad(20, 16), TextAnchor.MiddleLeft, expandHeight: false);
        if (p.IsLocal)
        {
            var outline = card.gameObject.AddComponent<Outline>();
            outline.effectColor = UIKit.WithAlpha(Theme.primary, 0.8f);
            outline.effectDistance = new Vector2(3f, -3f);
        }

        Avatar(card.transform, p.NickName, SkinColor(PenguinSkins.Of(p)), 68f);

        var texts = UIKit.Rect("Texts", card.transform);
        UIKit.VStack(texts, 6f, null, TextAnchor.MiddleLeft);
        UIKit.Size(texts, flexWidth: 1f);
        UIKit.OneLine(UIKit.Text(texts, string.IsNullOrEmpty(p.NickName) ? $"Player {p.ActorNumber}" : p.NickName, 30f, UIKit.Font.Display));
        var tags = UIKit.Rect("Tags", texts);
        UIKit.HStack(tags, 8f, null, TextAnchor.MiddleLeft, expandHeight: false);
        UIKit.Size(tags, height: 30f);
        if (p.IsMasterClient) UIKit.Chip(tags, "HOST", Theme.primary);
        if (p.IsLocal) UIKit.Chip(tags, "YOU", Theme.cyan);

        if (IsHost && !p.IsLocal)
        {
            var target = p;
            var kick = UIKit.RoundButton(card.transform, "×", () => PhotonNetwork.CloseConnection(target), 48f, UIKit.Style.Danger);
            kick.GetComponent<UIButtonFx>().ClickSound = UISound.Kind.Back;
        }
    }

    private void RefreshSwatches()
    {
        string current = PhotonNetwork.InRoom ? PenguinSkins.Of(PhotonNetwork.LocalPlayer) : PenguinSkins.Saved;
        foreach (var (ring, skin) in swatches) ring.enabled = skin == current;
    }

    private void RefreshMatch()
    {
        if (!PhotonNetwork.InRoom) return;
        string map = MatchRules.Map;
        mapName.text = Nicify(map);
        var thumb = Resources.Load<Texture2D>("MapThumbnails/" + map);
        mapThumb.texture = thumb;
        mapThumb.color = thumb != null ? Color.white : UIKit.WithAlpha(Theme.secondary, 0.4f);

        var mode = GameModeRegistry.Find(MatchRules.GameModeKey);
        modeName.text = mode != null ? mode.gameModeName : MatchRules.GameModeKey;
        modeDesc.text = mode != null ? mode.description : "";

        mapButton.gameObject.SetActive(IsHost);
        UIKit.Label(rulesButton).text = IsHost ? "Settings" : "Rules";
        playButton.gameObject.SetActive(IsHost);
        playButton.interactable = !starting;
        waiting.gameObject.SetActive(!IsHost);

        foreach (var (m, bg) in mapCards)
            bg.GetComponent<UIButtonFx>().FaceColor = m == map ? Theme.secondary : Theme.card;
        RequestLayout();
    }

    private static Color SkinColor(string skin)
    {
        foreach (var s in PenguinSkins.All) if (s.Name == skin) return s.Color;
        return Color.gray;
    }
}
