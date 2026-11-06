using UnityEngine;

/// <summary>
/// Boon: one extra jump while airborne, with the full jump height whatever the fall speed. Recharges on landing.
/// Not straight after leaving an edge (the normal coyote-time jump gets that press).
/// </summary>
public class DoubleJumpEffect : PlayerEffect
{
    private bool  hasDoubleJumped;
    private float lastGrounded;

    public override string DisplayName => "Double Jump";
    public override string Description => "Jump again in mid-air";

    protected override void OnEffectStart() { }
    protected override void OnEffectEnd()   { }

    private void Update()
    {
        if (!IsLocalEffect) return;

        if (player.IsGrounded)
        {
            hasDoubleJumped = false;
            lastGrounded = Time.time;
            return;
        }

        if (hasDoubleJumped || !player.Input.JumpPressed || Time.time - lastGrounded < 0.15f) return;
        if (player.IsMagnetized || player.State == Player.MovementState.Hang) return;
        hasDoubleJumped = true;

        // Sets the rise like a normal jump (an impulse would depend on the body's mass)
        Vector3 velocity = player.Body.linearVelocity;
        velocity.y = player.Stats.jumpStrength * player.Modifiers.Get(Stat.Jump);
        player.Body.linearVelocity = velocity;
        player.NotifyJumped();
        Vfx.Ring(player.transform.position, new Color(1f, 1f, 1f, 0.8f), 14, 3f, 0.3f, 0.35f);
        Sfx.Here(SfxKind.Jump, 0.6f, 1.3f);
    }
}
