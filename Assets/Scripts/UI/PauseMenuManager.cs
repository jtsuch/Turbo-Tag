using System;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// The pause menu (Escape), built in code with the UI kit: a dimmed screen, a menu card on the left (Resume,
/// Settings, Match Rules, Cheats, Leave Match, Quit Game) and the chosen page on the right:
///   - Settings: <see cref="SettingsPanel"/> (same as the main menu);
///   - Match Rules: the host's live switches (pause the match clock, cheats on/off) and the mode's rules
///     (<see cref="RulesEditor"/>, mode locked mid-match; host edits, everyone else reads);
///   - Cheats: <see cref="CheatsPanel"/>.
/// Pausing doesn't stop time; it frees the mouse and blocks gameplay input (<see cref="IsPaused"/>).
/// </summary>
public class PauseMenuManager : MonoBehaviourPunCallbacks
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    private static PauseMenuManager _instance;
    public static PauseMenuManager Instance
    {
        get
        {
            if (_instance == null) _instance = FindFirstObjectByType<PauseMenuManager>(FindObjectsInactive.Include);
            return _instance;
        }
    }

    public static bool IsPaused => (_instance != null && _instance.open) || AdminConsole.IsOpen; // The admin console pauses input too

    public static event Action<bool> PausedChanged;

    private UITheme Theme => UITheme.Current;
    private bool open, built, quittingApp;
    private RectTransform root;
    private CanvasGroup group;
    private float fade;
    private readonly System.Collections.Generic.List<(Button button, RectTransform page)> pages = new();
    private Button timerButton, cheatsButton;
    private TMP_Text roomText;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        Build();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>Called by Spawner once the local player exists.</summary>
    public void Initialize()
    {
        _instance = this;
        Build();
        Show(0);
        SetOpen(false, instant: true);
    }

    // ─── Pause state ──────────────────────────────────────────────────────────

    public void Toggle()
    {
        if (SettingsPanel.IsListening) return; // Escape cancels the key being set, not the menu
        if (open) Resume(); else Pause();
    }

    public void Pause()  => SetOpen(true, instant: false);
    public void Resume() => SetOpen(false, instant: false);

    private void SetOpen(bool value, bool instant)
    {
        if (open == value && !instant) return;
        bool changed = open != value;
        open = value;
        root.gameObject.SetActive(true);
        if (instant) { fade = value ? 1f : 0f; Apply(); }
        Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible   = value;
        if (value) { RefreshHost(); UIKit.Relayout(root); }
        if (changed && !instant) UISound.Play(value ? UISound.Kind.Click : UISound.Kind.Back);
        if (changed) PausedChanged?.Invoke(value);
    }

    private void Update()
    {
        if (root == null) return;
        fade = Mathf.MoveTowards(fade, open ? 1f : 0f, Time.unscaledDeltaTime / 0.15f);
        Apply();
    }

    private void Apply()
    {
        group.alpha = fade;
        group.interactable = group.blocksRaycasts = open;
        float s = Mathf.Lerp(0.97f, 1f, fade);
        root.localScale = Vector3.one; // The canvas itself stays put; the cards ease in
        foreach (Transform child in root) if (child.name.EndsWith("Card")) child.localScale = new Vector3(s, s, 1f);
        if (!open && fade <= 0f && root.gameObject.activeSelf) root.gameObject.SetActive(false);
    }

    // ─── Build ────────────────────────────────────────────────────────────────

    private void Build()
    {
        if (built) return;
        built = true;

        var canvas = UIKit.Canvas("Pause Canvas", 20);
        canvas.transform.SetParent(transform, false);
        root = (RectTransform)canvas.transform;
        group = canvas.gameObject.AddComponent<CanvasGroup>();

        var dim = UIKit.Rect("Dim", root).gameObject.AddComponent<Image>();
        UIKit.Stretch(dim.rectTransform);
        dim.color = UIKit.WithAlpha(Theme.backgroundTop, 0.82f);

        // Menu card (left)
        var menu = UIKit.Card(root, Theme.panel, 32f, 0.55f, "Menu Card");
        menu.anchorMin = new Vector2(0f, 0f);
        menu.anchorMax = new Vector2(0f, 1f);
        menu.pivot = new Vector2(0f, 0.5f);
        menu.offsetMin = new Vector2(80f, 80f);
        menu.offsetMax = new Vector2(500f, -80f);
        UIKit.VStack(menu, 14f, UIKit.Pad(32, 32), TextAnchor.UpperLeft);

        UIKit.Title(menu, "Paused", 64f);
        roomText = UIKit.OneLine(UIKit.Text(menu, "", 20f, UIKit.Font.Body, Theme.textMuted));
        UIKit.Spacer(menu, 10f);

        var resume = UIKit.Button(menu, "Resume", UIKit.Style.Primary, Resume, 80f, 34f);
        var settings = UIKit.Button(menu, "Settings", UIKit.Style.Ghost, () => Show(0), 66f, 28f);
        var rules = UIKit.Button(menu, "Match Rules", UIKit.Style.Ghost, () => Show(1), 66f, 28f);
        var cheats = UIKit.Button(menu, "Cheats", UIKit.Style.Ghost, () => Show(2), 66f, 28f);
        UIKit.Spacer(menu);
        var leave = UIKit.Button(menu, "Leave Match", UIKit.Style.Danger, () => Leave(false), 66f, 28f);
        var quit = UIKit.Button(menu, "Quit Game", UIKit.Style.Ghost, () => Leave(true), 60f, 26f);
        leave.GetComponent<UIButtonFx>().ClickSound = quit.GetComponent<UIButtonFx>().ClickSound = UISound.Kind.Back;
        UIKit.ChainVertical(resume, settings, rules, cheats, leave, quit);

        // Page card (right)
        var pageCard = UIKit.Card(root, Theme.panel, 32f, 0.55f, "Page Card");
        UIKit.Stretch(pageCard, 540f, 80f, 80f, 80f);
        var area = UIKit.Rect("Pages", pageCard);
        UIKit.Stretch(area, 40f, 40f, 36f, 36f);

        var settingsPage = Page(area, "Settings");
        SettingsPanel.Create(Body(settingsPage, "Settings"));

        var rulesPage = Page(area, "Match Rules");
        var rulesBody = Body(rulesPage, "Match Rules");
        var hostRow = UIKit.Rect("Host", rulesBody);
        hostRow.anchorMin = new Vector2(0f, 1f);
        hostRow.anchorMax = new Vector2(1f, 1f);
        hostRow.pivot = new Vector2(0.5f, 1f);
        hostRow.sizeDelta = new Vector2(0f, 64f);
        UIKit.HStack(hostRow, 14f, null, TextAnchor.MiddleLeft, expandHeight: false);
        timerButton = UIKit.Button(hostRow, "Pause Clock", UIKit.Style.Secondary, () => ToggleRule(RuleKeys.TimerPaused), 60f, 24f);
        UIKit.Size(timerButton, 260f, 60f);
        cheatsButton = UIKit.Button(hostRow, "Enable Cheats", UIKit.Style.Secondary, () => ToggleRule(RuleKeys.CheatsEnabled), 60f, 24f);
        UIKit.Size(cheatsButton, 260f, 60f);
        var rulesArea = UIKit.Rect("Rules", rulesBody);
        UIKit.Stretch(rulesArea, 0f, 0f, 84f, 0f);
        RulesEditor.Create(rulesArea, allowModeChange: false);

        var cheatsPage = Page(area, "Cheats");
        CheatsPanel.Create(Body(cheatsPage, "Cheats"));

        pages.Add((settings, settingsPage));
        pages.Add((rules, rulesPage));
        pages.Add((cheats, cheatsPage));
        root.gameObject.SetActive(false);
    }

    private static RectTransform Page(RectTransform area, string name)
    {
        var page = UIKit.Rect(name, area);
        UIKit.Stretch(page);
        return page;
    }

    // The page's title, and the area under it
    private RectTransform Body(RectTransform page, string title)
    {
        var t = UIKit.Title(page, title, 52f);
        UIKit.Place(t.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(800f, 64f));
        var body = UIKit.Rect("Body", page);
        UIKit.Stretch(body, 0f, 0f, 84f, 0f);
        return body;
    }

    private void Show(int index)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            pages[i].page.gameObject.SetActive(i == index);
            pages[i].button.GetComponent<UIButtonFx>().FaceColor = i == index ? Theme.secondary : Theme.card;
        }
        UIKit.Relayout(pages[index].page);
    }

    // ─── Host controls ────────────────────────────────────────────────────────

    public override void OnRoomPropertiesUpdate(Hashtable changed) => RefreshHost();
    public override void OnMasterClientSwitched(Photon.Realtime.Player newMaster) => RefreshHost();

    private void RefreshHost()
    {
        if (timerButton == null) return;
        bool host = PhotonNetwork.IsMasterClient;
        timerButton.interactable = cheatsButton.interactable = host;
        UIKit.Label(timerButton).text = MatchRules.TimerPaused ? "Resume Clock" : "Pause Clock";
        UIKit.Label(cheatsButton).text = MatchRules.CheatsEnabled ? "Disable Cheats" : "Enable Cheats";
        var mode = GameModeManager.Instance != null && GameModeManager.Instance.Mode != null ? GameModeManager.Instance.Mode.DisplayName : "";
        roomText.text = PhotonNetwork.InRoom ? $"{mode}  ·  {Nicify(MatchRules.Map)}" : mode;
    }

    private static void ToggleRule(string key)
    {
        if (PhotonNetwork.IsMasterClient) NetProps.SetRoom(key, !NetProps.Room(key, false));
    }

    private static string Nicify(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name ?? "", "(?<=[a-z])(?=[A-Z])", " ");

    // ─── Leaving ──────────────────────────────────────────────────────────────

    private void Leave(bool quitApp)
    {
        quittingApp = quitApp;
        if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
        else                      OnLeftRoom();
    }

    public override void OnLeftRoom()
    {
        if (!quittingApp)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
            return;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
