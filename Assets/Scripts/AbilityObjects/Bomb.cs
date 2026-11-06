using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Bomb for the BoomBomb ability. Remote mode (default): tossed, it sticks to the first non-player thing it
/// touches (walls, floors, props; moving ones carry it along) and waits for its thrower to set it off
/// (<see cref="Trigger"/>), going off by itself after <see cref="maxLifetime"/>. Fuse mode (remoteDetonation off):
/// bounces around and beeps down a fuse. Either way the blast launches every nearby rigidbody.
///   owner hit → RPC_Stick (all: same anchor + offset everywhere) · owner Trigger → RPC_Explode (all) → destroy
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

    [Header("Remote detonation")]
    [Tooltip("On: waits for its thrower to set it off (BoomBomb's second press). Off: the fuse below.")]
    [SerializeField] private bool  remoteDetonation = true;
    [Tooltip("Sticks to the first non-player thing it touches.")]
    [SerializeField] private bool  sticky = true;
    [TunableParam("Max Lifetime", 1f, 60f)]
    [SerializeField] private float maxLifetime = 20f;

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

    private bool       exploded;
    private bool       stuck;
    private Transform  anchor;          // What it's stuck to (null = fixed in place)
    private Vector3    anchorPosition;  // Pose in the anchor's space
    private Quaternion anchorRotation;

    /// <summary>True once it has gone off (or is going): the ability starts its cooldown.</summary>
    public bool HasExploded => exploded;

    /// <summary>Thrower only: sets the bomb off now.</summary>
    public void Trigger()
    {
        if (IsAuthority) Detonate();
    }

    protected override void OnLaunched()
    {
        if (!IsAuthority) return;
        StartCoroutine(remoteDetonation ? Lifetime() : Fuse());
    }

    // A remote bomb nobody sets off: beeps through its last three seconds, then goes off anyway
    private IEnumerator Lifetime()
    {
        float quiet = Mathf.Max(0f, maxLifetime - 3f);
        yield return new WaitForSeconds(quiet);
        for (float left = maxLifetime - quiet; left > 0f && !exploded; left -= 1f)
        {
            photonView.RPC(nameof(RPC_Beep), RpcTarget.All, left < 1.5f);
            yield return new WaitForSeconds(Mathf.Min(1f, left));
        }
        Detonate();
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

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsAuthority || exploded) return;
        if (explodeOnImpact) { Detonate(); return; }
        if (!sticky || stuck) return;

        // Players bounce it off; anything else holds it
        Transform hit = collision.rigidbody != null ? collision.rigidbody.transform : collision.collider.transform;
        if (hit.GetComponentInParent<Player>() != null) return;
        stuck = true;

        // Networked things (boxes, ladders) by view id; everything else by scene path
        var       view   = hit.GetComponentInParent<PhotonView>();
        int       viewId = view != null ? view.ViewID : 0;
        Transform target = view != null ? view.transform : hit;
        string    path   = view != null ? "" : PathOf(target);

        // Sit on the surface, flat against it
        var contact = collision.GetContact(0);
        Vector3    position = contact.point + contact.normal * 0.12f;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, contact.normal);
        Vector3    localPos = target.InverseTransformPoint(position);
        Quaternion localRot = Quaternion.Inverse(target.rotation) * rotation;
        photonView.RPC(nameof(RPC_Stick), RpcTarget.All, viewId, path, localPos, localRot, position, rotation);
    }

    [PunRPC]
    private void RPC_Stick(int viewId, string path, Vector3 localPos, Quaternion localRot, Vector3 position, Quaternion rotation)
    {
        stuck = true;
        Body.linearVelocity  = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.isKinematic     = true;
        foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false; // Never shoves what it's on

        var view = viewId != 0 ? PhotonView.Find(viewId) : null;
        anchor = view != null ? view.transform : !string.IsNullOrEmpty(path) ? GameObject.Find(path)?.transform : null;
        anchorPosition = localPos;
        anchorRotation = localRot;
        // Found by scene path, the name may belong to another object here (levels have many "Cube"s): only
        // trust the anchor if it puts the charge where the thrower saw it stick, else stay put in the world
        if (anchor != null && view == null && Vector3.Distance(anchor.TransformPoint(localPos), position) > 0.25f) anchor = null;
        if (anchor == null) transform.SetPositionAndRotation(position, rotation);
        else                FollowAnchor();

        Sfx.At(SfxKind.Snap, transform.position, 0.8f, 25f);
        Vfx.Burst(transform.position, new Color(1f, 0.35f, 0.35f), 8, 2.5f, 0.12f, 0.35f);
    }

    private void LateUpdate()
    {
        if (stuck && anchor != null) FollowAnchor();
    }

    private void FollowAnchor() =>
        transform.SetPositionAndRotation(anchor.TransformPoint(anchorPosition), anchor.rotation * anchorRotation);

    private static string PathOf(Transform t) => t.parent == null ? "/" + t.name : PathOf(t.parent) + "/" + t.name;

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
        exploded = true;
        float radius = explosionRadius * Mathf.Sqrt(Power);
        WorldFx.SpawnVfx(explosionVFX, position);
        WorldFx.PlayClip(explosionSFX, position, audioRadius);
        Fx.Explosion(position, radius);
        WorldFx.Explode(position, radius, explosionForce * Power, affectedLayers, sourceActor: photonView.OwnerActorNr, cause: "BoomBomb");
    }
}
