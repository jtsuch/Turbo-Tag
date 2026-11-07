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
