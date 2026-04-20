using UnityEngine;
using System;

/// <summary>
/// Persistent singleton that loads, saves, and applies global settings (sensitivity, FPS cap,
/// volume). Each property setter applies the change immediately, fires a static event for any
/// interested listeners (e.g. PlayerCam), and persists the new value via PlayerPrefs.
/// Attach to: a DontDestroyOnLoad manager GameObject — one instance for the entire session.
/// </summary>
public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    // ─── Events ───────────────────────────────────────────────────────────────
    public static event Action<float> OnSensitivityChanged;
    public static event Action<int>   OnFPSChanged;
    public static event Action<float> OnMusicVolumeChanged;
    public static event Action<float> OnSfxVolumeChanged;

    // ─── Backing Fields ───────────────────────────────────────────────────────
    [Header("Settings")]
    [SerializeField] private float sensitivity   = 100f;
    [SerializeField] private int   targetFPS     = 60;
    [SerializeField] private float musicVolume   = 100f;
    [SerializeField] private float sfxVolume     = 100f;

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadSettings();
        ApplySettings();
    }

    // ─── Save / Load ──────────────────────────────────────────────────────────

    public void LoadSettings()
    {
        sensitivity = PlayerPrefs.GetFloat("Sensitivity", 1f);
        targetFPS   = PlayerPrefs.GetInt("TargetFPS", 60);
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("MusicVolume", 0.5f));
        sfxVolume   = Mathf.Clamp01(PlayerPrefs.GetFloat("SfxVolume", 0.7f));
    }

    public void SaveSettings()
    {
        PlayerPrefs.SetFloat("Sensitivity",  sensitivity);
        PlayerPrefs.SetInt("TargetFPS",      targetFPS);
        PlayerPrefs.SetFloat("MusicVolume",  musicVolume);
        PlayerPrefs.SetFloat("SfxVolume",    sfxVolume);
        PlayerPrefs.Save();
    }

    private void ApplySettings()
    {
        Application.targetFrameRate = targetFPS;
        AudioListener.volume        = sfxVolume;
        // AudioManager may not exist yet on first load; it reads MusicVolume in its own Start()
        if (AudioManager.Instance != null)
            AudioManager.Instance.SetMusicVolume(musicVolume);
    }

    // ─── Accessors ────────────────────────────────────────────────────────────

    public float Sensitivity
    {
        get => sensitivity;
        set
        {
            if (Mathf.Approximately(sensitivity, value)) return;
            sensitivity = value;
            OnSensitivityChanged?.Invoke(value);
            SaveSettings();
        }
    }

    public int TargetFPS
    {
        get => targetFPS;
        set
        {
            if (targetFPS == value) return;
            targetFPS = value;
            Application.targetFrameRate = value;
            OnFPSChanged?.Invoke(value);
            SaveSettings();
        }
    }

    public float MusicVolume
    {
        get => musicVolume;
        set
        {
            if (Mathf.Approximately(musicVolume, value)) return;
            musicVolume = Mathf.Clamp01(value);
            if (AudioManager.Instance != null)
                AudioManager.Instance.SetMusicVolume(musicVolume);
            OnMusicVolumeChanged?.Invoke(musicVolume);
            SaveSettings();
        }
    }

    public float SfxVolume
    {
        get => sfxVolume;
        set
        {
            if (Mathf.Approximately(sfxVolume, value)) return;
            sfxVolume            = Mathf.Clamp01(value);
            AudioListener.volume = sfxVolume;
            OnSfxVolumeChanged?.Invoke(sfxVolume);
            SaveSettings();
        }
    }
}