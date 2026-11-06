using UnityEngine;

/// <summary>
/// Glider: while held in the air, caps fall speed and boosts air control; while still rising it holds up
/// part of gravity, so a jump (or launch) with Flappy held goes higher and further. Drains duration only
/// while actually airborne.
/// </summary>
[AbilityInfo("Hold in the air to glide: you fall slowly, steer much better and jumps carry higher.")]
public class Flappy : BasicAbility
{
    [Header("Glide")]
    [TunableParam("Glide Fall Speed", 0f, 10f)]
    [SerializeField] private float glideFallSpeed = 2f;
    [TunableParam("Air Control Boost", 1f, 20f)]
    [SerializeField] private float airControlMultiplier = 5f;
    [Tooltip("Share of gravity the wings hold up while still rising, so a held jump carries higher and further.")]
    [TunableParam("Rising Lift", 0f, 1f)]
    [SerializeField] private float risingLift = 0.55f;

    private bool held;

    /// <summary>True while gliding (held in the air), on the owner. The animation controller syncs it.</summary>
    public bool IsGliding => IsActive;

    protected override void OnPress()   => held = true;
    protected override void OnRelease() => Deactivate();

    protected override void Deactivate()
    {
        held     = false;
        IsActive = false;
        if (Owner != null) Owner.Modifiers.Remove(Stat.AirControl, this);
    }

    // Runs at the physics rate so gliding feels the same at any frame rate
    protected override void FixedUpdate()
    {
        if (IsLocal && held)
        {
            IsActive = !Owner.IsGrounded;
            if (IsActive)
            {
                // Air control stays boosted for the whole glide; falling is limited, rising is carried on
                Owner.Modifiers.Set(Stat.AirControl, this, airControlMultiplier);
                if (Body.linearVelocity.y > 0f && Body.useGravity)
                    Body.AddForce(-Physics.gravity * risingLift, ForceMode.Acceleration);
                if (Body.linearVelocity.y < -glideFallSpeed)
                    Body.linearVelocity = new Vector3(Body.linearVelocity.x, -glideFallSpeed, Body.linearVelocity.z);
            }
            else
            {
                Owner.Modifiers.Remove(Stat.AirControl, this);
            }
        }

        base.FixedUpdate();
    }
}
