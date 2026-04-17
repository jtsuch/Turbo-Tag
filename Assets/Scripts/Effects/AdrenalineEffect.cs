using UnityEngine;

/// <summary>
/// Effect: multiplies the player's movement speed by <see cref="speedMultiplier"/> for
/// the effect duration, then restores the original SpeedMultiplier.
/// Calls Player.SetState to flush targetSpeed immediately on apply and restore.
/// </summary>
public class AdrenalineEffect : PlayerEffect
{
    [Tooltip("Speed multiplier applied on top of the player's current SpeedMultiplier.")]
    [SerializeField] private float speedMultiplier = 1.5f;

    private float originalSpeedMultiplier;

    // -------------------------------------------------------------------------
    // PlayerEffect
    // -------------------------------------------------------------------------

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect || player == null) return;

        originalSpeedMultiplier = player.SpeedMultiplier;
        player.SpeedMultiplier  = originalSpeedMultiplier * speedMultiplier;

        // Flush targetSpeed so the change takes effect in the current movement state
        player.SetState(player.currentState);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;

        player.SpeedMultiplier = originalSpeedMultiplier;
        player.SetState(player.currentState);
    }
}
