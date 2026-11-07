using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pregame ability selection: four colour-coded columns (Basic, Quick, Throw, Trap), each a list of ability cards
/// (icon, name, description and stats from the player prefab). Click a card to equip it; the choice is written to
/// the player's properties at once and remembered for next time. Everyone presses Ready (the <see cref="PlayerKeys.Ready"/>
/// property); when every player is ready the host loads the map and everyone follows. The host can Force Start
/// without waiting for the rest.
/// Attach to one GameObject in the Pregame scene.
/// </summary>
public class PregameScreen : MenuScreen
{
    private sealed class Card
    {
        public AbilitySlot Slot;
        public string      Id;
        public Image       Bg;
        public Outline     Outline;
        public GameObject  Check;
    }

    private readonly List<Card> cards = new();
    private AbilityBrowser browser;
    private readonly Dictionary<AbilitySlot, TMP_Text> picked = new();
    private Button readyButton, unreadyButton, startButton;
    private TMP_Text readyCount, waitingFor;
    private bool loading;
    private bool localReady; // Our own state, ahead of Photon's echo

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

        // Top right: Ready / Not Ready, how many are ready and who is still choosing; Force Start for the host
        readyButton = UIKit.Button(Root, "Ready!", UIKit.Style.Primary, () => SetReady(true), 76f, 34f);
        UIKit.Place((RectTransform)readyButton.transform, new Vector2(1f, 1f), new Vector2(-180f, -78f), new Vector2(260f, 76f));
        unreadyButton = UIKit.Button(Root, "Not Ready", UIKit.Style.Secondary, () => SetReady(false), 76f, 30f);
        UIKit.Place((RectTransform)unreadyButton.transform, new Vector2(1f, 1f), new Vector2(-180f, -78f), new Vector2(260f, 76f));
        startButton = UIKit.Button(Root, "Force Start", UIKit.Style.Danger, StartMatch, 64f, 24f);
        UIKit.Place((RectTransform)startButton.transform, new Vector2(1f, 1f), new Vector2(-460f, -84f), new Vector2(220f, 64f));
        readyCount = UIKit.Text(Root, "", 26f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Right);
        UIKit.Place(readyCount.rectTransform, new Vector2(1f, 1f), new Vector2(-180f, -150f), new Vector2(520f, 34f));
        waitingFor = UIKit.Text(Root, "", 18f, UIKit.Font.Body, Theme.textMuted, TextAlignmentOptions.Right);
        UIKit.Place(waitingFor.rectTransform, new Vector2(1f, 1f), new Vector2(-180f, -182f), new Vector2(700f, 28f));
        waitingFor.textWrappingMode = TextWrappingModes.NoWrap;
        waitingFor.overflowMode = TextOverflowModes.Ellipsis;

        // Columns
        var columns = UIKit.Rect("Columns", Root);
        UIKit.Stretch(columns, 80f, 80f, 220f, 60f);
        var h = UIKit.HStack(columns, 24f, null, TextAnchor.UpperLeft, expandHeight: true);
        h.childForceExpandWidth = true;

        foreach (AbilitySlot slot in System.Enum.GetValues(typeof(AbilitySlot)))
            BuildColumn(columns, slot);

        browser = AbilityBrowser.Create(Root); // Last, so its pop-ups draw over the columns
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

        // ▶ watches the clip (the button takes the click, so the card isn't picked)
        var watch = UIKit.RoundButton(top, "", () => browser.ShowClip(id), 40f, UIKit.Style.Ghost);
        var watchLabel = UIKit.Label(watch);
        UISprites.AddPlayIcon(watchLabel.transform.parent, 18f, watchLabel.color); // The fonts have no \u25B6 glyph (it showed as a box)
        watch.gameObject.SetActive(AbilityClipPlayer.Has(id));

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

    private GameMode roleMode;
    private TMP_Text roleLabel;
    private float nextRoleCheck;

    // Your role for this match (dealt by the host as Pregame opens), beside the title
    private void BuildRole()
    {
        roleMode = GameModeRegistry.Create(MatchRules.GameModeKey);
        roleLabel = UIKit.Text(Root, "", 30f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Left);
        UIKit.Place(roleLabel.rectTransform, new Vector2(0f, 1f), new Vector2(900f, -70f), new Vector2(330f, 90f));
        roleLabel.textWrappingMode = TextWrappingModes.NoWrap;
        if (PhotonNetwork.IsMasterClient) roleMode.DealPregameRoles();
    }

    private void UpdateRole()
    {
        if (roleLabel == null || Time.unscaledTime < nextRoleCheck) return;
        nextRoleCheck = Time.unscaledTime + 0.3f;
        var role = PlayerRoles.Get(PhotonNetwork.LocalPlayer);
        bool show = role != PlayerRole.None && NetProps.Room(RoomKeys.RolesDealt, false);
        string hex = ColorUtility.ToHtmlStringRGB(PlayerRoles.Color(role));
        roleLabel.text = show ? $"<size=55%><color=#{ColorUtility.ToHtmlStringRGB(Theme.textMuted)}>YOUR ROLE</color></size>\n<color=#{hex}>{roleMode.RoleName(role)}</color>" : "";
    }

    private void Start()
    {
        BuildRole();
        // Start everyone on their last picks (written now, so a forced start still equips them)
        foreach (AbilitySlot slot in System.Enum.GetValues(typeof(AbilitySlot)))
            AbilityLoadout.SetLocal(slot, AbilityLoadout.LastChoice(slot));
        RefreshCards();
        SetReady(false);
        startButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
    }

    private void SetReady(bool ready)
    {
        if (ready && !localReady) UISound.Play(UISound.Kind.Confirm);
        localReady = ready;
        NetProps.SetLocal(PlayerKeys.Ready, ready);
        readyButton.gameObject.SetActive(!ready);
        unreadyButton.gameObject.SetActive(ready);
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

    // ─── Ready and start ──────────────────────────────────────────────────────

    private bool IsReady(Photon.Realtime.Player p) =>
        p.IsLocal ? localReady : NetProps.Of(p, PlayerKeys.Ready, false);

    private readonly List<string> waiting = new();

    private void Update()
    {
        UpdateRole();
        var players = PhotonNetwork.PlayerList;
        int ready = 0;
        waiting.Clear();
        foreach (var p in players)
        {
            if (IsReady(p)) ready++;
            else waiting.Add(string.IsNullOrEmpty(p.NickName) ? $"Player {p.ActorNumber}" : p.NickName);
        }
        readyCount.text = $"{ready} / {players.Length} ready";
        waitingFor.text = waiting.Count > 0 ? "Waiting for " + string.Join(", ", waiting) : "Starting!";

        // (a moment's grace after opening, so a ready flag left over from the last Pregame never starts it at once)
        if (PhotonNetwork.IsMasterClient && Time.timeSinceLevelLoad > 1.5f && players.Length > 0 && ready == players.Length) StartMatch();
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMaster) =>
        startButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);

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

        foreach (var p in PhotonNetwork.PlayerList) NetProps.SetPlayer(p, PlayerKeys.Ready, false); // Fresh next time
        PhotonNetwork.LoadLevel(map);
    }
}
