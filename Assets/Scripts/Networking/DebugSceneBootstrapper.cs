using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Editor-only behaviour: lets a level be played directly with the Play button. The class exists in
/// builds too (doing nothing there) so the LevelSystems prefab's serialized component stays valid;
/// wrapping the whole class in #if UNITY_EDITOR made it a missing script in builds.
///   Default — Photon offline mode, a private single-player room.
///   Multiplayer Test Mode (menu "Turbo Tag/Multiplayer Test Mode") — connects to Photon and joins
///   a hidden room shared by every editor instance on this machine, so Multiplayer Play Mode
///   virtual players land in the same match. Falls back to offline if Photon is unreachable.
/// In both cases the room gets the chosen game mode, then the Spawner spawns the player.
/// Attach to: any GameObject in a level scene (included in the LevelSystems prefab).
/// </summary>
public class DebugSceneBootstrapper : MonoBehaviour, IConnectionCallbacks, IMatchmakingCallbacks
{
    /// <summary>EditorPrefs key for Multiplayer Test Mode (shared by all editor instances).</summary>
    public const string MultiplayerTestPref = "TurboTag_MultiplayerTestMode";

    public static bool MultiplayerTestMode
    {
#if UNITY_EDITOR
        get => UnityEditor.EditorPrefs.GetBool(MultiplayerTestPref, false);
        set => UnityEditor.EditorPrefs.SetBool(MultiplayerTestPref, value);
#else
        get => false;
        set { }
#endif
    }

    [Header("Debug Session")]
    [Tooltip("Game mode key used when testing a level directly (e.g. Freeplay or Tag).")]
    [SerializeField] private string gameModeKey  = "Freeplay";
    [SerializeField] private bool   enableCheats = true;
    [Tooltip("Fallback spawn if the scene has no Spawner.")]
    [SerializeField] private Transform spawnPoint;

    private bool active;
    private bool joined;

    private void Awake()
    {
        if (!Application.isEditor) return; // Builds always arrive through the menus
        if (PhotonNetwork.IsConnected) return; // Arrived through the normal menu flow

        active = true;
        PhotonNetwork.AddCallbackTarget(this);
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.NickName = $"Tester{Random.Range(100, 1000)}";

        if (MultiplayerTestMode)
        {
            Debug.Log("[Debug] Multiplayer Test Mode — connecting to Photon...");
            NetworkConfig.Connect();
        }
        else
        {
            Debug.Log("[Debug] Direct scene load — starting offline mode.");
            PhotonNetwork.OfflineMode = true;
        }
    }

    private void OnDestroy()
    {
        if (active) PhotonNetwork.RemoveCallbackTarget(this);
    }

    // ─── Connection ───────────────────────────────────────────────────────────

    public void OnConnectedToMaster()
    {
        if (joined) return;

        var options = new RoomOptions
        {
            IsVisible  = false, // Never shows up in the real Join List
            MaxPlayers = RoomFactory.MaxPlayers,
            CustomRoomProperties = new Hashtable
            {
                [RoomKeys.GameMode]      = ModeKey,
                [RoomKeys.Map]           = gameObject.scene.name,
                [RuleKeys.CheatsEnabled] = enableCheats,
            },
        };

        if (PhotonNetwork.OfflineMode)
            PhotonNetwork.CreateRoom("DebugRoom", options);
        else
            PhotonNetwork.JoinOrCreateRoom(TestRoomName, options, TypedLobby.Default);
    }

    // Editor tests can pick the mode for the next play session (SessionState "TT_DebugMode")
    private string ModeKey
    {
        get
        {
#if UNITY_EDITOR
            string forced = UnityEditor.SessionState.GetString("TT_DebugMode", "");
            if (!string.IsNullOrEmpty(forced)) return forced;
#endif
            return gameModeKey;
        }
    }

    // One room per machine and level, so all local editor instances meet
    private string TestRoomName => $"EditorTest_{SystemInfo.deviceUniqueIdentifier}_{gameObject.scene.name}";

    public void OnDisconnected(DisconnectCause cause)
    {
        if (joined || PhotonNetwork.OfflineMode) return;
        Debug.LogWarning($"[Debug] Couldn't reach Photon ({cause}) — falling back to offline mode.");
        PhotonNetwork.OfflineMode = true;
    }

    public void OnJoinedRoom()
    {
        joined = true;
        Debug.Log($"[Debug] Joined '{PhotonNetwork.CurrentRoom.Name}' as {PhotonNetwork.NickName} " +
                  $"({PhotonNetwork.CurrentRoom.PlayerCount} player(s)).");

        if (Spawner.Instance != null)
        {
            Spawner.Instance.SpawnLocalPlayer();
            return;
        }

        AbilityLoadout.EnsureLocal();
        Vector3    position = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
        PhotonNetwork.Instantiate("Player/ThePlayer", position, rotation);
    }

    public void OnJoinRoomFailed(short returnCode, string message) =>
        Debug.LogError($"[Debug] Couldn't join the test room ({returnCode}): {message}");

    public void OnCreateRoomFailed(short returnCode, string message) =>
        Debug.LogError($"[Debug] Couldn't create the test room ({returnCode}): {message}");

    // Unused callbacks
    public void OnConnected() { }
    public void OnRegionListReceived(RegionHandler regionHandler) { }
    public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
    public void OnCustomAuthenticationFailed(string debugMessage) { }
    public void OnCreatedRoom() { }
    public void OnJoinRandomFailed(short returnCode, string message) { }
    public void OnLeftRoom() { }
    public void OnFriendListUpdate(List<FriendInfo> friendList) { }
}
