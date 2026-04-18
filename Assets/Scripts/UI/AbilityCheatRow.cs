using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;
using System.Reflection;

/// <summary>
/// A single row in a CheatsUI ability tab list. Supports two modes:
///   Equipped   — the ability currently in the player's slot; shows an Expand button for
///                modifying ability-specific variables via sliders.
///   Available  — an ability not currently equipped; shows a Bind Key button so the player
///                can hot-add it to a key without replacing their equipped ability.
/// Attach to: AbilityCheatRow prefab inside CheatsUI ability tab scroll content.
/// </summary>
public class AbilityCheatRow : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text  abilityNameLabel;
    public Button    expandButton;      // Shown when IsEquipped == true
    public TMP_Text  expandButtonLabel;
    public Button    bindButton;        // Shown when IsEquipped == false
    public TMP_Text  bindButtonLabel;
    public GameObject expandedPanel;   // Variable sliders live here; hidden by default

    public string  AbilityName { get; private set; }
    public bool    IsEquipped  { get; private set; }
    public KeyCode BoundKey    { get; private set; } = KeyCode.None;

    /// <summary>Fired when the player clicks the Bind Key button to start listening.</summary>
    public event Action<AbilityCheatRow> OnBindRequested;
    /// <summary>Fired when the equipped-ability row is expanded or collapsed.</summary>
    public event Action<AbilityCheatRow> OnExpandToggled;

    private bool expanded = false;

    public void Initialize(string abilityName, bool isEquipped, KeyCode existingBind = KeyCode.None)
    {
        AbilityName = abilityName;
        IsEquipped  = isEquipped;
        BoundKey    = existingBind;

        abilityNameLabel.text = abilityName;
        expandButton.gameObject.SetActive(isEquipped);
        bindButton.gameObject.SetActive(!isEquipped);
        expandedPanel.SetActive(false);

        RefreshBindLabel(false);

        expandButton.onClick.AddListener(ToggleExpand);
        bindButton.onClick.AddListener(() => OnBindRequested?.Invoke(this));
    }

    public void SetBoundKey(KeyCode key)
    {
        BoundKey = key;
        RefreshBindLabel(false);
    }

    public void ClearBoundKey()
    {
        BoundKey = KeyCode.None;
        RefreshBindLabel(false);
    }

    public void SetListening(bool listening) => RefreshBindLabel(listening);

    private void ToggleExpand()
    {
        expanded = !expanded;
        expandedPanel.SetActive(expanded);
        expandButtonLabel.text = expanded ? "▲ Collapse" : "▼ Expand";
        OnExpandToggled?.Invoke(this);
    }

    /// <summary>
    /// Scans <paramref name="abilityComp"/> for fields tagged with [TunableParam] and spawns
    /// a SliderRow inside expandedPanel for each one. Call after Initialize() for equipped rows.
    /// </summary>
    public void Populate(MonoBehaviour abilityComp, GameObject sliderRowPrefab)
    {
        if (abilityComp == null || sliderRowPrefab == null) return;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                 | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        // Walk the inheritance chain so base-class fields (e.g. cooldownTime) are included
        var type = abilityComp.GetType();
        while (type != null && type != typeof(MonoBehaviour))
        {
            foreach (var field in type.GetFields(flags))
            {
                var attr = field.GetCustomAttribute<TunableParamAttribute>();
                if (attr == null || field.FieldType != typeof(float)) continue;

                float current = (float)field.GetValue(abilityComp);
                var row = Instantiate(sliderRowPrefab, expandedPanel.transform)
                              .GetComponent<SliderRow>();
                row.Initialize(attr.DisplayName, attr.Min, attr.Max, current, abilityComp, field);
            }
            type = type.BaseType;
        }
    }

    private void RefreshBindLabel(bool listening)
    {
        bindButtonLabel.text = listening            ? "..."
                             : BoundKey == KeyCode.None ? "Bind Key"
                             : BoundKey.ToString();
    }
}
