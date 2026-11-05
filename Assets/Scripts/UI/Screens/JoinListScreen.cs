using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The game browser: every open game (from <see cref="RoomDirectory"/>, updated live) as a card with the host's
/// game name, mode, map and players, and a Join button. Search filters by name, mode or map. With no games,
/// an empty state offers to create one. Escape or Back returns to the main menu.
/// Attach to one GameObject in the JoinList scene.
/// </summary>
public class JoinListScreen : MenuScreen
{
    private RectTransform list;
    private GameObject empty;
    private TMP_InputField search;
    private TMP_Text countText;
    private bool joining, creating;

    protected override void Build()
    {
        Header("Join a Game", "Pick a lobby to jump into, or start your own.");

        var back = UIKit.Button(Root, "Back", UIKit.Style.Ghost, Back, 64f, 26f);
        UIKit.Place((RectTransform)back.transform, new Vector2(1f, 1f), new Vector2(-80f, -60f), new Vector2(180f, 64f));
        back.GetComponent<UIButtonFx>().ClickSound = UISound.Kind.Back;

        // Toolbar: search, count, create
        var bar = UIKit.Rect("Toolbar", Root);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(80f, -270f);
        bar.offsetMax = new Vector2(-80f, -200f);
        UIKit.HStack(bar, 16f, null, TextAnchor.MiddleLeft, expandHeight: false);
        search = UIKit.InputField(bar, "Search by name, mode or map…", height: 64f);
        UIKit.Size(search, 620f, 64f);
        search.onValueChanged.AddListener(_ => Rebuild());
        countText = UIKit.Text(bar, "", 24f, UIKit.Font.Bold, Theme.textMuted);
        UIKit.Size(countText, flexWidth: 1f);
        var create = UIKit.Button(bar, "Create Game", UIKit.Style.Primary, Create, 64f, 28f);
        UIKit.Size(create, 300f, 64f);

        // The list
        var panel = UIKit.Card(Root, Theme.panel, 28f, 0.45f, "Games");
        UIKit.Stretch(panel, 80f, 80f, 300f, 70f);
        var inner = UIKit.Rect("Inner", panel);
        UIKit.Stretch(inner, 24f, 12f, 24f, 24f);
        list = UIKit.ScrollList(inner, 12f, UIKit.Pad(4, 4));

        // Empty state
        var emptyRoot = UIKit.Rect("Empty", panel);
        UIKit.Stretch(emptyRoot);
        UIKit.VStack(emptyRoot, 18f, null, TextAnchor.MiddleCenter, expandWidth: false);
        UIKit.Title(emptyRoot, "No games yet", 56f, TextAlignmentOptions.Center);
        UIKit.Text(emptyRoot, "Be the first! Create a game and your friends will see it here.", 26f, UIKit.Font.Body,
            Theme.textMuted, TextAlignmentOptions.Center);
        var emptyCreate = UIKit.Button(emptyRoot, "Create Game", UIKit.Style.Primary, Create, 80f, 34f);
        UIKit.Size(emptyCreate, 380f, 80f);
        empty = emptyRoot.gameObject;
    }

    public override void OnEnable()
    {
        base.OnEnable();
        RoomDirectory.Changed += Rebuild;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        RoomDirectory.Changed -= Rebuild;
    }

    private void Start()
    {
        if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InLobby) PhotonNetwork.JoinLobby();
        Rebuild();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && !search.isFocused) Back();
    }

    private void Back() => UIFade.LoadScene(SceneNames.MainMenu);

    // ─── List ─────────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        if (list == null) return;
        foreach (Transform child in list) Destroy(child.gameObject);

        string filter = search.text.Trim().ToLowerInvariant();
        var rooms = new List<RoomInfo>(RoomDirectory.Rooms);
        rooms.RemoveAll(r => !r.IsOpen || !r.IsVisible || r.RemovedFromList);
        rooms.Sort((a, b) => b.PlayerCount.CompareTo(a.PlayerCount));

        int shown = 0;
        foreach (var info in rooms)
        {
            string mode = GameModeRegistry.DisplayName(NetProps.Get(info.CustomProperties, RoomKeys.GameMode, GameModeRegistry.DefaultKey));
            string map  = Nicify(NetProps.Get(info.CustomProperties, RoomKeys.Map, SceneNames.DefaultMap));
            if (filter.Length > 0 && !(info.Name + " " + mode + " " + map).ToLowerInvariant().Contains(filter)) continue;
            AddRow(info, mode, map, shown++);
        }

        int total = rooms.Count;
        countText.text = total == 0 ? "" : total == 1 ? "1 game open" : $"{total} games open";
        empty.SetActive(total == 0);
        RequestLayout();
    }

    private void AddRow(RoomInfo info, string mode, string map, int index)
    {
        var row = UIKit.Panel(list, Theme.card, 20f, info.Name).rectTransform;
        UIKit.Size(row, height: 104f);
        UIKit.HStack(row, 24f, UIKit.Pad(24, 16), TextAnchor.MiddleLeft, expandHeight: false);

        var party = Theme.Party;
        Avatar(row, info.Name, party[index % party.Length], 64f);

        var texts = UIKit.Rect("Texts", row);
        UIKit.VStack(texts, 2f, null, TextAnchor.MiddleLeft);
        UIKit.Size(texts, flexWidth: 1f);
        UIKit.OneLine(UIKit.Text(texts, info.Name, 30f, UIKit.Font.Display));
        UIKit.Text(texts, $"{mode}  ·  {map}", 22f, UIKit.Font.Body, Theme.textMuted).textWrappingMode = TextWrappingModes.NoWrap;

        bool full = info.MaxPlayers > 0 && info.PlayerCount >= info.MaxPlayers;
        UIKit.Chip(row, $"{info.PlayerCount}/{info.MaxPlayers} players", full ? Theme.danger : Theme.lime, fontSize: 20f);

        string roomName = info.Name;
        var join = UIKit.Button(row, full ? "Full" : "Join", UIKit.Style.Primary, () => Join(roomName), 64f, 28f);
        UIKit.Size(join, 180f, 64f);
        join.interactable = !full;
    }

    // ─── Joining ──────────────────────────────────────────────────────────────

    private void Join(string roomName)
    {
        if (joining || !PhotonNetwork.IsConnectedAndReady) return;
        joining = true;
        Toast("Joining…");
        PhotonNetwork.JoinRoom(roomName);
    }

    private void Create()
    {
        if (joining || !PhotonNetwork.IsConnectedAndReady) return;
        joining = creating = true;
        RoomFactory.CreateRoom();
    }

    public override void OnConnectedToMaster() => PhotonNetwork.JoinLobby();

    public override void OnCreatedRoom() => PhotonNetwork.LoadLevel(SceneNames.Lobby);

    public override void OnCreateRoomFailed(short returnCode, string message) => RoomFactory.CreateRoom();

    public override void OnJoinedRoom()
    {
        if (creating) return;
        // The host's scene normally loads automatically; this covers rooms where it isn't set
        if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("curScn"))
            PhotonNetwork.LoadLevel(SceneNames.Lobby);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        joining = false;
        Toast(returnCode == ErrorCode.GameFull ? "That game is full." : "Couldn't join that game. It may have started.", error: true);
    }
}
