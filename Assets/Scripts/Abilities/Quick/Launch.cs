using UnityEngine;

/// <summary>
/// Launches the player straight up. Downward velocity is cancelled first so the full impulse
/// always applies.
/// </summary>
[AbilityInfo("Launches you straight up into the sky.")]
public class Launch : QuickAbility
{
    [Header("Launch")]
    [TunableParam("Launch Force", 5f, 300f)]
    [SerializeField] private float launchForce = 20f;

    protected override void Activate()
    {
        Vector3 velocity = Body.linearVelocity;
        if (velocity.y < 0f) velocity.y = 0f;
        Body.linearVelocity = velocity;

        Body.AddForce(Vector3.up * launchForce * Mathf.Sqrt(Owner.AbilityPower), ForceMode.Impulse);
    }
}
