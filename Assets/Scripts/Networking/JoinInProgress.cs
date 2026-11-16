using System;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Joining a game that has already started, and getting back into one after a disconnect (Justin, Nov 16).
///
/// <b>How people get in</b> (the usual way for a casual game: the server browser shows running games and you drop in):
/// a room stays open and listed through its match whenever its mode takes joiners (<see cref="GameMode.AllowsJoining"/>:
/// everything but Last Man Standing), marked IN PROGRESS in the Join list (<see cref="RoomKeys.Stage"/>). Quick Play
/// looks for a game still in its lobby first, then any open one. A joiner loads straight into the map (Photon's scene
/// sync) with their last loadout and the host places them by the mode's rules (<see cref="GameMode.OnPlayerJoined"/>:
/// Tag as a hunter, Protect the President as a defender, team modes on the smaller team, Search &amp; Destroy from the
/// next round, Hunter as a ghost, Free for All and Freeplay just spawn).
///
/// <b>Admission</b> (host, every scene): Photon sends a joiner's cached player properties with them, so someone coming
/// from another game would arrive with that game's role, score and stats. The host wipes them unless they carry this
/// match's id (<see cref="RoomKeys.MatchId"/>, new every match): that's a player <b>rejoining</b> the match they
/// dropped out of, who keeps their score, stats and (in team modes) their team.
///
/// <b>Rejoining</b>: when the connection drops while in a room, the room is remembered for
/// <see cref="OfferMinutes"/> minutes; the main menu then shows a Rejoin button (<see cref="TryGetOffer"/>). Leaving
/// on purpose forgets it. It's a normal join (a new seat in the room), so the mode's joining rules apply: a mode that
/// takes no joiners takes no rejoiners either.
///
/// One day this is where invites and friends-only rooms would go; for now any open game can be joined.
/// </summary>
public static class JoinInProgress
{
    public const int StageLobby = 0, StageMatch = 1;
    public const float OfferMinutes = 10f;

    private const string PrefsRoom = "Rejoin_Room", PrefsAt = "Rejoin_At";
    private const string TestRoomPrefix = "EditorTest_";   // DebugSceneBootstrapper's Multiplayer Test Mode rooms

    private static Listener listener;
    private static string   currentRoom;     // The room this client is in (kept through a disconnect)
    private static bool     leavingOnPurpose;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (listener != null) PhotonNetwork.RemoveCallbackTarget(listener);
        listener = new Listener();
        PhotonNetwork.AddCallbackTarget(listener);
        currentRoom = null;
        leavingOnPurpose = false;
    }

    // ─── The room's door (host) ───────────────────────────────────────────────

    /// <summary>Host: opens or closes (and lists or hides) the room to match what the mode allows right now.</summary>
    public static void SetOpen(bool open)
    {
        var room = PhotonNetwork.CurrentRoom;
        if (room == null || !PhotonNetwork.IsMasterClient) return;
        if (room.Name.StartsWith(TestRoomPrefix)) return;   // The editor's hidden test rooms stay hidden, and open
        if (room.IsOpen != open) room.IsOpen = open;
        if (room.IsVisible != open) room.IsVisible = open;
    }

    /// <summary>True when the room is in a match (not its lobby): shown as IN PROGRESS in the Join list.</summary>
    public static bool InMatch(Hashtable roomProperties) => NetProps.Get(roomProperties, RoomKeys.Stage, StageLobby) == StageMatch;

    // ─── Admission (host) ─────────────────────────────────────────────────────

    private static void Admit(PhotonPlayer player)
    {
        if (!PhotonNetwork.IsMasterClient || player == null || PhotonNetwork.CurrentRoom == null) return;
        if (!InMatch(PhotonNetwork.CurrentRoom.CustomProperties)) return;   // The lobby: the match start resets everyone

        int  match    = NetProps.Room(RoomKeys.MatchId, 0);
        bool rejoined = match != 0 && NetProps.Of(player, PlayerKeys.MatchId, 0) == match;
        if (!rejoined)
        {
            // New to this match: nothing carried over from wherever they were before
            var props = new Hashtable
            {
                [PlayerKeys.Role]         = (int)PlayerRole.None,
                [PlayerKeys.Score]        = 0f,
                [PlayerKeys.DangerPoints] = 0f,
                [PlayerKeys.MatchId]      = match,
            };
            foreach (string stat in MatchStats.All) props[stat] = 0f;
            player.SetCustomProperties(props);
            PlayerRoles.Set(player, PlayerRole.None);   // The host reads it as None at once, before Photon echoes it
        }

        var manager = GameModeManager.Instance;
        if (manager != null && manager.Mode != null && manager.Phase != MatchPhase.Waiting)
            manager.Mode.OnPlayerJoined(player, rejoined);
        Debug.Log($"[Join] {player.NickName} {(rejoined ? "rejoined" : "joined")} the match in progress.");
    }

    // ─── Rejoin offer (this client) ───────────────────────────────────────────

    /// <summary>The room this client dropped out of, if it was recent enough to offer a way back.</summary>
    public static bool TryGetOffer(out string room)
    {
        room = PlayerPrefs.GetString(PrefsRoom, "");
        if (room.Length == 0) return false;
        if (!long.TryParse(PlayerPrefs.GetString(PrefsAt, "0"), out long at)) return false;
        double minutes = (DateTime.UtcNow - new DateTime(at, DateTimeKind.Utc)).TotalMinutes;
        if (minutes >= 0 && minutes <= OfferMinutes) return true;
        ClearOffer();
        return false;
    }

    public static void ClearOffer()
    {
        if (!PlayerPrefs.HasKey(PrefsRoom)) return;
        PlayerPrefs.DeleteKey(PrefsRoom);
        PlayerPrefs.DeleteKey(PrefsAt);
    }

    /// <summary>This client is about to leave its room on purpose (the pause menu, the lobby): no rejoin offer.</summary>
    public static void LeavingOnPurpose() => leavingOnPurpose = true;

    private static void RememberDrop()
    {
        if (string.IsNullOrEmpty(currentRoom) || leavingOnPurpose || currentRoom.StartsWith(TestRoomPrefix)) return;
        PlayerPrefs.SetString(PrefsRoom, currentRoom);
        PlayerPrefs.SetString(PrefsAt, DateTime.UtcNow.Ticks.ToString());
        PlayerPrefs.Save();
        Debug.Log($"[Join] Dropped out of '{currentRoom}': it can be rejoined from the main menu for {OfferMinutes:0} minutes.");
    }

    private sealed class Listener : IInRoomCallbacks, IMatchmakingCallbacks, IConnectionCallbacks
    {
        public void OnPlayerEnteredRoom(PhotonPlayer newPlayer) => Admit(newPlayer);

        public void OnJoinedRoom()
        {
            currentRoom = PhotonNetwork.CurrentRoom != null && !PhotonNetwork.OfflineMode ? PhotonNetwork.CurrentRoom.Name : null;
            leavingOnPurpose = false;
            ClearOffer();   // Back in a game: the old offer is spent
        }

        public void OnLeftRoom()
        {
            // Still connected: the player chose to leave. A dropped connection comes through OnDisconnected instead
            if (PhotonNetwork.IsConnected) { currentRoom = null; leavingOnPurpose = false; }
        }

        public void OnDisconnected(DisconnectCause cause)
        {
            bool onPurpose = cause == DisconnectCause.DisconnectByClientLogic || cause == DisconnectCause.ApplicationQuit;
            if (!onPurpose) RememberDrop();
            currentRoom = null;
            leavingOnPurpose = false;
        }

        public void OnPlayerLeftRoom(PhotonPlayer otherPlayer) { }
        public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }
        public void OnPlayerPropertiesUpdate(PhotonPlayer targetPlayer, Hashtable changedProps) { }
        public void OnMasterClientSwitched(PhotonPlayer newMasterClient) { }
        public void OnFriendListUpdate(System.Collections.Generic.List<FriendInfo> friendList) { }
        public void OnCreatedRoom() { }
        public void OnCreateRoomFailed(short returnCode, string message) { }
        public void OnJoinRoomFailed(short returnCode, string message) { }
        public void OnJoinRandomFailed(short returnCode, string message) { }
        public void OnConnected() { }
        public void OnConnectedToMaster() { }
        public void OnRegionListReceived(RegionHandler regionHandler) { }
        public void OnCustomAuthenticationResponse(System.Collections.Generic.Dictionary<string, object> data) { }
        public void OnCustomAuthenticationFailed(string debugMessage) { }
    }
}
