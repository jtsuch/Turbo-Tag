using UnityEditor;
using UnityEngine;

/// <summary>
/// "Turbo Tag/Multiplayer Test Mode" — a checkable menu item. When on, pressing Play in a level
/// connects to Photon and joins a room shared by every editor instance on this machine
/// (including Multiplayer Play Mode virtual players) instead of playing offline.
/// Turning it on also opens the Multiplayer Play Mode window so extra players can be enabled.
/// </summary>
public static class MultiplayerTestToggle
{
    private const string MenuPath  = "Turbo Tag/Multiplayer Test Mode";
    private const string MppmMenu  = "Window/Multiplayer/Multiplayer Play Mode";

    [MenuItem(MenuPath, priority = 0)]
    private static void Toggle()
    {
        bool enabled = !DebugSceneBootstrapper.MultiplayerTestMode;
        DebugSceneBootstrapper.MultiplayerTestMode = enabled;

        Debug.Log(enabled
            ? "[Turbo Tag] Multiplayer Test Mode ON — Play joins a shared online room. Enable extra players in the Multiplayer Play Mode window."
            : "[Turbo Tag] Multiplayer Test Mode OFF — Play runs offline.");

        if (enabled && !EditorApplication.ExecuteMenuItem(MppmMenu))
            Debug.LogWarning("[Turbo Tag] Multiplayer Play Mode window not found — is the com.unity.multiplayer.playmode package installed?");
    }

    [MenuItem(MenuPath, validate = true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, DebugSceneBootstrapper.MultiplayerTestMode);
        return !EditorApplication.isPlaying;
    }
}
