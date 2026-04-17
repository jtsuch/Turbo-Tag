using UnityEngine;

/// <summary>
/// QuickAbility: launches the player straight up with a burst of vertical impulse.
/// Resets any downward Y-velocity first so the full force is applied upward.
///
/// Unity setup:
///  - Add this component to the player prefab alongside other abilities.
///  - Tune launchForce and launchCooldown in the Inspector.
/// </summary>
public class Launch : QuickAbility
{
    [Header("Launch Settings")]
    [SerializeField] private float launchForce    = 20f;
    [SerializeField] private float launchCooldown = 3f;

    protected override void Awake()
    {
        base.Awake();
        cooldownTime = launchCooldown;
    }

    protected override void OnKeyDown()
    {
        if (rb == null) return;

        // Cancel downward momentum so the full impulse goes upward
        Vector3 vel = rb.linearVelocity;
        if (vel.y < 0f) vel.y = 0f;
        rb.linearVelocity = vel;

        rb.AddForce(Vector3.up * launchForce, ForceMode.Impulse);
    }
}
