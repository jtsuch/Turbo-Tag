using UnityEngine;

/// <summary>Curse: tunnel vision. The local camera's field of view narrows (dash and teleport kicks still work on top).</summary>
public class ShrinkFOVEffect : PlayerEffect
{
    [Tooltip("Share of the normal field of view while the effect lasts.")]
    [SerializeField] private float fovScale = 0.6f;

    public override string DisplayName => "Tunnel Vision";
    public override string Description => "Your view is squeezed";
    public override bool   IsGood      => false;

    protected override void OnEffectStart()
    {
        if (IsLocalEffect && PlayerCam.Local != null) PlayerCam.Local.SetFovScale(fovScale);
    }

    protected override void OnEffectEnd()
    {
        if (IsLocalEffect && PlayerCam.Local != null) PlayerCam.Local.SetFovScale(1f);
    }
}
