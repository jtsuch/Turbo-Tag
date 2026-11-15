using UnityEngine;

/// <summary>Boon: every other player moves at half speed for a few seconds (shields block it).</summary>
public class TimeWarpEffect : PlayerEffect
{
    public override string DisplayName => "Time Warp";
    public override string Description => "Everyone else at half speed";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => true;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        if (player.TryGetComponent(out DonutNet net)) net.TimeWarp(5f);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;

    }
}
