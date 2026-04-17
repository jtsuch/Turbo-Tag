using UnityEngine;

/// <summary>
/// Effect: allows the player one extra jump while airborne.
/// The second jump resets Y-velocity before applying the impulse so the player
/// always gets the full JumpStrength regardless of their current fall speed.
/// The double-jump charge resets each time the player lands.
/// </summary>
public class DoubleJumpEffect : PlayerEffect
{
    private bool hasDoubleJumped = false;

    // -------------------------------------------------------------------------
    // PlayerEffect
    // -------------------------------------------------------------------------

    protected override void OnEffectStart()  { /* nothing to set up */ }
    protected override void OnEffectEnd()    { /* nothing to restore */ }

    // -------------------------------------------------------------------------
    // Input polling — runs every frame while the component is alive
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (!IsLocalEffect) return;
        if (player.rb == null || player.Input == null) return;

        // Reset charge when the player touches the ground
        if (player.IsGrounded)
        {
            hasDoubleJumped = false;
            return;
        }

        // Second jump: airborne + jump key + charge available
        if (!hasDoubleJumped && player.Input.Jump)
        {
            hasDoubleJumped = true;

            // Cancel downward momentum so the full impulse is applied upward
            Vector3 vel = player.rb.linearVelocity;
            vel.y = 0f;
            player.rb.linearVelocity = vel;

            player.rb.AddForce(Vector3.up * player.JumpStrength, ForceMode.Impulse);
            player.TriggerJump(); // Fires animation/audio events
        }
    }
}
