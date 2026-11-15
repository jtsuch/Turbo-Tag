using UnityEngine;

/// <summary>Boon (chasing): everyone within 15 m is slowly pulled toward you.</summary>
public class MagnetEffect : PlayerEffect
{
    public override string DisplayName => "Magnet";
    public override string Description => "Everyone nearby is pulled to you";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.SetFlag(PlayerFlag.Magnet, true);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.SetFlag(PlayerFlag.Magnet, false);
    }
}
