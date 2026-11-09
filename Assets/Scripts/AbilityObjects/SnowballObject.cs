using Photon.Pun;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Snowball for the Snowball ability: a player it hits is iced (slides around with almost no grip)
/// for <see cref="iceDuration"/> seconds. Hitting anything else just splats. Higher power lasts longer.
///   thrower's client detects the hit → target.RequestIce (their owner applies it) → splat on every client
/// Attach to: Resources/Object/Snowball (with Rigidbody, Collider, PhotonView).
/// </summary>
public class SnowballObject : NetworkedProjectile
{
    [Header("Ice")]
    [TunableParam("Ice Duration", 1f, 30f)]
    [FormerlySerializedAs("slickDuration")]
    [SerializeField] private float iceDuration = 5f;

    [Header("Effects")]
    [SerializeField] private GameObject hitVFX;

    private bool hasHit;

    private void OnCollisionStay(Collision collision) => OnCollisionEnter(collision);

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsAuthority || hasHit || !RealContact(collision)) return;
        hasHit = true;

        var target = collision.collider.GetComponentInParent<Player>();
        if (target != null && !target.IsCaught && target.AffectedBy(photonView.OwnerActorNr)) target.RequestIce(iceDuration * Power);

        HideAndFreeze();
        photonView.RPC(nameof(RPC_Splat), RpcTarget.All, transform.position);
        DestroyNetworked();
    }

    [PunRPC]
    private void RPC_Splat(Vector3 hitPos)
    {
        WorldFx.SpawnVfx(hitVFX, hitPos);
        Sfx.At(SfxKind.Splat, hitPos, 0.9f, 30f);
        Fx.Frost(hitPos);
    }
}
