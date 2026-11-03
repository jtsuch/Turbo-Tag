using System;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

/// <summary>
/// PlayFab account calls used by the main menu. Passwords are never stored: after a
/// successful email login the device is linked to the account, and later sessions sign in
/// silently with that device id until the player logs out.
/// </summary>
public static class AccountService
{
    private const string RememberDeviceKey = "Account_RememberDevice";
    private const string LastEmailKey      = "LAST_EMAIL";
    private const string LegacyPasswordKey = "LAST_PASSWORD"; // Older builds stored this in plain text

    private static string DeviceId => SystemInfo.deviceUniqueIdentifier;

    public static string LastEmail     => PlayerPrefs.GetString(LastEmailKey, "");
    public static bool   HasLoggedInBefore => PlayerPrefs.HasKey(LastEmailKey);

    /// <summary>
    /// Silent login only in the first copy of the game on this PC. The device id and PlayerPrefs are shared by
    /// every copy on one machine, so a second window (a second exe or a Multiplayer Play Mode clone) would
    /// otherwise sign into the same account as the first.
    /// </summary>
    public static bool   CanAutoLogin  => !IsSecondaryInstance && PlayerPrefs.GetInt(RememberDeviceKey, 0) == 1;

    /// <summary>True for every copy of the game on this PC after the first one.</summary>
    public static bool IsSecondaryInstance
    {
        get
        {
            if (secondaryInstance == null)
            {
#if UNITY_EDITOR
                // Multiplayer Play Mode clones run from the main project's Library/VP folder.
                secondaryInstance = Application.dataPath.Replace('\\', '/').Contains("/Library/VP/");
#else
                // The first copy holds a machine-wide named mutex for as long as it runs.
                instanceMutex = new System.Threading.Mutex(true, "TurboTag_FirstInstance", out bool createdNew);
                secondaryInstance = !createdNew;
#endif
            }

            return secondaryInstance.Value;
        }
    }

    private static bool? secondaryInstance;
#if !UNITY_EDITOR
    private static System.Threading.Mutex instanceMutex; // Kept alive so the first copy keeps the mutex
#endif

    /// <summary>Removes credentials saved by older versions of the game.</summary>
    public static void PurgeLegacyCredentials()
    {
        if (!PlayerPrefs.HasKey(LegacyPasswordKey)) return;
        PlayerPrefs.DeleteKey(LegacyPasswordKey);
        PlayerPrefs.Save();
    }

    public static void Register(string username, string email, string password,
                                Action<string> onSuccess, Action<string> onError)
    {
        PlayFabClientAPI.RegisterPlayFabUser(new RegisterPlayFabUserRequest
        {
            Username                    = username,
            Email                       = email,
            DisplayName                 = username,
            Password                    = password,
            RequireBothUsernameAndEmail = true,
        },
        _ => Login(email, password, onSuccess, onError),
        error => onError(error.ErrorMessage));
    }

    /// <summary>Email login. On success returns the display name and remembers this device.</summary>
    public static void Login(string email, string password, Action<string> onSuccess, Action<string> onError)
    {
        PlayFabClientAPI.LoginWithEmailAddress(new LoginWithEmailAddressRequest
        {
            Email                 = email,
            Password              = password,
            InfoRequestParameters = ProfileRequest,
        },
        result =>
        {
            PlayerPrefs.SetString(LastEmailKey, email);
            RememberDevice();
            onSuccess(DisplayNameOf(result));
        },
        error => onError(error.ErrorMessage));
    }

    /// <summary>Silent login with the linked device id.</summary>
    public static void AutoLogin(Action<string> onSuccess, Action<string> onError)
    {
        PlayFabClientAPI.LoginWithCustomID(new LoginWithCustomIDRequest
        {
            CustomId              = DeviceId,
            CreateAccount         = false,
            InfoRequestParameters = ProfileRequest,
        },
        result => onSuccess(DisplayNameOf(result)),
        error =>
        {
            ForgetDevice();
            onError(error.ErrorMessage);
        });
    }

    public static void SendPasswordReset(string email, Action onSuccess, Action<string> onError)
    {
        PlayFabClientAPI.SendAccountRecoveryEmail(new SendAccountRecoveryEmailRequest
        {
            Email   = email,
            TitleId = PlayFabSettings.staticSettings.TitleId,
        },
        _ => onSuccess(),
        error => onError(error.ErrorMessage));
    }

    public static void Logout()
    {
        ForgetDevice();
        PlayFabClientAPI.ForgetAllCredentials();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static GetPlayerCombinedInfoRequestParams ProfileRequest => new() { GetPlayerProfile = true };

    private static string DisplayNameOf(LoginResult result) =>
        result.InfoResultPayload?.PlayerProfile?.DisplayName ?? "";

    private static void RememberDevice()
    {
        // A second copy must not move this PC's device link to its own account (the first copy's login uses it).
        if (IsSecondaryInstance) return;

        PlayFabClientAPI.LinkCustomID(new LinkCustomIDRequest { CustomId = DeviceId, ForceLink = true },
            _ =>
            {
                PlayerPrefs.SetInt(RememberDeviceKey, 1);
                PlayerPrefs.Save();
            },
            error => Debug.LogWarning($"[AccountService] Could not remember this device: {error.ErrorMessage}"));
    }

    private static void ForgetDevice()
    {
        PlayerPrefs.DeleteKey(RememberDeviceKey);
        PlayerPrefs.Save();
    }
}
