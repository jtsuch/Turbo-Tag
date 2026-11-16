using UnityEngine;

/// <summary>
/// Hold to raise a bubble: while it's up, other players' abilities can't touch you (blasts, pulls, ice, traps,
/// flashes, Grow Ray, Honk, Fish Bait). Your own still work. Sets <see cref="Player.ShieldHeld"/>; the player syncs
/// <see cref="Player.IsShielded"/> and <see cref="ShieldBubble"/> draws it on every screen. Every effect checks it
/// through <see cref="Player.AffectedBy"/>. Guns are not abilities: bullets still hit.
/// </summary>
[AbilityInfo("Hold to raise a bubble: other players' abilities can't touch you while it's up.", DisplayName = "Ability Shield")]
public class Shield : BasicAbility
{
    // Search & Destroy upgrade path: the value at levels 0, 1 and 2 (AbilityUpgrades reads it; Justin's table, Nov 14)
    private static readonly AbilityUpgrades.Entry Upgrades = AbilityUpgrades.Bar(2f, 3f, 4f);
    public override AbilityUpgrades.Entry UpgradePath => Upgrades;
    // What every screen plays when this is used (each ability owns its own: Ability.PlayUseFx)
    public override void PlayUseFx(Player player, Vector3 feet, Vector3 chest, bool mine)
    {
        Sfx.At(SfxKind.Shimmer, chest, 0.9f);
        Vfx.Burst(chest, Fx.Cyan, 24, 4f, 0.3f, 0.6f);
    }

    private bool held;

    protected override void OnPress()
    {
        if (CurrentDuration <= 0.01f) return;
        held = true;
        IsActive = true;
        Owner.ShieldHeld = true;
    }

    protected override void OnRelease() => Deactivate();

    protected override void Deactivate()
    {
        held     = false;
        IsActive = false;
        if (Owner != null && IsLocal) Owner.ShieldHeld = false;
    }

    protected override void FixedUpdate()
    {
        if (IsLocal && !held && Owner.ShieldHeld) Owner.ShieldHeld = false;
        base.FixedUpdate();
    }
}
