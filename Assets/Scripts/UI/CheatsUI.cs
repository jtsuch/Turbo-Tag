using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CheatsUI : MonoBehaviour
{
    [Header("Input References")]
    public Slider speedSlider;
    public TMP_InputField speedInput;
    public Slider accelerationSlider;
    public TMP_InputField accelerationInput;
    public Slider jumpSlider;
    public TMP_InputField jumpInput;
    public Slider scaleSizeSlider;
    public TMP_InputField scaleSizeInput;



    private bool updating = false;
    private void Start()
    {
        // Initialize UI with current values
        if (Player.Instance != null)
        {
            //speedSlider.value = Player.Instance.SpeedMultiplier;
            //speedInput.text = Player.Instance.SpeedMultiplier.ToString("F2");
        }
    }


    /*                                              
    *  ______         ____        ________     _________      ________     ________                           
    *  |  _  \       /    \      /  ______\    |___  ___|    /  ____  \   /  ______\                                  
    *  | |_| /      / /_\  \     |  |______        | |      /  /    \__\  |  |______                           
    *  |  _  \     /  ____  \    \_______  \       | |      |  |     ___  \_______  \                                      
    *  | |_|  \   /  /    \  \   \   \___\  \   ___| |___   \  \____/  /  \   \___\  \                                       
    *  |______/  /__/      \__\   \_________/   |________|   \________/    \_________/                                           
    */

     /*
     * Speed Update
     */
    public void SliderSpeedMod(float value)
    {
        if (Player.Instance == null || updating) return;

        updating = true;
        speedInput.text = value.ToString("F2");
        Player.Instance.SpeedMultiplier = value / 100f;
        updating = false;
    }

    public void InputSpeedMod()
    {
        if (Player.Instance == null || updating) return;

        updating = true;
        if (float.TryParse(speedInput.text, out float newSpeed))
        {
            speedSlider.value = newSpeed;
            Player.Instance.SpeedMultiplier = newSpeed / 100f;
        }
        updating = false;
    }

    /*
     * Acceleration Update
     */
    public void SliderAccelerationMod(float value)
    {
        if (Player.Instance == null || updating) return;

        updating = true;
        accelerationInput.text = value.ToString("F2");
        Player.Instance.Acceleration = value * 0.2f;
        updating = false;
    }

    public void InputAccelerationMod()
    {
        if (Player.Instance == null || updating) return;

        updating = true;
        if (float.TryParse(accelerationInput.text, out float newAcceleration))
        {
            accelerationSlider.value = newAcceleration;
            Player.Instance.Acceleration = newAcceleration * 0.2f;
        }
        updating = false;
    }

    /*
     * Jump Update
     */
    public void SliderJumpMod(float value)
    {
        if (Player.Instance == null || updating) return;

        updating = true;
        jumpInput.text = value.ToString("F2");
        Player.Instance.JumpStrength = value * 0.16f;
        updating = false;
    }

    public void InputJumpMod()
    {
        if (Player.Instance == null || updating) return;

        updating = true;
        if (float.TryParse(jumpInput.text, out float newJump))
        {
            jumpSlider.value = newJump;
            Player.Instance.JumpStrength = newJump * 0.16f;
        }
        updating = false;
    }

    // Note to future me: in menu dropdown, allow player to alter each of three directions

    /*
     * Height Update
     */
    public void SliderScaleSizeMod(float value)
    {
        if (Player.Instance == null || updating) return;

        updating = true;
        scaleSizeInput.text = value.ToString("F2");
        Player.Instance.currentXScale = value / 100f;
        Player.Instance.currentYScale = value / 100f;
        Player.Instance.currentZScale = value / 100f;
        Player.Instance.SetPlayerScale();
        updating = false;
    }

    public void InputScaleSizeMod()
    {
        if (Player.Instance == null || updating) return;

        updating = true;
        if (float.TryParse(scaleSizeInput.text, out float newScaleSize))
        {
            scaleSizeSlider.value = newScaleSize;
            Player.Instance.currentXScale = newScaleSize / 100f;
            Player.Instance.currentYScale = newScaleSize / 100f;
            Player.Instance.currentZScale = newScaleSize / 100f;
            Player.Instance.SetPlayerScale();
        }
        updating = false;
    }

/*
    public void TogglePanel()
    {
        StopAllCoroutines();
        StartCoroutine(AnimatePanel(isExpanded ? 0 : expandedHeight));
        isExpanded = !isExpanded;
    }

    private IEnumerator AnimatePanel(float targetHeight)
    {
        float startHeight = contentLayout.preferredHeight;
        float time = 0;

        while (time < 1)
        {
            time += Time.unscaledDeltaTime * animationSpeed; // unscaled so it still animates when paused
            contentLayout.preferredHeight = Mathf.Lerp(startHeight, targetHeight, time);
            yield return null;
        }

        contentLayout.preferredHeight = targetHeight;
    }

    public void */
}
