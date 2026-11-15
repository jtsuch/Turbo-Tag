using UnityEngine;

/// <summary>Boon: abilities hit harder (bigger blasts and pulls, longer dashes and launches).</summary>
public class OverdriveEffect : PlayerEffect
{
    public override string DisplayName => "Ability Overdrive";
    public override string Description => "Abilities twice as strong";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.AbilityPower = 2f;
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.AbilityPower = 1f;
    }
}
