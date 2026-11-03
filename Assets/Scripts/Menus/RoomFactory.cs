using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Creates rooms with consistent options: player cap, lobby-visible properties (so the Join
/// List can show mode and map) and default selections.
/// </summary>
public static class RoomFactory
{
    /// <summary>Room size: up to 12 players, which also allows 6v6 for future team modes.</summary>
    public const int MaxPlayers = 12;

    public static void CreateRoom()
    {
        string host = string.IsNullOrEmpty(PhotonNetwork.NickName) ? "Penguin" : PhotonNetwork.NickName;
        PhotonNetwork.CreateRoom($"{host}'s Game #{Random.Range(100, 1000)}", BuildOptions(), TypedLobby.Default);
    }

    private static RoomOptions BuildOptions() => new()
    {
        MaxPlayers                   = MaxPlayers,
        IsVisible                    = true,
        IsOpen                       = true,
        CustomRoomPropertiesForLobby = RoomKeys.LobbyVisible,
        CustomRoomProperties         = new Hashtable
        {
            [RoomKeys.GameMode] = GameModeRegistry.DefaultKey,
            [RoomKeys.Map]      = SceneNames.DefaultMap,
        },
    };
}
