using UnityEngine;

/// <summary>Boon (gun modes): any gun fires full-auto at triple speed without using ammo.</summary>
public class MinigunEffect : PlayerEffect
{
    public override string DisplayName => "Minigun";
    public override string Description => "Full-auto, triple speed, no reloads";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.Gun.ForceAutomatic = true;
        player.Gun.FireRateMultiplier = 3f;
        player.Gun.InfiniteAmmo = true;
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.Gun.ForceAutomatic = false;
        player.Gun.FireRateMultiplier = 1f;
        player.Gun.InfiniteAmmo = false;
    }
}
