using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pregame ability selection. Each player picks one ability per slot; picks are written to
/// their player properties immediately (and remembered for next time), so nothing needs
/// uploading at the end. When the shared timer runs out — or the host ends it early — the
/// host loads the selected map and everyone follows via AutomaticallySyncScene.
///
/// Each slot page lists every usable ability (<see cref="AbilityCatalog"/>): the first row in the
/// page's list (one with an <see cref="AbilityToggle"/>) is the template, and a copy is made for any
/// ability without a row, so new abilities appear without scene edits. Names and descriptions come
/// from [AbilityInfo]; abilities that can't be used yet are hidden.
/// Attach to: the PregameManager GameObject in the Pregame scene.
/// </summary>
public class PregameManager : MonoBehaviourPunCallbacks
{
    [Header("Header")]
    [Tooltip("Shows the game mode being played.")]
    [SerializeField] private TMP_Text   teamText;
    [SerializeField] private TMP_Text   countDown;
    [SerializeField] private GameObject endPhaseButtonObject;

    [Header("Slot Pages")]
    [SerializeField] private GameObject baseScrollView;
    [SerializeField] private GameObject quickScrollView;
    [SerializeField] private GameObject throwScrollView;
    [SerializeField] private GameObject trapScrollView;

    [Header("Timing")]
    [SerializeField] private float selectionSeconds = 20f;

    private readonly Dictionary<Toggle, (AbilitySlot slot, string id)> abilityToggles = new();
    private bool    loading;
    private double? masterDeadline; // Master's own copy, so it never acts on a stale property

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        endPhaseButtonObject.SetActive(PhotonNetwork.IsMasterClient);

        var mode = GameModeRegistry.Find(MatchRules.GameModeKey);
        if (teamText != null)
            teamText.text = mode != null ? mode.gameModeName : MatchRules.GameModeKey;

        SetupAbilityToggles();
        ShowPage(baseScrollView);

        if (PhotonNetwork.IsMasterClient)
        {
            masterDeadline = PhotonNetwork.Time + selectionSeconds;
            NetProps.SetRoom(RoomKeys.PregameEndsAt, masterDeadline.Value);
        }
    }

    private void Update()
    {
        float left = SecondsLeft;
        if (countDown != null) countDown.text = Mathf.CeilToInt(Mathf.Max(0f, left)).ToString();

        if (masterDeadline.HasValue && left <= 0f) StartMatch();
    }

    private float SecondsLeft =>
        (float)((masterDeadline ?? NetProps.Room(RoomKeys.PregameEndsAt, PhotonNetwork.Time + selectionSeconds))
                - PhotonNetwork.Time);

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMaster)
    {
        endPhaseButtonObject.SetActive(PhotonNetwork.IsMasterClient);
        if (PhotonNetwork.IsMasterClient)
            masterDeadline = NetProps.Room(RoomKeys.PregameEndsAt, PhotonNetwork.Time + selectionSeconds);
    }

    // ─── Ability selection ────────────────────────────────────────────────────

    private void SetupAbilityToggles()
    {
        // Existing rows, by ability, and the list each slot's rows live in
        var rows  = new Dictionary<string, AbilityToggle>();
        var lists = new Dictionary<AbilitySlot, Transform>();
        foreach (var marker in FindObjectsByType<AbilityToggle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var slot = AbilityCatalog.SlotOf(marker.abilityName);
            Transform row = RowOf(marker);
            if (slot == null || !AbilityCatalog.IsAvailable(marker.abilityName) || rows.ContainsKey(marker.abilityName))
            {
                row.gameObject.SetActive(false); // Unknown, unusable or duplicate
                if (slot != null && !lists.ContainsKey(slot.Value)) lists[slot.Value] = row.parent;
                continue;
            }
            rows[marker.abilityName] = marker;
            if (!lists.ContainsKey(slot.Value)) lists[slot.Value] = row.parent;
        }

        foreach (AbilitySlot slot in System.Enum.GetValues(typeof(AbilitySlot)))
        {
            string chosen = AbilityLoadout.LastChoice(slot);
            AbilityLoadout.SetLocal(slot, chosen);
            if (!lists.TryGetValue(slot, out Transform list)) continue;

            Transform template = null;
            foreach (Transform child in list)
                if (child.GetComponentInChildren<AbilityToggle>(true) != null) { template = child; break; }

            int index = 0;
            foreach (string id in AbilityCatalog.InSlot(slot))
            {
                if (!AbilityCatalog.IsAvailable(id)) continue;
                if (!rows.TryGetValue(id, out var marker))
                {
                    if (template == null) continue;
                    marker = Instantiate(template.gameObject, list).GetComponentInChildren<AbilityToggle>(true);
                    marker.abilityName = id;
                }
                SetupRow(marker, slot, id, chosen == id);
                RowOf(marker).SetSiblingIndex(index++);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(list as RectTransform);
        }
    }

    // A row is the toggle's parent: [Toggle | AbilityNameText | DescText]
    private static Transform RowOf(AbilityToggle marker) => marker.transform.parent;

    private void SetupRow(AbilityToggle marker, AbilitySlot slot, string id, bool isOn)
    {
        Transform row = RowOf(marker);
        row.name    = id;
        marker.name = id + "Toggle";
        row.gameObject.SetActive(true);

        foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.name == "AbilityNameText") text.text = AbilityCatalog.DisplayName(id);
            else if (text.name == "DescText")
            {
                // What it does, then its numbers (cooldown, uses, duration) straight from the player prefab.
                // Shrinks to fit: some descriptions already take two lines
                text.enableAutoSizing = true;
                if (text.fontSizeMax < text.fontSize) text.fontSizeMax = text.fontSize;
                text.fontSizeMin = Mathf.Min(16f, text.fontSizeMax);
                string stats = AbilityCatalog.Stats(id);
                text.text = string.IsNullOrEmpty(stats)
                    ? AbilityCatalog.Description(id)
                    : $"{AbilityCatalog.Description(id)}\n<size=80%><color=#B9C6D8>{stats}</color></size>";
            }
        }

        var toggle = marker.GetComponent<Toggle>();
        abilityToggles[toggle] = (slot, id);
        toggle.interactable = true;
        toggle.SetIsOnWithoutNotify(isOn);

        // Wired in code only (rows are copied, and copies would carry the template's scene events)
        for (int i = 0; i < toggle.onValueChanged.GetPersistentEventCount(); i++)
            toggle.onValueChanged.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(_ => OnAbilityToggleChanged(toggle));
    }

    private void OnAbilityToggleChanged(Toggle toggle)
    {
        if (!toggle.isOn || !abilityToggles.TryGetValue(toggle, out var entry)) return;
        AbilityLoadout.SetLocal(entry.slot, entry.id);
    }

    // Old scene event entry points (rows are wired in code now; kept so existing scene events resolve)
    public void OnBasicToggleChanged(Toggle toggle) { }
    public void OnQuickToggleChanged(Toggle toggle) { }
    public void OnThrowToggleChanged(Toggle toggle) { }
    public void OnTrapToggleChanged(Toggle toggle)  { }

    // ─── Pages ────────────────────────────────────────────────────────────────

    public void OnBaseTabPressed()  => ShowPage(baseScrollView);
    public void OnQuickTabPressed() => ShowPage(quickScrollView);
    public void OnThrowTabPressed() => ShowPage(throwScrollView);
    public void OnTrapTabPressed()  => ShowPage(trapScrollView);

    private void ShowPage(GameObject page)
    {
        baseScrollView.SetActive(page == baseScrollView);
        quickScrollView.SetActive(page == quickScrollView);
        throwScrollView.SetActive(page == throwScrollView);
        trapScrollView.SetActive(page == trapScrollView);

        // Size the list to its rows and start at the top
        var scroll = page.GetComponent<ScrollRect>();
        if (scroll == null || scroll.content == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        scroll.verticalNormalizedPosition = 1f;
    }

    // ─── Starting the match ───────────────────────────────────────────────────

    public void OnEndPhaseButtonPressed() => StartMatch();

    private void StartMatch()
    {
        if (!PhotonNetwork.IsMasterClient || loading) return;
        loading = true;

        string map = MatchRules.Map;
        if (!Application.CanStreamedLevelBeLoaded(map))
        {
            Debug.LogError($"[Pregame] Map '{map}' is not in Build Settings; loading {SceneNames.DefaultMap} instead.");
            map = SceneNames.DefaultMap;
        }

        // Clear the timer so returning to Pregame later starts a fresh countdown
        NetProps.SetRoom(RoomKeys.PregameEndsAt, null);
        PhotonNetwork.LoadLevel(map);
    }
}
