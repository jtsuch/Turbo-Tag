using System.Collections;
using UnityEngine;

/// <summary>
/// Gravity orb: floats slowly in a straight line (no gravity, passes through everything: its colliders are
/// triggers, so nothing can bounce or redirect it) and pulls nearby players and physics
/// objects toward itself for its lifetime. The pull fades gently: near full strength for most of
/// <see cref="pullRadius"/>, reaching zero at the edge.
/// Each client pulls the bodies it has authority over (its own player and local props).
/// Attach to: Resources/Object/GravBall (with Rigidbody, small SphereCollider, PhotonView).
/// </summary>
public class GravBallObject : NetworkedProjectile
{
    [Header("Flight")]
    [Tooltip("Speed the orb drifts at, whatever the throw charge (m/s).")]
    [TunableParam("Flight Speed", 0f, 40f)]
    [SerializeField] private float flightSpeed = 5f;

    [Header("Gravity")]
    [TunableParam("Pull Radius", 2f, 50f)]
    [SerializeField] private float pullRadius = 14f;
    [Tooltip("Pull at the orb's centre (m/s²), fading to 0 at the edge.")]
    [TunableParam("Pull Force", 10f, 400f)]
    [SerializeField] private float pullForce  = 90f;

    [Tooltip("The thrower isn't pulled until they have been outside the pull radius once (so it never drags them " +
             "along by surprise), or this many seconds have passed, whichever comes first.")]
    [TunableParam("Thrower Grace", 0f, 10f)]
    [SerializeField] private float throwerGrace = 3f;

    [Header("Lifetime")]
    [TunableParam("Lifetime", 1f, 30f)]
    [SerializeField] private float lifetime = 6f;

    [Header("Effects")]
    [Tooltip("Looping particle prefab parented to the orb.")]
    [SerializeField] private GameObject orbVFX;

    private static readonly Collider[] overlapBuffer = new Collider[32];

    public override float   GravityScale => 0f;
    public override Vector3 LaunchVelocity(Vector3 thrown) =>
        thrown.sqrMagnitude > 0.0001f ? thrown.normalized * flightSpeed : Vector3.zero;

    protected override void Awake()
    {
        base.Awake();
        foreach (var col in GetComponentsInChildren<Collider>()) col.isTrigger = true;
    }

    private Rigidbody throwerBody;
    private bool      throwerReleased; // The thrower can be pulled now
    private float     launchTime;

    protected override void OnLaunched()
    {
        launchTime  = Time.time;
        throwerBody = Thrower != null ? Thrower.GetComponent<Rigidbody>() : null;
        if (orbVFX != null) Instantiate(orbVFX, transform.position, transform.rotation, transform);
        if (IsAuthority) StartCoroutine(Expire());
    }

    private IEnumerator Expire()
    {
        yield return new WaitForSeconds(lifetime);
        DestroyNetworked();
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        float radius = pullRadius * Mathf.Sqrt(Power);

        if (!throwerReleased && throwerBody != null
            && (Time.time - launchTime > throwerGrace || (throwerBody.position - transform.position).sqrMagnitude > radius * radius))
            throwerReleased = true;
        int count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlapBuffer,
                                                  Physics.AllLayers, QueryTriggerInteraction.Ignore);
        var pulled = new System.Collections.Generic.HashSet<Rigidbody>();

        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = overlapBuffer[i].attachedRigidbody;
            if (rb == null || rb == Body || rb.isKinematic || !pulled.Add(rb)) continue;
            if (rb == throwerBody && !throwerReleased) continue;
            if (!WorldFx.HasAuthority(rb)) continue;

            Vector3 toOrb = transform.position - rb.position;
            float   dist  = toOrb.magnitude;
            if (dist < 0.1f || dist > radius) continue;

            // An acceleration, so it pulls a player as hard as a crate, and through movement braking
            float t = dist / radius;
            WorldFx.Push(rb, toOrb.normalized * (pullForce * Power * (1f - t * t)), ForceMode.Acceleration, photonView.OwnerActorNr, "GravBall");
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0f, 1f, 0.25f);
        Gizmos.DrawSphere(transform.position, pullRadius);
    }
#endif
}
