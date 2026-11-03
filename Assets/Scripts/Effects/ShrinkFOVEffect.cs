using UnityEngine;

/// <summary>Effect: narrows the local camera's field of view for the duration.</summary>
public class ShrinkFOVEffect : PlayerEffect
{
    [Tooltip("FOV to tween to while the effect is active.")]
    [SerializeField] private float shrunkenFOV = 40f;

    protected override void OnEffectStart()
    {
        if (IsLocalEffect && PlayerCam.Local != null) PlayerCam.Local.ChangeFov(shrunkenFOV);
    }

    protected override void OnEffectEnd()
    {
        if (IsLocalEffect && PlayerCam.Local != null) PlayerCam.Local.ResetFov();
    }
}
