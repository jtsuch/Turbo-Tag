using UnityEngine;

/// <summary>Curse (from someone's Time Warp): half speed, lower jumps.</summary>
public class SlowedEffect : PlayerEffect
{
    public override string DisplayName => "Time Warped";
    public override string Description => "Half speed";
    public override bool   IsGood      => false;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.Modifiers.Set(Stat.Speed, this, 0.5f);
        player.Modifiers.Set(Stat.Jump, this, 0.75f);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.Modifiers.Remove(Stat.Speed, this);
        player.Modifiers.Remove(Stat.Jump, this);
    }
}
