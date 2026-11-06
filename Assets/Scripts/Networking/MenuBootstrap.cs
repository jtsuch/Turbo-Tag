using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pressing Play on a menu scene in the editor (Main Menu, Join List, Lobby, Pregame, Scoreboard) skipped the
/// Loading scene, which connects to Photon and creates the persistent managers (music, settings): the menu sat on
/// "Connecting…" with its play buttons greyed out and no music. Starting on any of those scenes now goes through
/// Loading first. Builds always start at Loading, so this only matters in the editor. Levels have their own
/// offline bootstrap (DebugSceneBootstrapper).
/// </summary>
public static class MenuBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartFromLoading()
    {
        string scene = SceneManager.GetActiveScene().name;
        bool menu = scene == SceneNames.MainMenu || scene == SceneNames.JoinList || scene == SceneNames.Lobby
                 || scene == SceneNames.Pregame || scene == SceneNames.Scoreboard;
        if (menu && !PhotonNetwork.IsConnected && AudioManager.Instance == null)
            SceneManager.LoadScene(SceneNames.Loading);
    }
}
