using UnityEngine;

/// <summary>
/// While held, wall climbs never slow down (and can start from a standstill at the foot of a wall).
/// Sets <see cref="Player.SteadyClimbSpeed"/>, which PlayerMovement reads. Duration drains only
/// while actually climbing.
/// </summary>
[AbilityInfo("Hold to climb any wall in front of you at full speed, for as long as the meter lasts.")]
public class InfiniteClimb : BasicAbility
{
    // Search & Destroy upgrade path: the value at levels 0, 1 and 2 (AbilityUpgrades reads it; Justin's table, Nov 14)
    private static readonly AbilityUpgrades.Entry Upgrades = AbilityUpgrades.Power("climb speed", 8f / 9f, 1f, 10f / 9f, v => $"{v * 9f:0.#} m/s");
    public override AbilityUpgrades.Entry UpgradePath => Upgrades;
    // What every screen plays when this is used (each ability owns its own: Ability.PlayUseFx)
    public override void PlayUseFx(Player player, Vector3 feet, Vector3 chest, bool mine)
    {
        Sfx.At(SfxKind.ZapUp, chest, 0.6f);
        Vfx.Burst(chest, Fx.Yellow, 10, 2f, 0.2f, 0.5f, direction: Vector3.up, spread: 0.6f);
    }

    [Header("Climb")]
    [TunableParam("Climb Speed", 2f, 30f)]
    [SerializeField] private float climbSpeed = 9f;

    private bool held;

    protected override void OnPress()   => held = true;
    protected override void OnRelease() => Deactivate();

    protected override void Deactivate()
    {
        held     = false;
        IsActive = false;
        if (Owner != null) Owner.SteadyClimbSpeed = 0f;
    }

    protected override void FixedUpdate()
    {
        if (IsLocal && held)
        {
            bool canClimb = CurrentDuration > 0.01f;
            Owner.SteadyClimbSpeed = canClimb ? climbSpeed * UpgradePower : 0f;   // S&D: 8 → 10 m/s with upgrades
            IsActive = canClimb && Owner.State == Player.MovementState.Climb;
        }

        base.FixedUpdate();
    }
}
