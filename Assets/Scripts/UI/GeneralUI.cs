using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Photon.Pun;

public class GeneralUI : MonoBehaviourPunCallbacks
{
    [Header("Input References")]
    public Slider senseSlider;
    public TMP_InputField senseInput;
    public Slider volumeSlider;
    public TMP_InputField volumeInput;
    public Slider fpsSlider;
    public TMP_InputField fpsInput;

    private bool updatingUI = false;

    void Start()
    {
        // Load settings from SettingsManager
        var sm = SettingsManager.Instance;

        senseSlider.value = sm.Sensitivity;
        senseInput.text = sm.Sensitivity.ToString("F2");

        volumeSlider.value = sm.Volume;
        volumeInput.text = sm.Volume.ToString("F2");

        fpsSlider.value = sm.TargetFPS;
        fpsInput.text = sm.TargetFPS.ToString();

        // Subscribe to changes
        SettingsManager.OnSensitivityChanged += HandleSensitivityChanged;
        SettingsManager.OnVolumeChanged += HandleVolumeChanged;
        SettingsManager.OnFPSChanged += HandleFPSChanged;
    }

    void OnDestroy()
    {
        // Unsubscribe to prevent memory leaks
        SettingsManager.OnSensitivityChanged -= HandleSensitivityChanged;
        SettingsManager.OnVolumeChanged -= HandleVolumeChanged;
        SettingsManager.OnFPSChanged -= HandleFPSChanged;
    }

    /*
     * SENSITIVITY
     */
    public void SliderSense(float value)
    {
        if (updatingUI) return;
        updatingUI = true;
        senseInput.text = value.ToString("F2");
        updatingUI = false;
        SettingsManager.Instance.Sensitivity = value;
    }

    public void InputSense()
    {
        if (updatingUI) return;
        if (float.TryParse(senseInput.text, out float newSens))
        {
            updatingUI = true;
            senseSlider.value = newSens;
            updatingUI = false;
            SettingsManager.Instance.Sensitivity = newSens;
        }
    }

    private void HandleSensitivityChanged(float newValue)
    {
        updatingUI = true;
        senseSlider.value = newValue;
        senseInput.text = newValue.ToString("F2");
        updatingUI = false;
    }

    /*
     * VOLUME
     */
    public void SliderVolume(float value)
    {
        if (updatingUI) return;
        updatingUI = true;
        volumeInput.text = value.ToString("F2");
        updatingUI = false;
        SettingsManager.Instance.Volume = value;
    }

    public void InputVolume()
    {
        if (updatingUI) return;
        if (float.TryParse(volumeInput.text, out float newVol))
        {
            updatingUI = true;
            volumeSlider.value = newVol;
            updatingUI = false;
            SettingsManager.Instance.Volume = newVol;
        }
    }

    private void HandleVolumeChanged(float newValue)
    {
        updatingUI = true;
        volumeSlider.value = newValue;
        volumeInput.text = newValue.ToString("F2");
        updatingUI = false;
    }

    /*
     * FPS
     */
    public void SliderFPS(float value)
    {
        if (updatingUI) return;
        updatingUI = true;
        fpsInput.text = value.ToString("F0");
        updatingUI = false;
        SettingsManager.Instance.TargetFPS = (int)value;
    }

    public void InputFPS()
    {
        if (updatingUI) return;
        if (int.TryParse(fpsInput.text, out int newFps))
        {
            updatingUI = true;
            fpsSlider.value = newFps;
            updatingUI = false;
            SettingsManager.Instance.TargetFPS = newFps;
        }
    }

    private void HandleFPSChanged(int newValue)
    {
        updatingUI = true;
        fpsSlider.value = newValue;
        fpsInput.text = newValue.ToString();
        updatingUI = false;
    }

    public void QuitButtonPress()
    {
        Debug.Log("Quiting Game...");
        PhotonNetwork.LeaveRoom();
    }

    public override void OnLeftRoom()
    {
        Debug.Log("Left Room");
        // Quit application whether or not in editor
#if UNITY_STANDALONE
        Application.Quit();
#endif
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
        //PhotonNetwork.LoadLevel("MainMenu");
    }

}
