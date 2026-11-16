using UnityEngine;

/// <summary>
/// Hold to switch off your gravity: you keep drifting the way you were going (jump while holding it and you
/// float up and away). Movement keys gently curve the drift toward where you look, without adding speed, so
/// you can steer a float but not fly; when nearly still you drift slowly that way. The meter drains only in
/// the air. Gravity goes through PlayerMovement (<see cref="Player.ZeroGravity"/>), its only writer.
/// </summary>
[AbilityInfo("Hold to turn off your gravity and drift. Movement keys gently curve your float.")]
public class ZeroGravity : BasicAbility
{
    // Search & Destroy upgrade path: the value at levels 0, 1 and 2 (AbilityUpgrades reads it; Justin's table, Nov 14)
    private static readonly AbilityUpgrades.Entry Upgrades = AbilityUpgrades.Bar(3.5f, 4f, 4.5f);
    public override AbilityUpgrades.Entry UpgradePath => Upgrades;
    // What every screen plays when this is used (each ability owns its own: Ability.PlayUseFx)
    public override void PlayUseFx(Player player, Vector3 feet, Vector3 chest, bool mine)
    {
        Sfx.At(SfxKind.Shimmer, chest, 0.7f, 45f, 0.7f);
        Vfx.Burst(chest, Fx.Violet, 20, 2f, 0.3f, 1f);
    }

    [Header("Float")]
    [Tooltip("How fast the drift turns toward where you steer (degrees per second).")]
    [TunableParam("Steering", 0f, 360f)]
    [SerializeField] private float turnRate = 70f;
    [Tooltip("Below this speed the keys nudge you that way (m/s); above it they only turn the drift.")]
    [TunableParam("Drift Speed", 0f, 15f)]
    [SerializeField] private float driftSpeed = 3.5f;
    [SerializeField] private float driftAcceleration = 4f;

    [Header("References")]
    [Tooltip("The camera (steering is relative to where you look). Defaults to the local camera.")]
    [SerializeField] private Transform cam;

    private bool held;

    protected override bool Draining => !Owner.IsGrounded;

    protected override void OnPress()
    {
        if (CurrentDuration <= 0.01f) return;
        held = true;
        IsActive = true;
    }

    protected override void OnRelease() => Deactivate();

    protected override void Deactivate()
    {
        held     = false;
        IsActive = false;
        if (Owner != null) Owner.ZeroGravity = false;
    }

    protected override void FixedUpdate()
    {
        if (IsLocal && held)
        {
            Owner.ZeroGravity = true;
            if (!Owner.IsGrounded) Steer();
        }
        base.FixedUpdate();
    }

    private void Steer()
    {
        Vector2 input = Owner.CanMove ? Owner.Input.MoveInput : Vector2.zero;
        if (input.sqrMagnitude < 0.01f) return;

        Transform view = cam != null ? cam : PlayerCam.Local != null ? PlayerCam.Local.Camera.transform : transform;
        Vector3 wish = (view.forward * input.y + view.right * input.x).normalized;
        Vector3 v = Body.linearVelocity;
        float speed = v.magnitude;

        // Turn the drift toward the wish; speed stays the same
        if (speed > 0.5f)
            Body.linearVelocity = Vector3.RotateTowards(v, wish * speed, turnRate * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
        // Nearly still: a slow nudge so a stranded floater isn't stuck forever
        if (speed < driftSpeed)
            Body.AddForce(wish * driftAcceleration, ForceMode.Acceleration);
        Owner.NotePushed(); // Movement's air braking mustn't cancel the drift
    }
}
