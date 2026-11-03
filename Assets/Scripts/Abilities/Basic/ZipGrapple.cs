using UnityEngine;

/// <summary>
/// Zip line: while the key is held, the player is pulled quickly toward the anchor and the rope shortens
/// behind them, so they can't swing back out. Near the anchor they hang there until they let go.
/// </summary>
[AbilityInfo("Hold to fire a rope that reels you straight to where it hit.")]
public class ZipGrapple : GrappleAbility
{
    [Header("Zip")]
    [Tooltip("Top speed toward the anchor (m/s).")]
    [TunableParam("Zip Speed", 5f, 80f)]
    [SerializeField] private float zipSpeed = 30f;

    [Tooltip("How quickly the player reaches zip speed (m/s²). Also cancels most of gravity on the way.")]
    [TunableParam("Zip Acceleration", 10f, 400f)]
    [SerializeField] private float zipAcceleration = 120f;

    [Tooltip("Within this distance of the anchor the pull stops and the player just hangs (m).")]
    [SerializeField] private float arriveDistance = 1.5f;

    [Tooltip("Least rope stiffness while zipping, so the player can hang at the anchor once there (a higher Springiness wins).")]
    [SerializeField] private float holdSpring = 60f;

    protected override void WhileAttached(Vector3 anchor)
    {
        Vector3 toAnchor = anchor - Body.position;
        float distance = toAnchor.magnitude;

        // The rope never lets the player drift back out, and is firm enough to hang from
        Joint.maxDistance = Mathf.Min(Joint.maxDistance, distance);
        Joint.spring      = Mathf.Max(Joint.spring, holdSpring);

        Vector3 desired = distance > arriveDistance ? toAnchor / distance * zipSpeed : Vector3.zero;
        Vector3 change  = Vector3.ClampMagnitude(desired - Body.linearVelocity, zipAcceleration * Time.fixedDeltaTime);
        Body.AddForce(change, ForceMode.VelocityChange);
    }
}
