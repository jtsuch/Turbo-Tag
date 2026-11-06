using UnityEngine;

/// <summary>
/// Hold to stick to any surface: walls and ceilings become floors (walk into a wall to climb onto it, walk off
/// an edge to wrap around it, jump off and fall back to the same surface). Let go and normal gravity returns.
/// A short meter that only runs down while moving or in the air: standing still on any surface rests it.
/// The walking itself lives in PlayerMovement (<see cref="Player.MagnetUp"/>); the turned view in PlayerCam.
/// </summary>
[AbilityInfo("Hold to walk on walls and ceilings like they're the floor. Standing still saves the meter.")]
public class WallMagnet : BasicAbility
{
    private bool held;

    protected override bool Draining
    {
        get
        {
            var movement = Owner.Movement;
            bool standingStill = movement != null && movement.MagnetGrounded
                && Owner.Input.MoveInput.sqrMagnitude < 0.01f && Body.linearVelocity.sqrMagnitude < 1f;
            return !standingStill;
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
