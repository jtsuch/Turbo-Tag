using UnityEngine;

/// <summary>Boon (escape): triple movement speed.</summary>
public class SuperSpeedEffect : PlayerEffect
{
    public override string DisplayName => "Super Speed";
    public override string Description => "Triple speed";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.Modifiers.Set(Stat.Speed, this, 3f);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.Modifiers.Remove(Stat.Speed, this);
    }
}
