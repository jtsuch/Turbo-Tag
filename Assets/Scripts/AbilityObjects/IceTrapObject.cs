using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Placed Ice Trap. The first player other than its placer whose body comes within <see cref="triggerRadius"/>
/// (measured to the nearest point of their collider, so about a 6 m wide circle around the trap at 3) is iced (slides with almost no grip) for <see cref="iceDuration"/> seconds, then the trap is used up.
///   local player in range → Player.IceFor → RPC_Trigger (all: icing effect, trap hidden) → owner removes it
/// Each client only checks its own player, so the victim's machine decides (like the Mouse Trap).
/// Attach to: Resources/Object/IceTrap (kinematic Rigidbody, trigger collider, PhotonView). Optional humSFX/icingVFX.
/// </summary>
public class IceTrapObject : MonoBehaviourPun
{
    [Header("Detection")]
    [TunableParam("Trigger Radius", 0.5f, 20f)]
    [SerializeField] private float triggerRadius = 3f;

    /// <summary>How close a body has to come (m, before the placer's power).</summary>
    public float TriggerRadius => triggerRadius;

    [Header("Ice")]
    [TunableParam("Ice Duration", 1f, 30f)]
    [SerializeField] private float iceDuration = 5f;

    [Header("Optional")]
    [Tooltip("Removes itself after this many seconds if nobody triggers it (0 = never).")]
    [TunableParam("Lifespan (0 = forever)", 0f, 120f)]
    [SerializeField] private float      lifespan = 0f;
    [SerializeField] private AudioClip  humSFX;
    [SerializeField] private float      humVolume = 0.4f;
    [Tooltip("Spawned at the iced player's feet.")]
    [SerializeField] private GameObject icingVFX;

    private float power = 1f;
    private bool  triggered;
    private Collider bodyCollider;

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        ObjectTunables.Apply(photonView);
        Shootable.Ensure(gameObject, 25f, 0.45f);   // Has health (Justin, Nov 14): a few hits break it
    } // Cheat values from the placer

    private void Start()
    {
        power = SpawnData.Power(photonView);

        if (humSFX != null)
        {
            AudioSource hum = gameObject.AddComponent<AudioSource>();
            WorldFx.Configure3D(hum, triggerRadius * 1.5f, humVolume);
            hum.clip = humSFX;
            hum.loop = true;
            hum.Play();
        }

        if (lifespan > 0f && photonView.IsMine) Invoke(nameof(DestroySelf), lifespan);
    }

    private void DestroySelf()
    {
        if (this != null && photonView.IsMine) PhotonNetwork.Destroy(gameObject);
    }

    // ─── Trigger (each client checks its own player) ─────────────────────────

    private AoeDebug aoe;

    private void FixedUpdate()
    {
        if (triggered) return;
        if (AoeDebug.On && aoe == null) aoe = AoeDebug.Sphere(transform.position, triggerRadius * power, new Color(0.4f, 0.95f, 1f), "Ice Trap trigger (to the body)", 0f, transform);
        var local = Player.Local;
        if (local == null || local.IsCaught || local.ActorNumber == photonView.OwnerActorNr) return;
        if (!local.AffectedByTrap(photonView.OwnerActorNr)) return; // Shielded or a teammate: walks right over it
        if (DistanceToBody(local) > triggerRadius * power) return;

        triggered = true;
        local.IceFor(iceDuration * power);
        photonView.RPC(nameof(RPC_Trigger), RpcTarget.All, local.transform.position);
    }

    // To the nearest point of the player's collider (their whole body counts, not just their pivot)
    private float DistanceToBody(Player player)
    {
        if (bodyCollider == null || bodyCollider.attachedRigidbody != player.Body)
            player.TryGetComponent(out bodyCollider);
        Vector3 nearest = bodyCollider != null && bodyCollider.enabled
            ? bodyCollider.ClosestPoint(transform.position)
            : player.transform.position;
        return Vector3.Distance(transform.position, nearest);
    }

    [PunRPC]
    private void RPC_Trigger(Vector3 feet)
    {
        triggered = true;
        if (icingVFX != null) Instantiate(icingVFX, feet, Quaternion.identity);
        Sfx.At(SfxKind.Ice, feet);
        Vfx.Burst(feet + Vector3.up, new Color(0.6f, 0.9f, 1f), 40, 6f, 0.35f, 0.9f);
        Vfx.Ring(feet, new Color(0.8f, 0.95f, 1f), 28, 7f, 0.5f, 0.7f);
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        if (photonView.IsMine) StartCoroutine(RemoveSoon());
    }

    // A moment's delay so the trigger RPC reaches everyone before the object goes
    private IEnumerator RemoveSoon()
    {
        yield return new WaitForSeconds(0.5f);
        DestroySelf();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.2f);
        Gizmos.DrawSphere(transform.position, triggerRadius);
    }
#endif
}
