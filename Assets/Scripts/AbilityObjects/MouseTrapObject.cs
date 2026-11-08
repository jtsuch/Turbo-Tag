using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Placed Mouse Trap. The first player other than its placer to touch it is held still for
/// <see cref="holdSeconds"/>, then the trap is used up.
///   local player touches → RPC_Snap (all: snap sound, the bar slams) → victim holds → owner removes the trap
/// Each client only checks its own player, so the victim's machine decides (no physics sync needed).
/// Attach to: Resources/Object/MouseTrap (with a trigger collider, kinematic Rigidbody and PhotonView).
/// </summary>
public class MouseTrapObject : MonoBehaviourPun
{
    [TunableParam("Hold Time", 0.5f, 15f)]
    [SerializeField] private float holdSeconds = 3f;

    [Tooltip("The snapping bar, swung down when the trap goes off (optional).")]
    [SerializeField] private Transform bar;
    [SerializeField] private AudioClip snapSFX;
    [SerializeField] private float     audioRadius = 20f;

    private bool sprung;

    private void Awake() => ObjectTunables.Apply(photonView); // Cheat values from the placer

    // Placement leaves a small gap above the surface; settle the base onto it (same result on every client)
    private void Start()
    {
        Vector3 back = -transform.forward;
        float nearest = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(transform.position, back, 1f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<Player>() != null || hit.distance >= nearest) continue;
            nearest = hit.distance;
        }
        if (nearest < float.MaxValue) transform.position += back * nearest;
    }

    private void OnTriggerEnter(Collider other) => TryCatch(other);
    private void OnTriggerStay(Collider other)  => TryCatch(other);

    private void TryCatch(Collider other)
    {
        if (sprung) return;
        var player = other.GetComponentInParent<Player>();
        if (player == null || !player.IsLocal || player.IsCaught) return;
        if (player.ActorNumber == photonView.OwnerActorNr) return; // The placer knows where it is
        if (!player.AffectedByTrap(photonView.OwnerActorNr)) return; // Shielded, or on the placer's team

        sprung = true;
        player.HoldFor(holdSeconds);
        photonView.RPC(nameof(RPC_Snap), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_Snap()
    {
        sprung = true;
        Sfx.At(SfxKind.Snap, transform.position, 1f, 30f);
        Vfx.Burst(transform.position + Vector3.up * 0.3f, new Color(1f, 0.85f, 0.4f), 12, 3f, 0.18f, 0.4f);
        WorldFx.Beep(transform.position, audioRadius, snapSFX, 0.45f, 1f);
        if (bar != null) StartCoroutine(SlamBar());
        if (photonView.IsMine) StartCoroutine(RemoveAfterHold());
    }

    private IEnumerator SlamBar()
    {
        Quaternion start = bar.localRotation;
        Quaternion end   = start * Quaternion.Euler(0f, -175f, 0f);
        for (float t = 0f; t < 0.08f; t += Time.deltaTime)
        {
            bar.localRotation = Quaternion.Slerp(start, end, t / 0.08f);
            yield return null;
        }
        bar.localRotation = end;
    }

    private IEnumerator RemoveAfterHold()
    {
        yield return new WaitForSeconds(holdSeconds);
        PhotonNetwork.Destroy(gameObject);
    }
}
