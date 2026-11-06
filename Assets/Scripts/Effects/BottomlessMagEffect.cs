/// <summary>Boon (gun modes): shots don't use ammo.</summary>
public class BottomlessMagEffect : PlayerEffect
{
    public override string DisplayName => "Bottomless Mag";
    public override string Description => "Shots don't use ammo";

    protected override void OnEffectStart()
    {
        if (IsLocalEffect && player.Gun != null) player.Gun.InfiniteAmmo = true;
    }

    protected override void OnEffectEnd()
    {
        if (IsLocalEffect && player != null && player.Gun != null) player.Gun.InfiniteAmmo = false;
    }
}
