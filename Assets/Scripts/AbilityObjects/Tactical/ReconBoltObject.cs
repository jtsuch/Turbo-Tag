using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Recon Bolt (Resources/Object/ReconBolt): fired hard, sticks to the first surface it hits, then, after a beat, scans
/// twice: every enemy in its line of sight within <see cref="range"/> lights up through walls for the thrower and their
/// team (<see cref="HunterReveal"/>); the scanned player's map border pulses, and anyone near where it lands gets a
/// warning pulse. It blinks and chirps while it's up, so enemies can find it and shoot it down (<see cref="Shootable"/>).
/// Used by the Recon Bolt ability and by the Intel hunter's Recon Bolt tool (HunterKit), so it works in Tag too: the
/// hunters are a team there, so hunters light up hiders.
/// Each screen decides its own reveals from where the bolt stuck (no per-target messages).
/// Nov 14 (Justin): it latches onto the very first surface it touches (the thrower's copy sweeps its path every step
/// and freezes on the first hit, so it can't bounce off a slanted wall before the stick message goes out); it breaks
/// in one shot; each scan is a visible blue pulse spreading out to its range, and only enemies the pulse reaches
/// (in its line of sight) are revealed.
/// </summary>
public class ReconBoltObject : NetworkedProjectile
{
    [TunableParam("Range", 5f, 100f)]
    [SerializeField] private float range = 40f;
    [TunableParam("Reveal Time", 0.5f, 10f)]
    [SerializeField] private float revealSeconds = 2f;
    [SerializeField] private int scans = 2;

    private static readonly Color Blue = new(0.35f, 0.7f, 1f);
    private bool stuck;
    private Light glow;

    public override float GravityScale => 0.6f;

    protected override void OnLaunched()
    {
        if (TryGetComponent(out Shootable shootable)) shootable.health = 1f;   // One shot breaks it
        glow = new GameObject("Bolt Light").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.color = Blue;
        glow.range = 5f;
        glow.intensity = 2f;
        glow.shadows = LightShadows.None;
        if (IsAuthority) StartCoroutine(Lifetime());
    }

    // Never stuck anywhere: gone after a few seconds of flight
    private IEnumerator Lifetime()
    {
        yield return new WaitForSeconds(5f);
        if (!stuck) DestroyNetworked();
    }

    private bool sticking;   // Thrower's copy: frozen on its first surface, the stick message on its way

    // Thrower's copy: sweep the next step's path; the first surface (not a player) it would touch is where it sticks
    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        if (!IsAuthority || stuck || sticking || Body == null || Body.isKinematic) return;
        Vector3 v = Body.linearVelocity;
        float step = v.magnitude * Time.fixedDeltaTime;
        if (step < 0.001f) return;
        foreach (var hit in Physics.RaycastAll(Body.position, v / v.magnitude, step + 0.15f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.attachedRigidbody == Body || hit.collider.GetComponentInParent<Player>() != null) continue;
            StickAt(hit.point, hit.normal);
            return;
        }
    }

    private void OnCollisionStay(Collision collision) => OnCollisionEnter(collision);

    // A touch the sweep missed (its side brushing a wall): stick there, at once
    private void OnCollisionEnter(Collision collision)
    {
        if (!IsAuthority || stuck || sticking || collision.contactCount == 0) return;
        if (collision.collider.GetComponentInParent<Player>() != null) return;
        var contact = collision.GetContact(0);
        StickAt(contact.point, contact.normal);
    }

    private void StickAt(Vector3 point, Vector3 normal)
    {
        sticking = true;
        Body.linearVelocity = Vector3.zero;
        Body.isKinematic = true;   // No bounce while the message goes round
        photonView.RPC(nameof(RPC_Stick), RpcTarget.All, point + normal * 0.05f, normal);
    }

    [PunRPC]
    private void RPC_Stick(Vector3 at, Vector3 normal)
    {
        if (stuck) return;
        stuck = true;
        AoeDebug.Sphere(at, range * Mathf.Sqrt(Power), new Color(0.35f, 0.7f, 1f), "Recon Bolt scan (line of sight)", 3f);
        AoeDebug.Sphere(at, 20f, new Color(0.35f, 0.7f, 1f) * 0.6f, "Recon Bolt warning", 3f);
        Body.isKinematic = true;
        transform.SetPositionAndRotation(at, Quaternion.LookRotation(-normal));
        Sfx.At(SfxKind.Place, at, 0.8f, 30f, 1.4f);

        // Anyone it's aimed at gets a moment's warning (a blue map pulse) before it scans
        var local = Player.Local;
        if (local != null && !IsViewer(local) && Vector3.Distance(local.transform.position, at) < 20f)
            Minimap.Pulse(Blue);
        StartCoroutine(Scan(at));
    }

    private IEnumerator Scan(Vector3 at)
    {
        yield return new WaitForSeconds(0.8f);
        float reach = range * Mathf.Sqrt(Power);
        for (int i = 0; i < Mathf.Max(1, scans); i++)
        {
            Vfx.Ring(at, Blue, 40, 12f, 0.35f, 0.7f);
            Sfx.At(SfxKind.Shimmer, at, 1f, 50f, 0.8f + i * 0.15f);
            if (glow != null) { glow.range = 16f; glow.intensity = 7f; }

            // The pulse: a see-through blue shell spreading out to the range; whoever it passes (in its line of sight) shows
            var local = Player.Local;
            var thrower = Player.Find(photonView.OwnerActorNr);
            var shell = PulseShell(at);
            var shellRenderer = shell.GetComponent<Renderer>();
            var reached = new System.Collections.Generic.HashSet<Player>();
            const float spread = 0.7f;
            for (float t = 0f; t <= spread; t += Time.deltaTime)
            {
                float radius = reach * (t / spread);
                shell.transform.localScale = Vector3.one * radius * 2f;
                shellRenderer.sharedMaterial.color = new Color(Blue.r, Blue.g, Blue.b, 0.22f * (1f - t / spread) + 0.03f);
                foreach (var p in Player.All)
                {
                    if (reached.Contains(p) || !TacticalNet.IsTarget(thrower, p, enemiesOnly: true)) continue;
                    if (Vector3.Distance(AbilityDamage.Chest(p), at) > radius) continue;
                    reached.Add(p);
                    if (!AbilityDamage.InSight(at, p, transform)) continue;
                    if (local != null && IsViewer(local)) HunterReveal.Reveal(p.ActorNumber, revealSeconds);
                    else if (p == local) Minimap.Pulse(new Color(1f, 0.35f, 0.3f)); // You've been scanned
                }
                yield return null;
            }
            Destroy(shellRenderer.sharedMaterial);
            Destroy(shell);
            if (i < Mathf.Max(1, scans) - 1) yield return new WaitForSeconds(Mathf.Max(0f, 1.2f - spread));
        }
        // Just past the last scan: it shuts off and goes (it used to blink on long after)
        if (glow != null) glow.enabled = false;
        if (IsAuthority) { yield return new WaitForSeconds(0.3f); DestroyNetworked(); }
    }

    // A see-through blue sphere (both faces), grown by the scan
    private static GameObject PulseShell(Vector3 at)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "Recon Pulse";
        go.transform.position = at;
        go.transform.localScale = Vector3.zero;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(Blue.r, Blue.g, Blue.b, 0.2f) };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    // The thrower and their team see what it finds
    private bool IsViewer(Player p) => p.ActorNumber == photonView.OwnerActorNr || p.IsTeammateOf(photonView.OwnerActorNr);

    // Blinks: slow in flight, quick once stuck
    private void Update()
    {
        if (glow == null) return;
        float rate = stuck ? 12f : 6f;
        float target = Mathf.Sin(Time.time * rate) > 0f ? 2.5f : 0.4f;
        glow.intensity = Mathf.MoveTowards(glow.intensity, target, 40f * Time.deltaTime);
        glow.range = Mathf.MoveTowards(glow.range, 5f, 30f * Time.deltaTime);
    }
}
