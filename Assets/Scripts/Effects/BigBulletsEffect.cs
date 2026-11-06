using UnityEngine;

/// <summary>Boon (gun modes): bullets do double damage (and so knock harder).</summary>
public class BigBulletsEffect : PlayerEffect
{
    public override string DisplayName => "Big Bullets";
    public override string Description => "Double damage, extra knockback";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.Gun.DamageMultiplier = 2f;
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.Gun.DamageMultiplier = 1f;
    }
}
