using UnityEngine;

/// <summary>Boon (gun modes): no damage at all; you glow gold on every screen so everyone knows to run.</summary>
public class InvulnerableEffect : PlayerEffect
{
    public override string DisplayName => "Invulnerable";
    public override string Description => "Nothing can hurt you (you glow gold)";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.SetFlag(PlayerFlag.Golden, true);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.SetFlag(PlayerFlag.Golden, false);
    }
}
