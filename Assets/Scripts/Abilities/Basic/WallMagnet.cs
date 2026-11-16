using UnityEngine;

/// <summary>
/// Hold to stick to any surface: walls and ceilings become floors (walk into a wall to climb onto it, walk off
/// an edge to wrap around it, jump off and fall back to the same surface). Let go and normal gravity returns.
/// A short meter that runs down while moving or in the air, and slower (<c>stillDrain</c>) while standing still, so
/// nobody can park on a ceiling forever.
/// The walking itself lives in PlayerMovement (<see cref="Player.MagnetUp"/>); the turned view in PlayerCam.
/// </summary>
[AbilityInfo("Hold to walk on walls and ceilings like they're the floor. Standing still drains the meter slower.")]
public class WallMagnet : BasicAbility
{
    // Search & Destroy upgrade path: the value at levels 0, 1 and 2 (AbilityUpgrades reads it; Justin's table, Nov 14)
    private static readonly AbilityUpgrades.Entry Upgrades = AbilityUpgrades.Bar(4f, 6f, 8f);
    public override AbilityUpgrades.Entry UpgradePath => Upgrades;
    // What every screen plays when this is used (each ability owns its own: Ability.PlayUseFx)
    public override void PlayUseFx(Player player, Vector3 feet, Vector3 chest, bool mine)
    {
        Sfx.At(SfxKind.Snap, feet, 0.8f);
        Sfx.At(SfxKind.ZapDown, chest, 0.5f);
        Vfx.Ring(feet, Fx.Violet, 16, 3f, 0.25f, 0.4f);
    }

    [Tooltip("Meter drain while standing still on a surface (1 = as fast as moving).")]
    [TunableParam("Still Drain", 0f, 1f)]
    [SerializeField] private float stillDrain = 0.4f;

    private bool held;

    protected override float DrainRate
    {
        get
        {
            var movement = Owner.Movement;
            bool standingStill = movement != null && movement.MagnetGrounded
                && Owner.Input.MoveInput.sqrMagnitude < 0.01f && Body.linearVelocity.sqrMagnitude < 1f;
            return standingStill ? stillDrain : 1f;
        }
    }

    protected override void OnPress()
    {
        if (CurrentDuration <= 0.01f) return;
        held = true;
        IsActive = true;
        if (!Owner.IsMagnetized) Owner.MagnetUp = Vector3.up;
    }

    protected override void OnRelease() => Deactivate();

    protected override void Deactivate()
    {
        held     = false;
        IsActive = false;
        if (Owner != null) Owner.MagnetUp = null;
    }

    protected override void FixedUpdate()
    {
        if (IsLocal && held && !IsActive) Deactivate();
        base.FixedUpdate();
    }
}
