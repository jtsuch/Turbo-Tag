using UnityEngine;

/// <summary>
/// Turns the player nearly invisible (to others) while they stay still, for up to
/// <see cref="cloakDuration"/> seconds. Moving at all (keys, jumping, being pushed) ends it early.
/// Looking around is fine. The look is synced by <see cref="PlayerAppearance"/>.
/// </summary>
[AbilityInfo("Turn nearly invisible for 5 seconds. Moving at all breaks it, so pick your spot first.")]
public class Cloak : QuickAbility
{
    [Header("Cloak")]
    [TunableParam("Duration", 1f, 30f)]
    [SerializeField] private float cloakDuration = 5f;
    [Tooltip("Speed that counts as moving (m/s), so tiny physics drift doesn't break the cloak.")]
    [SerializeField] private float moveTolerance = 0.6f;
    [Tooltip("Grace after activating, for leftover momentum to settle (s).")]
    [SerializeField] private float settleTime = 0.25f;

    private float startTime = float.NegativeInfinity;
    private bool  cloaked;

    protected override void Activate()
    {
        if (Owner.Appearance == null) return;
        startTime = Time.time;
        cloaked   = true;
        Owner.Appearance.Cloaked = true;
        Owner.Caught += EndCloak;
    }

    private void FixedUpdate()
    {
        if (!cloaked || !IsLocal) return;

        float elapsed = Time.time - startTime;
        if (elapsed > cloakDuration) { EndCloak(); return; }
        if (elapsed < settleTime) return;

        var input = Owner.Input;
        Vector3 v = Body.linearVelocity;
        bool moving = input.MoveInput.sqrMagnitude > 0.01f || input.JumpPressed
                   || new Vector2(v.x, v.z).magnitude > moveTolerance || Mathf.Abs(v.y) > moveTolerance * 3f;
        if (moving) EndCloak();
    }

    // The cloak survives pauses and other abilities; only moving, the timer, being caught or unequipping end it
    public override void Cancel() { }

    protected override void OnDisable()
    {
        base.OnDisable();
        EndCloak();
    }

    private void EndCloak()
    {
        if (!cloaked) return;
        cloaked = false;
        if (Owner == null) return;
        Owner.Caught -= EndCloak;
        if (Owner.Appearance != null) Owner.Appearance.Cloaked = false;
    }
}
