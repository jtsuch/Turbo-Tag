using Photon.Pun;
using Photon.Realtime;
using PlayFab;
using PlayFab.ClientModels;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class PlayFabLoginManager : MonoBehaviourPunCallbacks
{
    const string LAST_EMAIL_KEY = "LAST_EMAIL", LAST_PASSWORD_KEY = "LAST_PASSWORD", LAST_USERNAME_KEY = "Username";

    [Header("Main Menu UI:")]
    [SerializeField] private GameObject gamePanel;

    [Header("Login & Register:")]
    [SerializeField] private GameObject loginWindow;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private GameObject usernameField;
    [SerializeField] private GameObject passworldField;
    [SerializeField] private GameObject confirmPasswordField;
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_InputField confirmPasswordInput;
    [SerializeField] private Button switchButton;
    [SerializeField] private Button forgotPassword;
    [SerializeField] private TMP_Text errorMessage;
    [SerializeField] private Button submitButton;

    [Header("Settings:")]
    [SerializeField] private GameObject settingsWindow;

    private bool onRegister = true; // Whether window shows register or login
    private bool resetWindowOpen = false;
    private bool gamePanelOpen = false;

    private void Start()
    {
        gamePanel.SetActive(false);
        //PlayerPrefs.SetString("Username", "MrLemons14");
        // Logged in
        if (PlayerPrefs.HasKey(LAST_EMAIL_KEY) && PlayerPrefs.GetString(LAST_EMAIL_KEY) != "")
        {
            string email = PlayerPrefs.GetString(LAST_EMAIL_KEY);
            string password = PlayerPrefs.GetString(LAST_PASSWORD_KEY);
            Login(email, password);
        }
        // Not logged in
        else
        {
            loginWindow.SetActive(true);
            // If they're brand new, show register page (default is login)
            if (!PlayerPrefs.HasKey(LAST_EMAIL_KEY))
                SetRegisterWindow();
            else
                SetLoginWindow();
        }
    }

    /*
    *
    * PLAY NAVIGATION
    *
    */
    public void PlayButton()
    {
        if (gamePanelOpen)
        {
            gamePanel.SetActive(false);
            gamePanelOpen = false;
        }
        else
        {
            gamePanel.SetActive(true);
            gamePanelOpen = true;
        }
    }

    public void CreateButton()
    {
        string hostName = "Room_" + Random.Range(1000, 9999); // Change to use the name of the host
        Debug.Log("Creating room: " + hostName);
        PhotonNetwork.CreateRoom(hostName, new RoomOptions() { MaxPlayers = 8, IsVisible = true, IsOpen = true }, TypedLobby.Default, null);
    }

    public override void OnCreatedRoom()
    {
        PhotonNetwork.LoadLevel("Lobby");
        Debug.Log("Created Room and loading Lobby");
    }

    public void JoinButton() 
    {
        PhotonNetwork.LoadLevel("JoinList");
        Debug.Log("Loading Join Lobby List");
    }

    public void QuitButton()
    {
        // Quit the application
        Debug.Log("Quitting Application...");
        Application.Quit();
        #if UNITY_EDITOR
        EditorApplication.isPlaying = false;
        #endif
    }

    /*
    *
    * SETTING NAVIGATION
    *
    */
    public void SettingsOpen()
    {
        settingsWindow.SetActive(true);
    }

    public void SettingsClose()
    {
        settingsWindow.SetActive(false);
    }

    public void OnLogOutPressed()
    {
        PlayerPrefs.SetString(LAST_EMAIL_KEY, "");
        PlayerPrefs.SetString(LAST_PASSWORD_KEY, "");
        PlayerPrefs.SetString(LAST_USERNAME_KEY, "");
        //loggedIn = false;
        settingsWindow.SetActive(false);
        PlayFabClientAPI.ForgetAllCredentials();
        Debug.Log("Logged Out");
        
        loginWindow.SetActive(true);
        SetLoginWindow();
    }

    /*
    *
    * LOGIN / REGISTER NAVIGATION AND FUNCTIONALITY
    *
    */
    public void OnSubmitPressed()
    {
        if (resetWindowOpen)
        {
            ResetPassword(emailInput.text.Trim());
        }
        else if (onRegister)
        {
            Register(usernameInput.text, emailInput.text, passwordInput.text, confirmPasswordInput.text);
        }
        else
        {
            Login(emailInput.text, passwordInput.text);
        }
    }

    public void SwitchLoginRegister()
    {
        if (onRegister)
        {
            SetLoginWindow();
        }
        else
        {
            SetRegisterWindow();
        }
    }

    private static readonly System.Text.RegularExpressions.Regex UsernameRegex =
        new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9]{3,24}$");

    private void Register(string username, string email, string password, string confirmPassword)
    {
        username = username.Trim();
        email    = email.Trim();

        if (string.IsNullOrEmpty(username) || !UsernameRegex.IsMatch(username))
        {
            errorMessage.text = "Username must be 3–24 characters, letters and numbers only.";
            return;
        }
        if (!email.Contains("@") || !email.Contains("."))
        {
            errorMessage.text = "Please enter a valid email address.";
            return;
        }
        if (password.Length < 6)
        {
            errorMessage.text = "Password must be at least 6 characters.";
            return;
        }
        if (password != confirmPassword)
        {
            errorMessage.text = "*Passwords do not match*";
            passwordInput.text = "";
            confirmPasswordInput.text = "";
            return;
        }
        PlayFabClientAPI.RegisterPlayFabUser(new RegisterPlayFabUserRequest()
        {
            Username = username,
            Email = email,
            DisplayName = username,
            Password = password,
            RequireBothUsernameAndEmail = true
        },
        result =>
        {
            Debug.Log("Registered and logged in!");
            // Save email and password
            PlayerPrefs.SetString(LAST_EMAIL_KEY, email);
            PlayerPrefs.SetString(LAST_PASSWORD_KEY, password);
            PlayerPrefs.SetString(LAST_USERNAME_KEY, username);
            Login(email, password);
        },
        PlayfabFailure);
    }

    private void Login(string email, string password)
    {
        PlayFabClientAPI.LoginWithEmailAddress(new LoginWithEmailAddressRequest()
        {
            Email = email,
            Password = password,
            InfoRequestParameters = new GetPlayerCombinedInfoRequestParams()
            {
                GetPlayerProfile = true
            }
        },
        result =>
        {
            Debug.Log("Logged in!");
            // Save email and password
            PlayerPrefs.SetString(LAST_EMAIL_KEY, email);
            PlayerPrefs.SetString(LAST_PASSWORD_KEY, password);

            // Set username as player prefs and Photon nickname
            string username = result.InfoResultPayload?.PlayerProfile?.DisplayName ?? "";
            if (string.IsNullOrEmpty(username))
                username = PlayerPrefs.GetString(LAST_USERNAME_KEY, "Player");
            PlayerPrefs.SetString(LAST_USERNAME_KEY, username);
            PhotonNetwork.NickName = username;
            loginWindow.SetActive(false);
        },
        error =>
        {
            Debug.Log("Login failed: " + error.ErrorMessage);
            errorMessage.text = "Login failed: " + error.ErrorMessage;
            SetLoginWindow();
            loginWindow.SetActive(true);
        });
    }
    private void PlayfabFailure(PlayFabError error)
    {
        Debug.Log("PlayFab API call failed: " + error.ErrorMessage);
        errorMessage.text = error.ErrorMessage;
    }

    public void ForgotPasswordPressed()
    {
        if (!resetWindowOpen)
        {
            SetResetWindow();
        }
        else
        {
            SetLoginWindow();
        }
    }

    public void ResetPassword(string email)
    {
        PlayFabClientAPI.SendAccountRecoveryEmail(new SendAccountRecoveryEmailRequest()
        {
            Email = email,
            TitleId = "158B53"
        },
        successResult =>
        {
            Debug.Log("Password reset email sent successfully.");
            errorMessage.text = "Reset email sent! Check your inbox.";
            SetLoginWindow();
        },
        PlayfabFailure);
    }
    private void SetLoginWindow()
    {
        onRegister = false;
        resetWindowOpen = false;
        titleText.text = "Login:";
        usernameField.SetActive(false);
        confirmPasswordField.SetActive(false);
        passworldField.SetActive(true);
        forgotPassword.gameObject.SetActive(true);
        switchButton.GetComponentInChildren<TMP_Text>().text = "No account? Register here.";
        submitButton.GetComponentInChildren<TMP_Text>().text = "Login";
        switchButton.gameObject.SetActive(true);
        errorMessage.text = "";
    }

    private void SetRegisterWindow()
    {
        onRegister = true;
        resetWindowOpen = false;
        titleText.text = "Register:";
        usernameField.SetActive(true);
        confirmPasswordField.SetActive(true);
        passworldField.SetActive(true);
        forgotPassword.gameObject.SetActive(false);
        switchButton.gameObject.SetActive(true);
        submitButton.GetComponentInChildren<TMP_Text>().text = "Register";
        switchButton.GetComponentInChildren<TMP_Text>().text = "Already have an account? Login here.";
        forgotPassword.GetComponentInChildren<TMP_Text>().text = "Forgot password";
        errorMessage.text = "";
    }

    private void SetResetWindow()
    {
        resetWindowOpen = true;
        titleText.text = "Password Reset:";
        usernameField.SetActive(false);
        confirmPasswordField.SetActive(false);
        passworldField.SetActive(false);
        forgotPassword.gameObject.SetActive(true);
        submitButton.GetComponentInChildren<TMP_Text>().text = "Send Email";
        forgotPassword.GetComponentInChildren<TMP_Text>().text = "Oh never mind, I remember it";
        switchButton.gameObject.SetActive(false);
        errorMessage.text = "";
    }

}
