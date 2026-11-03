using System.Collections.Generic;
using Photon.Pun;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Lifecycle phases shared by every game mode. Modes decide which phases they use and what
/// each one means (e.g. Freeroam goes straight to Active and stays there).
/// </summary>
public enum MatchPhase
{
    Waiting   = 0,  // Scene loaded, match not started yet
    Countdown = 1,  // Everyone frozen
    Hiding    = 2,  // Head start: hiders move, hunters frozen
    Active    = 3,  // Main gameplay
    RoundEnd  = 4,  // Scores shown, next round pending
    MatchEnd  = 5,  // Winner shown, returning to lobby
}

/// <summary>A player's team for the current round.</summary>
public enum PlayerRole
{
    None   = 0,
    Hider  = 1,
    Hunter = 2,
}

/// <summary>
/// Reads and writes player roles stored in player custom properties. Because roles are
/// properties (not RPC'd flags) every client — including late joiners — agrees on them.
/// </summary>
public static class PlayerRoles
{
    public static PlayerRole Get(PhotonPlayer player) =>
        (PlayerRole)NetProps.Of(player, PlayerKeys.Role, (int)PlayerRole.None);

    public static PlayerRole Get(int actorNumber) =>
        Get(PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber));

    /// <summary>Master client only.</summary>
    public static void Set(PhotonPlayer player, PlayerRole role) =>
        NetProps.SetPlayer(player, PlayerKeys.Role, (int)role);

    public static List<PhotonPlayer> With(PlayerRole role)
    {
        var result = new List<PhotonPlayer>();
        foreach (var player in PhotonNetwork.PlayerList)
            if (Get(player) == role) result.Add(player);
        return result;
    }

    public static int Count(PlayerRole role)
    {
        int count = 0;
        foreach (var player in PhotonNetwork.PlayerList)
            if (Get(player) == role) count++;
        return count;
    }
}
