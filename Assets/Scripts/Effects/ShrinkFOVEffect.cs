using UnityEngine;

/// <summary>
/// Effect: narrows the player's field of view for the effect duration,
/// then restores it to whatever it was before the effect was applied.
/// Uses PlayerCam.ChangeFov (DOTween tween) for smooth transitions.
/// </summary>
public class ShrinkFOVEffect : PlayerEffect
{
    [Tooltip("FOV to tween to while the effect is active.")]
    [SerializeField] private float shrunkenFOV = 40f;

    private float originalFOV;

    // -------------------------------------------------------------------------
    // PlayerEffect
    // -------------------------------------------------------------------------

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;

        Camera cam = GetCamera();
        if (cam == null) return;

        originalFOV = cam.fieldOfView;
        PlayerCam.Instance.ChangeFov(shrunkenFOV);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect) return;

        // Guard against null refs during scene cleanup
        if (PlayerCam.Instance == null) return;

        PlayerCam.Instance.ChangeFov(originalFOV);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Camera GetCamera()
    {
        if (PlayerCam.Instance != null && PlayerCam.Instance.cam != null)
            return PlayerCam.Instance.cam;
        return Camera.main;
    }
}
