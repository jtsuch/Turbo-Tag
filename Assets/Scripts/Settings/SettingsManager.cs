using System;
using UnityEngine;

/// <summary>
/// Persistent singleton that loads, applies and saves player settings (sensitivity, FPS cap,
/// volumes). Each property applies its change immediately, raises a static event so UI and
/// gameplay can react, and saves to PlayerPrefs.
/// Attach to: a manager GameObject in the Loading scene (DontDestroyOnLoad).
/// </summary>
public class SettingsManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    private static SettingsManager _instance;
    public static SettingsManager Instance => _instance;

    /// <summary>The settings, creating the manager if this scene was started directly (it normally comes from Loading).</summary>
    public static SettingsManager Ensure()
    {
        if (_instance == null) new GameObject("SettingsManager").AddComponent<SettingsManager>();
        return _instance;
    }

    // ─── Defaults ─────────────────────────────────────────────────────────────
    public const float DefaultSensitivity = 100f; // 100 = raw 1:1 mouse movement
    public const int   DefaultTargetFps   = 144;
    public const float DefaultMusicVolume = 0.4f;
    public const float DefaultSfxVolume   = 0.55f;
    public const float DefaultFieldOfView = 60f;   // Vertical degrees (the camera's own setting)
    public const float DefaultUiScale     = 1f;

    private const string SensitivityKey = "Settings_Sensitivity";
    private const string TargetFpsKey   = "Settings_TargetFPS";
    private const string MusicVolumeKey = "Settings_MusicVolume";
    private const string SfxVolumeKey   = "Settings_SfxVolume";
    private const string FovKey         = "Settings_FieldOfView";
    private const string InvertYKey     = "Settings_InvertY";
    private const string VSyncKey       = "Settings_VSync";
    private const string UiScaleKey     = "Settings_UiScale";

    // ─── Events ───────────────────────────────────────────────────────────────
    public static event Action<float> SensitivityChanged;
    public static event Action<int>   TargetFpsChanged;
    public static event Action<float> MusicVolumeChanged;
    public static event Action<float> SfxVolumeChanged;
    public static event Action<float> FieldOfViewChanged;
    public static event Action<bool>  InvertYChanged;
    public static event Action<float> UiScaleChanged;

    private float sensitivity = DefaultSensitivity;
    private int   targetFps   = DefaultTargetFps;
    private float musicVolume = DefaultMusicVolume;
    private float sfxVolume   = DefaultSfxVolume;
    private float fieldOfView = DefaultFieldOfView;
    private bool  invertY;
    private bool  vSync;
    private float uiScale     = DefaultUiScale;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        Load();
        Apply();
    }

    // ─── Persistence ──────────────────────────────────────────────────────────

    private void Load()
    {
        sensitivity = Mathf.Max(1f, PlayerPrefs.GetFloat(SensitivityKey, DefaultSensitivity));
        targetFps   = Mathf.Clamp(PlayerPrefs.GetInt(TargetFpsKey, DefaultTargetFps), 15, 500);
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume));
        sfxVolume   = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, DefaultSfxVolume));
        fieldOfView = Mathf.Clamp(PlayerPrefs.GetFloat(FovKey, DefaultFieldOfView), 50f, 100f);
        invertY     = PlayerPrefs.GetInt(InvertYKey, 0) == 1;
        vSync       = PlayerPrefs.GetInt(VSyncKey, 0) == 1;
        uiScale     = Mathf.Clamp(PlayerPrefs.GetFloat(UiScaleKey, DefaultUiScale), 0.8f, 1.25f);
    }

    private void Save()
    {
        PlayerPrefs.SetFloat(SensitivityKey, sensitivity);
        PlayerPrefs.SetInt(TargetFpsKey,     targetFps);
        PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
        PlayerPrefs.SetFloat(SfxVolumeKey,   sfxVolume);
        PlayerPrefs.SetFloat(FovKey,         fieldOfView);
        PlayerPrefs.SetInt(InvertYKey,       invertY ? 1 : 0);
        PlayerPrefs.SetInt(VSyncKey,         vSync ? 1 : 0);
        PlayerPrefs.SetFloat(UiScaleKey,     uiScale);
        PlayerPrefs.Save();
    }

    private void Apply()
    {
        ApplyFrameRate();
        AudioListener.volume        = sfxVolume;
        if (AudioManager.Instance != null) AudioManager.Instance.SetMusicVolume(musicVolume);
    }

    // ─── Settings ─────────────────────────────────────────────────────────────

    public float Sensitivity
    {
        get => sensitivity;
        set
        {
            value = Mathf.Max(1f, value);
            if (Mathf.Approximately(sensitivity, value)) return;
            sensitivity = value;
            Save();
            SensitivityChanged?.Invoke(value);
        }
    }

    public int TargetFps
    {
        get => targetFps;
        set
        {
            value = Mathf.Clamp(value, 15, 500);
            if (targetFps == value) return;
            targetFps = value;
            ApplyFrameRate();
            Save();
            TargetFpsChanged?.Invoke(value);
        }
    }

    /// <summary>0–1.</summary>
    public float MusicVolume
    {
        get => musicVolume;
        set
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(musicVolume, value)) return;
            musicVolume = value;
            if (AudioManager.Instance != null) AudioManager.Instance.SetMusicVolume(value);
            Save();
            MusicVolumeChanged?.Invoke(value);
        }
    }

    /// <summary>0–1. Applied as the global AudioListener volume.</summary>
    public float SfxVolume
    {
        get => sfxVolume;
        set
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(sfxVolume, value)) return;
            sfxVolume = value;
            AudioListener.volume = value;
            Save();
            SfxVolumeChanged?.Invoke(value);
        }
    }

    // ─── Look and controls ────────────────────────────────────────────────────

    /// <summary>Camera field of view in vertical degrees (50–100).</summary>
    public float FieldOfView
    {
        get => fieldOfView;
        set
        {
            value = Mathf.Clamp(value, 50f, 100f);
            if (Mathf.Approximately(fieldOfView, value)) return;
            fieldOfView = value;
            Save();
            FieldOfViewChanged?.Invoke(value);
        }
    }

    public bool InvertY
    {
        get => invertY;
        set
        {
            if (invertY == value) return;
            invertY = value;
            Save();
            InvertYChanged?.Invoke(value);
        }
    }

    /// <summary>Menu size on top of fitting the screen (0.8–1.25).</summary>
    public float UiScale
    {
        get => uiScale;
        set
        {
            value = Mathf.Clamp(value, 0.8f, 1.25f);
            if (Mathf.Approximately(uiScale, value)) return;
            uiScale = value;
            Save();
            UiScaleChanged?.Invoke(value);
        }
    }

    // ─── Display ──────────────────────────────────────────────────────────────
    // Resolution and window mode are remembered by Unity itself between launches.

    /// <summary>VSync on locks to the monitor's refresh rate (the frame cap then doesn't apply).</summary>
    public bool VSync
    {
        get => vSync;
        set
        {
            if (vSync == value) return;
            vSync = value;
            ApplyFrameRate();
            Save();
        }
    }

    private void ApplyFrameRate()
    {
        QualitySettings.vSyncCount  = vSync ? 1 : 0;
        Application.targetFrameRate = vSync ? -1 : targetFps; // targetFrameRate only works with VSync off
    }

    public static readonly FullScreenMode[] DisplayModes =
        { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };

    public static string DisplayModeName(FullScreenMode mode) => mode switch
    {
        FullScreenMode.ExclusiveFullScreen => "Fullscreen",
        FullScreenMode.Windowed            => "Windowed",
        _                                  => "Borderless",
    };

    public FullScreenMode DisplayMode
    {
        get => Screen.fullScreenMode == FullScreenMode.MaximizedWindow ? FullScreenMode.Windowed : Screen.fullScreenMode;
        set => Screen.SetResolution(Screen.width, Screen.height, value);
    }

    /// <summary>Every distinct resolution the monitor supports (largest first).</summary>
    public static System.Collections.Generic.List<Vector2Int> Resolutions()
    {
        var list = new System.Collections.Generic.List<Vector2Int>();
        foreach (var r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (!list.Contains(size)) list.Add(size);
        }
        var current = new Vector2Int(Screen.width, Screen.height);
        if (!list.Contains(current)) list.Add(current);
        list.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));
        return list;
    }

    public Vector2Int Resolution
    {
        get => new(Screen.width, Screen.height);
        set => Screen.SetResolution(value.x, value.y, Screen.fullScreenMode);
    }

    /// <summary>Graphics quality preset (Quality Settings levels).</summary>
    public int Quality
    {
        get => QualitySettings.GetQualityLevel();
        set => QualitySettings.SetQualityLevel(Mathf.Clamp(value, 0, QualitySettings.names.Length - 1), true);
    }
}
