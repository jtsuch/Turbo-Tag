using UnityEngine;

/// <summary>
/// While held, wall climbs never slow down (and can start from a standstill at the foot of a wall).
/// Sets <see cref="Player.SteadyClimbSpeed"/>, which PlayerMovement reads. Duration drains only
/// while actually climbing.
/// </summary>
[AbilityInfo("Hold to climb any wall in front of you at full speed, for as long as the meter lasts.")]
public class InfiniteClimb : BasicAbility
{
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
            Owner.SteadyClimbSpeed = canClimb ? climbSpeed : 0f;
            IsActive = canClimb && Owner.State == Player.MovementState.Climb;
        }

        base.FixedUpdate();
    }
}
