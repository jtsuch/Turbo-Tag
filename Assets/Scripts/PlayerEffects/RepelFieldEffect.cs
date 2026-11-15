using UnityEngine;

/// <summary>Boon (escape): anyone who comes within a few metres is shoved away (on their own screen).</summary>
public class RepelFieldEffect : PlayerEffect
{
    public override string DisplayName => "Repel Field";
    public override string Description => "Anyone close is pushed away";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.SetFlag(PlayerFlag.Repel, true);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.SetFlag(PlayerFlag.Repel, false);
    }
}
