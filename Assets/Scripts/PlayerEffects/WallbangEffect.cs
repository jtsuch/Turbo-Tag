using UnityEngine;

/// <summary>Boon (gun modes): bullets pass straight through the level.</summary>
public class WallbangEffect : PlayerEffect
{
    public override string DisplayName => "Wallbang";
    public override string Description => "Bullets go through walls";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.Gun.Wallbang = true;
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.Gun.Wallbang = false;
    }
}
