using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Session-wide cache of joinable rooms. Photon sends the full room list once when the client
/// joins the lobby and only changes afterwards, so a screen that starts listening later (the Join
/// List) would miss it. This listens from startup and keeps the current list for any screen.
/// </summary>
public static class RoomDirectory
{
    private static readonly Dictionary<string, RoomInfo> rooms = new();
    private static Listener listener;

    /// <summary>Raised whenever the list changes.</summary>
    public static event Action Changed;

    /// <summary>Open, visible, non-empty rooms.</summary>
    public static IEnumerable<RoomInfo> Rooms => rooms.Values;

    // Statics survive play sessions when domain reload is off, so they are cleared first thing.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        rooms.Clear();
        Changed = null;
        listener = null;
    }

    // Registered before the first scene, but after PUN's own editor reset (AfterAssembliesLoaded), which
    // replaces PhotonNetwork's client: a listener added earlier would sit on the discarded client and never
    // hear a room list update (the Join List stayed empty in the editor).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (listener != null) PhotonNetwork.RemoveCallbackTarget(listener);
        listener = new Listener();
        PhotonNetwork.AddCallbackTarget(listener);
    }

    private static void Apply(List<RoomInfo> changes)
    {
        foreach (var info in changes)
        {
            if (info.RemovedFromList || !info.IsOpen || !info.IsVisible || info.PlayerCount == 0)
                rooms.Remove(info.Name);
            else
                rooms[info.Name] = info;
        }
        Changed?.Invoke();
    }

    private static void Clear()
    {
        if (rooms.Count == 0) return;
        rooms.Clear();
        Changed?.Invoke();
    }

    private sealed class Listener : ILobbyCallbacks, IConnectionCallbacks
    {
        public void OnRoomListUpdate(List<RoomInfo> roomList) => Apply(roomList);
        public void OnJoinedLobby() => Clear();     // A full list follows
        public void OnLeftLobby()   => Clear();     // No more updates; the list would go stale
        public void OnDisconnected(DisconnectCause cause) => Clear();

        public void OnLobbyStatisticsUpdate(List<TypedLobbyInfo> lobbyStatistics) { }
        public void OnConnected() { }
        public void OnConnectedToMaster() { }
        public void OnRegionListReceived(RegionHandler regionHandler) { }
        public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
        public void OnCustomAuthenticationFailed(string debugMessage) { }
    }
}
