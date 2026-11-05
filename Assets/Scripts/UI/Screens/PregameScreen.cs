using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pregame ability selection: four colour-coded columns (Basic, Quick, Throw, Trap), each a list of ability cards
/// (icon, name, description and stats from the player prefab). Click a card to equip it; the choice is written to
/// the player's properties at once and remembered for next time. A countdown ring shows the shared timer; the host
/// can start early. When time runs out the host loads the map and everyone follows.
/// Attach to one GameObject in the Pregame scene.
/// </summary>
public class PregameScreen : MenuScreen
{
    [SerializeField] private float selectionSeconds = 20f;

    private sealed class Card
    {
        public AbilitySlot Slot;
        public string      Id;
        public Image       Bg;
        public Outline     Outline;
        public GameObject  Check;
    }

    private readonly List<Card> cards = new();
    private readonly Dictionary<AbilitySlot, TMP_Text> picked = new();
    private Image ring;
    private TMP_Text seconds;
    private Button startButton;
    private bool loading;
    private double? masterDeadline; // The master's own copy, so it never acts on a stale property

    private Color SlotColor(AbilitySlot slot) => slot switch
    {
        AbilitySlot.Basic => Theme.cyan,
        AbilitySlot.Quick => Theme.primary,
        AbilitySlot.Throw => Theme.pink,
        _                 => Theme.lime,
    };

    private static string SlotName(AbilitySlot slot) => slot switch
    {
        AbilitySlot.Basic => "Basic",
        AbilitySlot.Quick => "Quick",
        AbilitySlot.Throw => "Throw",
        _                 => "Trap",
    };

    private static InputAction SlotKey(AbilitySlot slot) => slot switch
    {
        AbilitySlot.Basic => InputAction.AbilityBasic,
        AbilitySlot.Quick => InputAction.AbilityQuick,
        AbilitySlot.Throw => InputAction.AbilityThrow,
        _                 => InputAction.AbilityTrap,
    };

    // ─── Build ────────────────────────────────────────────────────────────────

    protected override void Build()
    {
        var mode = GameModeRegistry.Find(MatchRules.GameModeKey);
        string modeName = mode != null ? mode.gameModeName : MatchRules.GameModeKey;
        Header("Choose Your Abilities", $"{modeName}  ·  {Nicify(MatchRules.Map)}  ·  One from each category");

        // Timer ring (top right) and Start Now for the host
        var timer = UIKit.Place(UIKit.Rect("Timer", Root), new Vector2(1f, 1f), new Vector2(-80f, -40f), new Vector2(140f, 140f));
        var back = timer.gameObject.AddComponent<Image>();
        back.sprite = UISprites.Circle;
        back.color = Theme.panel;
        ring = UIKit.Rect("Ring", timer).gameObject.AddComponent<Image>();
        UIKit.Stretch(ring.rectTransform, 8f, 8f, 8f, 8f);
        ring.sprite = UISprites.Ring;
        ring.type = Image.Type.Filled;
        ring.fillMethod = Image.FillMethod.Radial360;
        ring.fillOrigin = (int)Image.Origin360.Top;
        ring.fillClockwise = false;
        ring.color = Theme.primary;
        seconds = UIKit.Text(timer, "", 54f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Center);
        UIKit.Stretch(seconds.rectTransform);

        startButton = UIKit.Button(Root, "Start Now", UIKit.Style.Primary, StartMatch, 72f, 30f);
        UIKit.Place((RectTransform)startButton.transform, new Vector2(1f, 1f), new Vector2(-250f, -74f), new Vector2(260f, 72f));

        // Columns
        var columns = UIKit.Rect("Columns", Root);
        UIKit.Stretch(columns, 80f, 80f, 220f, 60f);
        var h = UIKit.HStack(columns, 24f, null, TextAnchor.UpperLeft, expandHeight: true);
        h.childForceExpandWidth = true;

        foreach (AbilitySlot slot in System.Enum.GetValues(typeof(AbilitySlot)))
            BuildColumn(columns, slot);
    }

    private void BuildColumn(Transform parent, AbilitySlot slot)
    {
        Color accent = SlotColor(slot);
        var column = UIKit.Card(parent, Theme.panel, 28f, 0.45f, SlotName(slot));
        UIKit.Size(column, flexWidth: 1f);

        // Coloured header: category, key, current pick
        var head = UIKit.Panel(column, accent, 28f, "Head");
        var headRect = head.rectTransform;
        headRect.anchorMin = new Vector2(0f, 1f);
        headRect.anchorMax = new Vector2(1f, 1f);
        headRect.pivot = new Vector2(0.5f, 1f);
        headRect.sizeDelta = new Vector2(0f, 120f);
        var title = UIKit.Text(head.transform, SlotName(slot), 44f, UIKit.Font.Display, Theme.textDark);
        UIKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -12f), new Vector2(280f, 56f));
        var key = UIKit.Chip(head.transform, Keybinds.Label(Keybinds.Get(SlotKey(slot))), Theme.textDark, Theme.text, 20f);
        UIKit.Place(key.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -22f), new Vector2(80f, 36f));
        var pick = UIKit.Text(head.transform, "", 22f, UIKit.Font.Bold, UIKit.WithAlpha(Theme.textDark, 0.8f));
        UIKit.Place(pick.rectTransform, new Vector2(0f, 0f), new Vector2(24f, 14f), new Vector2(380f, 34f));
        pick.textWrappingMode = TextWrappingModes.NoWrap;
        picked[slot] = pick;

        var area = UIKit.Rect("Abilities", column);
        UIKit.Stretch(area, 16f, 8f, 136f, 16f);
        var list = UIKit.ScrollList(area, 12f);

        foreach (string id in AbilityCatalog.InSlot(slot))
            if (AbilityCatalog.IsAvailable(id)) AddCard(list, slot, id, accent);
    }

    private void AddCard(Transform list, AbilitySlot slot, string id, Color accent)
    {
        var bg = UIKit.Panel(list, Theme.card, 20f, id);
        var v = UIKit.VStack(bg.transform, 8f, UIKit.Pad(18, 16), TextAnchor.UpperLeft);
        var outline = bg.gameObject.AddComponent<Outline>();
        outline.effectColor = accent;
        outline.effectDistance = new Vector2(4f, -4f);

        var top = UIKit.Rect("Top", bg.transform);
        UIKit.HStack(top, 14f, null, TextAnchor.MiddleLeft, expandHeight: false);
        UIKit.Size(top, height: 64f);

        var iconBack = UIKit.Panel(top, Theme.field, 16f, "Icon");
        UIKit.Size(iconBack, 64f, 64f);
        var sprite = AbilityIcons.Get(id);
        if (sprite != null)
        {
            var icon = UIKit.Rect("Image", iconBack.transform).gameObject.AddComponent<Image>();
            UIKit.Stretch(icon.rectTransform, 4f, 4f, 4f, 4f);
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }

        var name = UIKit.Text(top, AbilityCatalog.DisplayName(id), 28f, UIKit.Font.Display);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.enableAutoSizing = true;
        name.fontSizeMin = 20f;
        name.fontSizeMax = 28f;
        UIKit.Size(name, flexWidth: 1f);

        var check = UIKit.Rect("Check", top).gameObject.AddComponent<Image>();
        check.sprite = UISprites.Circle;
        check.color = accent;
        UIKit.Size(check, 36f, 36f);
        UIKit.CheckMark(check.transform, Theme.textDark, 24f);

        UIKit.Text(bg.transform, AbilityCatalog.Description(id), 19f, UIKit.Font.Body, Theme.textMuted);
        string stats = AbilityCatalog.Stats(id);
        if (!string.IsNullOrEmpty(stats)) UIKit.Text(bg.transform, stats, 17f, UIKit.Font.Bold, UIKit.WithAlpha(accent, 0.9f));

        var button = bg.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        var fx = bg.gameObject.AddComponent<UIButtonFx>();
        fx.Face = bg;
        fx.FaceColor = Theme.card;
        fx.LipDepth = 0f;
        fx.HoverScale = 1.02f;
        var card = new Card { Slot = slot, Id = id, Bg = bg, Outline = outline, Check = check.gameObject };
        button.onClick.AddListener(() => Choose(card));
        cards.Add(card);
    }

    // ─── Selection ────────────────────────────────────────────────────────────

    private void Start()
    {
        // Start everyone on their last picks (written now, so leaving the timer alone still equips them)
        foreach (AbilitySlot slot in System.Enum.GetValues(typeof(AbilitySlot)))
            AbilityLoadout.SetLocal(slot, AbilityLoadout.LastChoice(slot));
        RefreshCards();

        startButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        if (PhotonNetwork.IsMasterClient)
        {
            masterDeadline = PhotonNetwork.Time + selectionSeconds;
            NetProps.SetRoom(RoomKeys.PregameEndsAt, masterDeadline.Value);
        }
    }

    private void Choose(Card card)
    {
        AbilityLoadout.SetLocal(card.Slot, card.Id);
        RefreshCards();
    }

    private void RefreshCards()
    {
        foreach (var card in cards)
        {
            bool on = AbilityLoadout.LastChoice(card.Slot) == card.Id;
            card.Outline.enabled = on;
            card.Check.SetActive(on);
            card.Bg.GetComponent<UIButtonFx>().FaceColor = on ? Theme.cardHover : Theme.card;
            if (on) picked[card.Slot].text = AbilityCatalog.DisplayName(card.Id);
        }
    }

    // ─── Timer and start ──────────────────────────────────────────────────────

    private float SecondsLeft =>
        (float)((masterDeadline ?? NetProps.Room(RoomKeys.PregameEndsAt, PhotonNetwork.Time + selectionSeconds)) - PhotonNetwork.Time);

    private void Update()
    {
        float left = Mathf.Max(0f, SecondsLeft);
        seconds.text = Mathf.CeilToInt(left).ToString();
        ring.fillAmount = Mathf.Clamp01(left / selectionSeconds);
        ring.color = left <= 5f ? Color.Lerp(Theme.danger, Theme.primary, Mathf.PingPong(Time.unscaledTime * 3f, 1f)) : Theme.primary;

        if (masterDeadline.HasValue && left <= 0f) StartMatch();
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMaster)
    {
        startButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        if (PhotonNetwork.IsMasterClient)
            masterDeadline = NetProps.Room(RoomKeys.PregameEndsAt, PhotonNetwork.Time + selectionSeconds);
    }

    private void StartMatch()
    {
        if (!PhotonNetwork.IsMasterClient || loading) return;
        loading = true;
        startButton.interactable = false;

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
