using UnityEngine;

/// <summary>
/// Boon (escape): jams the hunters' gadgets. While it lasts no hunter tool can light you up (X-Ray Scan, Beacon,
/// Sonar, Alarm, Tracker Dart) and the hunter radar draws no circle for you. Synced as <see cref="PlayerFlag.Jammed"/>,
/// which <see cref="HunterReveal"/> and the minimap's radar read on the hunters' screens.
/// </summary>
public class JammerEffect : PlayerEffect
{
    public override string DisplayName => "Jammer";
    public override string Description => "Hunter gadgets and radar can't find you";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.SetFlag(PlayerFlag.Jammed, true);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.SetFlag(PlayerFlag.Jammed, false);
    }
}
