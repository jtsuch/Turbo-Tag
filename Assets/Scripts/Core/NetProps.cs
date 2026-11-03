using System;
using Photon.Pun;
using UnityEngine;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Typed, null-safe access to Photon room and player custom properties.
/// Photon serialises numbers loosely (an int written by one system may be read as a float by
/// another), so every read converts to the requested type and falls back to a default.
/// </summary>
public static class NetProps
{
    // ─── Reads ────────────────────────────────────────────────────────────────

    public static T Get<T>(Hashtable props, string key, T fallback)
    {
        if (props == null || !props.TryGetValue(key, out object raw) || raw == null)
            return fallback;

        if (raw is T typed) return typed;

        try
        {
            if (raw is IConvertible && typeof(IConvertible).IsAssignableFrom(typeof(T)))
                return (T)Convert.ChangeType(raw, typeof(T));
        }
        catch (Exception) { /* fall through to fallback */ }

        return fallback;
    }

    public static T Room<T>(string key, T fallback) =>
        Get(PhotonNetwork.CurrentRoom?.CustomProperties, key, fallback);

    public static T Of<T>(PhotonPlayer player, string key, T fallback) =>
        Get(player?.CustomProperties, key, fallback);

    public static bool RoomHas(string key) =>
        PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(key);

    // ─── Writes ───────────────────────────────────────────────────────────────

    /// <summary>Writes room properties. Ignored (with a warning) on non-master clients.</summary>
    public static void SetRoom(Hashtable props)
    {
        if (PhotonNetwork.CurrentRoom == null) return;
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("[NetProps] Only the master client may write room properties.");
            return;
        }
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    public static void SetRoom(string key, object value) => SetRoom(new Hashtable { [key] = value });

    public static void SetLocal(Hashtable props) => PhotonNetwork.LocalPlayer?.SetCustomProperties(props);

    public static void SetLocal(string key, object value) => SetLocal(new Hashtable { [key] = value });

    /// <summary>Writes another player's properties. Master-client authority only.</summary>
    public static void SetPlayer(PhotonPlayer player, string key, object value)
    {
        if (player == null || !PhotonNetwork.IsMasterClient) return;
        player.SetCustomProperties(new Hashtable { [key] = value });
    }
}
