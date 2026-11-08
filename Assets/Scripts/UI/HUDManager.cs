using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// The in-match HUD, built in code with the UI kit (same look as the menus):
///   - top left: role card (HUNTER / HIDER / DEFENDER / PRESIDENT, or the mode name) with the objective;
///   - top centre: the match clock (phase label, time, PAUSED; the last 10 s pulse red) and the round;
///   - top right: the local score (modes with scores), with the Tab hint;
///   - centre: big 3-2-1-GO countdowns and pop-in banners;
///   - bottom centre: the ability bar: one card per usable ability with its icon, key, a radial cooldown sweep
///     with seconds, uses left, and a meter for Basic abilities;
///   - a crosshair and the F3 debug readout.
/// Initialised by Spawner when the local player spawns (<see cref="Initialize"/>). Other HUD parts (combat,
/// scores, minimap) have their own canvases.
/// </summary>
public class HUDManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    private static HUDManager _instance;
    public static HUDManager Instance
    {
        get
        {
            if (_instance == null) _instance = FindFirstObjectByType<HUDManager>(FindObjectsInactive.Include);
            return _instance;
        }
    }

    private const KeyCode DebugToggleKey = KeyCode.F3;

    private UITheme Theme => UITheme.Current;
    private Player  player;
    private RectTransform root;
    private bool    built;

    // Top
    private RectTransform roleCard, scoreCard, clockCard;
    private Image    roleAccent;
    private TMP_Text roleText, objectiveText, phaseText, clockText, roundText, scoreText;
    private GameObject pausedChip;

    // Centre
    private RectTransform banner;
    private TMP_Text bannerText;
    private float bannerUntil, bannerStart = -10f;
    private TMP_Text countdownText;
    private int   lastCountdown = -1;
    private float countdownPop = -10f;
    private float goUntil;

    // Abilities
    private RectTransform bar;
    private sealed class Slot
    {
        public RectTransform Root;
        public Ability  Ability;
        public Image    Icon, Sweep, Accent, Meter, MeterBack;
        public TMP_Text Key, Name, Seconds, Uses;
        public GameObject UsesBadge;
        public int      LastCharges = -1;
        public float    ReadyPop = -10f;
    }
    private readonly List<Slot> slots = new();

    // Debug
    private TMP_Text debugText;
    private bool showDebug;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        _instance = this;
        Build();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        if (_instance == this) _instance = null;
    }

    public void Initialize(Player localPlayer)
    {
        Unsubscribe();
        player = localPlayer;
        root.gameObject.SetActive(true);
        RefreshAbilities();

        player.Abilities.LoadoutChanged += RefreshAbilities;
        player.Caught                   += HandleCaught;
        player.Respawned                += HandleRespawned;
        Keybinds.Changed                += RefreshAbilities;

        var manager = GameModeManager.Instance;
        if (manager != null)
        {
            manager.PhaseChanged += HandlePhaseChanged;
            manager.PlayerTagged += HandlePlayerTagged;
        }
    }

    private void Unsubscribe()
    {
        Keybinds.Changed -= RefreshAbilities;
        if (player != null)
        {
            player.Abilities.LoadoutChanged -= RefreshAbilities;
            player.Caught                   -= HandleCaught;
            player.Respawned                -= HandleRespawned;
        }
        var manager = GameModeManager.Instance;
        if (manager != null)
        {
            manager.PhaseChanged -= HandlePhaseChanged;
            manager.PlayerTagged -= HandlePlayerTagged;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(DebugToggleKey)) { showDebug = !showDebug; debugText.gameObject.SetActive(showDebug); }

        UpdateTop();
        UpdateCountdown();
        UpdateBanner();
        UpdatePrompt();
        if (player == null) return;
        UpdateAbilities();
        if (showDebug) UpdateDebug();
    }

    // ─── Build ────────────────────────────────────────────────────────────────

    private void Build()
    {
        if (built) return;
        built = true;
        var canvas = UIKit.Canvas("HUD Canvas", 0);
        canvas.transform.SetParent(transform, false);
        root = (RectTransform)canvas.transform;

        BuildCrosshair();
        BuildRoleCard();
        BuildClock();
        BuildScore();
        BuildCentre();
        BuildAbilityBar();
        Minimap.Create(root);
        EffectTray.Create(root); // Running effects (donuts), right side
        SpeedFx.Create(root);    // Speed lines and wind when moving fast
        TagFeel.Create(root);    // TAGGED! / +1 TAG stamps, heartbeat when a hunter is near
        EmoteMenu.Create(root);  // T: emotes

        debugText = UIKit.Text(root, "", 20f, UIKit.Font.Bold, Theme.text, TextAlignmentOptions.TopLeft, "Debug");
        UIKit.Place(debugText.rectTransform, new Vector2(0f, 1f), new Vector2(32f, -170f), new Vector2(420f, 220f));
        debugText.gameObject.SetActive(false);
    }

    // ─── Key prompt ("F  Withdraw camera") ────────────────────────────────────

    private RectTransform promptCard;
    private TMP_Text      promptKey, promptText;
    private int           promptFrame = -1;

    /// <summary>
    /// Shows "[key]  text" under the crosshair for this frame; call it every frame while the prompt applies
    /// (it hides itself the frame nobody asks).
    /// </summary>
    public static void Prompt(KeyCode key, string text)
    {
        var hud = Instance;
        if (hud == null) return;
        if (hud.promptCard == null) hud.BuildPrompt();
        hud.promptFrame = Time.frameCount;
        hud.promptKey.text  = Keybinds.Label(key);
        hud.promptText.text = text;
    }

    private void BuildPrompt()
    {
        promptCard = UIKit.Card(root, UIKit.WithAlpha(Theme.panel, 0.9f), 18f, 0.4f, "Prompt");
        UIKit.Place(promptCard, new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(380f, 56f));
        promptCard.pivot = new Vector2(0.5f, 0.5f);
        var chip = UIKit.Panel(promptCard, Theme.primary, 10f, "Key");
        UIKit.Place(chip.rectTransform, new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(48f, 40f));
        chip.rectTransform.pivot = new Vector2(0f, 0.5f);
        chip.rectTransform.anchoredPosition = new Vector2(12f, 0f);
        promptKey = UIKit.Text(chip.transform, "", 24f, UIKit.Font.Display, Theme.textDark, TextAlignmentOptions.Center);
        UIKit.Stretch(promptKey.rectTransform);
        promptText = UIKit.OneLine(UIKit.Text(promptCard, "", 24f, UIKit.Font.Bold, Theme.text, TextAlignmentOptions.MidlineLeft));
        UIKit.Stretch(promptText.rectTransform, 74f, 16f, 0f, 0f);
        foreach (var g in promptCard.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
    }

    private void UpdatePrompt()
    {
        if (promptCard == null) return;
        bool show = promptFrame >= Time.frameCount - 1 && !PauseMenuManager.IsPaused;
        if (promptCard.gameObject.activeSelf != show) promptCard.gameObject.SetActive(show);
        if (show) promptCard.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.025f);
    }

    private void BuildCrosshair() => Crosshair.Create(root); // Style, colour and size from Settings → Gameplay

    private void BuildRoleCard()
    {
        roleCard = UIKit.Card(root, UIKit.WithAlpha(Theme.panel, 0.88f), 22f, 0.4f, "Role");
        UIKit.Place(roleCard, new Vector2(0f, 1f), new Vector2(32f, -24f), new Vector2(460f, 112f));
        roleAccent = UIKit.Panel(roleCard, Theme.cyan, 6f, "Accent");
        roleAccent.rectTransform.anchorMin = new Vector2(0f, 0f);
        roleAccent.rectTransform.anchorMax = new Vector2(0f, 1f);
        roleAccent.rectTransform.pivot = new Vector2(0f, 0.5f);
        roleAccent.rectTransform.sizeDelta = new Vector2(10f, -24f);
        roleAccent.rectTransform.anchoredPosition = new Vector2(14f, 0f);

        roleText = UIKit.OneLine(UIKit.Text(roleCard, "", 40f, UIKit.Font.Display));
        UIKit.Place(roleText.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -10f), new Vector2(400f, 50f));
        objectiveText = UIKit.Text(roleCard, "", 19f, UIKit.Font.Body, Theme.textMuted);
        UIKit.Place(objectiveText.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -60f), new Vector2(400f, 46f));
    }

    private void BuildClock()
    {
        clockCard = UIKit.Card(root, UIKit.WithAlpha(Theme.panel, 0.88f), 26f, 0.4f, "Clock");
        UIKit.Place(clockCard, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(300f, 112f));
        phaseText = UIKit.OneLine(UIKit.Text(clockCard, "", 18f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Center));
        UIKit.Place(phaseText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(280f, 26f));
        clockText = UIKit.Text(clockCard, "", 58f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Center);
        UIKit.Place(clockText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(280f, 72f));

        roundText = UIKit.Text(root, "", 18f, UIKit.Font.Bold, Theme.primary, TextAlignmentOptions.Center, "Round");
        UIKit.Place(roundText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -142f), new Vector2(300f, 26f));

        var chip = UIKit.Chip(root, "PAUSED", Theme.danger, Theme.text, 18f);
        UIKit.Place(chip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -142f), new Vector2(120f, 30f));
        pausedChip = chip.gameObject;
    }

    private void BuildScore()
    {
        scoreCard = UIKit.Card(root, UIKit.WithAlpha(Theme.panel, 0.88f), 22f, 0.4f, "Score");
        UIKit.Place(scoreCard, new Vector2(1f, 1f), new Vector2(-32f, -24f), new Vector2(240f, 112f));
        var label = UIKit.Text(scoreCard, "SCORE", 18f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Center);
        UIKit.Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(220f, 26f));
        scoreText = UIKit.Text(scoreCard, "", 52f, UIKit.Font.Display, Theme.primary, TextAlignmentOptions.Center);
        UIKit.Place(scoreText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(220f, 70f));
        var hint = UIKit.Text(root, $"Hold {Keybinds.Label(Keybinds.Get(InputAction.Scores))} for scores", 16f, UIKit.Font.Body,
            Theme.textMuted, TextAlignmentOptions.Right, "Scores Hint");
        UIKit.Place(hint.rectTransform, new Vector2(1f, 1f), new Vector2(-36f, -142f), new Vector2(300f, 24f));
        hint.transform.SetParent(scoreCard, true);
    }

    private void BuildCentre()
    {
        countdownText = UIKit.Text(root, "", 220f, UIKit.Font.Display, Theme.primary, TextAlignmentOptions.Center, "Countdown");
        UIKit.Place(countdownText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(900f, 260f));
        countdownText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        var shadow = countdownText.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        shadow.effectDistance = new Vector2(0f, -10f);

        banner = UIKit.Card(root, Theme.secondary, 28f, 0.5f, "Banner");
        UIKit.Place(banner, new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(900f, 96f));
        banner.pivot = new Vector2(0.5f, 0.5f);
        bannerText = UIKit.Text(banner, "", 46f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Center);
        UIKit.Stretch(bannerText.rectTransform, 24f, 24f, 0f, 0f);
        bannerText.enableAutoSizing = true;
        bannerText.fontSizeMin = 24f;
        bannerText.fontSizeMax = 46f;
        banner.gameObject.SetActive(false);
    }

    private void BuildAbilityBar()
    {
        bar = UIKit.Rect("Abilities", root);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, 28f);
        var h = UIKit.HStack(bar, 16f, null, TextAnchor.LowerCenter, expandHeight: false);
        h.childControlWidth = h.childControlHeight = true;
        var fit = bar.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    // ─── Abilities ────────────────────────────────────────────────────────────

    private Color SlotColor(AbilitySlot slot) => slot switch
    {
        AbilitySlot.Basic => Theme.cyan,
        AbilitySlot.Quick => Theme.primary,
        AbilitySlot.Throw => Theme.pink,
        _                 => Theme.lime,
    };

    private void RefreshAbilities()
    {
        if (player == null) return;
        foreach (var s in slots) if (s.Root != null) Destroy(s.Root.gameObject);
        slots.Clear();

        foreach (var (ability, key) in player.Abilities.Usable())
            slots.Add(BuildSlot(ability, key));
        UIKit.Relayout(bar);
    }

    private Slot BuildSlot(Ability ability, KeyCode key)
    {
        var s = new Slot { Ability = ability };
        Color accent = SlotColor(ability.Slot);

        var column = UIKit.Rect(ability.Id, bar);
        UIKit.VStack(column, 6f, null, TextAnchor.UpperCenter, expandWidth: false);
        UIKit.Size(column, 116f);
        s.Root = column;

        var card = UIKit.Card(column, UIKit.WithAlpha(Theme.panel, 0.92f), 22f, 0.4f, "Card");
        UIKit.Size(card, 108f, 108f);
        s.Accent = UIKit.CardBg(card);
        var ring = s.Accent.gameObject.AddComponent<Outline>();
        ring.effectColor = UIKit.WithAlpha(accent, 0.9f);
        ring.effectDistance = new Vector2(3f, -3f);

        s.Icon = UIKit.Rect("Icon", card).gameObject.AddComponent<Image>();
        UIKit.Stretch(s.Icon.rectTransform, 12f, 12f, 12f, 12f);
        s.Icon.sprite = AbilityIcons.Get(ability.Id);
        s.Icon.preserveAspect = true;
        s.Icon.raycastTarget = false;
        s.Icon.enabled = s.Icon.sprite != null;
        if (s.Icon.sprite == null)
        {
            var initials = UIKit.Text(card, AbilityCatalog.DisplayName(ability.Id).Substring(0, 2), 40f, UIKit.Font.Display, accent, TextAlignmentOptions.Center);
            UIKit.Stretch(initials.rectTransform);
        }

        // Cooldown: a dark radial sweep that unwinds, with the seconds left
        s.Sweep = UIKit.Rect("Sweep", card).gameObject.AddComponent<Image>();
        UIKit.Stretch(s.Sweep.rectTransform, 4f, 4f, 4f, 4f);
        s.Sweep.sprite = UISprites.Circle;
        s.Sweep.type = Image.Type.Filled;
        s.Sweep.fillMethod = Image.FillMethod.Radial360;
        s.Sweep.fillOrigin = (int)Image.Origin360.Top;
        s.Sweep.fillClockwise = false;
        s.Sweep.color = new Color(0.05f, 0.03f, 0.12f, 0.72f);
        s.Sweep.raycastTarget = false;
        s.Seconds = UIKit.Text(card, "", 34f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Center, "Seconds");
        UIKit.Stretch(s.Seconds.rectTransform);

        // Basic abilities: a meter along the bottom (how much is left)
        if (ability is BasicAbility)
        {
            s.MeterBack = UIKit.Panel(card, UIKit.WithAlpha(Color.black, 0.5f), 4f, "Meter");
            s.MeterBack.rectTransform.anchorMin = new Vector2(0f, 0f);
            s.MeterBack.rectTransform.anchorMax = new Vector2(1f, 0f);
            s.MeterBack.rectTransform.pivot = new Vector2(0.5f, 0f);
            s.MeterBack.rectTransform.sizeDelta = new Vector2(-24f, 8f);
            s.MeterBack.rectTransform.anchoredPosition = new Vector2(0f, 8f);
            s.Meter = UIKit.Panel(s.MeterBack.transform, accent, 4f, "Fill");
            s.Meter.rectTransform.anchorMin = Vector2.zero;
            s.Meter.rectTransform.anchorMax = Vector2.one;
            s.Meter.rectTransform.sizeDelta = Vector2.zero;
        }

        // Key chip (top left) and uses badge (bottom right), hanging off the card
        var keyChip = UIKit.Chip(card, Keybinds.Label(key), accent, Theme.textDark, 18f);
        UIKit.Place(keyChip.rectTransform, new Vector2(0f, 1f), new Vector2(-8f, 10f), new Vector2(60f, 30f));
        keyChip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        s.Key = keyChip.GetComponentInChildren<TMP_Text>();

        var badge = UIKit.Rect("Uses", card).gameObject.AddComponent<Image>();
        badge.sprite = UISprites.Circle;
        badge.color = Theme.primary;
        badge.raycastTarget = false;
        UIKit.Place(badge.rectTransform, new Vector2(1f, 0f), new Vector2(10f, -10f), new Vector2(36f, 36f));
        s.Uses = UIKit.Text(badge.transform, "", 20f, UIKit.Font.Display, Theme.textDark, TextAlignmentOptions.Center);
        UIKit.Stretch(s.Uses.rectTransform);
        s.UsesBadge = badge.gameObject;

        s.Name = UIKit.OneLine(UIKit.Text(column, AbilityCatalog.DisplayName(ability.Id), 16f, UIKit.Font.Bold, Theme.text, TextAlignmentOptions.Center));
        UIKit.Size(s.Name, 116f, 22f);
        return s;
    }

    private void UpdateAbilities()
    {
        float now = Time.unscaledTime;
        foreach (var s in slots)
        {
            var a = s.Ability;
            if (a == null || s.Root == null) continue;

            if (a is BasicAbility)
            {
                float fill = a.Readiness;
                s.Meter.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
                s.MeterBack.gameObject.SetActive(fill < 0.999f);
                s.Sweep.fillAmount = 0f;
                s.Seconds.text = "";
                s.UsesBadge.SetActive(false);
                s.Icon.color = Color.white;
                continue;
            }

            int charges = a.Charges;
            float refill = a.NextChargeIn;
            float duration = Mathf.Max(0.01f, a.CooldownDuration);
            bool empty = charges == 0;

            // The sweep shows the next use coming back; it fills the card only when none are left
            s.Sweep.fillAmount = refill > 0f ? Mathf.Clamp01(refill / duration) : 0f;
            s.Sweep.color = new Color(0.05f, 0.03f, 0.12f, empty ? 0.75f : 0.35f);
            s.Seconds.text = empty && refill > 0f ? (refill >= 10f ? $"{refill:0}" : $"{refill:0.0}") : "";
            s.Icon.color = empty ? new Color(1f, 1f, 1f, 0.45f) : Color.white;

            bool multiple = a.MaxCharges > 1;
            s.UsesBadge.SetActive(multiple);
            if (multiple) s.Uses.text = charges.ToString();

            // A pop when a use comes back
            if (s.LastCharges >= 0 && charges > s.LastCharges) s.ReadyPop = now;
            s.LastCharges = charges;
            float pop = Mathf.Clamp01(1f - (now - s.ReadyPop) / 0.25f);
            float scale = 1f + 0.12f * Mathf.Sin(pop * Mathf.PI);
            s.Root.GetChild(0).localScale = new Vector3(scale, scale, 1f);
        }
    }

    // ─── Top ──────────────────────────────────────────────────────────────────

    private void UpdateTop()
    {
        var manager = GameModeManager.Instance;
        var mode = manager != null ? manager.Mode : null;
        var phase = manager != null ? manager.Phase : MatchPhase.Waiting;

        // Role card
        bool roles = mode != null && mode.UsesRoles && player != null && player.Role != PlayerRole.None;
        string title = roles ? mode.RoleName(player.Role) : mode != null ? mode.DisplayName.ToUpperInvariant() : "";
        string objective = mode != null && player != null ? mode.GetObjective(player.Role, phase) : "";
        Color color = !roles ? Theme.primary : player.Role switch
        {
            PlayerRole.Hunter    => Theme.danger,
            PlayerRole.President => Theme.primary,
            PlayerRole.TeamRed or PlayerRole.TeamBlue or PlayerRole.Ghost => PlayerRoles.Color(player.Role),
            _                    => Theme.cyan,
        };
        roleCard.gameObject.SetActive(!string.IsNullOrEmpty(title));
        roleText.text = title;
        roleText.color = color;
        roleAccent.color = color;
        objectiveText.text = objective;
        float height = string.IsNullOrEmpty(objective) ? 70f : 112f; // Just the title when there's nothing to do
        if (!Mathf.Approximately(roleCard.sizeDelta.y, height)) roleCard.sizeDelta = new Vector2(roleCard.sizeDelta.x, height);

        // Clock
        var clock = manager != null ? manager.Clock : null;
        bool showClock = clock != null && phase != MatchPhase.Waiting && mode != null && mode.ShowsTimer;
        clockCard.gameObject.SetActive(showClock);
        if (showClock)
        {
            float time = clock.DisplayTime;
            clockText.text = MatchTimerController.FormatTime(time, roundUp: clock.HasLimit);
            phaseText.text = (manager.PhaseLabel ?? "").ToUpperInvariant();
            bool urgent = clock.HasLimit && time <= 10f && !clock.IsPaused && phase == MatchPhase.Active;
            clockText.color = urgent ? Color.Lerp(Theme.danger, Color.white, Mathf.PingPong(Time.unscaledTime * 2f, 1f)) : Theme.text;

            // The last ten seconds tick (higher as it runs out) and the clock thumps on each one
            int second = urgent ? Mathf.CeilToInt(time) : -1;
            if (urgent && second != lastTick && second > 0)
            {
                Sfx.Here(SfxKind.Tick, 0.7f, Mathf.Lerp(1.3f, 0.9f, second / 10f));
                clockPop = Time.unscaledTime;
            }
            lastTick = second;
            float pop = Mathf.Clamp01(1f - (Time.unscaledTime - clockPop) / 0.2f);
            clockCard.localScale = Vector3.one * (1f + 0.08f * pop);

            // The music speeds up near the end
            bool closing = clock.HasLimit && !clock.IsPaused && phase == MatchPhase.Active;
            if (AudioManager.Instance != null)
                AudioManager.Instance.SetTempo(closing && time <= 10f ? 1.12f : closing && time <= 30f ? 1.05f : 1f);
        }
        else if (AudioManager.Instance != null) AudioManager.Instance.SetTempo(1f);
        bool paused = showClock && clock.IsPaused;
        pausedChip.SetActive(paused);
        string round = showClock && !paused && mode.HasRounds && manager.Round > 0 ? $"ROUND {manager.Round}" : "";
        var modifier = RoundModifiers.Current;
        bool modLive = modifier != null && (phase == MatchPhase.Countdown || phase == MatchPhase.Hiding || phase == MatchPhase.Active);
        roundText.text = modLive ? (round.Length > 0 ? $"{round}  ·  " : "") + $"<color=#FF8A3D>{modifier.Name.ToUpperInvariant()}</color>" : round;

        // Score
        bool showScore = mode != null && mode.ShowsScores && player != null;
        scoreCard.gameObject.SetActive(showScore);
        if (showScore) UpdateScore(mode, manager);
    }

    // ─── Score pop and leader chip ────────────────────────────────────────────

    private float lastScore = float.NaN, scorePopAt = -10f, clockPop = -10f;
    private int   lastTick = -1;
    private TMP_Text scoreGain;
    private GameObject leaderChip;

    private void UpdateScore(GameMode mode, GameModeManager manager)
    {
        float score = manager.Scores.GetLive(player.Owner);
        scoreText.text = mode.TeamScoreText ?? mode.FormatScore(score); // Team modes: both teams' totals

        // A real jump (a kill, a tag), not a timer ticking up: the card bumps and "+1" floats off it
        if (!float.IsNaN(lastScore) && Mathf.Abs(score - lastScore) >= 0.99f && manager.Phase == MatchPhase.Active)
        {
            scorePopAt = Time.unscaledTime;
            float gain = score - lastScore;
            if (scoreGain == null)
            {
                scoreGain = UIKit.Text(scoreCard, "", 40f, UIKit.Font.Display, Theme.lime, TextAlignmentOptions.Center, "Gain");
                UIKit.Place(scoreGain.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -40f), new Vector2(200f, 50f));
                scoreGain.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
            }
            scoreGain.text  = gain > 0f ? $"+{mode.FormatScore(gain)}" : mode.FormatScore(gain);
            scoreGain.color = gain > 0f ? Theme.lime : Theme.danger;
            if (gain > 0f) Sfx.Here(SfxKind.Pop, 0.5f, 1.4f);
        }
        lastScore = score;

        float age = Time.unscaledTime - scorePopAt;
        float bump = age < 0.3f ? Mathf.Sin(age / 0.3f * Mathf.PI) : 0f;
        scoreCard.localScale = Vector3.one * (1f + 0.15f * bump);
        if (scoreGain != null)
        {
            bool on = age < 1.1f;
            scoreGain.gameObject.SetActive(on);
            if (on)
            {
                scoreGain.rectTransform.anchoredPosition = new Vector2(0f, -40f - age * 50f);
                scoreGain.alpha = Mathf.Clamp01((1.1f - age) / 0.3f);
            }
        }

        // In the lead (modes that crown a leader): a gold chip under the score
        bool leading = mode.CrownsLeader && manager.IsLeader(player.Owner);
        if (leaderChip == null && leading)
        {
            var chip = UIKit.Chip(scoreCard, "LEADER", Theme.primary, Theme.textDark, 16f);
            UIKit.Place(chip.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -16f), new Vector2(110f, 26f));
            leaderChip = chip.gameObject;
        }
        if (leaderChip != null && leaderChip.activeSelf != leading) leaderChip.SetActive(leading);
    }

    // ─── Countdowns ───────────────────────────────────────────────────────────
    // The pre-round countdown, and the last 5 s of the hide phase ("hunters released in…"), as big popping numbers

    private void UpdateCountdown()
    {
        var manager = GameModeManager.Instance;
        int number = -1;
        if (manager != null && manager.Clock != null && manager.Clock.HasLimit && !manager.Clock.IsPaused)
        {
            float left = manager.Clock.Remaining;
            // Long pre-rounds (Search & Destroy's 40 s) only pop the last 5 s, like the hide phase
            bool counting = (manager.Phase == MatchPhase.Countdown && (left <= 5f || manager.Clock.Duration <= 10f))
                            || (manager.Phase == MatchPhase.Hiding && left <= 5f);
            if (counting && left > 0f) number = Mathf.CeilToInt(left);
        }

        float now = Time.unscaledTime;
        if (number != lastCountdown && number > 0) { countdownPop = now; Sfx.Here(SfxKind.Hit, 0.7f, number <= 3 ? 0.5f : 0.4f); }
        lastCountdown = number;

        string text = number > 0 ? number.ToString() : now < goUntil ? "GO!" : "";
        countdownText.text = text;
        if (text.Length == 0) return;

        float t = Mathf.Clamp01((now - (number > 0 ? countdownPop : goUntil - 0.9f)) / 0.35f);
        float scale = Mathf.Lerp(1.6f, 1f, 1f - Mathf.Pow(1f - t, 3f));
        countdownText.rectTransform.localScale = new Vector3(scale, scale, 1f);
        countdownText.color = number > 0 ? Theme.primary : Theme.lime;
        countdownText.alpha = number > 0 ? 1f : Mathf.Clamp01((goUntil - now) / 0.3f);
    }

    // ─── Banner ───────────────────────────────────────────────────────────────

    /// <summary>A big message that pops in at the centre of the screen for <paramref name="seconds"/>.</summary>
    public void ShowBanner(string message, float seconds = 3f)
    {
        if (string.IsNullOrEmpty(message)) return;
        bannerText.text = message;
        bannerStart = Time.unscaledTime;
        bannerUntil = bannerStart + seconds;
        banner.gameObject.SetActive(true);
    }

    private void UpdateBanner()
    {
        if (!banner.gameObject.activeSelf) return;
        float now = Time.unscaledTime;
        if (now >= bannerUntil) { banner.gameObject.SetActive(false); return; }

        float inT = Mathf.Clamp01((now - bannerStart) / 0.3f);
        float scale = inT < 1f ? Mathf.LerpUnclamped(0.6f, 1f, 1f + 2.2f * Mathf.Pow(inT - 1f, 3f) + 1.2f * Mathf.Pow(inT - 1f, 2f)) : 1f;
        float outT = Mathf.Clamp01((bannerUntil - now) / 0.25f);
        banner.localScale = new Vector3(scale, scale, 1f) * Mathf.Lerp(0.9f, 1f, outT);
        var group = banner.GetComponent<CanvasGroup>();
        if (group == null) group = banner.gameObject.AddComponent<CanvasGroup>();
        group.alpha = outT;
        group.blocksRaycasts = false;
    }

    private void HandleCaught()    => ShowModeBanner(m => m.CaughtBanner, 3f);
    private void HandleRespawned() => ShowModeBanner(m => m.RespawnBanner, 2f);

    private void ShowModeBanner(System.Func<GameMode, string> text, float seconds)
    {
        var mode = GameModeManager.Instance != null ? GameModeManager.Instance.Mode : null;
        if (mode != null) ShowBanner(text(mode), seconds);
    }

    private void HandlePlayerTagged(PhotonPlayer tagger, PhotonPlayer target)
    {
        if (target == null || target.IsLocal) return; // The caught player gets their own banner
        var mode = GameModeManager.Instance != null ? GameModeManager.Instance.Mode : null;
        if (mode != null && !mode.AnnouncesTags) return;
        string what = tagger != null ? $"{tagger.NickName} tagged {target.NickName}!" : $"{target.NickName} fell off the edge!";
        // Tag: how many hiders are still out (the tagged one's role may not have arrived yet)
        int left = TagRoundSummary.HidersLeft();
        if (PlayerRoles.Get(target) == PlayerRole.Hider) left--;
        if (mode is TagMode && left >= 0) what += left == 0 ? "\n<size=60%>Everyone's caught!</size>" : $"\n<size=60%>{left} left</size>";
        ShowBanner(what, 2f);
    }

    private void HandlePhaseChanged(MatchPhase phase)
    {
        var manager = GameModeManager.Instance;
        switch (phase)
        {
            case MatchPhase.Countdown:
                if (manager.Mode.UsesRoles && manager.Mode is not SearchDestroyMode) ShowBanner($"Round {manager.Round}", 2.2f); // S&D has its own
                break;
            case MatchPhase.Hiding:
            case MatchPhase.Active:
                goUntil = Time.unscaledTime + 0.9f;
                Sfx.Here(SfxKind.Horn, 0.7f);
                if (phase == MatchPhase.Active && manager.Mode.UsesRoles && manager.Mode is not TeamMode) ShowBanner("Hunters released!", 2f);
                break;
            case MatchPhase.RoundEnd:
                if (manager.Mode is not SearchDestroyMode) ShowBanner("Round over!", 4f); // S&D says who won and how
                Sfx.Here(SfxKind.Horn, 0.6f, 0.8f);
                break;
            case MatchPhase.MatchEnd:
                var winner = PhotonNetwork.CurrentRoom?.GetPlayer(manager.WinnerActorNumber);
                ShowBanner(winner != null ? $"{winner.NickName} wins!" : "Match over!", 10f);
                Sfx.Here(SfxKind.Fanfare, 0.8f);
                break;
        }
    }

    // ─── Debug ────────────────────────────────────────────────────────────────

    private void UpdateDebug()
    {
        debugText.text =
            $"{Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime))} FPS\n" +
            $"Speed: {player.Body.linearVelocity.magnitude:F2}\n" +
            $"State: {player.State}\n" +
            $"Grounded: {player.IsGrounded}\n" +
            $"Target Speed: {player.TargetSpeed:F1}\n" +
            $"Size: {player.Size:F2}";
    }
}
