using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;

/// <summary>
/// A single row in the keybind settings list. Displays the action name and its current key.
/// Notifies the parent (GeneralUI or CheatsUI) via events when the player wants to rebind or clear.
/// Attach to: KeybindRow prefab — expected layout: [Label | KeyButton | RemoveButton] in a horizontal group.
/// </summary>
public class KeybindRow : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text actionLabel;
    public Button   bindButton;
    public TMP_Text bindButtonLabel;
    public Button   removeButton;

    public string  ActionName     { get; private set; }  // Internal key used in InputHandler (e.g. "Jump")
    public string  PlayerPrefsKey { get; private set; }  // e.g. "Keybind_Jump"
    public KeyCode CurrentKey     { get; private set; }

    /// <summary>Fired when the player clicks the key button to begin listening.</summary>
    public event Action<KeybindRow> OnListenRequested;
    /// <summary>Fired when the player clicks the remove button.</summary>
    public event Action<KeybindRow> OnRemoveRequested;

    public void Initialize(string displayName, string actionName, string playerPrefsKey, KeyCode initialKey)
    {
        ActionName     = actionName;
        PlayerPrefsKey = playerPrefsKey;
        CurrentKey     = initialKey;

        actionLabel.text = displayName;
        RefreshLabel(false);

        bindButton.onClick.AddListener(() => OnListenRequested?.Invoke(this));
        removeButton.onClick.AddListener(() => OnRemoveRequested?.Invoke(this));
    }

    public void SetKey(KeyCode key)
    {
        CurrentKey = key;
        RefreshLabel(false);
    }

    public void ClearKey()
    {
        CurrentKey = KeyCode.None;
        RefreshLabel(false);
    }

    /// <summary>Switches the button label between "..." (listening) and the bound key name.</summary>
    public void SetListening(bool listening) => RefreshLabel(listening);

    private void RefreshLabel(bool listening)
    {
        bindButtonLabel.text = listening            ? "..."
                             : CurrentKey == KeyCode.None ? "—"
                             : CurrentKey.ToString();
    }
}
