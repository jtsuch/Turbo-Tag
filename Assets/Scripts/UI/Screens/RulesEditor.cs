using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// The lobby's game settings: a game mode picker, the mode's description, then its rules grouped by category,
/// each as a card with a − value + stepper (numbers, m:ss times) or a switch (on/off). Built from the mode's
/// <see cref="RuleSetting"/>s, so new rules appear without UI work. The host edits (values are clamped to the
/// rule's limits); everyone else sees the same live values, read-only. Values live in room properties.
/// </summary>
public class RulesEditor : MonoBehaviourPunCallbacks
{
    private sealed class Row
    {
        public RuleSetting Setting;
        public CanvasGroup Group;
        public UIStepper   Stepper;
        public UISwitch    Switch;
    }

    private readonly List<Row> rows = new();
    private readonly List<GameModeDefinition> modes = new();
    private UISelector modeSelector;
    private TMP_Text description, hostNote;
    private RectTransform list;
    private string builtFor;
    private bool refreshing;

    private static bool IsHost => PhotonNetwork.IsMasterClient;

    /// <summary>True while a value is being typed (so Escape doesn't leave the lobby).</summary>
    public bool IsTyping
    {
        get
        {
            foreach (var row in rows)
                if (row.Stepper != null && row.Stepper.GetComponentInChildren<TMP_InputField>().isFocused) return true;
            return false;
        }
    }

    /// <param name="allowModeChange">False mid-match (pause menu): the mode is shown but can't change.</param>
    public static RulesEditor Create(RectTransform parent, bool allowModeChange = true)
    {
        var root = UIKit.Rect("Rules", parent);
        UIKit.Stretch(root);
        var editor = root.gameObject.AddComponent<RulesEditor>();
        editor.modeLocked = !allowModeChange;
        editor.Build();
        return editor;
    }

    private bool modeLocked;

    private void Build()
    {
        var theme = UITheme.Current;
        var top = UIKit.Rect("Mode", transform);
        top.anchorMin = new Vector2(0f, 1f);
        top.anchorMax = new Vector2(1f, 1f);
        top.pivot = new Vector2(0.5f, 1f);
        top.sizeDelta = new Vector2(0f, 150f);
        UIKit.VStack(top, 10f, null, TextAnchor.UpperLeft);

        var pickRow = UIKit.Rect("Pick", top);
        UIKit.HStack(pickRow, 16f, null, TextAnchor.MiddleLeft, expandHeight: false);
        UIKit.Size(pickRow, height: 60f);
        UIKit.Text(pickRow, "GAME MODE", 20f, UIKit.Font.Bold, theme.textMuted).textWrappingMode = TextWrappingModes.NoWrap;
        modes.AddRange(GameModeRegistry.Definitions);
        modeSelector = UIKit.Selector(pickRow, modes.ConvertAll(m => m.gameModeName).ToArray(), 0, OnModePicked, 460f);
        hostNote = UIKit.Text(pickRow, "Only the host can change these.", 20f, UIKit.Font.Body, theme.textMuted);
        hostNote.textWrappingMode = TextWrappingModes.NoWrap;

        description = UIKit.Text(top, "", 22f, UIKit.Font.Body, theme.textMuted);
        UIKit.Size(description, height: 70f);

        var area = UIKit.Rect("List", transform);
        UIKit.Stretch(area, 0f, 0f, 160f, 0f);
        list = UIKit.ScrollList(area, 10f);
    }

    private void Start() => Rebuild();

    public override void OnEnable()
    {
        base.OnEnable();
        if (!PhotonNetwork.InRoom || builtFor == null) return;
        if (MatchRules.GameModeKey != builtFor) Rebuild(); else Refresh();
    }

    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        if (changed.ContainsKey(RoomKeys.GameMode) && MatchRules.GameModeKey != builtFor) Rebuild();
        else Refresh();
    }

    public override void OnPlayerEnteredRoom(PhotonPlayer _)    => Refresh();
    public override void OnPlayerLeftRoom(PhotonPlayer _)       => Refresh();
    public override void OnMasterClientSwitched(PhotonPlayer _) => Refresh();

    private void OnModePicked(int index)
    {
        if (!IsHost || refreshing || index < 0 || index >= modes.Count) return;
        MatchRules.ApplyGameMode(modes[index], overwrite: true); // Every rule back to this mode's default
    }

    // ─── Rows ─────────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        if (!PhotonNetwork.InRoom) return;
        foreach (Transform child in list) Destroy(child.gameObject);
        rows.Clear();

        builtFor = MatchRules.GameModeKey;
        var definition = GameModeRegistry.Find(builtFor);
        description.text = definition != null ? definition.description : "";
        var settings = definition != null && definition.settings != null ? definition.settings : new RuleSetting[0];

        if (settings.Length == 0)
            UIKit.Text(list, "This mode has nothing to change. Just play!", 26f, UIKit.Font.Bold, UITheme.Current.textMuted,
                TextAlignmentOptions.Center);

        string category = null;
        foreach (var setting in settings)
        {
            if (string.IsNullOrEmpty(setting.roomPropertyKey)) continue;
            if (!string.IsNullOrEmpty(setting.category) && setting.category != category)
            {
                category = setting.category;
                var header = UIKit.Text(list, category.ToUpperInvariant(), 22f, UIKit.Font.Display, UITheme.Current.primary);
                UIKit.Size(header, height: 44f);
            }
            rows.Add(MakeRow(setting));
        }
        Refresh();
        relayoutFrames = 2;
    }

    private int relayoutFrames;

    private void LateUpdate()
    {
        if (relayoutFrames <= 0) return;
        relayoutFrames--;
        UIKit.Relayout((RectTransform)transform);
    }

    private Row MakeRow(RuleSetting s)
    {
        string unit = s.fieldType == RuleSetting.FieldType.Time || string.IsNullOrEmpty(s.unit) ? null : s.unit;
        string hint = s.fieldType == RuleSetting.FieldType.Toggle ? null
                    : s.fieldType == RuleSetting.FieldType.Time ? $"{Format(s, s.minValue)} to {Format(s, s.maxValue)}"
                    : $"{Format(s, s.minValue)} to {Format(s, s.maxValue)}{(unit != null ? " " + unit : "")}";
        var card = UIKit.Row(list, s.displayName, hint, 80f);
        var row = new Row { Setting = s, Group = card.gameObject.AddComponent<CanvasGroup>() };

        if (s.fieldType == RuleSetting.FieldType.Toggle)
        {
            row.Switch = UIKit.Switch(card, s.defaultValue > 0f, on =>
            {
                if (!refreshing && IsHost) NetProps.SetRoom(s.roomPropertyKey, on);
            });
        }
        else
        {
            float step = s.fieldType == RuleSetting.FieldType.Time ? (s.maxValue >= 600f ? 30f : 5f)
                       : s.wholeNumbers ? (s.maxValue - s.minValue >= 100f ? 5f : 1f) : 0.1f;
            row.Stepper = UIKit.Stepper(card, s.defaultValue, step, v => Format(s, v) + (unit == "%" ? "%" : ""),
                text => Parse(s, text), v => Commit(row, v), 280f);
        }
        return row;
    }

    private void Commit(Row row, float value)
    {
        if (refreshing || !IsHost) { Refresh(); return; }
        var s = row.Setting;
        value = s.Clamp(value, PhotonNetwork.CurrentRoom.PlayerCount);
        NetProps.SetRoom(s.roomPropertyKey, value);
        row.Stepper.SetWithoutNotify(value); // The clamped value straight away
    }

    private void Refresh()
    {
        if (!PhotonNetwork.InRoom) return;
        refreshing = true;

        int index = modes.FindIndex(m => m.gameModeKey == MatchRules.GameModeKey);
        if (index >= 0) modeSelector.Set(index, notify: false);
        modeSelector.Interactable = IsHost && !modeLocked;
        hostNote.gameObject.SetActive(!IsHost || modeLocked);
        if (modeLocked) hostNote.text = "The mode is set for this match.";

        foreach (var row in rows)
        {
            var s = row.Setting;
            if (row.Switch != null) row.Switch.SetWithoutNotify(NetProps.Room(s.roomPropertyKey, s.defaultValue > 0f));
            if (row.Stepper != null) row.Stepper.SetWithoutNotify(NetProps.Room(s.roomPropertyKey, s.defaultValue));

            bool enabled = string.IsNullOrEmpty(s.enabledByKey) || NetProps.Room(s.enabledByKey, false);
            row.Group.alpha = enabled ? 1f : 0.45f;
            bool editable = IsHost && enabled;
            if (row.Switch != null) row.Switch.interactable = editable;
            if (row.Stepper != null) row.Stepper.Interactable = editable;
        }
        refreshing = false;
    }

    private static string Format(RuleSetting s, float value) => FormatValue(s, value);

    private static float? Parse(RuleSetting s, string text)
    {
        text = text.Replace("%", "").Trim();
        if (s.fieldType == RuleSetting.FieldType.Time)
            return TryParseTime(text, out float seconds) ? seconds : null;
        return float.TryParse(text, out float value) ? value : null;
    }

    // ─── Values ───────────────────────────────────────────────────────────────

    /// <summary>A rule value as shown: m:ss for times, whole or two-decimal numbers otherwise.</summary>
    public static string FormatValue(RuleSetting s, float value)
    {
        if (s.fieldType == RuleSetting.FieldType.Time)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(value));
            return $"{total / 60}:{total % 60:D2}";
        }
        return s.wholeNumbers ? Mathf.RoundToInt(value).ToString() : value.ToString("0.##");
    }

    /// <summary>Accepts "m:ss" or plain seconds.</summary>
    public static bool TryParseTime(string text, out float seconds)
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
