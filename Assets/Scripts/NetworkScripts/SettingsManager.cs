using UnityEngine;
using System;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    // --- EVENTS ---
    public static event Action<float> OnSensitivityChanged;
    public static event Action<int> OnFPSChanged;
    public static event Action<float> OnVolumeChanged;

    // --- SETTINGS DATA ---
    [Header("Settings")]
    [SerializeField] private float sensitivity = 100f;
    [SerializeField] private int targetFPS = 60;
    [SerializeField] private float volume = 100f;

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

    // --- SAVE / LOAD ---

    public void LoadSettings()
    {
        sensitivity = PlayerPrefs.GetFloat("Sensitivity", 1f);
        targetFPS = PlayerPrefs.GetInt("TargetFPS", 60);
        volume = PlayerPrefs.GetFloat("Volume", 1f);
    }

    public void SaveSettings()
    {
        PlayerPrefs.SetFloat("Sensitivity", sensitivity);
        PlayerPrefs.SetInt("TargetFPS", targetFPS);
        PlayerPrefs.SetFloat("Volume", volume);
        PlayerPrefs.Save();
    }

    private void ApplySettings()
    {
        // Apply FPS limit
        Application.targetFrameRate = targetFPS;

        // Apply volume globally (assuming Unity Audio Listener)
        AudioListener.volume = volume;
    }

    // --- ACCESSORS ---

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

    public float Volume
    {
        get => volume;
        set
        {
            if (Mathf.Approximately(volume, value)) return;
            volume = Mathf.Clamp01(value);
            AudioListener.volume = volume;
            OnVolumeChanged?.Invoke(value);
            SaveSettings();
        }
    }
}