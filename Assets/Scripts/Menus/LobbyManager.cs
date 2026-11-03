using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Lobby screen. The host picks the map and starts the game; the game mode and its rules are edited
/// in the game-settings window by <see cref="GameModeRulesPanel"/> (rows built from the mode's data).
/// Everything is written to room properties, so everyone sees the same settings read-only.
/// Starting closes the room and moves everyone to Pregame via AutomaticallySyncScene.
/// Attach to: the LobbyManager GameObject in the Lobby scene (buttons call the public methods).
/// </summary>
public class LobbyManager : MonoBehaviourPunCallbacks
{
    [Header("Buttons & Windows")]
    [SerializeField] private Button     playButton;
    [SerializeField] private Button     mapButton;
    [SerializeField] private Button     gameModeButton;
    [SerializeField] private GameObject settingsWindow;
    [SerializeField] private GameObject mapSelectionWindow;
    [SerializeField] private GameObject gameModeSelectionWindow;

    [Header("Summary Text")]
    [SerializeField] private TMP_Text mapText;
    [SerializeField] private TMP_Text gameModeText;
    [FormerlySerializedAs("PlayerTotal")]
    [SerializeField] private TMP_Text playerTotal;

    [Header("Map Selection")]
    [SerializeField] private Toggle defaultMapToggle;

    private MapToggle[] mapToggles;
    private bool        starting;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        settingsWindow.SetActive(false);
        BuildMapToggles();

        if (PhotonNetwork.IsMasterClient) EnsureRoomDefaults();

        Refresh();
    }

    // ─── Photon Callbacks ─────────────────────────────────────────────────────

    public override void OnRoomPropertiesUpdate(Hashtable changed)   => Refresh();
    public override void OnPlayerEnteredRoom(PhotonPlayer newPlayer) => Refresh();
    public override void OnPlayerLeftRoom(PhotonPlayer otherPlayer)  => Refresh();

    public override void OnMasterClientSwitched(PhotonPlayer newMaster)
    {
        if (PhotonNetwork.IsMasterClient) EnsureRoomDefaults();
        Refresh();
    }

    public override void OnLeftRoom() => SceneManager.LoadScene(SceneNames.MainMenu);

    // ─── Button entry points (wired in the scene) ─────────────────────────────

    public void OnPlayPressed()
    {
        if (!PhotonNetwork.IsMasterClient || starting) return;
        starting = true;
        MatchLauncher.StartMatch();
    }

    public void OnExitPressed() => PhotonNetwork.LeaveRoom();

    public void OnDonePressed() => settingsWindow.SetActive(false);

    public void OnMapOpened()      => OpenSettings(mapSelectionWindow);
    public void OnGameModeOpened() => OpenSettings(gameModeSelectionWindow);

    /// <summary>Map toggle changed (wired on each map Toggle).</summary>
    public void OnToggleChanged(Toggle changed)
    {
        if (!changed.isOn || !changed.TryGetComponent(out MapToggle map)) return;
        SetRule(RoomKeys.Map, map.mapName);
    }

    private void OpenSettings(GameObject window)
    {
        settingsWindow.SetActive(true);
        mapSelectionWindow.SetActive(window == mapSelectionWindow);
        gameModeSelectionWindow.SetActive(window == gameModeSelectionWindow);
    }

    // ─── Host settings ────────────────────────────────────────────────────────

    private void EnsureRoomDefaults()
    {
        var props = new Hashtable();
        if (!NetProps.RoomHas(RoomKeys.Map) || !CanLoad(MatchRules.Map))
            props[RoomKeys.Map] = DefaultMapName();
        if (props.Count > 0) NetProps.SetRoom(props);

        // Fill in any rules the selected mode defines but the room doesn't have yet
        MatchRules.ApplyGameMode(GameModeRegistry.Find(MatchRules.GameModeKey) ?? FirstMode(), overwrite: false);
    }

    private static void SetRule(string key, object value)
    {
        if (PhotonNetwork.IsMasterClient) NetProps.SetRoom(key, value);
    }

    // ─── Display ──────────────────────────────────────────────────────────────

    private void Refresh()
    {
        if (!PhotonNetwork.InRoom) return;
        bool isHost = PhotonNetwork.IsMasterClient;

        var room = PhotonNetwork.CurrentRoom;
        playerTotal.text = $"{room.PlayerCount}/{room.MaxPlayers}";

        string modeKey = MatchRules.GameModeKey;
        mapText.text      = "Map: " + MatchRules.Map;
        gameModeText.text = "Mode: " + GameModeRegistry.DisplayName(modeKey);

        foreach (var toggle in mapToggles)
        {
            var t = toggle.GetComponent<Toggle>();
            if (t != null) t.SetIsOnWithoutNotify(toggle.mapName == MatchRules.Map);
        }

        // Only the host can change anything
        playButton.interactable = isHost && !starting;
        foreach (var toggle in mapToggles)
        {
            var t = toggle.GetComponent<Toggle>();
            if (t != null) t.interactable = isHost && CanLoad(toggle.mapName);
        }
    }

    /// <summary>
    /// One toggle per map in Build Settings (see <see cref="SceneNames.Maps"/>). Toggles authored
    /// in the scene are kept for their maps (with their pictures); missing maps get a copy of the
    /// first toggle, and toggles for maps that no longer exist are hidden.
    /// Thumbnails for generated toggles load from Resources/MapThumbnails/{map} if present.
    /// </summary>
    private void BuildMapToggles()
    {
        var authored = FindObjectsByType<MapToggle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var maps     = SceneNames.Maps();
        var result   = new List<MapToggle>();

        foreach (var toggle in authored)
        {
            bool valid = maps.Contains(toggle.mapName);
            toggle.gameObject.SetActive(valid);
            if (valid) result.Add(toggle);
        }

        MapToggle template = authored.Length > 0 ? authored[0] : null;
        foreach (string map in maps)
        {
            if (result.Exists(t => t.mapName == map)) continue;
            if (template == null)
            {
                Debug.LogWarning("[Lobby] No MapToggle in the scene to copy; add one to list maps.");
                break;
            }

            var copy = Instantiate(template.gameObject, template.transform.parent);
            copy.name = map + "Toggle";
            copy.SetActive(true);

            var toggle = copy.GetComponent<MapToggle>();
            toggle.mapName = map;
            var label = copy.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = Nicify(map);

            var thumbnail = Resources.Load<Texture2D>("MapThumbnails/" + map);
            foreach (var image in copy.GetComponentsInChildren<RawImage>(true))
            {
                if (thumbnail != null) image.texture = thumbnail;
                else                   image.gameObject.SetActive(false); // Don't show another map's picture
            }
            result.Add(toggle);
        }

        // Alphabetical order in the list
        result.Sort((a, b) => string.CompareOrdinal(a.mapName, b.mapName));
        for (int i = 0; i < result.Count; i++) result[i].transform.SetSiblingIndex(i);

        mapToggles = result.ToArray();
    }

    // "PenguinsEdge" → "Penguins Edge"
    private static string Nicify(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");

    private string DefaultMapName()
    {
        if (defaultMapToggle != null && defaultMapToggle.TryGetComponent(out MapToggle map) && CanLoad(map.mapName))
            return map.mapName;
        foreach (var toggle in mapToggles)
            if (CanLoad(toggle.mapName)) return toggle.mapName;
        return SceneNames.DefaultMap;
    }

    private static GameModeDefinition FirstMode()
    {
        foreach (var mode in GameModeRegistry.Definitions) return mode;
        return null;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static bool CanLoad(string sceneName) => MatchLauncher.CanLoad(sceneName);
}
