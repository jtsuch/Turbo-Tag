using System.Text.RegularExpressions;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Main menu controller: the login/register/password-reset window, the Play panel (create or
/// join a game), settings and quit. Account calls are delegated to <see cref="AccountService"/>.
/// Attach to: the menu manager GameObject in the MainMenu scene (buttons call the public methods).
/// </summary>
public class PlayFabLoginManager : MonoBehaviourPunCallbacks
{
    public const string UsernamePrefsKey = "Username";

    [Header("Main Menu")]
    [SerializeField] private GameObject gamePanel;
    [SerializeField] private GameObject settingsWindow;

    [Header("Login & Register")]
    [SerializeField] private GameObject     loginWindow;
    [SerializeField] private TMP_Text       titleText;
    [SerializeField] private GameObject     usernameField;
    [FormerlySerializedAs("passworldField")]
    [SerializeField] private GameObject     passwordField;
    [SerializeField] private GameObject     confirmPasswordField;
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_InputField confirmPasswordInput;
    [SerializeField] private Button         switchButton;
    [SerializeField] private Button         forgotPassword;
    [SerializeField] private TMP_Text       errorMessage;
    [SerializeField] private Button         submitButton;

    private enum LoginMode { Login, Register, Reset }
    private LoginMode mode;
    private bool      busy;

    private static readonly Regex UsernameRegex = new(@"^[a-zA-Z0-9]{3,24}$");

    // ─── Startup ──────────────────────────────────────────────────────────────

    private void Start()
    {
        gamePanel.SetActive(false);
        settingsWindow.SetActive(false);
        AccountService.PurgeLegacyCredentials();

        if (AccountService.CanAutoLogin)
        {
            loginWindow.SetActive(false);
            AccountService.AutoLogin(OnLoggedIn, _ => ShowLoginWindow(LoginMode.Login));
        }
        else
        {
            ShowLoginWindow(AccountService.HasLoggedInBefore ? LoginMode.Login : LoginMode.Register);
        }
    }

    // ─── Play navigation ──────────────────────────────────────────────────────

    public void PlayButton() => gamePanel.SetActive(!gamePanel.activeSelf);

    public void CreateButton()
    {
        if (!PhotonNetwork.IsConnectedAndReady) return;
        RoomFactory.CreateRoom();
    }

    public void JoinButton() => UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.JoinList);

    public override void OnCreatedRoom() => PhotonNetwork.LoadLevel(SceneNames.Lobby);

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"[MainMenu] Create room failed ({returnCode}): {message}. Retrying with a new name.");
        RoomFactory.CreateRoom();
    }

    public void QuitButton()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ─── Settings ─────────────────────────────────────────────────────────────

    public void SettingsOpen()  => settingsWindow.SetActive(true);
    public void SettingsClose() => settingsWindow.SetActive(false);

    public void OnLogOutPressed()
    {
        AccountService.Logout();
        PlayerPrefs.DeleteKey(UsernamePrefsKey);
        settingsWindow.SetActive(false);
        gamePanel.SetActive(false);
        ShowLoginWindow(LoginMode.Login);
    }

    // ─── Login window ─────────────────────────────────────────────────────────

    public void OnSubmitPressed()
    {
        if (busy) return;

        string email = emailInput.text.Trim();
        switch (mode)
        {
            case LoginMode.Reset:
                Run(done => AccountService.SendPasswordReset(email,
                    () => { done(); ShowLoginWindow(LoginMode.Login); SetError("Reset email sent! Check your inbox."); },
                    e  => { done(); SetError(e); }));
                break;

            case LoginMode.Register:
                string username = usernameInput.text.Trim();
                string error = ValidateRegistration(username, email, passwordInput.text, confirmPasswordInput.text);
                if (error != null) { SetError(error); return; }
                Run(done => AccountService.Register(username, email, passwordInput.text,
                    name => { done(); OnLoggedIn(string.IsNullOrEmpty(name) ? username : name); },
                    e    => { done(); SetError(e); }));
                break;

            default:
                Run(done => AccountService.Login(email, passwordInput.text,
                    name => { done(); OnLoggedIn(name); },
                    e    => { done(); SetError("Login failed: " + e); }));
                break;
        }
    }

    public void SwitchLoginRegister() =>
        ShowLoginWindow(mode == LoginMode.Register ? LoginMode.Login : LoginMode.Register);

    public void ForgotPasswordPressed() =>
        ShowLoginWindow(mode == LoginMode.Reset ? LoginMode.Login : LoginMode.Reset);

    private void OnLoggedIn(string displayName)
    {
        if (string.IsNullOrEmpty(displayName))
            displayName = PlayerPrefs.GetString(UsernamePrefsKey, "Penguin");

        PlayerPrefs.SetString(UsernamePrefsKey, displayName);
        PhotonNetwork.NickName = displayName;
        passwordInput.text = confirmPasswordInput.text = "";
        loginWindow.SetActive(false);
    }

    // Disables submit while a request is in flight
    private void Run(System.Action<System.Action> request)
    {
        busy = true;
        submitButton.interactable = false;
        SetError("");
        request(() => { busy = false; submitButton.interactable = true; });
    }

    private static string ValidateRegistration(string username, string email, string password, string confirm)
    {
        if (!UsernameRegex.IsMatch(username))            return "Username must be 3–24 letters or numbers.";
        if (!email.Contains("@") || !email.Contains(".")) return "Please enter a valid email address.";
        if (password.Length < 6)                          return "Password must be at least 6 characters.";
        if (password != confirm)                          return "Passwords do not match.";
        return null;
    }

    private void ShowLoginWindow(LoginMode newMode)
    {
        mode = newMode;
        loginWindow.SetActive(true);
        SetError("");

        bool register = newMode == LoginMode.Register;
        bool reset    = newMode == LoginMode.Reset;

        if (newMode == LoginMode.Login && string.IsNullOrEmpty(emailInput.text))
            emailInput.text = AccountService.LastEmail;

        titleText.text = reset ? "Password Reset:" : register ? "Register:" : "Login:";
        usernameField.SetActive(register);
        confirmPasswordField.SetActive(register);
        passwordField.SetActive(!reset);
        forgotPassword.gameObject.SetActive(!register);
        switchButton.gameObject.SetActive(!reset);

        Label(submitButton,   reset ? "Send Email" : register ? "Register" : "Login");
        Label(switchButton,   register ? "Already have an account? Login here." : "No account? Register here.");
        Label(forgotPassword, reset ? "Oh never mind, I remember it" : "Forgot password");
    }

    private void SetError(string message) => errorMessage.text = message;

    private static void Label(Button button, string text)
    {
        var label = button.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = text;
    }
}
