using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class DebugSceneBootstrapper : MonoBehaviour, IConnectionCallbacks, IMatchmakingCallbacks
{
    [Header("Debug Spawn Settings")]
    [SerializeField] private string playerPrefabName = "Player/ThePlayer"; // Must match name in Resources folder
    [SerializeField] private Transform spawnPoint;               // Assign in Inspector, or falls back to Vector3.zero

#if UNITY_EDITOR
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

    // -------------------------------------------------------------------------
    // IConnectionCallbacks — fires when offline mode is ready
    // -------------------------------------------------------------------------

    public void OnConnectedToMaster()
    {
        // Offline mode lands here first — now create a room
        PhotonNetwork.CreateRoom("DebugRoom");
    }

    // -------------------------------------------------------------------------
    // IMatchmakingCallbacks — fires when room is created and ready
    // -------------------------------------------------------------------------

    public void OnCreatedRoom() { }

    public void OnJoinedRoom()
    {
        // Room is ready — safe to spawn player now
        SpawnDebugPlayer();
    }

    private void SpawnDebugPlayer()
    {
        Vector3 position = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        // PhotonNetwork.Instantiate requires the prefab to be in a Resources folder
        GameObject playerObj = PhotonNetwork.Instantiate(
            playerPrefabName,
            position,
            rotation
        );

        Debug.Log($"[Debug] Spawned player at {position}");
    }

    // -------------------------------------------------------------------------
    // Unused required interface methods
    // -------------------------------------------------------------------------

    public void OnConnected() { }
    public void OnDisconnected(DisconnectCause cause) { }
    public void OnRegionListReceived(RegionHandler regionHandler) { }
    public void OnCustomAuthenticationResponse(System.Collections.Generic.Dictionary<string, object> data) { }
    public void OnCustomAuthenticationFailed(string debugMessage) { }
    public void OnCreateRoomFailed(short returnCode, string message) { }
    public void OnJoinedLobby() { }
    public void OnLeftLobby() { }
    public void OnRoomListUpdate(System.Collections.Generic.List<RoomInfo> roomList) { }
    public void OnJoinRoomFailed(short returnCode, string message) { }
    public void OnJoinRandomFailed(short returnCode, string message) { }
    public void OnLeftRoom() { }
    public void OnFriendListUpdate(System.Collections.Generic.List<FriendInfo> list) { }
    public void OnLobbyStatisticsUpdate(System.Collections.Generic.List<TypedLobbyInfo> lobbyStatistics) { }
#endif
}