using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Bomb for the BoomBomb ability: bounces around and detonates after a fuse, launching every
/// nearby rigidbody. Optionally detonates early on impact.
/// Attach to: Resources/Object/BoomBomb (with Rigidbody, Collider, PhotonView).
/// </summary>
public class Bomb : NetworkedProjectile
{
    [Header("Explosion")]
    [TunableParam("Blast Radius", 1f, 30f)]
    [SerializeField] private float     explosionRadius = 5f;
    [TunableParam("Blast Force", 5f, 300f)]
    [SerializeField] private float     explosionForce  = 50f;
    [Tooltip("Layers pushed by the blast. Nothing = everything.")]
    [SerializeField] private LayerMask affectedLayers;

    [Header("Fuse")]
    [TunableParam("Fuse Time", 0.2f, 10f)]
    [SerializeField] private float fuseTime          = 2.5f;
    [Tooltip("Beep played every second before the explosion. Empty = a generated beep.")]
    [SerializeField] private AudioClip beepSFX;
    [SerializeField] private bool  explodeOnImpact;

    [Header("Effects")]
    [SerializeField] private GameObject explosionVFX;
    [SerializeField] private AudioClip  explosionSFX;
    [SerializeField] private float      audioRadius = 25f;

    private bool exploded;

    protected override void OnLaunched()
    {
        if (IsAuthority) StartCoroutine(Fuse());
    }

    // Beeps on each whole second left (…3, 2, 1) so players can count down to the blast
    private IEnumerator Fuse()
    {
        float remaining = fuseTime;
        while (remaining > 0f)
        {
            float wait = remaining - Mathf.Ceil(remaining - 1f); // Time until the next whole second left
            if (wait <= 0f) wait = 1f;
            yield return new WaitForSeconds(Mathf.Min(wait, remaining));
            remaining -= Mathf.Min(wait, remaining);
            if (exploded) yield break;
            if (remaining >= 0.99f) photonView.RPC(nameof(RPC_Beep), RpcTarget.All, remaining < 1.5f);
        }
        Detonate();
    }

    [PunRPC]
    private void RPC_Beep(bool last) => WorldFx.Beep(transform.position, audioRadius, beepSFX, last ? 1.5f : 1f);

    private void OnCollisionEnter(Collision _)
    {
        if (explodeOnImpact && IsAuthority) Detonate();
    }

    private void Detonate()
    {
        if (exploded) return;
        exploded = true;

        HideAndFreeze();
        photonView.RPC(nameof(RPC_Explode), RpcTarget.All, transform.position);
        DestroyNetworked();
    }

    [PunRPC]
    private void RPC_Explode(Vector3 position)
    {
        WorldFx.SpawnVfx(explosionVFX, position);
        WorldFx.PlayClip(explosionSFX, position, audioRadius);
        WorldFx.Explode(position, explosionRadius * Mathf.Sqrt(Power), explosionForce * Power, affectedLayers);
    }
}
