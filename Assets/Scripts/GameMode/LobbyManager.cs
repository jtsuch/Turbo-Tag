using TMPro;
using UnityEngine;
using Photon.Pun;
using UnityEngine.UI;
using System.Linq;
using System.Collections;
using System.Data;
using System;

public class LobbyManager : MonoBehaviourPunCallbacks
{
    [Header("UI References")]
    public Button playButton;
    public Button mapButton;
    public Button gameModeButton;
    public GameObject settingsWindow;
    public GameObject mapSelectionWindow;
    public GameObject gameModeSelectionWindow;
    public TMP_Text mapText;
    public TMP_Text gameModeText;
    public ToggleGroup mapToggleGroup;
    private Toggle activeMapToggle;
    public Toggle defaultMapToggle;

    [Header("Game Settings References")]
    public TMP_Dropdown gameModeDropdown;
    public Toggle allowCheats;
    public TMP_InputField hidingDuration;
    public TMP_InputField seekingDuration;
    public Slider hunterCount;

    [Header("Player Count")]
    public TMP_Text PlayerTotal;
    private int currentPlayerCount = 0;
    private int maxPlayers = 0;

    public void Start()
    {
        currentPlayerCount = PhotonNetwork.CurrentRoom.PlayerCount;
        maxPlayers = PhotonNetwork.CurrentRoom.MaxPlayers;
        PlayerTotal.text = currentPlayerCount + "/" + maxPlayers;
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        currentPlayerCount++;
        PlayerTotal.text = currentPlayerCount + "/" + maxPlayers;
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player oldPlayer)
    {
        currentPlayerCount--;
        PlayerTotal.text = currentPlayerCount + "/" + maxPlayers;
    }

    /*
    *
    * General UI
    *
    */
    public void OnExitPressed()
    {
        PhotonNetwork.LeaveRoom();
    }
    public override void OnLeftRoom()
    {
        PhotonNetwork.LoadLevel("MainMenu");
    }
    public void OnDonePressed()
    {
        settingsWindow.SetActive(false);
    }

    public void OnPlayPressed()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        UploadSettings();
        AssignHunters((int)hunterCount.value);
        StartCoroutine(BriefPauseToUploadData());
    }

    private IEnumerator BriefPauseToUploadData()
    {
        if(PhotonNetwork.IsMasterClient)
        {
            // Small delay to allow Lproperties to be sent
            yield return new WaitForSeconds(0.5f);
            photonView.RPC("EnterNextPhase", RpcTarget.All);
        }
    }

    public void OnMapOpened()
    {
        settingsWindow.SetActive(true);
        gameModeSelectionWindow.SetActive(false);
        mapSelectionWindow.SetActive(true);
    }

    public void OnGameModeOpened()
    {
        settingsWindow.SetActive(true);
        mapSelectionWindow.SetActive(false);
        gameModeSelectionWindow.SetActive(true);
    }

    public void OnToggleChanged(Toggle changedToggle)
    {
        activeMapToggle = changedToggle;
    }

    /*
    *
    * Backend
    *
    */
    private const string MAP_KEY = "Map";
    private const string GAME_MODE_KEY = "GameMode";
    private const string CHEATS_KEY = "Cheats";
    private const string HIDING_DURATION_KEY = "HidingDuration";
    private const string SEEKING_DURATION_KEY = "SeekingDuration";
    private const string HUNTER_COUNT_KEY = "HunterCount";
    private readonly string[] mapNameList = new string[3] { "OG", "Level2", "PenguinWorld" };
    private void UploadSettings()
    {
        // Map
        if (activeMapToggle == null) 
            UpdateRoomProperty(MAP_KEY, mapNameList[0]);
        else
        {
            int index = activeMapToggle.transform.GetSiblingIndex();
            UpdateRoomProperty(MAP_KEY, mapNameList[index]);
        }

        // Game Mode
        UpdateRoomProperty(GAME_MODE_KEY, gameModeDropdown.options[gameModeDropdown.value].text);

        // Allow Cheats
        UpdateRoomProperty(CHEATS_KEY, allowCheats.isOn);

        // Hiding Duration
        UpdateRoomProperty(HIDING_DURATION_KEY, hidingDuration.text);

        // Seeking Duration
        UpdateRoomProperty(SEEKING_DURATION_KEY, seekingDuration.text);

        // Hunter Count
        UpdateRoomProperty(HUNTER_COUNT_KEY, (int)hunterCount.value);
    }

    public void OnMapSelected(string mapName)
    {
        mapText.text = "Map: " + mapName;
        UpdateRoomProperty(MAP_KEY, mapName);
    }
    
    public void AssignHunters(int hunterCount)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // Get all players
        var players = PhotonNetwork.PlayerList.ToList();
        Debug.Log("Players: " + players);
        
        if (players.Count == 1) return; // No need to assign hunters if only one player

        // Shuffle them
        players = players.OrderBy(x => UnityEngine.Random.value).ToList();
        Debug.Log("Players again: " + players);

        // Pick the hunters
        var hunters = players.Take(hunterCount).Select(p => p.ActorNumber).ToArray();
        Debug.Log("Hunters first: " + hunters);

        // Upload the list of hunters
        UpdateRoomProperty("Hunters", hunters);
        //var props = new ExitGames.Client.Photon.Hashtable() { { "Hunters", hunters } };
        //PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    private void UpdateRoomProperty(string key, object value)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        var props = new ExitGames.Client.Photon.Hashtable() { { key, value } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    [PunRPC]
    private void EnterNextPhase()
    {
        Debug.Log("Entering Pregame Phase");
        // Optional: lock the room so no one new joins mid-load
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.CurrentRoom.IsOpen = false;
            PhotonNetwork.CurrentRoom.IsVisible = false;
        }

        // Load the next scene for all players
        PhotonNetwork.LoadLevel("Pregame");
    }
}
