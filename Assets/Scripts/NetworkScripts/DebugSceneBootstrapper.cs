#if UNITY_EDITOR
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class DebugSceneBootstrapper : MonoBehaviour, IConnectionCallbacks, IMatchmakingCallbacks
{
    [Header("Debug Spawn Settings")]
    [SerializeField] private string playerPrefabName = "Player/ThePlayer";
    [SerializeField] private Transform spawnPoint;

    private void Awake()
    {
        if (PhotonNetwork.IsConnected) return;

        Debug.Log("[Debug] Direct scene load detected — starting offline mode");
        PhotonNetwork.AddCallbackTarget(this);
        PhotonNetwork.OfflineMode = true;
    }

    private void OnDestroy()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    // IConnectionCallbacks
    public void OnConnectedToMaster()
    {
        PhotonNetwork.CreateRoom("DebugRoom");
    }

    // IMatchmakingCallbacks
    public void OnCreatedRoom() { }

    public void OnJoinedRoom()
    {
        SpawnDebugPlayer();
    }

    private void SpawnDebugPlayer()
    {
        SetDefaultAbilityProperties();

        Vector3    position = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        PhotonNetwork.Instantiate(playerPrefabName, position, rotation);
        Debug.Log($"[Debug] Spawned player at {position}");
    }

    private void SetDefaultAbilityProperties()
    {
        var props = new ExitGames.Client.Photon.Hashtable
        {
            ["BasicAbility"] = "BasicGrapple",
            ["QuickAbility"]  = "Dash",
            ["ThrowAbility"]  = "BoomStick",
            ["TrapAbility"]   = "Box",
        };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        Debug.Log("[Debug] Default abilities assigned.");
    }

    // Required interface stubs
    public void OnConnected() { }
    public void OnDisconnected(DisconnectCause cause) { }
    public void OnRegionListReceived(RegionHandler regionHandler) { }
    public void OnCustomAuthenticationResponse(System.Collections.Generic.Dictionary<string, object> data) { }
    public void OnCustomAuthenticationFailed(string debugMessage) { }
    public void OnCreateRoomFailed(short returnCode, string message) { }
    public void OnJoinedLobby() { }
    public void OnLeftLobby() { }
    public void OnRoomListUpdate(System.Collections.Generic.List<RoomInfo> _) { }
    public void OnJoinRoomFailed(short returnCode, string message) { }
    public void OnJoinRandomFailed(short returnCode, string message) { }
    public void OnLeftRoom() { }
    public void OnFriendListUpdate(System.Collections.Generic.List<FriendInfo> list) { }
    public void OnLobbyStatisticsUpdate(System.Collections.Generic.List<TypedLobbyInfo> _) { }
}
#endif
