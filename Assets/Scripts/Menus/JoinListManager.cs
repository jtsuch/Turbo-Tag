using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Join List scene controller: makes sure we're in the Photon lobby (so the room list
/// updates), joins the room the player picks, and goes back to the main menu on Escape.
/// After joining, AutomaticallySyncScene loads whatever scene the host is in.
/// Attach to: the JoinListManager GameObject in the JoinList scene.
/// </summary>
public class JoinListManager : MonoBehaviourPunCallbacks
{
    private void Start()
    {
        if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InLobby)
            PhotonNetwork.JoinLobby();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) BackToMenu();
    }

    public void JoinRoomInList(string roomName)
    {
        if (PhotonNetwork.IsConnectedAndReady) PhotonNetwork.JoinRoom(roomName);
    }

    public void BackToMenu() => SceneManager.LoadScene(SceneNames.MainMenu);

    public override void OnConnectedToMaster() => PhotonNetwork.JoinLobby();

    public override void OnJoinedRoom()
    {
        // Normally the host's scene is synced automatically; this covers rooms where it isn't set
        if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("curScn"))
            PhotonNetwork.LoadLevel(SceneNames.Lobby);
    }

    public override void OnJoinRoomFailed(short returnCode, string message) =>
        Debug.LogWarning($"[JoinList] Could not join ({returnCode}): {message}");
}
