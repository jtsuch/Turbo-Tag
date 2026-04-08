using UnityEngine;
using Photon.Pun;
using UnityEngine.SceneManagement;

public class ConnectToServer : MonoBehaviourPunCallbacks
{
    public SettingsManager loading;
    void Start()
    {
        Debug.Log("Connecting...");
        PhotonNetwork.ConnectUsingSettings(); // Connect to master server
    }

    public override void OnConnectedToMaster()
    {
        print("Connected to Server");
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        print("We're connected and moving to Lobby scene!");
        SceneManager.LoadScene("MainMenu");
    }

    public override void OnJoinedRoom()
    {
        print("In ConnectToServer: OnJoinedRoom() called. We are in a room.");
    }
}
