using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// The pause menu's Cheats page (works only while the host has cheats on):
///   - Self: multipliers on the local player (speed, acceleration, jump, size, cooldown, air control, gravity), 100% = normal;
///   - Basic / Quick / Throw / Trap: every ability of that kind as a card (icon, name, EQUIPPED tag), with a key to bind
///     it to (extra abilities on extra keys; the equipped one rebinds its slot), a clear button, and an expandable
///     section with its description and [TunableParam] sliders (and its object's tunables).
/// Cards keep a fixed order (the catalog's); the EQUIPPED tag marks every ability that holds a key. Changes go through StatModifiers (source <see cref="Cheats.ModifierSource"/>) and
/// AbilityHandler bindings, exactly as before; turning cheats off reverts everything (AbilityHandler.ClearCheats).
/// </summary>
public class CheatsPanel : MonoBehaviourPunCallbacks
{
    private static readonly (Stat stat, string label)[] SelfStats =
    {
        (Stat.Speed,        "Speed"),
        (Stat.Acceleration, "Acceleration"),
        (Stat.Jump,         "Jump"),
        (Stat.Size,         "Size"),
        (Stat.Cooldown,     "Cooldowns"),
        (Stat.AirControl,   "Air Control"),
        (Stat.Gravity,      "Gravity"),
    };

    private sealed class AbilityRow
    {
        public Ability       Ability;
        public bool          Equipped;
        public KeyCode       Key;
        public RectTransform Root;
        public TMP_Text      KeyLabel;
        public GameObject    EquippedTag;
        public GameObject    Details;
        public List<(Slider slider, System.Func<float> get)> Sliders = new();
    }

    private readonly List<(Button tab, RectTransform page, RectTransform list)> tabs = new();
    private readonly Dictionary<Stat, Slider> selfSliders = new();
    private readonly List<AbilityRow> rows = new();
    private readonly KeyListener listener = new();
    private AbilityRow listening;
    private CanvasGroup content;
    private GameObject locked;
    private Player builtFor;
    private string builtSignature;
    private int current;

    private static Player Local => Player.Local;

    // Typed values have no upper limit (mischief welcome); size just can't reach zero
    private const float MinSizePercent = 0.1f;
    private static UITheme Theme => UITheme.Current;

    public static CheatsPanel Create(RectTransform parent)
    {
        var root = UIKit.Rect("Cheats", parent);
        UIKit.Stretch(root);
        var panel = root.gameObject.AddComponent<CheatsPanel>();
        panel.Build();
        return panel;
    }

    // ─── Build ────────────────────────────────────────────────────────────────

    private void Build()
    {
        var holder = UIKit.Rect("Content", transform);
        UIKit.Stretch(holder);
        content = holder.gameObject.AddComponent<CanvasGroup>();

        var bar = UIKit.Rect("Tabs", holder);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 60f);
        UIKit.HStack(bar, 10f, null, TextAnchor.MiddleLeft, expandHeight: true);

        var pages = UIKit.Rect("Pages", holder);
        UIKit.Stretch(pages, 0f, 0f, 80f, 0f);

        foreach (var name in new[] { "Self", "Basic", "Quick", "Throw", "Trap" })
        {
            int index = tabs.Count;
            var button = UIKit.Button(bar, name, UIKit.Style.Ghost, () => Show(index), 56f, 24f);
            UIKit.Size(button, 150f, 56f);
            var page = UIKit.Rect(name, pages);
            UIKit.Stretch(page);
            tabs.Add((button, page, UIKit.ScrollList(page, 10f)));
        }

        BuildSelf(tabs[0].list);

        // Shown over everything while cheats are off
        var lockCard = UIKit.Card(transform, UIKit.WithAlpha(Theme.card, 0.97f), 24f, 0.5f, "Locked");
        UIKit.Place(lockCard, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 200f));
        lockCard.pivot = new Vector2(0.5f, 0.5f);
        UIKit.VStack(lockCard, 8f, UIKit.Pad(32, 28), TextAnchor.MiddleCenter);
        UIKit.Title(lockCard, "Cheats are off", 42f, TextAlignmentOptions.Center);
        UIKit.Text(lockCard, "The host can turn them on in Match Rules.", 22f, UIKit.Font.Body, Theme.textMuted, TextAlignmentOptions.Center);
        locked = lockCard.gameObject;

        Show(0);
    }

    private void BuildSelf(RectTransform list)
    {
        foreach (var (stat, label) in SelfStats)
        {
            var s = stat;
            selfSliders[stat] = SettingsPanel.SliderRow(list, label, null, stat == Stat.Size ? 10f : 0f, 300f, 100f, true,
                v => ApplySelf(s, v), v => $"{v:0.#}%", typeable: true, hardMin: stat == Stat.Size ? MinSizePercent : 0f);
        }
        var reset = UIKit.Button(list, "Reset to Normal", UIKit.Style.Danger, () =>
        {
            foreach (var (stat, _) in SelfStats) ApplySelf(stat, 100f);
            RefreshSelf();
        }, 60f, 26f);
        UIKit.Size(reset, height: 60f);
    }

    private void Show(int index)
    {
        current = index;
        for (int i = 0; i < tabs.Count; i++)
        {
            tabs[i].page.gameObject.SetActive(i == index);
            tabs[i].tab.GetComponent<UIButtonFx>().FaceColor = i == index ? Theme.secondary : Theme.card;
        }
        StopListening();
        UIKit.Relayout(tabs[index].page);
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    public override void OnEnable()
    {
        base.OnEnable();
        if (locked == null) return; // AddComponent runs OnEnable before Build
        RefreshLock();
        RefreshSelf();
        BuildAbilities();
        foreach (var row in rows) RefreshSliders(row);
    }

    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        if (!changed.ContainsKey(RuleKeys.CheatsEnabled)) return;
        RefreshLock();
        RefreshSelf();
        RefreshBindings();
    }

    public override void OnDisable()
    {
        base.OnDisable();
        StopListening(); // Page switched or menu hidden mid-bind
    }

    private void OnDestroy()
    {
        if (builtFor != null) builtFor.Abilities.LoadoutChanged -= BuildAbilities;
    }

    private void Update()
    {
        // The menu closed (Escape, Resume) while waiting for a key: forget the bind, don't take the next key press
        if (listening != null && (!PauseMenuManager.IsPaused || !isActiveAndEnabled)) { StopListening(); return; }
        if (listening == null || !listener.Poll(out KeyCode key)) return;
        Bind(listening, key);
        listening = null;
        UISound.Play(UISound.Kind.Confirm);
    }

    private void RefreshLock()
    {
        bool on = Cheats.Enabled;
        locked.SetActive(!on);
        content.alpha = on ? 1f : 0.35f;
        content.interactable = content.blocksRaycasts = on;
    }

    // ─── Self ─────────────────────────────────────────────────────────────────

    private void ApplySelf(Stat stat, float percent)
    {
        if (Local == null || !Cheats.Enabled) { RefreshSelf(); return; }
        percent = Mathf.Max(stat == Stat.Size ? MinSizePercent : 0f, percent);
        Local.Modifiers.Set(stat, Cheats.ModifierSource, percent / 100f);
    }

    private void RefreshSelf()
    {
        foreach (var pair in selfSliders)
        {
            float percent = Local != null ? Local.Modifiers.Get(pair.Key, Cheats.ModifierSource) * 100f : 100f;
            SettingsPanel.ShowValue(pair.Value, percent);
        }
    }

    // ─── Abilities ────────────────────────────────────────────────────────────

    // Rebuilt only when the equipped abilities change (binding a key also raises LoadoutChanged)
    private void BuildAbilities()
    {
        if (Local == null) return;
        string signature = Signature(Local.Abilities);
        if (builtFor == Local && signature == builtSignature) { RefreshBindings(); return; }

        foreach (var row in rows) if (row.Root != null) Destroy(row.Root.gameObject);
        rows.Clear();
        listening = null;

        if (builtFor != null) builtFor.Abilities.LoadoutChanged -= BuildAbilities;
        builtFor = Local;
        builtSignature = signature;
        builtFor.Abilities.LoadoutChanged += BuildAbilities;

        var handler = builtFor.Abilities;
        for (int t = 1; t < tabs.Count; t++)
        {
            var slot = (AbilitySlot)(t - 1);
            var equipped = handler.GetEquipped(slot);
            foreach (string id in AbilityCatalog.InSlot(slot))
            {
                var ability = handler.Find(id);
                if (ability == null || !AbilityCatalog.IsAvailable(id)) continue;
                rows.Add(BuildRow(tabs[t].list, ability, ability == equipped));
            }
        }
        RefreshBindings();
    }

    private static string Signature(AbilityHandler handler)
    {
        var sb = new System.Text.StringBuilder();
        foreach (AbilitySlot slot in System.Enum.GetValues(typeof(AbilitySlot)))
            sb.Append(handler.GetEquipped(slot)?.Id).Append('|');
        return sb.ToString();
    }

    private AbilityRow BuildRow(Transform list, Ability ability, bool equipped)
    {
        var row = new AbilityRow { Ability = ability, Equipped = equipped };
        var card = UIKit.Panel(list, Theme.card, 18f, ability.Id).rectTransform;
        UIKit.VStack(card, 8f, UIKit.Pad(18, 12), TextAnchor.UpperLeft);
        row.Root = card;

        var top = UIKit.Rect("Top", card);
        UIKit.HStack(top, 14f, null, TextAnchor.MiddleLeft, expandHeight: false);
        UIKit.Size(top, height: 64f);

        var iconBack = UIKit.Panel(top, Theme.field, 14f, "Icon");
        UIKit.Size(iconBack, 56f, 56f);
        var sprite = AbilityIcons.Get(ability.Id);
        if (sprite != null)
        {
            var icon = UIKit.Rect("Image", iconBack.transform).gameObject.AddComponent<Image>();
            UIKit.Stretch(icon.rectTransform, 4f, 4f, 4f, 4f);
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }

        var name = UIKit.OneLine(UIKit.Text(top, AbilityCatalog.DisplayName(ability.Id), 26f, UIKit.Font.Display));
        UIKit.Size(name, 210f);
        var description = UIKit.Text(top, AbilityCatalog.Description(ability.Id), 18f, UIKit.Font.Body, Theme.textMuted);
        description.enableAutoSizing = true;
        description.fontSizeMin = 14f;
        description.fontSizeMax = 18f;
        UIKit.Size(description, height: 60f, flexWidth: 1f);
        row.EquippedTag = UIKit.Chip(top, "EQUIPPED", Theme.lime).gameObject; // Shown while it holds a key (RefreshBindings)

        var bind = UIKit.Button(top, "", UIKit.Style.Ghost, () => Listen(row), 48f, 22f);
        UIKit.Size(bind, 170f, 48f);
        row.KeyLabel = UIKit.Label(bind);
        row.KeyLabel.font = UIKit.FontAsset(UIKit.Font.Bold);
        var clear = UIKit.RoundButton(top, "×", () => Bind(row, KeyCode.None), 44f);
        clear.GetComponent<UIButtonFx>().ClickSound = UISound.Kind.Back;

        // Details: description, stats and tunable sliders
        var details = UIKit.Rect("Details", card);
        UIKit.VStack(details, 8f, null, TextAnchor.UpperLeft);
        string stats = AbilityCatalog.Stats(ability.Id);
        if (!string.IsNullOrEmpty(stats)) UIKit.Text(details, stats, 18f, UIKit.Font.Bold, Theme.primary);

        foreach (var (field, attr) in TunableParamAttribute.FieldsOf(ability.GetType()))
        {
            var f = field;
            var slider = SettingsPanel.SliderRow(details, attr.DisplayName, null, attr.Min, attr.Max, (float)f.GetValue(ability), false,
                v => f.SetValue(ability, Mathf.Max(attr.HardMin, v)), v => v.ToString("0.##"), typeable: true, hardMin: attr.HardMin);
            row.Sliders.Add((slider, () => (float)f.GetValue(ability)));
        }
        foreach (var entry in ability.ObjectTunableEntries)
        {
            var e = entry;
            var slider = SettingsPanel.SliderRow(details, e.Attribute.DisplayName, null, e.Attribute.Min, e.Attribute.Max,
                ability.GetObjectTunable(e), false, v => ability.SetObjectTunable(e, Mathf.Max(e.Attribute.HardMin, v)), v => v.ToString("0.##"),
                typeable: true, hardMin: e.Attribute.HardMin);
            row.Sliders.Add((slider, () => ability.GetObjectTunable(e)));
        }
        details.gameObject.SetActive(false);
        row.Details = details.gameObject;

        var toggle = UIKit.LinkButton(card, "Show details", null, 18f);
        var toggleLabel = UIKit.Label(toggle);
        toggleLabel.alignment = TextAlignmentOptions.Left;
        toggle.onClick.AddListener(() =>
        {
            bool open = !row.Details.activeSelf;
            row.Details.SetActive(open);
            toggleLabel.text = open ? "Hide details" : "Show details";
            UIKit.Relayout(tabs[current].page);
        });
        toggle.gameObject.SetActive(details.childCount > 0); // Nothing to show: no link
        return row;
    }

    private void RefreshSliders(AbilityRow row)
    {
        foreach (var (slider, get) in row.Sliders) SettingsPanel.ShowValue(slider, get());
    }

    // ─── Binding ──────────────────────────────────────────────────────────────

    private void Listen(AbilityRow row)
    {
        if (!Cheats.Enabled) return;
        StopListening();
        listening = row;
        row.KeyLabel.text = "Press a key…";
        row.KeyLabel.color = Theme.primary;
        listener.Start();
    }

    private void StopListening()
    {
        if (listening == null) return;
        listener.Stop();
        listening = null;
        RefreshBindings();
    }

    // The equipped ability's key is its slot key; any other ability gets an extra binding
    private void Bind(AbilityRow row, KeyCode key)
    {
        if (Local == null) return;
        if (row.Equipped) Local.Abilities.SetSlotBinding(row.Ability.Slot, key);
        else              Local.Abilities.SetCustomBinding(row.Ability.Id, key);
        RefreshBindings();
    }

    private void RefreshBindings()
    {
        if (Local == null) return;
        foreach (var row in rows)
        {
            KeyCode key;
            if (row.Equipped) key = Keybinds.ForSlot(row.Ability.Slot);
            else Local.Abilities.CustomBindings.TryGetValue(row.Ability.Id, out key);
            row.Key = key;
            row.KeyLabel.text = key == KeyCode.None ? "Bind key" : Keybinds.Label(key);
            row.KeyLabel.color = key == KeyCode.None ? Theme.textMuted : Theme.text;
            // Binding another ability to the slot's key takes the key off the starting one: the tag moves with it
            if (row.EquippedTag != null) row.EquippedTag.SetActive(key != KeyCode.None);
        }
        if (tabs.Count > 0) UIKit.Relayout(tabs[current].page);
    }
}
