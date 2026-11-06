/// <summary>Curse: the screen turns upside down.</summary>
public class UpsideDownEffect : PlayerEffect
{
    public override string DisplayName => "Upside Down";
    public override string Description => "Your screen is flipped";
    public override bool   IsGood      => false;

    protected override void OnEffectStart()
    {
        if (IsLocalEffect && PlayerCam.Local != null) PlayerCam.Local.SetUpsideDown(true);
    }

    protected override void OnEffectEnd()
    {
        if (IsLocalEffect && PlayerCam.Local != null) PlayerCam.Local.SetUpsideDown(false);
    }
}
