using System.Collections.Generic;
using System.Text.RegularExpressions;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The main menu: logo and big buttons on the left (Quick Play, Create Game, Join Game, Settings, Quit), the
/// player's card on the right (name, penguin colour picker, log out), connection status and version along
/// the bottom. Logs in or registers first (PlayFab through <see cref="AccountService"/>) in a modal that
/// can't be dismissed. Attach to one GameObject in the MainMenu scene.
/// </summary>
public class MainMenuScreen : MenuScreen
{
    public const string UsernamePrefsKey = "Username";

    private static readonly Regex UsernameRegex = new(@"^[a-zA-Z0-9]{3,24}$");

    private readonly List<Button> playButtons = new();
    private TMP_Text nameText, statusText;
    private PenguinPreview preview;
    private readonly List<(Image ring, string skin)> swatches = new();
    private UIModal settings;
    private bool busy, creating;

    // Login
    private enum LoginMode { Login, Register, Reset }
    private LoginMode mode;
    private UIModal login;
    private Button loginTab, registerTab, submit, forgot;
    private RectTransform usernameRow, passwordRow, confirmRow;
    private TMP_InputField usernameInput, emailInput, passwordInput, confirmInput;
    private TMP_Text loginError;
    private bool loginBusy;

    // ─── Build ────────────────────────────────────────────────────────────────

    protected override void Build()
    {
        BuildLeft();
        BuildPlayerCard();
        BuildFooter();

        settings = UIKit.Modal(Root, "Settings", new Vector2(1280f, 860f));
        settings.NoFooter();
        SettingsPanel.Create(settings.Body);

        BuildLogin();
    }

    private void BuildLeft()
    {
        var logo = UIKit.Rect("Logo", Root).gameObject.AddComponent<RawImage>();
        logo.texture = Theme.logo;
        logo.raycastTarget = false;
        float aspect = Theme.logo != null ? Theme.logo.width / (float)Theme.logo.height : 1.45f;
        UIKit.Place(logo.rectTransform, new Vector2(0f, 1f), new Vector2(30f, 10f), new Vector2(560f * aspect, 560f));

        var column = UIKit.Place(UIKit.Rect("Buttons", Root), new Vector2(0f, 0f), new Vector2(110f, 120f), new Vector2(560f, 560f));
        UIKit.VStack(column, 18f, null, TextAnchor.LowerLeft);

        var quick  = UIKit.Button(column, "Quick Play", UIKit.Style.Primary, QuickPlay, 104f, 44f);
        var create = UIKit.Button(column, "Create Game", UIKit.Style.Secondary, CreateGame, 84f, 36f);
        var join   = UIKit.Button(column, "Join Game", UIKit.Style.Accent, () => UIFade.LoadScene(SceneNames.JoinList), 84f, 36f);
        var opts   = UIKit.Button(column, "Settings", UIKit.Style.Ghost, () => settings.Open(), 72f, 30f);
        var quit   = UIKit.Button(column, "Quit", UIKit.Style.Ghost, Quit, 72f, 30f);
        quit.GetComponent<UIButtonFx>().ClickSound = UISound.Kind.Back;
        playButtons.AddRange(new[] { quick, create, join });
        UIKit.ChainVertical(quick, create, join, opts, quit);
    }

    private void BuildPlayerCard()
    {
        var card = UIKit.Card(Root, Theme.panel, 32f, 0.5f, "Player Card");
        UIKit.Place(card, new Vector2(1f, 0.5f), new Vector2(-100f, -10f), new Vector2(560f, 820f));
        UIKit.VStack(card, 14f, UIKit.Pad(40, 36), TextAnchor.UpperCenter);

        UIKit.Text(card, "PLAYING AS", 20f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Center);
        preview = PenguinPreview.Create(card, 330f);
        nameText = UIKit.Text(card, "", 52f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Center);
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.enableAutoSizing = true;
        nameText.fontSizeMin = 28f;
        nameText.fontSizeMax = 52f;
        UIKit.Size(nameText, height: 70f);

        UIKit.Text(card, "Penguin Colour", 22f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Center);
        BuildSwatches(card);

        UIKit.Spacer(card);
        UIKit.LinkButton(card, "Log out", LogOut, 22f);
        RefreshPlayer();
    }

    private void BuildSwatches(Transform parent)
    {
        var grid = UIKit.Rect("Swatches", parent);
        UIKit.Size(grid, height: 130f);
        var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(54f, 54f);
        layout.spacing = new Vector2(14f, 14f);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 5;

        foreach (var skin in PenguinSkins.All)
        {
            var swatch = UIKit.Rect(skin.Name, grid).gameObject.AddComponent<Image>();
            swatch.sprite = UISprites.Circle;
            swatch.color = skin.Color;
            var ring = UIKit.Rect("Ring", swatch.transform).gameObject.AddComponent<Image>();
            ring.sprite = UISprites.Ring;
            ring.raycastTarget = false;
            UIKit.Stretch(ring.rectTransform, -8f, -8f, -8f, -8f);
            var button = swatch.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var fx = swatch.gameObject.AddComponent<UIButtonFx>();
            fx.HoverScale = 1.15f;
            string name = skin.Name;
            button.onClick.AddListener(() =>
            {
                PenguinSkins.SetLocal(name);
                RefreshPlayer();
            });
            swatches.Add((ring, name));
        }
    }

    private void BuildFooter()
    {
        statusText = UIKit.Text(Root, "", 22f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Left, "Status");
        UIKit.Place(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(110f, 40f), new Vector2(700f, 36f));

        var version = UIKit.Text(Root, $"Turbo Tag  v{Application.version}", 20f, UIKit.Font.Body, Theme.textMuted,
            TextAlignmentOptions.Right, "Version");
        UIKit.Place(version.rectTransform, new Vector2(1f, 0f), new Vector2(-100f, 40f), new Vector2(500f, 36f));
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        AccountService.PurgeLegacyCredentials();
        if (AccountService.CanAutoLogin)
            AccountService.AutoLogin(OnLoggedIn, _ => ShowLogin(LoginMode.Login));
        else
            ShowLogin(AccountService.HasLoggedInBefore ? LoginMode.Login : LoginMode.Register);
    }

    private float nextStatus;

    private void Update()
    {
        if (Time.unscaledTime < nextStatus) return;
        nextStatus = Time.unscaledTime + 1f;

        bool online = PhotonNetwork.IsConnectedAndReady;
        statusText.text = online
            ? $"<color=#7CE85A>●</color>  Online  ·  {PhotonNetwork.GetPing()} ms  ·  {PhotonNetwork.CountOfPlayers} playing"
            : "<color=#FF4D6D>●</color>  Connecting…";
        foreach (var b in playButtons) b.interactable = online && !busy;
    }

    // ─── Play ─────────────────────────────────────────────────────────────────

    private void QuickPlay()
    {
        if (!PhotonNetwork.IsConnectedAndReady || busy) return;
        busy = true;
        Toast("Finding a game…");
        PhotonNetwork.JoinRandomRoom();
    }

    private void CreateGame()
    {
        if (!PhotonNetwork.IsConnectedAndReady || busy) return;
        busy = creating = true;
        RoomFactory.CreateRoom();
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        // Nobody to join: start a game for others to find
        creating = true;
        RoomFactory.CreateRoom();
    }

    public override void OnCreatedRoom() => PhotonNetwork.LoadLevel(SceneNames.Lobby);

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"[MainMenu] Create room failed ({returnCode}): {message}. Retrying with a new name.");
        RoomFactory.CreateRoom();
    }

    public override void OnJoinedRoom()
    {
        if (creating) return; // OnCreatedRoom loads the lobby
        // The host's scene normally loads automatically; rooms that never synced one go to the lobby
        if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("curScn"))
            PhotonNetwork.LoadLevel(SceneNames.Lobby);
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ─── Player card ──────────────────────────────────────────────────────────

    private void RefreshPlayer()
    {
        string name = string.IsNullOrEmpty(PhotonNetwork.NickName) ? PlayerPrefs.GetString(UsernamePrefsKey, "Penguin") : PhotonNetwork.NickName;
        nameText.text = name;
        string current = PenguinSkins.Saved;
        preview.SetSkin(current);
        foreach (var (ring, skin) in swatches) ring.enabled = skin == current;
    }

    private void LogOut()
    {
        AccountService.Logout();
        PlayerPrefs.DeleteKey(UsernamePrefsKey);
        ShowLogin(LoginMode.Login);
    }

    // ─── Login / register ─────────────────────────────────────────────────────

    private void BuildLogin()
    {
        login = UIKit.Modal(Root, "Welcome!", new Vector2(760f, 800f), closable: false);
        var body = login.Body;
        UIKit.VStack(body, 14f, null, TextAnchor.UpperCenter);

        var tabs = UIKit.Rect("Tabs", body);
        UIKit.Size(tabs, height: 64f);
        UIKit.HStack(tabs, 12f, null, TextAnchor.MiddleCenter);
        loginTab    = UIKit.Button(tabs, "Log In", UIKit.Style.Ghost, () => ShowLogin(LoginMode.Login), 60f, 26f);
        registerTab = UIKit.Button(tabs, "Sign Up", UIKit.Style.Ghost, () => ShowLogin(LoginMode.Register), 60f, 26f);
        UIKit.Size(loginTab, flexWidth: 1f);
        UIKit.Size(registerTab, flexWidth: 1f);

        usernameRow = Field(body, "Username", out usernameInput, false);
        Field(body, "Email", out emailInput, false);
        passwordRow = Field(body, "Password", out passwordInput, true);
        confirmRow  = Field(body, "Confirm Password", out confirmInput, true);

        loginError = UIKit.Text(body, "", 22f, UIKit.Font.Bold, Theme.danger, TextAlignmentOptions.Center, "Error");
        UIKit.Size(loginError, height: 32f);

        forgot = UIKit.LinkButton(body, "Forgot your password?", () =>
            ShowLogin(mode == LoginMode.Reset ? LoginMode.Login : LoginMode.Reset), 20f);

        submit = UIKit.Button(login.Footer, "Log In", UIKit.Style.Primary, Submit, 76f, 34f);
        UIKit.Size(submit, flexWidth: 1f);

        // Enter submits from any field
        foreach (var field in new[] { usernameInput, emailInput, passwordInput, confirmInput })
            field.onSubmit.AddListener(_ => Submit());
    }

    private static RectTransform Field(Transform parent, string label, out TMP_InputField input, bool password)
    {
        var group = UIKit.Rect(label, parent);
        UIKit.VStack(group, 6f);
        UIKit.Text(group, label.ToUpperInvariant(), 18f, UIKit.Font.Bold, UITheme.Current.textMuted);
        input = UIKit.InputField(group, label == "Email" ? "you@example.com" : label, password, 60f);
        return group;
    }

    private void ShowLogin(LoginMode newMode)
    {
        mode = newMode;
        bool register = mode == LoginMode.Register;
        bool reset = mode == LoginMode.Reset;

        if (mode == LoginMode.Login && string.IsNullOrEmpty(emailInput.text)) emailInput.text = AccountService.LastEmail;
        login.TitleText.text = reset ? "Reset Password" : register ? "Join the Party!" : "Welcome Back!";
        usernameRow.gameObject.SetActive(register);
        confirmRow.gameObject.SetActive(register);
        passwordRow.gameObject.SetActive(!reset);
        forgot.gameObject.SetActive(!register);
        UIKit.Label(forgot).text = reset ? "Never mind, I remember it" : "Forgot your password?";
        UIKit.Label(submit).text = reset ? "Send Reset Email" : register ? "Create Account" : "Log In";

        loginTab.GetComponent<UIButtonFx>().FaceColor    = mode == LoginMode.Login ? Theme.secondary : Theme.card;
        registerTab.GetComponent<UIButtonFx>().FaceColor = register ? Theme.secondary : Theme.card;
        loginError.text = "";
        if (!login.IsOpen) login.Open();
    }

    private void Submit()
    {
        if (loginBusy) return;
        string email = emailInput.text.Trim();

        switch (mode)
        {
            case LoginMode.Reset:
                Run(done => AccountService.SendPasswordReset(email,
                    () => { done(); ShowLogin(LoginMode.Login); Toast("Reset email sent! Check your inbox."); },
                    e => { done(); LoginError(e); }));
                break;

            case LoginMode.Register:
                string username = usernameInput.text.Trim();
                string problem = Validate(username, email, passwordInput.text, confirmInput.text);
                if (problem != null) { LoginError(problem); return; }
                Run(done => AccountService.Register(username, email, passwordInput.text,
                    name => { done(); OnLoggedIn(string.IsNullOrEmpty(name) ? username : name); },
                    e => { done(); LoginError(e); }));
                break;

            default:
                Run(done => AccountService.Login(email, passwordInput.text,
                    name => { done(); OnLoggedIn(name); },
                    e => { done(); LoginError("Login failed: " + e); }));
                break;
        }
    }

    private void Run(System.Action<System.Action> request)
    {
        loginBusy = true;
        submit.interactable = false;
        loginError.text = "";
        request(() => { loginBusy = false; submit.interactable = true; });
    }

    private void LoginError(string message)
    {
        loginError.text = message;
        UISound.Play(UISound.Kind.Error);
    }

    private static string Validate(string username, string email, string password, string confirm)
    {
        if (!UsernameRegex.IsMatch(username))             return "Username must be 3–24 letters or numbers.";
        if (!email.Contains("@") || !email.Contains("."))  return "Please enter a valid email address.";
        if (password.Length < 6)                           return "Password must be at least 6 characters.";
        if (password != confirm)                           return "Passwords don't match.";
        return null;
    }

    private void OnLoggedIn(string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) displayName = PlayerPrefs.GetString(UsernamePrefsKey, "Penguin");
        PlayerPrefs.SetString(UsernamePrefsKey, displayName);
        PhotonNetwork.NickName = displayName;
        passwordInput.text = confirmInput.text = "";
        if (login.IsOpen)
        {
            login.Close();
            Toast($"Welcome, {displayName}!");
        }
        RefreshPlayer();
    }
}
