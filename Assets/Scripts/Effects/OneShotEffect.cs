using UnityEngine;

/// <summary>Boon (gun modes): every hit is lethal.</summary>
public class OneShotEffect : PlayerEffect
{
    public override string DisplayName => "One Shot";
    public override string Description => "Any hit kills";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.Gun.DamageMultiplier = 100f;
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.Gun.DamageMultiplier = 1f;
    }
}
