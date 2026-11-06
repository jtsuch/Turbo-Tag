/// <summary>Curse: everyone else sees you through walls (synced: PlayerAppearance draws the X-ray pass on their screens).</summary>
public class BeaconEffect : PlayerEffect
{
    public override string DisplayName => "Beacon";
    public override string Description => "Everyone sees you through walls";
    public override bool   IsGood      => false;

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) player.IsBeacon = true;
    }

    protected override void OnEffectEnd()
    {
        if (IsLocalEffect && player != null) player.IsBeacon = false;
    }
}
