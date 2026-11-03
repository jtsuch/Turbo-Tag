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
    private void Start()
    {
        // Only the master loads scenes; everyone else follows automatically
        PhotonNetwork.AutomaticallySyncScene = true;
        // Lets the host kick players from the lobby
        PhotonNetwork.EnableCloseConnection = true;

        if (string.IsNullOrEmpty(PhotonNetwork.NickName))
            PhotonNetwork.NickName = PlayerPrefs.GetString(PlayFabLoginManager.UsernamePrefsKey, $"Penguin{Random.Range(100, 999)}");

        if (PhotonNetwork.IsConnected)
            OnConnectedToMaster();
        else
            NetworkConfig.Connect();
    }

    public override void OnConnectedToMaster()
    {
        if (!PhotonNetwork.InLobby) PhotonNetwork.JoinLobby();
        else OnJoinedLobby();
    }

    public override void OnJoinedLobby()
    {
        SceneManager.LoadScene(SceneNames.MainMenu);
    }

    public override void OnDisconnected(Photon.Realtime.DisconnectCause cause)
    {
        Debug.LogWarning($"[ConnectToServer] Disconnected: {cause}. Retrying...");
        NetworkConfig.Connect();
    }
}
