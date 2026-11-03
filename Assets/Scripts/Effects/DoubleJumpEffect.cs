using UnityEngine;

/// <summary>
/// Effect: grants one extra jump while airborne. The second jump cancels falling speed so it
/// always gets the full jump height. Recharges on landing.
/// </summary>
public class DoubleJumpEffect : PlayerEffect
{
    private bool hasDoubleJumped;

    protected override void OnEffectStart() { }
    protected override void OnEffectEnd()   { }

    private void Update()
    {
        if (!IsLocalEffect) return;

        if (player.IsGrounded)
        {
            hasDoubleJumped = false;
            return;
        }

        if (hasDoubleJumped || !player.Input.JumpPressed) return;
        hasDoubleJumped = true;

        Vector3 velocity = player.Body.linearVelocity;
        velocity.y = 0f;
        player.Body.linearVelocity = velocity;

        player.Body.AddForce(Vector3.up * (player.Stats.jumpStrength * player.Modifiers.Get(Stat.Jump)), ForceMode.Impulse);
        player.NotifyJumped();
    }
}
