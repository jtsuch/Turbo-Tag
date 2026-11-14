using Photon.Pun;
using UnityEngine;

/// <summary>
/// Shoots a ball of ink straight ahead at a slowish speed (Justin, Nov 14: was an instant cone). A player it touches
/// gets ink over their screen (<see cref="InkedEffect"/>) and splats on their face that everyone else sees; hitting a
/// wall or floor bursts it, inking anyone within <see cref="splashRadius"/> who can see the burst. Hits teammates too.
/// Every screen flies its own copy of the ball (<see cref="InkBall"/>) from the same start; the thrower's screen
/// decides what it hits and pops it everywhere. Health modes only.
/// </summary>
[AbilityInfo("Shoot a slow ball of ink: whoever it hits (or anyone close to where it bursts) gets a screen full of ink.", DisplayName = "Ink Blast", HealthModesOnly = true)]
public class InkBlast : QuickAbility
{
    [TunableParam("Ball Speed", 4f, 40f)]
    [SerializeField] private float ballSpeed = 21f;
    [TunableParam("Range", 5f, 60f)]
    [SerializeField] private float range = 30f;
    [TunableParam("Splash Radius", 0f, 6f)]
    [SerializeField] private float splashRadius = 2.5f;
    [TunableParam("Ink Time", 0.5f, 10f)]
    [SerializeField] private float inkSeconds = 3.5f;

    private InkBall flying;   // The thrower's own copy, which decides hits

    protected override void Activate()
    {
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera.transform : transform;
        Vector3 from = cam.position + cam.forward * 0.9f;
        Owner.photonView.RPC(nameof(RPC_InkBall), RpcTarget.All, from, cam.forward, ballSpeed, range);
    }

    [PunRPC]
    private void RPC_InkBall(Vector3 from, Vector3 dir, float speed, float reach)
    {
        Sfx.At(SfxKind.Splat, from, 0.8f, 35f, 1.3f);
        var ball = InkBall.Launch(from, dir, speed, reach, Owner != null ? Owner.transform : null);
        if (!IsLocal) return;
        flying = ball;
        ball.Hit += OnBallHit;
    }

    // Thrower: what the ball hit (a player, or a surface: splash round it)
    private void OnBallHit(Vector3 at, Player target)
    {
        float seconds = inkSeconds * UpgradePower;   // S&D: 2.5 → 3.5 → 4.5 s
        if (target != null && TacticalNet.IsTarget(Owner, target, enemiesOnly: false))
            TacticalNet.Send(target, TacticalNet.Status.Ink, seconds);
        foreach (var p in Player.All)
        {
            if (p == target || !TacticalNet.IsTarget(Owner, p, enemiesOnly: false)) continue;
            if (Vector3.Distance(AbilityDamage.Chest(p), at) > splashRadius) continue;
            if (!AbilityDamage.InSight(at, p, Owner.transform)) continue;
            TacticalNet.Send(p, TacticalNet.Status.Ink, seconds);
        }
        Owner.photonView.RPC(nameof(RPC_InkPop), RpcTarget.All, at);
        flying = null;
    }

    [PunRPC]
    private void RPC_InkPop(Vector3 at)
    {
        InkBall.PopNear(at);
        AoeDebug.Sphere(at, splashRadius, new Color(0.75f, 0.45f, 1f), "Ink splash (line of sight)", 1f);
        Sfx.At(SfxKind.Splat, at, 1f, 35f, 0.8f);
        Vfx.Burst(at, new Color(0.05f, 0.04f, 0.1f, 0.95f), 26, 4f, 0.45f, 0.7f);
    }
}

/// <summary>
/// One ink ball in flight (no network object: every screen flies its own copy in a straight line from the same start).
/// The thrower's copy sweeps a sphere along its path and reports the first player or surface it meets
/// (<see cref="Hit"/>); the others stop at surfaces and wait for the pop. Built from a sphere, no prefab.
/// </summary>
public class InkBall : MonoBehaviour
{
    public event System.Action<Vector3, Player> Hit;

    private const float Radius = 0.35f;
    private static readonly System.Collections.Generic.List<InkBall> live = new();
    private static Material material;

    private Vector3 velocity;
    private float travelled, reach;
    private Transform thrower;
    private bool stopped;
    private float nextDrip;

    public static InkBall Launch(Vector3 from, Vector3 dir, float speed, float reach, Transform thrower)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Ink Ball";
        Destroy(go.GetComponent<Collider>());
        go.transform.position = from;
        go.transform.localScale = Vector3.one * Radius * 2f;
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { color = new Color(0.08f, 0.05f, 0.14f) };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.85f);
        }
        go.GetComponent<Renderer>().sharedMaterial = material;
        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.startWidth = Radius * 1.6f;
        trail.endWidth = 0f;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.startColor = new Color(0.1f, 0.06f, 0.18f, 0.8f);
        trail.endColor = new Color(0.1f, 0.06f, 0.18f, 0f);

        var ball = go.AddComponent<InkBall>();
        ball.velocity = dir.normalized * speed;
        ball.reach = reach;
        ball.thrower = thrower;
        live.Add(ball);
        return ball;
    }

    /// <summary>Removes the ball nearest <paramref name="at"/> (the thrower's screen said it hit there).</summary>
    public static void PopNear(Vector3 at)
    {
        InkBall nearest = null;
        float best = 6f;
        foreach (var b in live)
        {
            if (b == null) continue;
            float d = Vector3.Distance(b.transform.position, at);
            if (d < best) { best = d; nearest = b; }
        }
        if (nearest != null) Destroy(nearest.gameObject);
    }

    private void OnDestroy() => live.Remove(this);

    private void Update()
    {
        if (stopped) return;
        Vector3 step = velocity * Time.deltaTime;
        Vector3 from = transform.position;
        bool reporter = Hit != null;

        // The first thing in the way (never the thrower)
        float bestDistance = step.magnitude;
        Player hitPlayer = null;
        bool hitSomething = false;
        foreach (var h in Physics.SphereCastAll(from, Radius, step.normalized, step.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (thrower != null && h.transform.IsChildOf(thrower)) continue;
            if (h.distance > bestDistance && hitSomething) continue;
            var p = h.collider.GetComponentInParent<Player>();
            if (p != null && (p.IsCaught || p.Role == PlayerRole.Ghost)) continue;
            bestDistance = h.distance;
            hitPlayer = p;
            hitSomething = true;
        }

        if (hitSomething)
        {
            Vector3 at = from + step.normalized * bestDistance;
            transform.position = at;
            if (reporter) { Hit?.Invoke(at, hitPlayer); Hit = null; }
            if (hitPlayer == null) { stopped = true; Destroy(gameObject, reporter ? 0f : 0.5f); }   // Others wait briefly for the pop
            else if (!reporter) { stopped = true; Destroy(gameObject, 0.5f); }
            return;
        }

        transform.position = from + step;
        travelled += step.magnitude;
        // Ink dripping off it in flight
        if (Time.time >= nextDrip)
        {
            nextDrip = Time.time + 0.035f;
            Vfx.Burst(transform.position, new Color(0.07f, 0.04f, 0.13f, 0.9f), 3, 1.4f, 0.16f, 0.55f, gravity: true, direction: -velocity.normalized, spread: 0.8f);
        }
        if (travelled >= reach)
        {
            // Out of range: it bursts in the air (still splashes anyone close)
            if (reporter) { Hit?.Invoke(transform.position, null); Hit = null; }
            Destroy(gameObject, reporter ? 0f : 0.5f);
            stopped = true;
        }
    }
}
