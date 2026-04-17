using UnityEngine;

/// <summary>
/// Drops the local client's target frame rate to 10 FPS for the effect duration.
/// Restores the original frame rate cap and vsync setting on end.
/// </summary>
public class DropFrameRateEffect : PlayerEffect
{
    private int originalTargetFrameRate;
    private int originalVSyncCount;

    protected override void OnEffectStart()
    {
        originalTargetFrameRate = Application.targetFrameRate;
        originalVSyncCount      = QualitySettings.vSyncCount;

        QualitySettings.vSyncCount  = 0;   // Must be 0 for targetFrameRate to take effect
        Application.targetFrameRate = 10;
    }

    protected override void OnEffectEnd()
    {
        QualitySettings.vSyncCount  = originalVSyncCount;
        Application.targetFrameRate = originalTargetFrameRate;
    }
}
