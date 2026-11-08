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

        // The finished match's phase must not survive into the next level (Play Again went straight back to
        // the Scoreboard: the new level read MatchEnd with an expired clock before its own reset arrived)
        NetProps.SetRoom(RoomKeys.Phase, (int)MatchPhase.Waiting);

        // The map must belong to the mode's category (Search & Destroy maps only for Search & Destroy, and the
        // reverse) and exist in this build: the lobby keeps them matched, but nothing else may load a wrong one
        var    mode     = GameModeRegistry.Find(MatchRules.GameModeKey);
        string map      = MatchRules.Map;
        string category = mode != null ? mode.mapCategory ?? "" : "";
        if (SceneNames.Category(map) != category || !CanLoad(map))
        {
            string fix = SceneNames.FirstMap(category);
            if (fix != null && fix != map)
            {
                Debug.LogWarning($"[MatchLauncher] {map} isn't a {(category == "" ? "standard" : category)} map: playing {fix}");
                map = fix;
                NetProps.SetRoom(RoomKeys.Map, map);
            }
        }

        // Modes like Freeplay go straight to the map with everyone's last loadout
        bool skipPregame = mode != null && mode.skipAbilitySelection && CanLoad(map);
        PhotonNetwork.LoadLevel(skipPregame ? map : SceneNames.Pregame);
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
