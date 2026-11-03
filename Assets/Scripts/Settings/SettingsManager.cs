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

    // ─── Defaults ─────────────────────────────────────────────────────────────
    public const float DefaultSensitivity = 100f; // 100 = raw 1:1 mouse movement
    public const int   DefaultTargetFps   = 144;
    public const float DefaultMusicVolume = 0.4f;
    public const float DefaultSfxVolume   = 0.55f;

    private const string SensitivityKey = "Settings_Sensitivity";
    private const string TargetFpsKey   = "Settings_TargetFPS";
    private const string MusicVolumeKey = "Settings_MusicVolume";
    private const string SfxVolumeKey   = "Settings_SfxVolume";

    // ─── Events ───────────────────────────────────────────────────────────────
    public static event Action<float> SensitivityChanged;
    public static event Action<int>   TargetFpsChanged;
    public static event Action<float> MusicVolumeChanged;
    public static event Action<float> SfxVolumeChanged;

    private float sensitivity = DefaultSensitivity;
    private int   targetFps   = DefaultTargetFps;
    private float musicVolume = DefaultMusicVolume;
    private float sfxVolume   = DefaultSfxVolume;

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
    }

    private void Save()
    {
        PlayerPrefs.SetFloat(SensitivityKey, sensitivity);
        PlayerPrefs.SetInt(TargetFpsKey,     targetFps);
        PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
        PlayerPrefs.SetFloat(SfxVolumeKey,   sfxVolume);
        PlayerPrefs.Save();
    }

    private void Apply()
    {
        QualitySettings.vSyncCount  = 0; // Required for targetFrameRate to take effect
        Application.targetFrameRate = targetFps;
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
            Application.targetFrameRate = value;
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
}
