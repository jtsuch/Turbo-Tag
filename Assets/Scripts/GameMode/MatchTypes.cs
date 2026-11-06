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
    None      = 0,
    Hider     = 1,  // Tag: hides. Protect the President: a defender
    Hunter    = 2,
    President = 3,  // Protect the President: the only player who can be tagged
    TeamRed   = 4,  // Team modes (Team Deathmatch, Keep Away)
    TeamBlue  = 5,
    Ghost     = 6,  // Hunter: a hider who died; translucent, no hitbox, still uses abilities
}

/// <summary>
/// Reads and writes player roles stored in player custom properties. Because roles are
/// properties (not RPC'd flags) every client — including late joiners — agrees on them.
/// </summary>
public static class PlayerRoles
{
    // Master: roles just written, until Photon echoes them back (player properties only update on the echo, so
    // without this a mode reading roles the next frame acts on the old ones: e.g. Team modes re-placing everyone
    // as "newcomers" right after dealing the teams)
    private static readonly System.Collections.Generic.Dictionary<int, (PlayerRole role, float time)> pending = new();
    private const float PendingTimeout = 5f;

    public static PlayerRole Get(PhotonPlayer player)
    {
        if (player == null) return PlayerRole.None;
        var synced = (PlayerRole)NetProps.Of(player, PlayerKeys.Role, (int)PlayerRole.None);
        if (pending.TryGetValue(player.ActorNumber, out var p))
        {
            if (p.role == synced || UnityEngine.Time.realtimeSinceStartup - p.time > PendingTimeout) pending.Remove(player.ActorNumber);
            else return p.role;
        }
        return synced;
    }

    public static PlayerRole Get(int actorNumber) =>
        Get(PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber));

    /// <summary>Master client only. Reads return the new role at once on the master.</summary>
    public static void Set(PhotonPlayer player, PlayerRole role)
    {
        if (player == null || !PhotonNetwork.IsMasterClient) return;
        pending[player.ActorNumber] = (role, UnityEngine.Time.realtimeSinceStartup);
        NetProps.SetPlayer(player, PlayerKeys.Role, (int)role);
    }

    /// <summary>Forgets roles written but not yet echoed (a level load resets every role).</summary>
    public static void ClearPending() => pending.Clear();

    public static List<PhotonPlayer> With(PlayerRole role)
    {
        var result = new List<PhotonPlayer>();
        foreach (var player in PhotonNetwork.PlayerList)
            if (Get(player) == role) result.Add(player);
        return result;
    }

    /// <summary>True for the two team roles.</summary>
    public static bool IsTeam(PlayerRole role) => role == PlayerRole.TeamRed || role == PlayerRole.TeamBlue;

    /// <summary>The other team (team roles only).</summary>
    public static PlayerRole Opponent(PlayerRole team) => team == PlayerRole.TeamRed ? PlayerRole.TeamBlue : PlayerRole.TeamRed;

    /// <summary>A role's colour on the HUD, minimap and kill feed.</summary>
    public static UnityEngine.Color Color(PlayerRole role) => role switch
    {
        PlayerRole.Hunter    => new UnityEngine.Color(1f, 0.3f, 0.43f),
        PlayerRole.TeamRed   => new UnityEngine.Color(1f, 0.3f, 0.43f),
        PlayerRole.TeamBlue  => new UnityEngine.Color(0.2f, 0.65f, 1f),
        PlayerRole.President => new UnityEngine.Color(1f, 0.79f, 0.24f),
        PlayerRole.Ghost     => new UnityEngine.Color(0.75f, 0.85f, 1f),
        _                    => new UnityEngine.Color(0.13f, 0.83f, 0.93f),
    };

    public static int Count(PlayerRole role)
    {
        int count = 0;
        foreach (var player in PhotonNetwork.PlayerList)
            if (Get(player) == role) count++;
        return count;
    }
}
