using UnityEngine;

/// <summary>Curse (from someone's Freeze!): you can't move for a few seconds.</summary>
public class FrozenEffect : PlayerEffect
{
    public override string DisplayName => "Frozen";
    public override string Description => "Someone froze everyone!";
    public override bool   IsGood      => false;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;

    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;

    }
}
