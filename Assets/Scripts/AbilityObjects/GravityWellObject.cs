using UnityEngine;
using Photon.Pun;

/// <summary>
/// Placed object for the GravityWell TrapAbility: a gravity spike. Anything inside its zone is yanked
/// straight down, hard. The zone is a cylinder along the device's forward axis (+Z, away from the
/// surface it was placed on): a column above it on a floor, a tube out from it on a wall.
///
/// Network behaviour: each client yanks only the bodies it has authority over (its own player and
/// local props), so the effect is consistent without syncing forces. Power scales the yank.
///
/// Unity setup: Resources/Object/GravityWell with Rigidbody (kinematic, no gravity), a trigger
/// collider and a PhotonView. Optionally assign humSFX.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GravityWellObject : MonoBehaviourPun
{
    [Header("Pull Settings")]
    [Tooltip("Downward speed the well yanks bodies to (m/s).")]
    [TunableParam("Yank Speed", 5f, 200f)]
    [SerializeField] private float yankSpeed        = 70f;
    [Tooltip("How quickly bodies reach that speed (m/s²).")]
    [TunableParam("Yank Acceleration", 20f, 2000f)]
    [SerializeField] private float yankAcceleration = 600f;
    [TunableParam("Range", 2f, 40f)]
    [SerializeField] private float maxRange      = 10f;   // End of the cylinder and falloff distance
    [TunableParam("Radius", 0.5f, 15f)]
    [SerializeField] private float cylinderRadius = 4f;   // Cross-sectional radius of the pull zone

    [Header("Audio")]
    [SerializeField] private AudioClip humSFX;
    [SerializeField] private float humVolume = 0.5f;

    private static readonly Collider[] overlapBuffer = new Collider[32];
    private readonly System.Collections.Generic.HashSet<Rigidbody> pulled = new();
    private float power = 1f;

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake() => ObjectTunables.Apply(photonView); // Cheat values from the placer

    private void Start()
    {
        power = SpawnData.Power(photonView);

        if (humSFX != null)
        {
            AudioSource hum = gameObject.AddComponent<AudioSource>();
            WorldFx.Configure3D(hum, maxRange * 2f, humVolume);
            hum.clip = humSFX;
            hum.loop = true;
            hum.Play();
        }
    }

    // Each client pulls only the bodies it has authority over (its own player, local props)
    private void FixedUpdate()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, maxRange + cylinderRadius, overlapBuffer,
                                                  Physics.AllLayers, QueryTriggerInteraction.Ignore);
        pulled.Clear();
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = overlapBuffer[i].attachedRigidbody;
            if (rb == null || rb.isKinematic || !pulled.Add(rb)) continue;
            if (WorldFx.HasAuthority(rb)) TryApplyPull(rb);
        }
    }

    // ─── Pull logic ──────────────────────────────────────────────────────────

    /// <summary>Yanks <paramref name="rb"/> straight down if it is inside the zone.</summary>
    private void TryApplyPull(Rigidbody rb)
    {
        if (!IsInCylinder(rb.position)) return;

        // Accelerate toward a fast downward speed (never slows a body already falling faster)
        float targetY = -yankSpeed * power;
        float vy      = rb.linearVelocity.y;
        if (vy <= targetY) return;
        float change = Mathf.Max(targetY - vy, -yankAcceleration * power * Time.fixedDeltaTime);
        WorldFx.Push(rb, Vector3.up * change, ForceMode.VelocityChange);
    }

    // ─── Cylinder check ──────────────────────────────────────────────────────

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

    // ─── Debug visualisation (editor only) ───────────────────────────────────

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
