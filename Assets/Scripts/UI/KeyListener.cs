using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Shared "press any key" capture used by the keybind and cheat-binding UIs. Skips the frame
/// that started listening (so the click itself isn't captured) and ignores Escape, which is
/// reserved for the pause menu.
/// </summary>
public class KeyListener
{
    private bool skipFrame;

    public bool IsListening { get; private set; }

    public void Start()
    {
        IsListening = true;
        skipFrame   = true;
        // Deselect UI so Space/Enter aren't swallowed by the EventSystem
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    public void Stop() => IsListening = false;

    /// <summary>Call every Update. Returns true (with the key) on the frame a key is pressed.</summary>
    public bool Poll(out KeyCode key)
    {
        key = KeyCode.None;
        if (!IsListening) return false;
        if (skipFrame) { skipFrame = false; return false; }

        foreach (KeyCode candidate in Keybinds.AllKeys)
        {
            if (candidate == KeyCode.None || candidate == KeyCode.Escape) continue;
            if (candidate >= KeyCode.JoystickButton0) break; // Keyboard and mouse only
            if (!Input.GetKeyDown(candidate)) continue;

            key = candidate;
            IsListening = false;
            return true;
        }
        return false;
    }
}
