using UnityEngine;

/// <summary>Boon (gun modes): a bullet that hits a wall bounces off it once.</summary>
public class RicochetEffect : PlayerEffect
{
    public override string DisplayName => "Ricochet";
    public override string Description => "Bullets bounce once off walls";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.Gun.Ricochet = true;
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.Gun.Ricochet = false;
    }
}
