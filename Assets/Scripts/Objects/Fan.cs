using UnityEngine;
using Photon.Pun;

/// <summary>
/// Continuously blows nearby Rigidbodies in the fan's forward direction.
/// Only applies force to locally-owned objects (PhotonView.IsMine) so the
/// effect isn't stacked across multiple clients in a PUN2 session.
///
/// Unity setup:
///   - Attach to the fan root GameObject.
///   - Assign spinPart to the rotating mesh child (base stays separate).
///   - Set affectedLayers to the Player layer (and any physics props to push).
///   - The spinning mesh needs no collider; the blow zone is detected via OverlapSphere.
/// </summary>
public class Fan : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform spinPart;

    [Header("Blow Settings")]
    [SerializeField] private float force      = 25f;    // ForceMode.Acceleration, so mass-independent
    [SerializeField] private float range      = 6f;     // Sphere radius around the fan origin
    [SerializeField] private float coneAngle  = 50f;    // Half-angle of the blow cone in degrees
    [SerializeField] private LayerMask affectedLayers = ~0;

    [Header("Spin Settings")]
    [SerializeField] private float spinSpeed  = 360f;   // Degrees per second

    // ─── Blade spin ───────────────────────────────────────────────────────────

    private void Update()
    {
        if (spinPart != null)
            spinPart.Rotate(Vector3.forward, spinSpeed * Time.deltaTime, Space.Self);
    }

    // ─── Wind force ───────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, range, affectedLayers);
        foreach (Collider col in hits)
        {
            Rigidbody rb = col.attachedRigidbody;
            if (rb == null) continue;

            // Only apply to objects this client owns; remote Rigidbodies are driven by
            // their owner's PhotonView, so pushing them here would cause jitter.
            PhotonView pv = col.GetComponentInParent<PhotonView>();
            if (pv != null && !pv.IsMine) continue;

            Vector3 toTarget = col.transform.position - transform.position;
            if (Vector3.Angle(transform.forward, toTarget) > coneAngle) continue;

            rb.AddForce(transform.forward * force, ForceMode.Acceleration);
        }
    }

    // ─── Scene-view gizmos ────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        // Range sphere
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, range);

        // Cone edges (4 cardinal directions)
        Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
        Vector3 fwd = transform.forward * range;
        Gizmos.DrawRay(transform.position, Quaternion.Euler( coneAngle,  0,         0) * fwd);
        Gizmos.DrawRay(transform.position, Quaternion.Euler(-coneAngle,  0,         0) * fwd);
        Gizmos.DrawRay(transform.position, Quaternion.Euler( 0,          coneAngle, 0) * fwd);
        Gizmos.DrawRay(transform.position, Quaternion.Euler( 0,         -coneAngle, 0) * fwd);
    }
}
