using UnityEngine;

/// <summary>Boon (Keep Away): your team scores double while you hold the disc.</summary>
public class PointFrenzyEffect : PlayerEffect
{
    public override string DisplayName => "Point Frenzy";
    public override string Description => "Double points while you hold it";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.SetFlag(PlayerFlag.DoubleScore, true);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.SetFlag(PlayerFlag.DoubleScore, false);
    }
}
