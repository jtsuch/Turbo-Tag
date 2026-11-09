using Photon.Pun;
using UnityEngine;

/// <summary>
/// In the air only: slam straight down at high speed. Landing sends out a shockwave that knocks nearby players
/// and loose objects outward and up (shielded players ignore it).
///   owner key (airborne) → fast fall → first touch → RPC_GroundPound (all: boom, dust ring, shake) → each client
///   pushes the bodies it has authority over.
/// On the ground the key does nothing and the cooldown isn't spent.
/// </summary>
[AbilityInfo("In the air: slam into the ground. The landing knocks everyone nearby away and stuns anyone within 5 m.")]
public class GroundPound : QuickAbility
{
    [Header("Slam")]
    [TunableParam("Slam Speed", 10f, 150f)]
    [SerializeField] private float slamSpeed = 60f;
    [Tooltip("Share of sideways speed kept when the slam starts (0 = straight down).")]
    [SerializeField, Range(0f, 1f)] private float keepSideways = 0.15f;

    [Header("Shockwave")]
    [TunableParam("Shockwave Radius", 1f, 20f)]
    [SerializeField] private float radius = 6f;
    [TunableParam("Shockwave Push", 1f, 60f)]
    [SerializeField] private float push = 18f;
    [TunableParam("Shockwave Lift", 0f, 40f)]
    [SerializeField] private float lift = 10f;
    [TunableParam("Stun Radius", 0f, 15f)]
    [SerializeField] private float stunRadius = 5f;
    [TunableParam("Stun Time", 0f, 5f)]
    [SerializeField] private float stunSeconds = 1.5f;

    private static readonly Collider[] overlap = new Collider[64];
    private bool  slamming;
    private float slamStarted;

    public override void HandleInput(AbilityInputEvent inputEvent)
    {
        if (inputEvent != AbilityInputEvent.Down || !IsReady || slamming) return;
        if (Owner.IsGrounded || Owner.IsMagnetized || Owner.IsAnchored) return; // Air only
        Activate();
        StartCooldown();
    }

    protected override void Activate()
    {
        Vector3 v = Body.linearVelocity;
        Body.linearVelocity = new Vector3(v.x * keepSideways, -slamSpeed, v.z * keepSideways);
        Owner.NotePushed();
        slamming    = true;
        slamStarted = Time.time;
    }

    public override void Cancel() => slamming = false;

    private void FixedUpdate()
    {
        if (!IsLocal || !slamming) return;

        // Keep slamming (nothing slows it), until something stops the fall
        float elapsed = Time.time - slamStarted;
        bool stopped = Owner.IsGrounded || (elapsed > 0.08f && Body.linearVelocity.y > -slamSpeed * 0.3f);
        if (!stopped)
        {
            if (Body.linearVelocity.y > -slamSpeed)
                Body.linearVelocity = new Vector3(Body.linearVelocity.x, -slamSpeed, Body.linearVelocity.z);
            if (elapsed > 4f) slamming = false; // Fell into the void
            return;
        }

        slamming = false;
        Owner.photonView.RPC(nameof(RPC_GroundPound), RpcTarget.All, transform.position);
    }

    [PunRPC]
    private void RPC_GroundPound(Vector3 at)
    {
        Sfx.At(SfxKind.Explosion, at, 0.6f, 60f, 1.4f);
        Sfx.At(SfxKind.Land, at, 1f, 40f, 0.7f);
        Vfx.Ring(at, new Color(0.85f, 0.85f, 0.9f, 0.85f), 40, radius * 2.2f, 0.7f, 0.6f);
        Vfx.Burst(at, new Color(1f, 0.79f, 0.24f), 18, 6f, 0.25f, 0.5f, direction: Vector3.up, spread: 0.8f);

        var local = Player.Local;
        if (local != null)
            Fx.Shake(local == Owner ? 0.45f : Mathf.Clamp01(1f - Vector3.Distance(local.transform.position, at) / (radius * 3f)) * 0.5f);

        // Anyone else within the stun radius is stunned (each screen checks its own player; a shield keeps it off)
        if (local != null && local != Owner && !local.IsCaught && !local.IsShielded
            && Vector3.Distance(local.transform.position, at) <= stunRadius)
            PlayerEffect.Apply(typeof(StunnedEffect), local, stunSeconds);

        // Each client pushes what it simulates: its own player and its own or loose props
        int count = Physics.OverlapSphereNonAlloc(at, radius, overlap, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        var done = new System.Collections.Generic.HashSet<Rigidbody>();
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = overlap[i].attachedRigidbody;
            if (rb == null || rb == Body || rb.isKinematic || !done.Add(rb) || !WorldFx.HasAuthority(rb)) continue;

            Vector3 away = rb.position - at;
            away.y = 0f;
            float falloff = Mathf.Lerp(1f, 0.35f, away.magnitude / radius);
            Vector3 dir = away.sqrMagnitude > 0.01f ? away.normalized : Random.insideUnitSphere;
            dir.y = 0f;
            Vector3 kick = (dir.normalized * push + Vector3.up * lift) * falloff;

            if (rb.TryGetComponent(out Player target))
            {
                if (target.IsCaught) continue;
                WorldFx.Push(rb, kick, ForceMode.VelocityChange, Owner.ActorNumber, "GroundPound");
            }
            else
            {
                WorldFx.Push(rb, kick * rb.mass * 0.6f, ForceMode.Impulse, Owner.ActorNumber, "GroundPound");
            }
        }
    }
}
