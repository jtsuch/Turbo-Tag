using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// In-level HUD: ability hotbar (icons, keybinds, cooldowns for every usable ability), a duration ring per Basic ability,
/// role/objective text, running score, an announcement banner (round start, tags, winner)
/// and an F3 debug overlay. Purely a view — it reads state and listens to game events.
/// Initialize() is called by Spawner when the local player has spawned.
/// Attach to: the PlayerHUD canvas in each level.
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

    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Debug Overlay (F3)")]
    [SerializeField] private TextMeshProUGUI fpsText;
    [SerializeField] private TextMeshProUGUI otherText;

    [Header("Role / Objective")]
    [SerializeField] private TextMeshProUGUI roleText;

    [Header("Banner / Score")]
    [Tooltip("Large centre-screen announcement text.")]
    [SerializeField] private TextMeshProUGUI caughtText;
    [Tooltip("Running score for the local player.")]
    [SerializeField] private TextMeshProUGUI totalTimeText;

    [Header("Abilities")]
    [Tooltip("Hotbar entries, each with [0] RawImage icon, [1] keybind text, [2] name text. The first is copied for extra abilities.")]
    [SerializeField] private GameObject abilityList;
    [Tooltip("Basic-ability duration ring root. Copied (stacked upward) when several Basic abilities are usable.")]
    [SerializeField] private GameObject timer;
    [SerializeField] private Image      yellowCircle;

    [Header("Colours")]
    [SerializeField] private Color hunterColor = new(1f, 0.25f, 0.2f);
    [SerializeField] private Color hiderColor  = new(0.3f, 0.85f, 1f);
    [SerializeField] private Color presidentColor = new(1f, 0.78f, 0.15f);

    private const string IconFolder = "AbilityIcons/";
    private const KeyCode DebugToggleKey = KeyCode.F3;

    private Player    player;
    private Coroutine bannerRoutine;
    private bool      showDebug;

    private struct HotbarSlot { public GameObject root; public RawImage icon; public TMP_Text key; public TMP_Text name; }
    private readonly List<HotbarSlot> slots = new();
    private List<(Ability ability, KeyCode key)> shown = new();
    private Vector2 timerHome; // The scene timer's position; extra timers stack above it

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        _instance = this;
        if (timer != null) timerHome = ((RectTransform)timer.transform).anchoredPosition;
    }

    private void Start()
    {
        if (player == null) SetHudVisible(false);
        SetBanner(null);
        SetDebugVisible(false);
    }

    public void Initialize(Player localPlayer)
    {
        Unsubscribe();
        player = localPlayer;

        CacheHotbar();
        RefreshHotbar();
        SetHudVisible(true);

        player.Abilities.LoadoutChanged += RefreshHotbar;
        player.Caught                   += HandleCaught;
        player.Respawned                += HandleRespawned;
        Keybinds.Changed                += RefreshHotbar;

        var manager = GameModeManager.Instance;
        if (manager != null)
        {
            manager.PhaseChanged += HandlePhaseChanged;
            manager.PlayerTagged += HandlePlayerTagged;
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
        if (_instance == this) _instance = null;
    }

    private void Unsubscribe()
    {
        Keybinds.Changed -= RefreshHotbar;
        if (player != null)
        {
            player.Abilities.LoadoutChanged -= RefreshHotbar;
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
        if (Input.GetKeyDown(DebugToggleKey)) SetDebugVisible(!showDebug);
        if (player == null) return;

        UpdateHotbarState();
        UpdateBasicRing();
        UpdateRoleAndScore();
        if (showDebug) UpdateDebugText();
    }

    private void SetHudVisible(bool visible)
    {
        if (abilityList   != null) abilityList.SetActive(visible);
        if (roleText      != null) roleText.gameObject.SetActive(visible);
        if (totalTimeText != null) totalTimeText.gameObject.SetActive(visible);
        if (timer         != null) timer.SetActive(false);
    }

    // ─── Hotbar ───────────────────────────────────────────────────────────────

    // The scene's first hotbar entry is the template; one copy per usable ability (starting four + Cheats extras)
    private void CacheHotbar()
    {
        if (abilityList == null || slots.Count > 0 || abilityList.transform.childCount == 0) return;

        foreach (Transform child in abilityList.transform) slots.Add(ReadSlot(child.gameObject));
    }

    private static HotbarSlot ReadSlot(GameObject root)
    {
        Transform t = root.transform;
        return new HotbarSlot
        {
            root = root,
            icon = t.childCount > 0 ? t.GetChild(0).GetComponent<RawImage>() : null,
            key  = t.childCount > 1 ? t.GetChild(1).GetComponent<TMP_Text>() : null,
            name = t.childCount > 2 ? t.GetChild(2).GetComponent<TMP_Text>() : null,
        };
    }

    private void RefreshHotbar()
    {
        if (slots.Count == 0 || player == null) return;

        shown = player.Abilities.Usable();
        while (slots.Count < shown.Count)
            slots.Add(ReadSlot(Instantiate(slots[0].root, abilityList.transform)));

        for (int i = 0; i < slots.Count; i++)
        {
            bool used = i < shown.Count;
            slots[i].root.SetActive(used);
            if (!used) continue;

            var (ability, key) = shown[i];
            // No icon yet: hide the image rather than show a blank white square
            if (slots[i].icon != null)
            {
                var icon = Resources.Load<Texture2D>(IconFolder + ability.Id + "Icon");
                slots[i].icon.texture = icon;
                slots[i].icon.enabled = icon != null;
            }
            if (slots[i].key  != null) slots[i].key.text  = Keybinds.Label(key);
            if (slots[i].name != null) slots[i].name.text = PrettyName(ability.Id);
        }

        RefreshTimers();
    }

    // Dims abilities on cooldown and shows the seconds left
    private void UpdateHotbarState()
    {
        for (int i = 0; i < shown.Count && i < slots.Count; i++)
        {
            var ability = shown[i].ability;
            if (ability == null || ability is BasicAbility) continue;

            float remaining = ability.CooldownRemaining;
            if (slots[i].icon != null)
                slots[i].icon.color = remaining > 0f ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            if (slots[i].name != null)
                slots[i].name.text = remaining > 0f ? $"{remaining:0.0}s" : PrettyName(ability.Id);
        }
    }

    // ─── Basic-ability timers ─────────────────────────────────────────────────
    // One ring per usable Basic ability (copies of the scene's timer), stacked upward from the
    // timer's spot. A ring shows while its meter isn't full.

    private struct TimerRing { public GameObject root; public Image fill; public TMP_Text label; public BasicAbility ability; }
    private readonly List<TimerRing> rings = new();

    private void RefreshTimers()
    {
        if (timer == null || yellowCircle == null) return;

        var basics = new List<BasicAbility>();
        foreach (var (ability, _) in shown)
            if (ability is BasicAbility basic) basics.Add(basic);

        while (rings.Count < basics.Count)
        {
            GameObject root = rings.Count == 0 ? timer : Instantiate(timer, timer.transform.parent);
            // The copy's fill image sits at the same place in its hierarchy as yellowCircle does in the original
            Image fill = yellowCircle;
            if (rings.Count > 0)
            {
                string path = PathFrom(timer.transform, yellowCircle.transform);
                Transform found = path == null ? root.transform : root.transform.Find(path);
                fill = found != null ? found.GetComponent<Image>() : null;
            }
            rings.Add(new TimerRing
            {
                root  = root,
                fill  = fill,
                label = root.GetComponentInChildren<TMP_Text>(true),
            });
        }

        for (int i = 0; i < rings.Count; i++)
        {
            var ring = rings[i];
            ring.ability = i < basics.Count ? basics[i] : null;
            if (ring.label != null && ring.ability != null) ring.label.text = PrettyName(ring.ability.Id);
            rings[i] = ring;
            if (ring.ability == null) ring.root.SetActive(false);
        }
    }

    private void UpdateBasicRing()
    {
        if (rings.Count == 0) return;

        var   baseRect = (RectTransform)timer.transform;
        float step     = baseRect.rect.height + 12f;
        int   visible  = 0;
        foreach (var ring in rings)
        {
            float fill = ring.ability != null ? ring.ability.Readiness : 1f;
            bool  show = ring.ability != null && fill < 0.999f;
            if (ring.root.activeSelf != show) ring.root.SetActive(show);
            if (!show) continue;

            if (ring.fill != null) ring.fill.fillAmount = fill;
            ((RectTransform)ring.root.transform).anchoredPosition = timerHome + Vector2.up * (step * visible++);
        }
    }

    // Relative path from a root to one of its descendants ("" = the root itself, null = not under it)
    private static string PathFrom(Transform root, Transform target)
    {
        if (target == root) return null;
        var parts = new List<string>();
        for (var t = target; t != null; t = t.parent)
        {
            if (t == root) { parts.Reverse(); return string.Join("/", parts); }
            parts.Add(t.name);
        }
        return null;
    }

    private static string PrettyName(string id) => AbilityCatalog.DisplayName(id);

    // ─── Role / Score ─────────────────────────────────────────────────────────

    private void UpdateRoleAndScore()
    {
        var manager = GameModeManager.Instance;
        var mode    = manager != null ? manager.Mode : null;

        if (roleText != null)
        {
            bool showRole = mode != null && mode.UsesRoles && player.Role != PlayerRole.None;
            roleText.enabled = showRole;
            if (showRole)
            {
                string role      = mode.RoleName(player.Role);
                string objective = mode.GetObjective(player.Role, manager.Phase);
                roleText.color = player.Role switch
                {
                    PlayerRole.Hunter    => hunterColor,
                    PlayerRole.President => presidentColor,
                    _                    => hiderColor,
                };
                roleText.text = string.IsNullOrEmpty(objective) ? role : $"{role}\n<size=50%>{objective}</size>";
            }
        }

        if (totalTimeText != null)
        {
            bool showScore = mode != null && mode.ShowsScores;
            totalTimeText.enabled = showScore;
            if (showScore)
                totalTimeText.text = "Score: " + MatchTimerController.FormatTime(manager.Scores.GetLive(player.Owner));
        }
    }

    // ─── Banner ───────────────────────────────────────────────────────────────

    public void ShowBanner(string message, float seconds = 3f)
    {
        if (bannerRoutine != null) StopCoroutine(bannerRoutine);
        bannerRoutine = StartCoroutine(BannerRoutine(message, seconds));
    }

    private IEnumerator BannerRoutine(string message, float seconds)
    {
        SetBanner(message);
        yield return new WaitForSeconds(seconds);
        SetBanner(null);
        bannerRoutine = null;
    }

    private void SetBanner(string message)
    {
        if (caughtText == null) return;
        caughtText.text = message ?? "";
        caughtText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    private void HandleCaught()    => ShowBanner("Caught!", 3f);
    private void HandleRespawned() => ShowBanner("You're a hunter now!", 2f);

    private void HandlePlayerTagged(PhotonPlayer tagger, PhotonPlayer target)
    {
        if (target == null || target.IsLocal) return; // The caught player gets their own banner
        string taggerName = tagger != null ? tagger.NickName : "Someone";
        ShowBanner($"{taggerName} tagged {target.NickName}!", 2f);
    }

    private void HandlePhaseChanged(MatchPhase phase)
    {
        var manager = GameModeManager.Instance;
        switch (phase)
        {
            case MatchPhase.Countdown:
                if (manager.Mode.UsesRoles) ShowBanner($"Round {manager.Round}", 2.5f);
                break;
            case MatchPhase.Active:
                if (manager.Mode.UsesRoles) ShowBanner("Hunters released!", 2f);
                break;
            case MatchPhase.RoundEnd:
                ShowBanner("Round over!", 4f);
                break;
            case MatchPhase.MatchEnd:
                var winner = Photon.Pun.PhotonNetwork.CurrentRoom?.GetPlayer(manager.WinnerActorNumber);
                ShowBanner(winner != null ? $"{winner.NickName} wins!" : "Match over!", 10f);
                break;
        }
    }

    // ─── Debug ────────────────────────────────────────────────────────────────

    private void SetDebugVisible(bool visible)
    {
        showDebug = visible;
        if (fpsText   != null) fpsText.gameObject.SetActive(visible);
        if (otherText != null) otherText.gameObject.SetActive(visible);
    }

    private void UpdateDebugText()
    {
        if (fpsText != null)
            fpsText.text = Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)) + " FPS";

        if (otherText != null)
            otherText.text =
                $"Speed: {player.Body.linearVelocity.magnitude:F2}\n" +
                $"State: {player.State}\n" +
                $"Grounded: {player.IsGrounded}\n" +
                $"Target Speed: {player.TargetSpeed:F1}\n" +
                $"Size: {player.Size:F2}";
    }
}
