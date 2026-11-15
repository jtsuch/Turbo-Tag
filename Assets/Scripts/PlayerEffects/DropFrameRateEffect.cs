using UnityEngine;

/// <summary>Curse: the local game drops to 15 FPS. Restores the frame cap and VSync afterwards.</summary>
public class DropFrameRateEffect : PlayerEffect
{
    [SerializeField] private int frameRate = 15;

    private int originalTargetFrameRate;
    private int originalVSyncCount;

    public override string DisplayName => "Potato Mode";
    public override string Description => $"{frameRate} FPS";
    public override bool   IsGood      => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        originalTargetFrameRate = Application.targetFrameRate;
        originalVSyncCount      = QualitySettings.vSyncCount;

        QualitySettings.vSyncCount  = 0;   // Must be 0 for targetFrameRate to take effect
        Application.targetFrameRate = frameRate;
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect) return;
        QualitySettings.vSyncCount  = originalVSyncCount;
        Application.targetFrameRate = originalTargetFrameRate;
    }
}
