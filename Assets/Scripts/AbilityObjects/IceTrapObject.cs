using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Placed Ice Trap. The first player other than its placer to come within <see cref="triggerRadius"/>
/// is iced (slides with almost no grip) for <see cref="iceDuration"/> seconds, then the trap is used up.
///   local player in range → Player.IceFor → RPC_Trigger (all: icing effect, trap hidden) → owner removes it
/// Each client only checks its own player, so the victim's machine decides (like the Mouse Trap).
/// Attach to: Resources/Object/IceTrap (kinematic Rigidbody, trigger collider, PhotonView). Optional humSFX/icingVFX.
/// </summary>
public class IceTrapObject : MonoBehaviourPun
{
    [Header("Detection")]
    [TunableParam("Trigger Radius", 1f, 20f)]
    [SerializeField] private float triggerRadius = 5f;

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

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake() => ObjectTunables.Apply(photonView); // Cheat values from the placer

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

    private void FixedUpdate()
    {
        if (triggered) return;
        var local = Player.Local;
        if (local == null || local.IsCaught || local.ActorNumber == photonView.OwnerActorNr) return;
        if (Vector3.Distance(transform.position, local.transform.position) > triggerRadius * power) return;

        triggered = true;
        local.IceFor(iceDuration * power);
        photonView.RPC(nameof(RPC_Trigger), RpcTarget.All, local.transform.position);
    }

    [PunRPC]
    private void RPC_Trigger(Vector3 feet)
    {
        triggered = true;
        if (icingVFX != null) Instantiate(icingVFX, feet, Quaternion.identity);
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
