using UnityEngine;

/// <summary>Boon (Keep Away): your throws bend toward the nearest teammate ahead.</summary>
public class HomingThrowEffect : PlayerEffect
{
    public override string DisplayName => "Homing Throw";
    public override string Description => "Throws curve to a teammate";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        KeepAwayDisc.HomingThrow = true;
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        KeepAwayDisc.HomingThrow = false;
    }
}
