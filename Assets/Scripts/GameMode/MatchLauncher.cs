using Photon.Pun;
using UnityEngine;

/// <summary>
/// Starts a match from the lobby or the scoreboard ("Play again"), host only: closes the room and
/// loads Pregame, or the map directly for modes that skip ability selection (Freeplay). Everyone
/// follows through AutomaticallySyncScene. Callers make sure it runs once.
/// </summary>
public static class MatchLauncher
{
    public static void StartMatch()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        PhotonNetwork.CurrentRoom.IsOpen    = false;
        PhotonNetwork.CurrentRoom.IsVisible = false;

        // Modes like Freeplay go straight to the map with everyone's last loadout
        var  mode        = GameModeRegistry.Find(MatchRules.GameModeKey);
        bool skipPregame = mode != null && mode.skipAbilitySelection && CanLoad(MatchRules.Map);
        PhotonNetwork.LoadLevel(skipPregame ? MatchRules.Map : SceneNames.Pregame);
    }

    /// <summary>Host: reopens the room and takes everyone back to the lobby.</summary>
    public static void ReturnToLobby()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        PhotonNetwork.CurrentRoom.IsOpen    = true;
        PhotonNetwork.CurrentRoom.IsVisible = true;
        PhotonNetwork.LoadLevel(SceneNames.Lobby);
    }

    public static bool CanLoad(string sceneName) =>
        !string.IsNullOrEmpty(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName);
}
