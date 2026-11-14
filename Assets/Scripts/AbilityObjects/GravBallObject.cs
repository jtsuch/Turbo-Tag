using System.Collections;
using UnityEngine;

/// <summary>
/// Gravity orb: floats slowly in a straight line (no gravity, passes through everything: its colliders are
/// triggers, so nothing can bounce or redirect it) and draws nearby players and physics objects toward itself for its
/// lifetime. Since Nov 14 (Justin: "don't fling players around") it's a glide, not a force: each body's velocity eases
/// toward "the way to the middle × <see cref="pullRate"/>" (plus the orb's own drift), strongest near the middle and
/// fading to nothing at <see cref="pullRadius"/>, so things arrive and settle instead of being slingshot through it.
/// The reach shows as a faint purple shell with motes drifting inward.
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
    [Tooltip("Speed toward the middle per metre away (1/s): 10 m out it draws you in at about this × 10 m/s.")]
    [TunableParam("Pull Rate", 0.1f, 4f)]
    [SerializeField] private float pullRate = 0.9f;
    [Tooltip("How quickly a body's velocity eases to the pull (1/s) at the middle, fading to 0 at the edge.")]
    [TunableParam("Grip", 0.5f, 20f)]
    [SerializeField] private float grip = 3.5f;

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
        BuildShell();
        if (IsAuthority) StartCoroutine(Expire());
    }

    private IEnumerator Expire()
    {
        yield return new WaitForSeconds(lifetime);
        DestroyNetworked();
    }

    private AoeDebug aoe;

    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        float radius = pullRadius * Mathf.Sqrt(Power);
        if (AoeDebug.On && aoe == null) aoe = AoeDebug.Sphere(transform.position, radius, new Color(0.75f, 0.45f, 1f), "GravBall pull", 0f, transform);

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

            // Ease the velocity toward a glide to the middle (no overshoot: the closer, the slower it wants to go)
            var player = rb.GetComponent<Player>();
            if (player != null && !player.AffectedBy(photonView.OwnerActorNr)) continue; // Shielded / a ghost
            float t = dist / radius;
            float weight = 1f - Mathf.Exp(-grip * (1f - t * t) * Mathf.Sqrt(Power) * Time.fixedDeltaTime);
            Vector3 want = toOrb * pullRate + Body.linearVelocity;
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, want, weight);
            if (player != null) player.NotePushed(photonView.OwnerActorNr, "GravBall");
        }
    }

    // ─── The reach, visible (every screen) ────────────────────────────────────

    private Transform shell;
    private Material shellMaterial;
    private float nextMote;

    private void BuildShell()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "GravBall Reach";
        shell = go.transform;
        shell.SetParent(transform, false);
        shell.localScale = Vector3.one * 0.1f;
        shellMaterial = new Material(Shader.Find("Sprites/Default"));   // Both faces: seen from inside too
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = shellMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void Update()
    {
        if (shell == null) return;
        float radius = pullRadius * Mathf.Sqrt(Power);
        // The shell grows out to the reach over half a second, then breathes; never scaled by the orb's own size
        float grow = Mathf.Clamp01((Time.time - launchTime) / 0.5f);
        float scale = 2f * radius * grow / Mathf.Max(0.0001f, transform.lossyScale.x);
        shell.localScale = Vector3.one * scale;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
        shellMaterial.color = new Color(0.65f, 0.35f, 1f, 0.05f + 0.04f * pulse);
        // Motes drifting in from the edge
        if (Time.time >= nextMote)
        {
            nextMote = Time.time + 0.05f;
            Vector3 dir = Random.onUnitSphere;
            Vfx.Burst(transform.position + dir * radius * Random.Range(0.6f, 1f), new Color(0.75f, 0.5f, 1f, 0.9f), 1,
                      radius * 0.9f, 0.18f, 0.9f, direction: -dir, spread: 0.05f);
        }
    }

    private void OnDestroy()
    {
        if (shellMaterial != null) Destroy(shellMaterial);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0f, 1f, 0.25f);
        Gizmos.DrawSphere(transform.position, pullRadius);
    }
#endif
}
