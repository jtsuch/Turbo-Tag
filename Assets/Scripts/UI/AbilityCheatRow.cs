using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One ability in a Cheats ability tab. Every row can expand to show sliders for the ability's
/// [TunableParam] fields, then those of the object it spawns (fuse, blast radius, pull…). The equipped ability shows its slot key and can be rebound or unbound;
/// other abilities can be bound to an extra key so they become usable alongside the equipped loadout.
/// Every row has a Remove button that clears its key.
/// Attach to: AbilityCheatRow prefab.
/// </summary>
public class AbilityCheatRow : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text   abilityNameLabel;
    [SerializeField] private Button     expandButton;
    [SerializeField] private TMP_Text   expandButtonLabel;
    [SerializeField] private Button     bindButton;
    [SerializeField] private TMP_Text   bindButtonLabel;
    [SerializeField] private Button     clearButton;
    [SerializeField] private GameObject expandedPanel;

    public Ability Ability    { get; private set; }
    public bool    IsEquipped { get; private set; }
    public KeyCode BoundKey   { get; private set; }

    public event Action<AbilityCheatRow> BindRequested;
    public event Action<AbilityCheatRow> ClearRequested;

    private readonly List<SliderRow> sliders = new();
    private bool expanded;

    public void Initialize(Ability ability, bool isEquipped, KeyCode key, GameObject sliderRowPrefab)
    {
        Ability    = ability;
        IsEquipped = isEquipped;

        string name = AbilityCatalog.DisplayName(ability.Id);
        abilityNameLabel.text = isEquipped ? $"{name} (Equipped)" : name;
        expandedPanel.SetActive(false);
        expandButtonLabel.text = "▼ Expand";

        expandButton.onClick.AddListener(ToggleExpand);
        bindButton.onClick.AddListener(() => BindRequested?.Invoke(this));
        if (clearButton != null) clearButton.onClick.AddListener(() => ClearRequested?.Invoke(this));

        bindButton.interactable = true;
        if (clearButton != null) clearButton.gameObject.SetActive(true);

        SetBoundKey(key);
        BuildSliders(sliderRowPrefab);
    }

    public void SetBoundKey(KeyCode key)
    {
        BoundKey = key;
        SetListening(false);
    }

    public void SetListening(bool listening) =>
        bindButtonLabel.text = listening ? "..." : BoundKey == KeyCode.None ? "Bind Key" : Keybinds.Label(BoundKey);

    /// <summary>Re-reads every slider from the ability (after cheats reset tunables).</summary>
    public void RefreshSliders()
    {
        foreach (var slider in sliders) slider.Refresh();
    }

    private void ToggleExpand()
    {
        expanded = !expanded;
        expandedPanel.SetActive(expanded);
        expandButtonLabel.text = expanded ? "▲ Collapse" : "▼ Expand";
        CheatsUI.RebuildLayout(transform);
    }

    private void BuildSliders(GameObject sliderRowPrefab)
    {
        if (sliderRowPrefab == null) return;

        foreach (var (field, attr) in TunableParamAttribute.FieldsOf(Ability.GetType()))
            AddSlider(sliderRowPrefab).Initialize(attr, Ability, field);

        foreach (var entry in Ability.ObjectTunableEntries)
        {
            var e = entry;
            AddSlider(sliderRowPrefab).Initialize(e.Attribute, () => Ability.GetObjectTunable(e), v => Ability.SetObjectTunable(e, v));
        }

        expandButton.gameObject.SetActive(sliders.Count > 0);
    }

    private SliderRow AddSlider(GameObject sliderRowPrefab)
    {
        var row = Instantiate(sliderRowPrefab, expandedPanel.transform).GetComponent<SliderRow>();
        sliders.Add(row);
        return row;
    }
}
