using UnityEngine;

/// <summary>Boon (Keep Away): nobody can steal the disc from you.</summary>
public class ButterHandsEffect : PlayerEffect
{
    public override string DisplayName => "Butter Hands";
    public override string Description => "The disc can't be stolen from you";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.SetFlag(PlayerFlag.Unstealable, true);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.SetFlag(PlayerFlag.Unstealable, false);
    }
}
