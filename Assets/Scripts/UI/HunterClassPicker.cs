using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The hunter's class screen, in the style of Pregame: three columns (Speed, Intel, Trapper), each headed by the class
/// and its passive, with its left-click tools as cards below; click a tool to take that class and tool
/// (<see cref="HunterClasses.Choose"/>, remembered). It opens by itself for hunters while the hiders hide (they're
/// frozen anyway) and for a hider who has just become a hunter, closes when the hunt starts, and can be put away
/// (Done) and brought back with <see cref="InputAction.HunterClass"/> (default B) until then. Frees the mouse
/// (PlayerCam). Its own canvas, built in code; added next to the GameModeManager.
/// </summary>
public class HunterClassPicker : MonoBehaviour
{
    private const float NewHunterWindow = 8f; // A tagged hider can pick for this long after becoming a hunter

    public static bool IsOpen { get; private set; }

    private GameObject screen;
    private TMP_Text subtitle;
    private readonly List<(HunterClasses.Class cls, HunterClasses.Tool tool, Image face, Outline outline, Image check)> cards = new();
    private bool dismissed;
    private PlayerRole lastRole;
    private MatchPhase lastPhase;
    private float becameHunterAt = -99f;

    private void Start()
    {
        Build();
        screen.SetActive(false);
        HunterClasses.Changed += Refresh;
    }

    private void OnDestroy()
    {
        HunterClasses.Changed -= Refresh;
        IsOpen = false;
    }

    // ─── Build ────────────────────────────────────────────────────────────────

    private void Build()
    {
        var theme = UITheme.Current;
        var canvas = UIKit.Canvas("Hunter Class Canvas", 6);
        canvas.transform.SetParent(transform, false);

        var dim = UIKit.Rect("Screen", canvas.transform).gameObject.AddComponent<Image>();
        UIKit.Stretch(dim.rectTransform);
        dim.color = UIKit.WithAlpha(theme.backgroundTop, 0.9f);
        screen = dim.gameObject;

        var title = UIKit.Text(dim.transform, "Choose Your Hunter Class", 72f, UIKit.Font.Display, theme.text, TextAlignmentOptions.TopLeft);
        UIKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(80f, -50f), new Vector2(1300f, 90f));
        subtitle = UIKit.Text(dim.transform, "", 26f, UIKit.Font.Body, theme.textMuted, TextAlignmentOptions.TopLeft);
        UIKit.Place(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(84f, -140f), new Vector2(1300f, 40f));

        var done = UIKit.Button(dim.transform, "Done", UIKit.Style.Primary, () => { dismissed = true; Close(); }, 70f, 30f);
        UIKit.Place((RectTransform)done.transform, new Vector2(1f, 1f), new Vector2(-80f, -60f), new Vector2(240f, 70f));

        var row = UIKit.Rect("Classes", dim.transform);
        row.anchorMin = new Vector2(0f, 0f);
        row.anchorMax = new Vector2(1f, 1f);
        row.offsetMin = new Vector2(80f, 60f);
        row.offsetMax = new Vector2(-80f, -210f);
        UIKit.HStack(row, 32f, null, TextAnchor.UpperCenter);
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.childForceExpandWidth = true;
        layout.childControlWidth = true;

        foreach (var cls in HunterClasses.All) BuildColumn(row, cls, theme);
    }

    private sealed class Column
    {
        public HunterClasses.Class Cls;
        public Image Card, Head, PassiveCard;
        public Outline Border;
        public CanvasGroup Group;
        public GameObject Selected;
    }

    private readonly List<Column> columns = new();

    private void BuildColumn(Transform row, HunterClasses.Class cls, UITheme theme)
    {
        var card = UIKit.Card(row, theme.panel, 28f, 0.4f, cls.Name);
        UIKit.Size(card, 1f, flexWidth: 1f); // Equal columns, whatever their text
        UIKit.VStack(card, 12f, UIKit.Pad(18, 18), TextAnchor.UpperCenter);
        var bg = UIKit.CardBg(card); // The card's background (the container itself has no image)
        var col = new Column
        {
            Cls = cls,
            Card = bg,
            Border = bg.gameObject.AddComponent<Outline>(),
            Group = card.gameObject.AddComponent<CanvasGroup>(),
        };
        col.Border.effectColor = cls.Color;

        // Header: the class in its colour; clicking it takes the class (with its first tool, or the one you had)
        var head = UIKit.Panel(card, cls.Color, 22f, "Header");
        UIKit.Size(head, height: 112f);
        var name = UIKit.Text(head.transform, cls.Name, 54f, UIKit.Font.Display, theme.textDark, TextAlignmentOptions.TopLeft);
        UIKit.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -10f), new Vector2(360f, 60f));
        var blurb = UIKit.Text(head.transform, cls.Blurb, 21f, UIKit.Font.Bold, UIKit.WithAlpha(theme.textDark, 0.75f), TextAlignmentOptions.TopLeft);
        UIKit.Place(blurb.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -70f), new Vector2(360f, 30f));
        var selected = UIKit.Chip(head.transform, "SELECTED", theme.textDark, cls.Color, 18f);
        UIKit.Place(selected.rectTransform, new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(128f, 34f));
        col.Selected = selected.gameObject;
        var headButton = head.gameObject.AddComponent<Button>();
        headButton.transition = Selectable.Transition.None;
        headButton.onClick.AddListener(() =>
        {
            var keep = HunterClasses.Chosen == cls ? HunterClasses.ChosenTool : cls.Tools[0];
            HunterClasses.Choose(cls, keep);
        });
        col.Head = head;

        // The passive on its own: it comes with the class, nothing to press
        var passive = UIKit.Panel(card, theme.field, 18f, "Passive");
        UIKit.Size(passive, height: 104f);
        var passiveBorder = passive.gameObject.AddComponent<Outline>();
        passiveBorder.effectColor = UIKit.WithAlpha(cls.Color, 0.8f);
        passiveBorder.effectDistance = new Vector2(2f, -2f);
        var passiveLabel = UIKit.Text(passive.transform, "PASSIVE  ·  ALWAYS ON WITH THIS CLASS", 15f, UIKit.Font.Bold, cls.Color, TextAlignmentOptions.TopLeft);
        UIKit.Place(passiveLabel.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -12f), new Vector2(420f, 22f));
        var passiveText = UIKit.Text(passive.transform, cls.Passive, 23f, UIKit.Font.Bold, theme.text, TextAlignmentOptions.TopLeft);
        UIKit.Place(passiveText.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -38f), new Vector2(430f, 60f));
        col.PassiveCard = passive;

        var label = UIKit.Text(card, "LEFT-CLICK TOOL  ·  PICK ONE", 16f, UIKit.Font.Bold, theme.textMuted, TextAlignmentOptions.Left);
        UIKit.Size(label, height: 22f);

        foreach (var tool in cls.Tools)
        {
            var face = UIKit.Panel(card, theme.card, 20f, tool.Name);
            UIKit.Size(face, height: 128f);
            UIKit.VStack(face.transform, 4f, UIKit.Pad(18, 12), TextAnchor.UpperLeft);
            var outline = face.gameObject.AddComponent<Outline>();
            outline.effectColor = cls.Color;
            outline.effectDistance = new Vector2(3f, -3f);
            outline.enabled = false; // An Outline draws even at zero size: switched on for the chosen tool

            var top = UIKit.Rect("Top", face.transform);
            UIKit.HStack(top, 10f, null, TextAnchor.MiddleLeft, expandHeight: false);
            UIKit.Size(top, height: 38f);
            var title = UIKit.OneLine(UIKit.Text(top, tool.Name, 28f, UIKit.Font.Display, theme.text));
            UIKit.Size(title, flexWidth: 1f);
            string chip = tool.MaxOut > 0 ? $"{tool.Cooldown:0}s  ·  up to {tool.MaxOut}" : $"{tool.Cooldown:0}s";
            UIKit.Chip(top, chip, UIKit.WithAlpha(cls.Color, 0.9f), theme.textDark, 16f);
            var check = UIKit.Rect("Check", top).gameObject.AddComponent<Image>();
            check.sprite = UISprites.Circle;
            check.color = cls.Color;
            UIKit.Size(check, 28f, 28f);

            var desc = UIKit.Text(face.transform, tool.Description, 18f, UIKit.Font.Body, theme.textMuted);
            UIKit.Size(desc, height: 64f);

            var button = face.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var fx = face.gameObject.AddComponent<UIButtonFx>();
            fx.HoverScale = 1.03f;
            var (c, t) = (cls, tool);
            button.onClick.AddListener(() => HunterClasses.Choose(c, t));
            cards.Add((cls, tool, face, outline, check));
        }
        UIKit.Spacer(card);
        columns.Add(col);
    }

    // The chosen class stands out (full colour, a thick border, SELECTED, its passive lit); the others step back
    private void Refresh()
    {
        var chosen = HunterClasses.Chosen;
        var tool = HunterClasses.ChosenTool;
        var theme = UITheme.Current;
        foreach (var (cls, t, face, outline, check) in cards)
        {
            bool on = cls == chosen && t == tool;
            face.color = on ? theme.cardHover : theme.card;
            outline.enabled = on;
            check.enabled = on;
        }
        foreach (var col in columns)
        {
            bool on = col.Cls == chosen;
            col.Group.alpha = on ? 1f : 0.55f;
            col.Border.effectDistance = new Vector2(6f, -6f);
            col.Border.enabled = on;
            col.Card.color = on ? Color.Lerp(theme.panel, col.Cls.Color, 0.18f) : theme.panel;
            col.Head.color = on ? col.Cls.Color : UIKit.WithAlpha(col.Cls.Color, 0.5f);
            col.PassiveCard.color = on ? Color.Lerp(theme.field, col.Cls.Color, 0.25f) : theme.field;
            col.Selected.SetActive(on);
        }
    }

    // ─── When it shows ────────────────────────────────────────────────────────

    private void Update()
    {
        var manager = GameModeManager.Instance;
        var mode = manager != null ? manager.Mode : null;
        var local = Player.Local;
        if (mode == null || !mode.HasHunterClasses || local == null) { Close(); return; }

        var phase = manager.Phase;
        var role = local.Role;
        if (role == PlayerRole.Hunter && lastRole != PlayerRole.Hunter && phase == MatchPhase.Active)
        {
            becameHunterAt = Time.time; // Just tagged: a few seconds to pick
            dismissed = false;
        }
        if (phase != lastPhase) dismissed = false;
        lastRole = role;
        lastPhase = phase;

        bool setup = phase == MatchPhase.Countdown || phase == MatchPhase.Hiding;
        bool fresh = phase == MatchPhase.Active && Time.time - becameHunterAt < NewHunterWindow;
        bool allowed = role == PlayerRole.Hunter && (setup || fresh) && !PauseMenuManager.IsPaused;
        if (allowed && Input.GetKeyDown(Keybinds.Get(InputAction.HunterClass))) dismissed = !dismissed;

        if (allowed && !dismissed) Open(); else Close();
        if (!IsOpen) return;

        float left = fresh ? NewHunterWindow - (Time.time - becameHunterAt) : manager.Clock.Remaining;
        string when = phase == MatchPhase.Active ? $"Back in the hunt in {Mathf.CeilToInt(left)}s" : $"The hunt starts in {Mathf.CeilToInt(left + (phase == MatchPhase.Countdown ? MatchRules.HideTime : 0f))}s";
        subtitle.text = $"{when}  ·  {Keybinds.Label(Keybinds.Get(InputAction.HunterTool))} uses your tool  ·  {Keybinds.Label(Keybinds.Get(InputAction.HunterClass))} to show or hide this";
    }

    private void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        screen.SetActive(true);
        Refresh();
    }

    private void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        screen.SetActive(false);
    }
}
