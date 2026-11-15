using UnityEngine;

/// <summary>Boon (Keep Away): your throws fly twice as fast (and so twice as far).</summary>
public class RocketDiscEffect : PlayerEffect
{
    public override string DisplayName => "Rocket Disc";
    public override string Description => "Throws go twice as fast";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        KeepAwayDisc.ThrowBoost = 2f;
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        KeepAwayDisc.ThrowBoost = 1f;
    }
}
