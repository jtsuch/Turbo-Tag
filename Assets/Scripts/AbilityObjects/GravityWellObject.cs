using System.Collections;
using UnityEngine;
using Photon.Pun;

/// <summary>
/// Placed object for the GravityWell TrapAbility: a one-shot gravity spike for floors, walls or ceilings. It reaches
/// out along its facing (the surface normal) in a long column (<see cref="maxRange"/> long, <see cref="cylinderRadius"/>
/// wide). The first other player who walks into the column with a clear line to it is reeled in: drawn smoothly and
/// quickly right up to the well (<see cref="pullSeconds"/>, not a push they could slide past), and the well breaks.
///
/// Network behaviour: each client checks only its own player. The caught client tells everyone (RPC_Sprung: the
/// flash and the well goes dark) and moves its own body; the placer's client removes the well after the pull.
///
/// Unity setup: Resources/Object/GravityWell with Rigidbody (kinematic, no gravity), a trigger
/// collider and a PhotonView. Optionally assign humSFX.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GravityWellObject : MonoBehaviourPun
{
    [Header("Reach")]
    [Tooltip("How far the pull column reaches out from the well, along its facing (m).")]
    [TunableParam("Reach", 2f, 80f)]
    [SerializeField] private float maxRange       = 35f;
    [Tooltip("Width of the column, either side of its middle line (m).")]
    [TunableParam("Radius", 0.5f, 15f)]
    [SerializeField] private float cylinderRadius = 3f;

    [Header("Pull")]
    [Tooltip("Time to reel a caught player all the way in (s).")]
    [TunableParam("Pull Time", 0.1f, 2f)]
    [SerializeField] private float pullSeconds = 0.45f;

    private const float Behind = 1.5f; // The column starts a little behind the well (feet on the same floor count)

    [Header("Audio")]
    [SerializeField] private AudioClip humSFX;
    [SerializeField] private float humVolume = 0.5f;

    private bool used;

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake() => ObjectTunables.Apply(photonView); // Cheat values from the placer

    private void Start()
    {
        maxRange *= Mathf.Sqrt(SpawnData.Power(photonView));

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
        if (used) return;
        // This client's own player (never the placer)
        var local = Player.Local;
        if (local == null || local.Body == null || local.ActorNumber == photonView.OwnerActorNr || local.IsCaught
            || !local.AffectedByTrap(photonView.OwnerActorNr)) return;
        Vector3 chest = AbilityDamage.Chest(local);
        if (!InColumn(chest) || !InSight(chest, local)) return;

        used = true;
        photonView.RPC(nameof(RPC_Sprung), RpcTarget.All);
        StartCoroutine(ReelIn(local));
    }

    // ─── The pull ────────────────────────────────────────────────────────────

    // Drawn in on a smooth curve (slow off the mark, fast at the end) to just in front of the well, then let go
    private IEnumerator ReelIn(Player victim)
    {
        var rb = victim.Body;
        Vector3 from = rb.position;
        float radius = victim.TryGetComponent(out CapsuleCollider capsule) ? capsule.radius : 0.7f;
        // In front of the well; on a wall or ceiling the feet end up level with it, on a floor right on top of it
        Vector3 to = transform.position + transform.forward * (radius + 0.25f);
        if (transform.forward.y < 0.5f) to += Vector3.down * Mathf.Min(1f, victim.Height * 0.5f);

        for (float t = 0f; t < pullSeconds; t += Time.fixedDeltaTime)
        {
            if (victim == null || rb == null || victim.IsCaught) yield break;
            float k = t / pullSeconds;
            k = k * k * (3f - 2f * k); // Smooth in and out
            rb.linearVelocity = Vector3.zero;
            rb.MovePosition(Vector3.Lerp(from, to, k));
            victim.NotePushed(photonView.OwnerActorNr, "GravityWell");
            yield return new WaitForFixedUpdate();
        }
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.MovePosition(to); }
    }

    [PunRPC]
    private void RPC_Sprung()
    {
        used = true;
        Sfx.At(SfxKind.ZapDown, transform.position, 1f, 50f, 0.7f);
        Vfx.Burst(transform.position, new Color(0.55f, 0.2f, 1f), 24, 6f, 0.25f, 0.5f, direction: transform.forward, spread: 0.6f);
        Vfx.Ring(transform.position, new Color(0.55f, 0.2f, 1f), 36, 4f, 0.3f, 0.4f);
        if (photonView.IsMine) Invoke(nameof(Break), pullSeconds + 0.25f); // One use: it breaks once the pull is done
    }

    private void Break()
    {
        if (this == null || !photonView.IsMine) return;
        Vfx.Burst(transform.position, new Color(0.55f, 0.2f, 1f), 16, 4f, 0.2f, 0.4f);
        PhotonNetwork.Destroy(gameObject);
    }

    /// <summary>Inside the column: within the radius of the well's facing line, from just behind it out to its reach.</summary>
    private bool InColumn(Vector3 worldPos)
    {
        Vector3 to = worldPos - transform.position;
        float along = Vector3.Dot(to, transform.forward);
        if (along < -Behind || along > maxRange) return false;
        return (to - transform.forward * along).magnitude <= cylinderRadius;
    }

    // Nothing solid between the well and the player (other players don't block it)
    private bool InSight(Vector3 chest, Player victim)
    {
        Vector3 from = transform.position + transform.forward * 0.3f;
        Vector3 d = chest - from;
        foreach (var hit in Physics.RaycastAll(from, d.normalized, d.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Player>() != null) continue;
            return false;
        }
        return true;
    }

    // ─── Debug visualisation (editor only) ───────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0f, 1f, 0.4f);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        const int segments = 16;
        Vector3 origin = Vector3.back * Behind, tip = Vector3.forward * maxRange;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
            Vector3 p0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * cylinderRadius;
            Vector3 p1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * cylinderRadius;
            Gizmos.DrawLine(origin + p0, origin + p1);
            Gizmos.DrawLine(tip + p0, tip + p1);
            Gizmos.DrawLine(origin + p0, tip + p0);
        }
    }
#endif
}
