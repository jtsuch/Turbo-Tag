using UnityEngine;

/// <summary>Boon (escape): pass through other players (so no tag can land), see-through like a ghost.</summary>
public class PhantomEffect : PlayerEffect
{
    public override string DisplayName => "Phantom";
    public override string Description => "Pass through players; can't be tagged";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.SetFlag(PlayerFlag.Phantom, true);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.SetFlag(PlayerFlag.Phantom, false);
    }
}
