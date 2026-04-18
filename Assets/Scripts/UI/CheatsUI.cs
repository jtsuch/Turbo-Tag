using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

/// <summary>
/// Drives the Cheats settings page. Cheats are only functional when the room property
/// "Rule_CheatsEnabled" is true (set by the host via RulesUI). Any player may modify their
/// own stats and abilities when cheats are active.
///
/// Layout (five sub-tabs inside the Cheats panel):
///   Self      — movement speed, acceleration, jump, scale, cooldown multiplier
///   Basic     — Basic ability list (equipped at top, others bindable below)
///   Quick     — Quick ability list
///   Throw     — Throw ability list
///   Trap      — Trap ability list
///
/// Ability tab behaviour:
///   Equipped row  → "Expand" to reveal per-variable sliders (add sliders as the game grows).
///   Available row → "Bind Key" to capture a keypress and hot-add that ability to InputHandler.
///                   The binding is saved to PlayerPrefs as "Keybind_Custom_{abilityName}".
///
/// Attach to: CheatsPage panel inside the PauseMenu canvas.
/// </summary>
public class CheatsUI : MonoBehaviour
{
    // ─── Sub-tab panels ───────────────────────────────────────────────────────
    [Header("Sub-Tab Buttons")]
    public Button selfTabButton;
    public Button basicTabButton;
    public Button quickTabButton;
    public Button throwTabButton;
    public Button trapTabButton;

    // Each panel lives directly inside the shared ScrollView Content.
    // The panel itself is the content container — no separate content Transform needed.
    [Header("Sub-Tab Panels (inside ScrollView Content)")]
    public GameObject selfPanel;
    public GameObject basicPanel;
    public GameObject quickPanel;
    public GameObject throwPanel;
    public GameObject trapPanel;

    // ─── Self tab sliders ─────────────────────────────────────────────────────
    [Header("Self — Speed")]
    public Slider         speedSlider;
    public TMP_InputField speedInput;

    [Header("Self — Acceleration")]
    public Slider         accelerationSlider;
    public TMP_InputField accelerationInput;

    [Header("Self — Jump")]
    public Slider         jumpSlider;
    public TMP_InputField jumpInput;

    [Header("Self — Scale")]
    public Slider         scaleSizeSlider;
    public TMP_InputField scaleSizeInput;

    [Header("Self — Cooldown Multiplier")]
    public Slider         cooldownSlider;
    public TMP_InputField cooldownInput;

    [Header("Ability Row Prefab")]
    [Tooltip("Prefab with AbilityCheatRow component.")]
    public GameObject abilityRowPrefab;

    [Header("Slider Row Prefab")]
    [Tooltip("Prefab with SliderRow component — spawned inside each equipped ability's expanded panel.")]
    public GameObject sliderRowPrefab;

    // ─── Cheats-disabled overlay ──────────────────────────────────────────────
    [Header("Cheats Lock")]
    [Tooltip("CanvasGroup over the entire cheats panel — dims and blocks input when cheats are off.")]
    public CanvasGroup cheatsLockOverlay;

    // ─── Known abilities by type ──────────────────────────────────────────────
    // Extend these arrays as new abilities are added.
    private static readonly string[] BasicAbilities = { "BasicGrapple", "Flappy", "SpringyGrapple", "StiffGrapple" };
    private static readonly string[] QuickAbilities = { "Dash", "Launch", "Shrink" };
    private static readonly string[] ThrowAbilities = { "BoomBomb", "BoomStick", "Flashbang", "Frisbee", "GravBall", "Rock", "Semtex", "Snowball" };
    private static readonly string[] TrapAbilities  = { "Box", "GravityWell", "IceTrap", "Ladder", "Nuke" };

    // ─── Listening state for ability key-binding ──────────────────────────────
    private AbilityCheatRow listeningAbilityRow = null;
    private bool            skipFrameAfterClick = false;

    // Track all spawned ability rows so we can check for key conflicts
    private readonly List<AbilityCheatRow> abilityRows = new();

    private bool updating = false;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        // Wire sub-tab buttons
        selfTabButton .onClick.AddListener(() => ShowSubTab(selfPanel));
        basicTabButton.onClick.AddListener(() => ShowSubTab(basicPanel));
        quickTabButton.onClick.AddListener(() => ShowSubTab(quickPanel));
        throwTabButton.onClick.AddListener(() => ShowSubTab(throwPanel));
        trapTabButton .onClick.AddListener(() => ShowSubTab(trapPanel));

        ShowSubTab(selfPanel);

        InitSelfSliders();
        BuildAbilityTab(basicPanel.transform, BasicAbilities, 0);
        BuildAbilityTab(quickPanel.transform, QuickAbilities, 1);
        BuildAbilityTab(throwPanel.transform, ThrowAbilities, 2);
        BuildAbilityTab(trapPanel.transform,  TrapAbilities,  3);
    }

    private void OnEnable()
    {
        ApplyCheatsLock();
    }

    private void Update()
    {
        if (listeningAbilityRow == null) return;

        if (skipFrameAfterClick)
        {
            skipFrameAfterClick = false;
            return;
        }

        foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
        {
            if (kc == KeyCode.Escape) continue;
            if (!Input.GetKeyDown(kc)) continue;

            CommitAbilityBind(listeningAbilityRow, kc);
            listeningAbilityRow.SetListening(false);
            listeningAbilityRow = null;
            return;
        }
    }

    // ─── Cheats lock ──────────────────────────────────────────────────────────

    private void ApplyCheatsLock()
    {
        if (cheatsLockOverlay == null || PhotonNetwork.CurrentRoom == null) return;
        var props   = PhotonNetwork.CurrentRoom.CustomProperties;
        bool active = props.TryGetValue(RulesUI.KEY_CHEATS, out object c) && (bool)c;
        cheatsLockOverlay.interactable   = active;
        cheatsLockOverlay.alpha          = active ? 0f : 0.6f;
        cheatsLockOverlay.blocksRaycasts = !active;
    }

    // ─── Sub-tab navigation ───────────────────────────────────────────────────

    private void ShowSubTab(GameObject panel)
    {
        selfPanel   .SetActive(panel == selfPanel);
        basicPanel  .SetActive(panel == basicPanel);
        quickPanel  .SetActive(panel == quickPanel);
        throwPanel  .SetActive(panel == throwPanel);
        trapPanel   .SetActive(panel == trapPanel);
    }

    // ─── Self tab initialisation ──────────────────────────────────────────────

    private void InitSelfSliders()
    {
        if (Player.Instance == null) return;
        // Sliders are Inspector-configured with appropriate min/max ranges.
        // Multiplier sliders use a 0–200 scale where 100 = 1× multiplier.
        cooldownSlider.value = Player.Instance.CooldownMultiplier * 100f;
        cooldownInput.text   = (Player.Instance.CooldownMultiplier * 100f).ToString("F0");
    }

    // ─── Ability tab construction ─────────────────────────────────────────────

    /// <summary>
    /// Populates one ability tab. The player's equipped ability for <paramref name="slotIndex"/>
    /// is placed first with an Expand button; all others follow with Bind Key buttons.
    /// </summary>
    private void BuildAbilityTab(Transform container, string[] abilities, int slotIndex)
    {
        if (abilityRowPrefab == null || container == null) return;

        string equipped = (Player.Instance != null && Player.Instance.abilityList != null
                          && slotIndex < Player.Instance.abilityList.Length)
                        ? Player.Instance.abilityList[slotIndex]
                        : null;

        // Equipped ability first
        if (!string.IsNullOrEmpty(equipped))
            SpawnAbilityRow(container, equipped, isEquipped: true);

        // Remaining abilities (excluding the equipped one)
        foreach (string name in abilities)
        {
            if (name == equipped) continue;
            KeyCode savedBind = LoadCustomBind(name);
            SpawnAbilityRow(container, name, isEquipped: false, savedBind);
        }
    }

    private void SpawnAbilityRow(Transform container, string abilityName, bool isEquipped, KeyCode existingBind = KeyCode.None)
    {
        var row = Instantiate(abilityRowPrefab, container).GetComponent<AbilityCheatRow>();
        row.Initialize(abilityName, isEquipped, existingBind);
        row.OnBindRequested += StartAbilityListening;
        abilityRows.Add(row);

        if (isEquipped)
            row.Populate(FindAbilityComponent(abilityName), sliderRowPrefab);
    }

    private MonoBehaviour FindAbilityComponent(string abilityName)
    {
        if (Player.Instance == null) return null;
        foreach (var mb in Player.Instance.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
        {
            if (mb.GetType().Name == abilityName) return mb;
        }
        return null;
    }

    // ─── Ability key-binding ──────────────────────────────────────────────────

    private void StartAbilityListening(AbilityCheatRow row)
    {
        listeningAbilityRow?.SetListening(false);
        listeningAbilityRow = row;
        skipFrameAfterClick = true;
        row.SetListening(true);
    }

    private void CommitAbilityBind(AbilityCheatRow row, KeyCode key)
    {
        var ih = Player.Instance != null ? Player.Instance.GetComponent<InputHandler>() : null;

        // Clear any other ability row already using this key
        foreach (var other in abilityRows)
        {
            if (other == row || other.BoundKey != key) continue;
            other.ClearBoundKey();
            ClearCustomBind(other.AbilityName);
            if (ih != null) ih.RemoveKeyBinding(other.AbilityName);
        }

        row.SetBoundKey(key);
        SaveCustomBind(row.AbilityName, key);
        if (ih != null) ih.RebindKey(row.AbilityName, key);
    }

    // Custom bind persistence — separate from the slot-based keys used by GeneralUI
    private static KeyCode LoadCustomBind(string abilityName)
    {
        string raw = PlayerPrefs.GetString($"Keybind_Custom_{abilityName}", "");
        return System.Enum.TryParse(raw, out KeyCode kc) ? kc : KeyCode.None;
    }

    private static void SaveCustomBind(string abilityName, KeyCode key)
    {
        PlayerPrefs.SetString($"Keybind_Custom_{abilityName}", key.ToString());
        PlayerPrefs.Save();
    }

    private static void ClearCustomBind(string abilityName)
    {
        PlayerPrefs.DeleteKey($"Keybind_Custom_{abilityName}");
        PlayerPrefs.Save();
    }

    // ─── Self stat callbacks ──────────────────────────────────────────────────

    public void SliderSpeedMod(float value)
    {
        if (Player.Instance == null || updating) return;
        updating                       = true;
        speedInput.text                = value.ToString("F2");
        Player.Instance.SpeedMultiplier = value / 100f;
        updating                       = false;
    }

    public void InputSpeedMod()
    {
        if (Player.Instance == null || updating) return;
        if (!float.TryParse(speedInput.text, out float v)) return;
        updating                       = true;
        speedSlider.value              = v;
        Player.Instance.SpeedMultiplier = v / 100f;
        updating                       = false;
    }

    public void SliderAccelerationMod(float value)
    {
        if (Player.Instance == null || updating) return;
        updating                     = true;
        accelerationInput.text       = value.ToString("F2");
        Player.Instance.Acceleration = value * 0.2f;
        updating                     = false;
    }

    public void InputAccelerationMod()
    {
        if (Player.Instance == null || updating) return;
        if (!float.TryParse(accelerationInput.text, out float v)) return;
        updating                     = true;
        accelerationSlider.value     = v;
        Player.Instance.Acceleration = v * 0.2f;
        updating                     = false;
    }

    public void SliderJumpMod(float value)
    {
        if (Player.Instance == null || updating) return;
        updating                      = true;
        jumpInput.text                = value.ToString("F2");
        Player.Instance.JumpStrength  = value * 0.16f;
        updating                      = false;
    }

    public void InputJumpMod()
    {
        if (Player.Instance == null || updating) return;
        if (!float.TryParse(jumpInput.text, out float v)) return;
        updating                      = true;
        jumpSlider.value              = v;
        Player.Instance.JumpStrength  = v * 0.16f;
        updating                      = false;
    }

    public void SliderScaleSizeMod(float value)
    {
        if (Player.Instance == null || updating) return;
        updating                       = true;
        scaleSizeInput.text            = value.ToString("F2");
        Player.Instance.currentXScale  = value / 100f;
        Player.Instance.currentYScale  = value / 100f;
        Player.Instance.currentZScale  = value / 100f;
        Player.Instance.SetPlayerScale();
        updating                       = false;
    }

    public void InputScaleSizeMod()
    {
        if (Player.Instance == null || updating) return;
        if (!float.TryParse(scaleSizeInput.text, out float v)) return;
        updating                       = true;
        scaleSizeSlider.value          = v;
        Player.Instance.currentXScale  = v / 100f;
        Player.Instance.currentYScale  = v / 100f;
        Player.Instance.currentZScale  = v / 100f;
        Player.Instance.SetPlayerScale();
        updating                       = false;
    }

    // ─── Cooldown multiplier ──────────────────────────────────────────────────
    // Abilities should multiply their cooldownTime by Player.Instance.CooldownMultiplier.

    public void SliderCooldownMod(float value)
    {
        if (Player.Instance == null || updating) return;
        updating                           = true;
        cooldownInput.text                 = value.ToString("F0");
        Player.Instance.CooldownMultiplier = value / 100f;
        updating                           = false;
    }

    public void InputCooldownMod()
    {
        if (Player.Instance == null || updating) return;
        if (!float.TryParse(cooldownInput.text, out float v)) return;
        updating                           = true;
        cooldownSlider.value               = v;
        Player.Instance.CooldownMultiplier = v / 100f;
        updating                           = false;
    }
}
