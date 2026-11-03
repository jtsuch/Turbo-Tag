using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row in the keybind list: action label, key button (click to rebind) and remove button.
/// Raises events; the owning settings page decides what a rebind means.
/// Attach to: KeybindRow prefab — [Label | KeyButton | RemoveButton].
/// </summary>
public class KeybindRow : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text actionLabel;
    [SerializeField] private Button   bindButton;
    [SerializeField] private TMP_Text bindButtonLabel;
    [SerializeField] private Button   removeButton;

    public InputAction Action     { get; private set; }
    public KeyCode     CurrentKey { get; private set; }

    public event Action<KeybindRow> ListenRequested;
    public event Action<KeybindRow> RemoveRequested;

    public void Initialize(string displayName, InputAction action, KeyCode key, bool removable = true)
    {
        Action           = action;
        actionLabel.text = displayName;
        SetKey(key);

        bindButton.onClick.AddListener(() => ListenRequested?.Invoke(this));
        removeButton.onClick.AddListener(() => RemoveRequested?.Invoke(this));
        removeButton.gameObject.SetActive(removable);
    }

    public void SetKey(KeyCode key)
    {
        CurrentKey = key;
        SetListening(false);
    }

    /// <summary>Shows "..." while waiting for a key press.</summary>
    public void SetListening(bool listening) =>
        bindButtonLabel.text = listening ? "..." : Keybinds.Label(CurrentKey);
}
