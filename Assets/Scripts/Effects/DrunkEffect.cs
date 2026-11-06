/// <summary>Curse: a wobbly, drifting, tilting camera (the admin "dizzy").</summary>
public class DrunkEffect : PlayerEffect
{
    public override string DisplayName => "Drunk";
    public override string Description => "Wobbly camera";
    public override bool   IsGood      => false;

    protected override void OnEffectStart()
    {
        if (IsLocalEffect && PlayerCam.Local != null) PlayerCam.Local.Dizzy(Duration);
    }

    protected override void OnEffectEnd()
    {
        if (IsLocalEffect && PlayerCam.Local != null) PlayerCam.Local.StopAdminEffects();
    }
}
