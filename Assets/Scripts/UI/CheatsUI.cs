using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Cheats tab of the pause menu. Only usable while the host has cheats enabled; otherwise a
/// lock overlay covers it. Turning cheats off undoes everything (handled by AbilityHandler).
///
/// Every slider covers a sensible range, but its text box accepts any value (only negatives —
/// and a zero size — are rejected).
///
/// Sub-tabs:
///   Self                     — speed, acceleration, jump, size, cooldown, air control, gravity,
///                              ability power and ability duration as % of normal. The last four
///                              rows are cloned from the Cooldown row at runtime (see ExtraSelfStats).
///   Basic / Quick / Throw / Trap — every available ability in that slot. Rows expand to show
///                              sliders for the ability's [TunableParam] fields (dash strength…)
///                              and its spawned object's (fuse, blast radius…). Cooldowns live in
///                              Self. Any ability can be bound to a key; a key drives one ability.
///                              Active abilities (equipped, then bound extras) sit at the top.
/// Everything sits in one ScrollRect; layout groups size every row (no nested size fitters).
/// Attach to: the CheatsPage panel inside the PauseMenu.
/// </summary>
public class CheatsUI : MonoBehaviourPunCallbacks
{
    [Header("Sub-Tab Buttons")]
    [SerializeField] private Button selfTabButton;
    [SerializeField] private Button basicTabButton;
    [SerializeField] private Button quickTabButton;
    [SerializeField] private Button throwTabButton;
    [SerializeField] private Button trapTabButton;

    [Header("Sub-Tab Panels (each is its own row container)")]
    [SerializeField] private GameObject selfPanel;
    [SerializeField] private GameObject basicPanel;
    [SerializeField] private GameObject quickPanel;
    [SerializeField] private GameObject throwPanel;
    [SerializeField] private GameObject trapPanel;

    [Header("Self (all 0–200, where 100 = normal)")]
    [SerializeField] private Slider         speedSlider;
    [SerializeField] private TMP_InputField speedInput;
    [SerializeField] private Slider         accelerationSlider;
    [SerializeField] private TMP_InputField accelerationInput;
    [SerializeField] private Slider         jumpSlider;
    [SerializeField] private TMP_InputField jumpInput;
    [SerializeField] private Slider         scaleSizeSlider;
    [SerializeField] private TMP_InputField scaleSizeInput;
    [SerializeField] private Slider         cooldownSlider;
    [SerializeField] private TMP_InputField cooldownInput;

    [Header("Prefabs")]
    [SerializeField] private GameObject abilityRowPrefab;
    [SerializeField] private GameObject sliderRowPrefab;

    [Header("Cheats Lock")]
    [Tooltip("Overlay shown (and blocking input) while cheats are disabled.")]
    [SerializeField] private CanvasGroup cheatsLockOverlay;

    // Sliders cover sensible ranges, but typed values have no upper limit — only what's
    // physically possible: nothing negative, and size can't reach zero.
    private const float MinSizePercent = 0.1f;

    // Self rows built in code by cloning the Cooldown row: (stat, label)
    private static readonly (Stat stat, string label)[] ExtraSelfStats =
    {
        (Stat.AirControl,      "Air Control"),
        (Stat.Gravity,         "Gravity"),
        (Stat.AbilityPower,    "Ability Power"),
        (Stat.AbilityDuration, "Ability Duration"),
    };

    private readonly Dictionary<Stat, (Slider slider, TMP_InputField input)> selfControls = new();
    private readonly List<AbilityCheatRow> abilityRows = new();
    private readonly KeyListener           listener    = new();
    private AbilityCheatRow listeningRow;
    private Player          builtFor;
    private string          builtSignature;
    private ScrollRect      scroll;

    private static Player Local => Player.Local;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        selfTabButton .onClick.AddListener(() => ShowSubTab(selfPanel));
        basicTabButton.onClick.AddListener(() => ShowSubTab(basicPanel));
        quickTabButton.onClick.AddListener(() => ShowSubTab(quickPanel));
        throwTabButton.onClick.AddListener(() => ShowSubTab(throwPanel));
        trapTabButton .onClick.AddListener(() => ShowSubTab(trapPanel));

        scroll = GetComponentInChildren<ScrollRect>(true);
        BuildSelfControls();
        RefreshSelf();
        ShowSubTab(selfPanel);
    }

    public override void OnEnable()
    {
        base.OnEnable();
        ApplyCheatsLock();
        RefreshSelf();

        if (Local != null) BuildAbilityTabs();
        foreach (var row in abilityRows) row.RefreshSliders();
    }

    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        if (!changed.ContainsKey(RuleKeys.CheatsEnabled)) return;
        ApplyCheatsLock();
        RefreshSelf();
        RefreshAbilityBindings();
        foreach (var row in abilityRows) row.RefreshSliders();
    }

    private void Update()
    {
        if (listeningRow == null || !listener.Poll(out KeyCode key)) return;
        Bind(listeningRow, key);
        listeningRow = null;
    }

    private void ApplyCheatsLock()
    {
        if (cheatsLockOverlay != null) cheatsLockOverlay.gameObject.SetActive(!Cheats.Enabled);
    }

    private void ShowSubTab(GameObject panel)
    {
        selfPanel .SetActive(panel == selfPanel);
        basicPanel.SetActive(panel == basicPanel);
        quickPanel.SetActive(panel == quickPanel);
        throwPanel.SetActive(panel == throwPanel);
        trapPanel .SetActive(panel == trapPanel);

        RebuildLayout(panel.transform);
        if (scroll != null) scroll.verticalNormalizedPosition = 1f; // Each tab starts at the top
    }

    /// <summary>Re-lays out the whole Cheats list now (rows added, removed, expanded or collapsed).</summary>
    public static void RebuildLayout(Transform anyChild)
    {
        var scrollRect = anyChild.GetComponentInParent<ScrollRect>(true);
        var root = scrollRect != null && scrollRect.content != null ? scrollRect.content : anyChild as RectTransform;
        if (root != null) LayoutRebuilder.ForceRebuildLayoutImmediate(root);
    }

    // ─── Self tab ─────────────────────────────────────────────────────────────

    private (Slider slider, TMP_InputField input) Controls(Stat stat) =>
        selfControls.TryGetValue(stat, out var controls) ? controls : (null, null);

    private void BuildSelfControls()
    {
        // Speed…Size call the public methods below from their scene events
        selfControls[Stat.Speed]        = (speedSlider, speedInput);
        selfControls[Stat.Acceleration] = (accelerationSlider, accelerationInput);
        selfControls[Stat.Jump]         = (jumpSlider, jumpInput);
        selfControls[Stat.Size]         = (scaleSizeSlider, scaleSizeInput);
        selfControls[Stat.Cooldown]     = (cooldownSlider, cooldownInput);
        Listen(Stat.Cooldown);

        // The rest are copies of the Cooldown row, so they always match the hand-made rows
        if (cooldownSlider == null || cooldownInput == null) return;
        Transform template = cooldownSlider.transform.parent;

        foreach (var (stat, label) in ExtraSelfStats)
        {
            var row = Instantiate(template.gameObject, template.parent);
            row.name = label;

            var slider = row.GetComponentInChildren<Slider>(true);
            var input  = row.GetComponentInChildren<TMP_InputField>(true);
            MuteSceneEvents(slider.onValueChanged);
            MuteSceneEvents(input.onEndEdit);

            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
                if (text.GetComponentInParent<TMP_InputField>(true) == null && text.GetComponentInParent<Button>(true) == null)
                    text.text = label;

            selfControls[stat] = (slider, input);
            Listen(stat);
        }
    }

    private void Listen(Stat stat)
    {
        var (slider, input) = Controls(stat);
        if (slider != null) slider.onValueChanged.AddListener(_ => FromSlider(stat));
        if (input  != null) input.onEndEdit.AddListener(_ => FromInput(stat));
    }

    // A copied row still carries the template's inspector events; switch them off
    private static void MuteSceneEvents(UnityEngine.Events.UnityEventBase unityEvent)
    {
        for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
            unityEvent.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
    }

    private void RefreshSelf()
    {
        foreach (var stat in selfControls.Keys)
        {
            float percent = Local != null ? Local.Modifiers.Get(stat, Cheats.ModifierSource) * 100f : 100f;
            Show(stat, percent);
        }
    }

    private void Show(Stat stat, float percent)
    {
        var (slider, input) = Controls(stat);
        if (slider != null) slider.SetValueWithoutNotify(percent);
        if (input  != null) input.SetTextWithoutNotify(percent.ToString(percent < 10f ? "0.#" : "F0"));
    }

    private void FromSlider(Stat stat)
    {
        var (slider, _) = Controls(stat);
        if (slider != null) Apply(stat, slider.value);
    }

    private void FromInput(Stat stat)
    {
        var (_, input) = Controls(stat);
        if (input == null) return;

        if (!SliderInputPair.TryParse(input.text, out float percent))
        {
            RefreshSelf();
            return;
        }
        Apply(stat, percent);
    }

    private void Apply(Stat stat, float percent)
    {
        if (Local == null || !Cheats.Enabled)
        {
            RefreshSelf();
            return;
        }

        percent = Mathf.Max(stat == Stat.Size ? MinSizePercent : 0f, percent);

        Local.Modifiers.Set(stat, Cheats.ModifierSource, percent / 100f);
        Show(stat, percent);
    }

    // Scene event entry points (sliders: On Value Changed, inputs: On End Edit)
    public void SliderSpeedMod(float _)        => FromSlider(Stat.Speed);
    public void InputSpeedMod()                => FromInput(Stat.Speed);
    public void SliderAccelerationMod(float _) => FromSlider(Stat.Acceleration);
    public void InputAccelerationMod()         => FromInput(Stat.Acceleration);
    public void SliderJumpMod(float _)         => FromSlider(Stat.Jump);
    public void InputJumpMod()                 => FromInput(Stat.Jump);
    public void SliderScaleSizeMod(float _)    => FromSlider(Stat.Size);
    public void InputScaleSizeMod()            => FromInput(Stat.Size);

    // ─── Ability tabs ─────────────────────────────────────────────────────────

    // Rebuilds only when the equipped abilities change. Binding a key also raises LoadoutChanged
    // (the extra abilities are re-published), and rebuilding then collapsed and re-stacked the list.
    private void BuildAbilityTabs()
    {
        string signature = Local != null ? LoadoutSignature(Local.Abilities) : null;
        if (builtFor == Local && signature == builtSignature)
        {
            RefreshAbilityBindings();
            return;
        }

        foreach (var row in abilityRows)
        {
            if (row == null) continue;
            row.gameObject.SetActive(false); // Out of the layout now; Destroy only lands at the end of the frame
            Destroy(row.gameObject);
        }
        abilityRows.Clear();
        listeningRow = null;

        if (builtFor != null) builtFor.Abilities.LoadoutChanged -= BuildAbilityTabs;
        builtFor       = Local;
        builtSignature = signature;
        if (builtFor == null || abilityRowPrefab == null) return;

        BuildAbilityTab(basicPanel.transform, AbilitySlot.Basic);
        BuildAbilityTab(quickPanel.transform, AbilitySlot.Quick);
        BuildAbilityTab(throwPanel.transform, AbilitySlot.Throw);
        BuildAbilityTab(trapPanel.transform,  AbilitySlot.Trap);

        builtFor.Abilities.LoadoutChanged += BuildAbilityTabs;
        SortRows();
    }

    private static string LoadoutSignature(AbilityHandler handler)
    {
        var sb = new System.Text.StringBuilder();
        foreach (AbilitySlot slot in System.Enum.GetValues(typeof(AbilitySlot)))
            sb.Append(handler.GetEquipped(slot)?.Id).Append('|');
        return sb.ToString();
    }

    private void BuildAbilityTab(Transform container, AbilitySlot slot)
    {
        var handler  = builtFor.Abilities;
        var equipped = handler.GetEquipped(slot);

        if (equipped != null) SpawnRow(container, equipped, isEquipped: true, Keybinds.ForSlot(slot));

        foreach (string id in AbilityCatalog.InSlot(slot))
        {
            var ability = handler.Find(id);
            if (ability == null || ability == equipped || !AbilityCatalog.IsAvailable(id)) continue;

            handler.CustomBindings.TryGetValue(id, out KeyCode key);
            SpawnRow(container, ability, isEquipped: false, key);
        }
    }

    private void SpawnRow(Transform container, Ability ability, bool isEquipped, KeyCode key)
    {
        var row = Instantiate(abilityRowPrefab, container).GetComponent<AbilityCheatRow>();
        row.Initialize(ability, isEquipped, key, sliderRowPrefab);
        row.BindRequested  += StartListening;
        row.ClearRequested += r => Bind(r, KeyCode.None);
        abilityRows.Add(row);
    }

    // The equipped ability's key is its slot key; any other ability gets an extra binding
    private void Bind(AbilityCheatRow row, KeyCode key)
    {
        if (Local == null) return;
        if (row.IsEquipped) Local.Abilities.SetSlotBinding(row.Ability.Slot, key);
        else                Local.Abilities.SetCustomBinding(row.Ability.Id, key);
        RefreshAbilityBindings();
    }

    private void StartListening(AbilityCheatRow row)
    {
        if (!Cheats.Enabled) return;
        listeningRow?.SetListening(false);
        listeningRow = row;
        row.SetListening(true);
        listener.Start();
    }

    private void RefreshAbilityBindings()
    {
        if (Local == null) return;
        foreach (var row in abilityRows)
        {
            KeyCode key;
            if (row.IsEquipped) key = Keybinds.ForSlot(row.Ability.Slot);
            else                Local.Abilities.CustomBindings.TryGetValue(row.Ability.Id, out key);
            row.SetBoundKey(key);
        }
        SortRows();
    }

    // Active abilities at the top of each tab: the equipped one, then extras with a key, then the rest
    private void SortRows()
    {
        var sorted = new List<AbilityCheatRow>(abilityRows);
        sorted.RemoveAll(r => r == null);
        sorted.Sort((x, y) =>
        {
            int rank = Rank(x).CompareTo(Rank(y));
            return rank != 0 ? rank : string.CompareOrdinal(x.Ability.Id, y.Ability.Id);
        });

        var next = new Dictionary<Transform, int>();
        foreach (var row in sorted)
        {
            Transform parent = row.transform.parent;
            next.TryGetValue(parent, out int index);
            row.transform.SetSiblingIndex(index);
            next[parent] = index + 1;
        }
        if (sorted.Count > 0) RebuildLayout(sorted[0].transform);
    }

    private static int Rank(AbilityCheatRow row) =>
        row.IsEquipped ? 0 : row.BoundKey != KeyCode.None ? 1 : 2;

    private void OnDestroy()
    {
        if (builtFor != null) builtFor.Abilities.LoadoutChanged -= BuildAbilityTabs;
    }
}
