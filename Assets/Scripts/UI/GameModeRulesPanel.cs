using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// The game-mode settings panel: an optional mode dropdown, then one row per rule of the selected
/// mode (<see cref="GameModeDefinition.settings"/>), built at runtime. A mode with no settings (Freeplay)
/// shows no rows. Only the host can edit; everyone else sees the same values read-only.
///
/// Rows are copies of this scene's own controls, so they match its style:
///   rowTemplate    — a row with a label (TMP_Text) and a TMP_InputField: numbers and times (m:ss)
///   toggleTemplate — a Toggle, swapped in for the input field on on/off rules
/// Values live in room properties (written through NetProps, read back the same way), so the panel
/// always shows what the game uses. Choosing a mode in the dropdown applies its defaults.
/// Used by: the Lobby game-settings window (with dropdown) and the pause menu Rules tab (without).
/// </summary>
public class GameModeRulesPanel : MonoBehaviourPunCallbacks
{
    [Header("Mode")]
    [Tooltip("Optional: picks the game mode (host only). Leave empty where the mode can't change (mid-match).")]
    [SerializeField] private TMP_Dropdown modeDropdown;
    [Tooltip("Optional: shows \"<Mode> Rules\" (or that the mode has none).")]
    [SerializeField] private TMP_Text     titleText;

    [Header("Rows")]
    [SerializeField] private Transform  rowContainer;
    [SerializeField] private GameObject rowTemplate;
    [SerializeField] private Toggle     toggleTemplate;
    [Tooltip("Width given to each row's label so long names fit.")]
    [SerializeField] private float      labelWidth = 420f;
    [Tooltip("Adds a header row above each category, and the mode description at the top.")]
    [SerializeField] private bool       showHeadersAndDescription = true;

    private sealed class Row
    {
        public RuleSetting    setting;
        public GameObject     root;
        public TMP_InputField input;
        public Toggle         toggle;
        public CanvasGroup    group;
    }

    private readonly List<Row>                rows    = new();
    private readonly List<GameObject>         extras  = new(); // Headers and the description
    private readonly List<GameModeDefinition> modes   = new();
    private string builtFor;
    private bool   refreshing;

    private static bool IsHost => PhotonNetwork.IsMasterClient;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        if (rowTemplate != null) rowTemplate.SetActive(false);
        if (toggleTemplate != null) toggleTemplate.gameObject.SetActive(false);

        if (modeDropdown != null)
        {
            modes.AddRange(GameModeRegistry.Definitions);
            modeDropdown.ClearOptions();
            var names = new List<string>();
            foreach (var mode in modes) names.Add(mode.gameModeName);
            modeDropdown.AddOptions(names);
            modeDropdown.onValueChanged.AddListener(OnModePicked);
        }

        Rebuild();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        if (!PhotonNetwork.InRoom || builtFor == null) return;
        // The mode may have changed while this panel was closed (callbacks only reach enabled panels)
        if (MatchRules.GameModeKey != builtFor) Rebuild();
        else Refresh();
    }

    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        if (changed.ContainsKey(RoomKeys.GameMode) && MatchRules.GameModeKey != builtFor) Rebuild();
        else Refresh();
    }

    // Player count changes the hunter cap; a new host can edit
    public override void OnPlayerEnteredRoom(PhotonPlayer _)     => Refresh();
    public override void OnPlayerLeftRoom(PhotonPlayer _)        => Refresh();
    public override void OnMasterClientSwitched(PhotonPlayer _)  => Refresh();

    // ─── Mode ─────────────────────────────────────────────────────────────────

    private void OnModePicked(int index)
    {
        if (!IsHost || refreshing || index < 0 || index >= modes.Count) return;
        MatchRules.ApplyGameMode(modes[index], overwrite: true); // Every rule back to this mode's default
    }

    // ─── Building ─────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        if (!PhotonNetwork.InRoom || rowContainer == null || rowTemplate == null) return;

        foreach (var row in rows) if (row.root != null) Destroy(row.root);
        foreach (var extra in extras) if (extra != null) Destroy(extra);
        rows.Clear();
        extras.Clear();

        builtFor = MatchRules.GameModeKey;
        var definition = GameModeRegistry.Find(builtFor);
        var settings   = definition != null && definition.settings != null ? definition.settings : new RuleSetting[0];

        if (titleText != null)
        {
            string name = definition != null ? definition.gameModeName : builtFor;
            titleText.text = settings.Length > 0 ? $"{name} Rules" : $"{name}: no rules to change";
        }

        if (showHeadersAndDescription && definition != null && !string.IsNullOrEmpty(definition.description))
            extras.Add(MakeTextRow(definition.description, bold: false, wide: true));

        string category = null;
        foreach (var setting in settings)
        {
            if (string.IsNullOrEmpty(setting.roomPropertyKey)) continue;
            if (showHeadersAndDescription && !string.IsNullOrEmpty(setting.category) && setting.category != category)
            {
                category = setting.category;
                extras.Add(MakeTextRow(category.ToUpperInvariant(), bold: true, wide: false));
            }
            rows.Add(MakeRow(setting));
        }

        Refresh();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rowContainer as RectTransform);
    }

    private Row MakeRow(RuleSetting setting)
    {
        var root = Instantiate(rowTemplate, rowContainer);
        root.name = setting.displayName;
        root.SetActive(true);

        var row = new Row
        {
            setting = setting,
            root    = root,
            input   = root.GetComponentInChildren<TMP_InputField>(true),
        };
        // Unity's == (not ??): a missing component can be a "fake null" object in the editor
        row.group = root.GetComponent<CanvasGroup>();
        if (row.group == null) row.group = root.AddComponent<CanvasGroup>();

        string unit = setting.fieldType == RuleSetting.FieldType.Time ? "m:ss" : setting.unit;
        SetLabel(root, string.IsNullOrEmpty(unit) ? $"{setting.displayName}:" : $"{setting.displayName} ({unit}):", bold: false, wide: false);

        if (setting.fieldType == RuleSetting.FieldType.Toggle)
        {
            // Swap the input field for a toggle in the same spot
            if (toggleTemplate != null && row.input != null)
            {
                row.toggle = Instantiate(toggleTemplate, row.input.transform.parent);
                row.toggle.transform.SetSiblingIndex(row.input.transform.GetSiblingIndex());
                row.toggle.gameObject.SetActive(true);
                row.toggle.onValueChanged.RemoveAllListeners();
                row.toggle.onValueChanged.AddListener(on => Commit(row, on));
            }
            if (row.input != null) row.input.gameObject.SetActive(false);
        }
        else if (row.input != null)
        {
            row.input.onEndEdit.RemoveAllListeners();
            row.input.onEndEdit.AddListener(text => Commit(row, text));
            row.input.contentType = TMP_InputField.ContentType.Standard; // m:ss and negatives
        }
        return row;
    }

    // A row with only text: category headers and the mode description
    private GameObject MakeTextRow(string text, bool bold, bool wide)
    {
        var root = Instantiate(rowTemplate, rowContainer);
        root.name = bold ? "Header" : "Description";
        root.SetActive(true);
        foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true)) input.gameObject.SetActive(false);
        SetLabel(root, text, bold, wide);
        return root;
    }

    private void SetLabel(GameObject root, string text, bool bold, bool wide)
    {
        TMP_Text label = null;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            if (t.GetComponentInParent<TMP_InputField>(true) == null && t.GetComponentInParent<Toggle>(true) == null) { label = t; break; }
        if (label == null) return;

        label.text = text;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.enableAutoSizing = true;
        label.fontSizeMax = label.fontSize;
        label.fontSizeMin = Mathf.Min(18f, label.fontSize);
        var rect = label.rectTransform;
        float width = wide ? Mathf.Max(labelWidth, ((RectTransform)root.transform).rect.width - 60f) : labelWidth;
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
    }

    // ─── Display ──────────────────────────────────────────────────────────────

    private void Refresh()
    {
        if (!PhotonNetwork.InRoom) return;
        refreshing = true;

        if (modeDropdown != null)
        {
            int index = modes.FindIndex(m => m.gameModeKey == MatchRules.GameModeKey);
            if (index >= 0) modeDropdown.SetValueWithoutNotify(index);
            modeDropdown.interactable = IsHost;
        }

        int players = PhotonNetwork.CurrentRoom.PlayerCount;
        foreach (var row in rows)
        {
            var s = row.setting;
            if (s.fieldType == RuleSetting.FieldType.Toggle)
            {
                if (row.toggle != null) row.toggle.SetIsOnWithoutNotify(NetProps.Room(s.roomPropertyKey, s.defaultValue > 0f));
            }
            else if (row.input != null)
            {
                float value = NetProps.Room(s.roomPropertyKey, s.defaultValue);
                row.input.SetTextWithoutNotify(Format(s, value));
            }

            // Editable by the host, and only while the toggle it depends on is on
            bool enabled = string.IsNullOrEmpty(s.enabledByKey) || NetProps.Room(s.enabledByKey, false);
            row.group.alpha          = enabled ? 1f : 0.4f;
            row.group.interactable   = IsHost && enabled;
            row.group.blocksRaycasts = IsHost && enabled;
        }

        refreshing = false;
    }

    private static string Format(RuleSetting s, float value)
    {
        if (s.fieldType == RuleSetting.FieldType.Time)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(value));
            return $"{total / 60}:{total % 60:D2}";
        }
        return s.wholeNumbers ? Mathf.RoundToInt(value).ToString() : value.ToString("0.##");
    }

    // ─── Editing (host) ───────────────────────────────────────────────────────

    private void Commit(Row row, bool on)
    {
        if (!refreshing && IsHost) NetProps.SetRoom(row.setting.roomPropertyKey, on);
    }

    private void Commit(Row row, string text)
    {
        if (refreshing || !IsHost) return;
        var s = row.setting;

        bool parsed = s.fieldType == RuleSetting.FieldType.Time ? TryParseTime(text, out float value)
                                                                 : float.TryParse(text, out value);
        if (!parsed) { Refresh(); return; } // Put the current value back

        value = s.Clamp(value, PhotonNetwork.CurrentRoom.PlayerCount);
        NetProps.SetRoom(s.roomPropertyKey, value);
        row.input.SetTextWithoutNotify(Format(s, value)); // Show the clamped value straight away
    }

    /// <summary>Accepts "m:ss" or plain seconds.</summary>
    private static bool TryParseTime(string text, out float seconds)
    {
        seconds = 0f;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] parts = text.Trim().Split(':');
        if (parts.Length == 1) return float.TryParse(parts[0], out seconds);
        if (parts.Length == 2 && int.TryParse(parts[0], out int m) && int.TryParse(parts[1], out int s))
        {
            seconds = m * 60 + Mathf.Clamp(s, 0, 59);
            return true;
        }
        return false;
    }
}
