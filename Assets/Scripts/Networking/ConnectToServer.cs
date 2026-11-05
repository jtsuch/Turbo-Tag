using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loading screen: connects to Photon, joins the default lobby (needed for the room list),
/// then opens the main menu. Also sets session-wide network options.
/// Attach to: a GameObject in the Loading scene.
/// </summary>
public class ConnectToServer : MonoBehaviourPunCallbacks
{
    /// <summary>What the connection is doing right now, for the loading screen.</summary>
    public static string Status { get; private set; } = "Starting up…";

    private void Start()
    {
        // Only the master loads scenes; everyone else follows automatically
        PhotonNetwork.AutomaticallySyncScene = true;
        // Lets the host kick players from the lobby
        PhotonNetwork.EnableCloseConnection = true;

        if (string.IsNullOrEmpty(PhotonNetwork.NickName))
            PhotonNetwork.NickName = PlayerPrefs.GetString(MainMenuScreen.UsernamePrefsKey, $"Penguin{Random.Range(100, 999)}");

        Status = "Connecting to servers…";
        if (PhotonNetwork.IsConnected)
            OnConnectedToMaster();
        else
            NetworkConfig.Connect();
    }

    public override void OnConnectedToMaster()
    {
        Status = "Finding games…";
        if (!PhotonNetwork.InLobby) PhotonNetwork.JoinLobby();
        else OnJoinedLobby();
    }

    public override void OnJoinedLobby()
    {
        Status = "Ready!";
        UIFade.LoadScene(SceneNames.MainMenu);
    }

    public override void OnDisconnected(Photon.Realtime.DisconnectCause cause)
    {
        Debug.LogWarning($"[ConnectToServer] Disconnected: {cause}. Retrying...");
        Status = "Connection lost. Retrying…";
        NetworkConfig.Connect();
    }
}
