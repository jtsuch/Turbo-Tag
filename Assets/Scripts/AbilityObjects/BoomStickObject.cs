using Photon.Pun;
using UnityEngine;

/// <summary>
/// Dynamite for the BoomStick ability: explodes on its first collision, pushing every nearby
/// rigidbody. The thrower's client detects the hit; the blast runs on all clients.
/// Attach to: Resources/Object/BoomStick (with Rigidbody, Collider, PhotonView).
/// </summary>
public class BoomStickObject : NetworkedProjectile
{
    [Header("Explosion")]
    [TunableParam("Blast Radius", 1f, 30f)]
    [SerializeField] private float     explosionRadius = 4f;
    [TunableParam("Blast Force", 5f, 300f)]
    [SerializeField] private float     explosionForce  = 40f;
    [Tooltip("Layers pushed by the blast. Nothing = everything.")]
    [SerializeField] private LayerMask affectedLayers  = ~0;

    [Header("Effects")]
    [SerializeField] private GameObject explosionVFX;
    [SerializeField] private AudioClip  explosionSFX;
    [SerializeField] private float      vfxScale    = 0.3f;
    [SerializeField] private float      audioRadius = 20f;
    [SerializeField] private float      audioVolume = 1f;

    private bool exploded;

    private void OnCollisionEnter(Collision _)
    {
        if (!IsAuthority || exploded) return;
        exploded = true;

        HideAndFreeze();
        photonView.RPC(nameof(RPC_Explode), RpcTarget.All, transform.position);
        DestroyNetworked();
    }

    [PunRPC]
    private void RPC_Explode(Vector3 position)
    {
        var vfx = WorldFx.SpawnVfx(explosionVFX, position);
        if (vfx != null) vfx.transform.localScale = Vector3.one * vfxScale;

        WorldFx.PlayClip(explosionSFX, position, audioRadius, audioVolume);
        Fx.Explosion(position, explosionRadius * Mathf.Sqrt(Power));
        WorldFx.Explode(position, explosionRadius * Mathf.Sqrt(Power), explosionForce * Power, affectedLayers, sourceActor: photonView.OwnerActorNr, cause: "BoomStick");
    }
}
