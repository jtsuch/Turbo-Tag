using Photon.Pun;
using UnityEngine;

/// <summary>
/// Dynamite for the BoomStick ability: explodes on its first collision, pushing every nearby
/// rigidbody. The thrower's client detects the hit; the blast runs on all clients. Only a real touch counts: a fast
/// (fully charged) throw got speculative contacts from the floor or a wall it hadn't reached yet, and blew up in
/// the thrower's hand.
/// Attach to: Resources/Object/BoomStick (with Rigidbody, Collider, PhotonView).
/// </summary>
public class BoomStickObject : NetworkedProjectile
{
    [Header("Explosion")]
    [TunableParam("Blast Radius", 1f, 30f)]
    [SerializeField] private float     explosionRadius = 8f; // Twice the first 4 m, with a steep falloff (Nov 9)
    [TunableParam("Blast Force", 5f, 300f)]
    [SerializeField] private float     explosionForce  = 40f;
    [Tooltip("How fast the push dies away toward the edge (1 = straight line; 2 = squared: the outer half gets a quarter or less).")]
    [SerializeField] private float     falloffPower    = 2f;
    [Tooltip("Layers pushed by the blast. Nothing = everything.")]
    [SerializeField] private LayerMask affectedLayers  = ~0;

    [Header("Effects")]
    [SerializeField] private GameObject explosionVFX;
    [SerializeField] private AudioClip  explosionSFX;
    [SerializeField] private float      vfxScale    = 0.3f;
    [SerializeField] private float      audioRadius = 20f;
    [SerializeField] private float      audioVolume = 1f;

    private bool exploded;

    private void OnCollisionEnter(Collision collision) => TryExplode(collision);
    private void OnCollisionStay(Collision collision)  => TryExplode(collision);

    private void TryExplode(Collision collision)
    {
        if (!IsAuthority || exploded || !Touching(collision)) return;
        exploded = true;

        HideAndFreeze();
        photonView.RPC(nameof(RPC_Explode), RpcTarget.All, transform.position);
        DestroyNetworked();
    }

    // Speculative contacts report a positive separation (not touching yet); a real hit is at or inside the surface
    private static bool Touching(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
            if (collision.GetContact(i).separation <= 0.05f) return true;
        return false;
    }

    [PunRPC]
    private void RPC_Explode(Vector3 position)
    {
        var vfx = WorldFx.SpawnVfx(explosionVFX, position);
        if (vfx != null) vfx.transform.localScale = Vector3.one * vfxScale;

        WorldFx.PlayClip(explosionSFX, position, audioRadius, audioVolume);
        Fx.Explosion(position, explosionRadius * Mathf.Sqrt(Power));
        WorldFx.Explode(position, explosionRadius * Mathf.Sqrt(Power), explosionForce * Power, affectedLayers, sourceActor: photonView.OwnerActorNr, cause: "BoomStick",
                        falloffPower: falloffPower);
    }
}
