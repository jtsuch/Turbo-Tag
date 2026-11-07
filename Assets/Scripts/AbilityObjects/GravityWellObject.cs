using UnityEngine;
using Photon.Pun;

/// <summary>
/// Placed object for the GravityWell TrapAbility: a gravity spike. Every other player inside a tall column above it
/// (<see cref="maxRange"/> high, <see cref="cylinderRadius"/> wide, straight up whatever surface it's on) is yanked
/// straight down, hard; loose props too. Its placer is left alone.
///
/// Network behaviour: each client yanks only what it has authority over: its own player (checked directly, so a
/// level full of colliders can't crowd them out of an overlap query, which is how players used to slip through)
/// and local props. Power scales the yank.
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
    [SerializeField] private float yankSpeed        = 90f;
    [Tooltip("How quickly bodies reach that speed (m/s²).")]
    [TunableParam("Yank Acceleration", 20f, 3000f)]
    [SerializeField] private float yankAcceleration = 900f;
    [Tooltip("Height of the pull column above the well (m).")]
    [TunableParam("Height", 2f, 80f)]
    [SerializeField] private float maxRange       = 35f;
    [TunableParam("Radius", 0.5f, 15f)]
    [SerializeField] private float cylinderRadius = 5f;

    private const float Below = 1.5f; // The column starts a little under the well (feet on the same floor count)

    [Header("Audio")]
    [SerializeField] private AudioClip humSFX;
    [SerializeField] private float humVolume = 0.5f;

    private static readonly Collider[] overlapBuffer = new Collider[256];
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
            WorldFx.Configure3D(hum, 30f, humVolume);
            hum.clip = humSFX;
            hum.loop = true;
            hum.Play();
        }
    }

    private void FixedUpdate()
    {
        // This client's own player (never the placer)
        var local = Player.Local;
        if (local != null && local.Body != null && local.ActorNumber != photonView.OwnerActorNr && !local.IsCaught
            && local.AffectedBy(photonView.OwnerActorNr) && InColumn(local.Body.position))
            Yank(local.Body);

        // Loose props this client simulates
        Vector3 bottom = transform.position + Vector3.down * Below, top = transform.position + Vector3.up * maxRange;
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, cylinderRadius, overlapBuffer, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        pulled.Clear();
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = overlapBuffer[i].attachedRigidbody;
            if (rb == null || rb.isKinematic || !pulled.Add(rb) || rb.GetComponent<Player>() != null) continue;
            if (WorldFx.HasAuthority(rb) && InColumn(rb.position)) Yank(rb);
        }
    }

    // ─── Pull logic ──────────────────────────────────────────────────────────

    /// <summary>Accelerates <paramref name="rb"/> toward a fast downward speed (never slows one already falling faster).</summary>
    private void Yank(Rigidbody rb)
    {
        float targetY = -yankSpeed * power;
        float vy      = rb.linearVelocity.y;
        if (vy <= targetY) return;
        float change = Mathf.Max(targetY - vy, -yankAcceleration * power * Time.fixedDeltaTime);
        WorldFx.Push(rb, Vector3.up * change, ForceMode.VelocityChange, photonView.OwnerActorNr, "GravityWell");
    }

    /// <summary>Inside the vertical column: within the radius sideways, from just under the well to its height.</summary>
    private bool InColumn(Vector3 worldPos)
    {
        Vector3 to = worldPos - transform.position;
        if (to.y < -Below || to.y > maxRange) return false;
        return new Vector2(to.x, to.z).magnitude <= cylinderRadius;
    }

    // ─── Debug visualisation (editor only) ───────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0f, 1f, 0.4f);
        const int segments = 16;
        Vector3 origin = transform.position + Vector3.down * Below, tip = transform.position + Vector3.up * maxRange;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
            Vector3 p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * cylinderRadius;
            Vector3 p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * cylinderRadius;
            Gizmos.DrawLine(origin + p0, origin + p1);
            Gizmos.DrawLine(tip + p0, tip + p1);
            Gizmos.DrawLine(origin + p0, tip + p0);
        }
    }
#endif
}
