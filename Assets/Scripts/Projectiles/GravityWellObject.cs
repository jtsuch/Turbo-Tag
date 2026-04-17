using UnityEngine;
using Photon.Pun;

/// <summary>
/// Placed object for the GravityWell TrapAbility.
/// Continuously applies a centripetal pull force to any Rigidbody inside a cylindrical zone
/// that extends along the object's forward axis.
///
/// Pull strength scales linearly from <see cref="pullForce"/> at the device to 0 at
/// <see cref="maxRange"/> distance.  The cylinder's cross-sectional radius is
/// <see cref="cylinderRadius"/>.
///
/// Network behaviour:
///   - All clients apply the pull force to their own local player's Rigidbody each FixedUpdate.
///   - Only the owning client applies force to non-player Rigidbodies (environment objects,
///     crates, etc.) to avoid physics conflicts between clients.
///
/// Unity setup:
///  - Attach to the GravityWell prefab (Resources/Object/GravityWell).
///  - Prefab also needs: Rigidbody (Kinematic ✓, Use Gravity ✗), Collider (Is Trigger ✓),
///    PhotonView.
///  - The object's forward axis (+Z) points into the pull zone.
///    Orient the prefab so that forward faces away from the wall it is mounted on.
///  - Optionally assign humSFX for ambient audio.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GravityWellObject : MonoBehaviourPun
{
    [Header("Pull Settings")]
    [SerializeField] private float pullForce     = 25f;
    [SerializeField] private float maxRange      = 10f;   // End of the cylinder and falloff distance
    [SerializeField] private float cylinderRadius = 3f;   // Cross-sectional radius of the pull zone

    [Header("Audio")]
    [SerializeField] private AudioClip humSFX;
    [SerializeField] private float humVolume = 0.5f;

    private static readonly Collider[] overlapBuffer = new Collider[32];

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        if (humSFX != null)
        {
            AudioSource hum = gameObject.AddComponent<AudioSource>();
            hum.clip         = humSFX;
            hum.loop         = true;
            hum.spatialBlend = 1f;
            hum.rolloffMode  = AudioRolloffMode.Linear;
            hum.minDistance  = 1f;
            hum.maxDistance  = maxRange * 2f;
            hum.volume       = humVolume;
            hum.Play();
        }
    }

    private void FixedUpdate()
    {
        // All clients: pull the local player if they are in the cylinder
        ApplyPullToLocalPlayer();

        // Owner only: pull non-networked environment rigidbodies
        if (photonView.IsMine)
            ApplyPullToNearbyRigidbodies();
    }

    // -------------------------------------------------------------------------
    // Pull logic
    // -------------------------------------------------------------------------

    private void ApplyPullToLocalPlayer()
    {
        if (Player.Instance == null || Player.Instance.rb == null) return;
        TryApplyPull(Player.Instance.rb);
    }

    private void ApplyPullToNearbyRigidbodies()
    {
        // Use NonAlloc to avoid allocating every physics tick
        int count = Physics.OverlapSphereNonAlloc(transform.position, maxRange + cylinderRadius, overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col == null) continue;

            // Players are handled in ApplyPullToLocalPlayer; skip them here
            if (col.TryGetComponent<Player>(out _)) continue;

            Rigidbody rb = col.attachedRigidbody;
            if (rb == null || rb.isKinematic) continue;

            TryApplyPull(rb);
        }
    }

    /// <summary>
    /// Applies pull force to <paramref name="rb"/> if it is inside the cylinder, scaling
    /// strength by distance from the well.
    /// </summary>
    private void TryApplyPull(Rigidbody rb)
    {
        Vector3 pos = rb.position;
        if (!IsInCylinder(pos)) return;

        float dist     = Vector3.Distance(pos, transform.position);
        float strength = pullForce * Mathf.Clamp01(1f - dist / maxRange);
        Vector3 dir    = (transform.position - pos).normalized;

        rb.AddForce(dir * strength, ForceMode.Force);
    }

    // -------------------------------------------------------------------------
    // Cylinder check
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true if <paramref name="worldPos"/> lies inside the cylindrical pull zone.
    /// The cylinder extends from the well along its forward axis (0 → maxRange) with radius
    /// <see cref="cylinderRadius"/>.
    /// </summary>
    private bool IsInCylinder(Vector3 worldPos)
    {
        Vector3 toPos        = worldPos - transform.position;
        float   forwardDist  = Vector3.Dot(toPos, transform.forward);

        // Must be in front of the well and not past the maximum range
        if (forwardDist < 0f || forwardDist > maxRange) return false;

        // Perpendicular distance from the forward axis must be within the cylinder radius
        Vector3 axialComponent    = transform.forward * forwardDist;
        Vector3 radialComponent   = toPos - axialComponent;
        return radialComponent.magnitude <= cylinderRadius;
    }

    // -------------------------------------------------------------------------
    // Debug visualisation (editor only)
    // -------------------------------------------------------------------------

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Draw the cylinder as a wireframe approximation
        Gizmos.color = new Color(0.4f, 0f, 1f, 0.4f);
        int segments = 16;
        Vector3 origin = transform.position;
        Vector3 tip    = origin + transform.forward * maxRange;

        for (int i = 0; i < segments; i++)
        {
            float angle0 = (i / (float)segments) * Mathf.PI * 2f;
            float angle1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;

            Vector3 right = transform.right;
            Vector3 up    = transform.up;

            Vector3 p0Base = origin + (right * Mathf.Cos(angle0) + up * Mathf.Sin(angle0)) * cylinderRadius;
            Vector3 p1Base = origin + (right * Mathf.Cos(angle1) + up * Mathf.Sin(angle1)) * cylinderRadius;
            Vector3 p0Tip  = tip    + (right * Mathf.Cos(angle0) + up * Mathf.Sin(angle0)) * cylinderRadius;
            Vector3 p1Tip  = tip    + (right * Mathf.Cos(angle1) + up * Mathf.Sin(angle1)) * cylinderRadius;

            Gizmos.DrawLine(p0Base, p1Base);
            Gizmos.DrawLine(p0Tip,  p1Tip);
            Gizmos.DrawLine(p0Base, p0Tip);
        }
    }
#endif
}
