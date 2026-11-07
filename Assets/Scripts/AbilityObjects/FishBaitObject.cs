using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Fish Bait: a juicy fish that, once it lands, makes every other penguin within <see cref="lureRadius"/> stare
/// at it for <see cref="lureTime"/> seconds. They can still move, but their view is locked on the fish (and
/// "forward" is toward it, so walking forward walks to it).
///   owner first touch → RPC_Land (all: same spot everywhere, plop) → each client lures its own player while in
///   range (PlayerCam.Lure) → owner removes it after the lure time.
/// It only settles on ground (walls and ceilings bounce it off), lying flat on the floor under where it touched.
/// The thrower and shielded players aren't lured. Attach to: Resources/Object/FishBait (Rigidbody, Collider, PhotonView).
/// </summary>
public class FishBaitObject : NetworkedProjectile
{
    [Header("Lure")]
    [TunableParam("Lure Radius", 1f, 40f)]
    [SerializeField] private float lureRadius = 10f;
    [TunableParam("Lure Time", 0.5f, 20f)]
    [SerializeField] private float lureTime = 4f;
    [Tooltip("Seconds before it is removed if it never lands.")]
    [SerializeField] private float maxFlightTime = 6f;
    [Tooltip("Half its thickness lying on its side: how far above the floor its middle rests.")]
    [SerializeField] private float restHeight = 0.11f;

    private bool  landed;
    private float lureEndsAt;
    private float nextSparkle;

    protected override void OnLaunched()
    {
        if (IsAuthority) StartCoroutine(RemoveIfLost());
    }

    private IEnumerator RemoveIfLost()
    {
        yield return new WaitForSeconds(maxFlightTime);
        if (!landed) DestroyNetworked();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsAuthority || landed) return;
        if (collision.collider.GetComponentInParent<Player>() != null) return; // Bounces off penguins
        if (!OnGround(collision, out Vector3 floor)) return;                       // Walls: keep falling
        photonView.RPC(nameof(RPC_Land), RpcTarget.All, floor + Vector3.up * restHeight);
    }

    private void OnCollisionStay(Collision collision) => OnCollisionEnter(collision);

    // Touching something it can lie on: the floor point under its middle
    private bool OnGround(Collision collision, out Vector3 floor)
    {
        floor = default;
        bool ground = false;
        for (int i = 0; i < collision.contactCount; i++)
            if (collision.GetContact(i).normal.y > 0.5f) { ground = true; floor = collision.GetContact(i).point; break; }
        if (!ground) return false;
        if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 1.5f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<Player>() == null && !hit.collider.transform.IsChildOf(transform))
            floor = hit.point;
        return true;
    }

    [PunRPC]
    private void RPC_Land(Vector3 position)
    {
        if (landed) return;
        landed = true;
        lureEndsAt = Time.time + lureTime;
        Body.linearVelocity  = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.isKinematic     = true;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, transform.eulerAngles.y, 90f)); // Lying on its side

        Sfx.At(SfxKind.Plop, position, 1f, 40f);
        Sfx.At(SfxKind.Shimmer, position, 0.6f, 40f);
        Vfx.Ring(position, new Color(0.55f, 0.85f, 1f, 0.8f), 18, 3f, 0.3f, 0.5f);

        if (IsAuthority) StartCoroutine(RemoveAfterLure());
    }

    private IEnumerator RemoveAfterLure()
    {
        yield return new WaitForSeconds(lureTime + 0.2f);
        DestroyNetworked();
    }

    private void Update()
    {
        if (!landed || Time.time > lureEndsAt) return;

        // Flop about and glint so it's impossible to miss
        transform.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 18f) * 20f, transform.eulerAngles.y, 90f);
        if (Time.time >= nextSparkle)
        {
            nextSparkle = Time.time + 0.25f;
            Vfx.Burst(transform.position + Vector3.up * 0.3f, new Color(1f, 0.85f, 0.4f), 3, 1.5f, 0.15f, 0.5f, direction: Vector3.up, spread: 0.7f);
        }

        // This client's own player, if they're close enough, can't look away
        var local = Player.Local;
        if (local == null || local.IsCaught || local.ActorNumber == photonView.OwnerActorNr) return;
        if (!local.AffectedBy(photonView.OwnerActorNr)) return;
        if (Vector3.Distance(local.transform.position, transform.position) > lureRadius * Mathf.Sqrt(Power)) return;
        if (PlayerCam.Local != null) PlayerCam.Local.Lure(transform.position);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, lureRadius);
    }
#endif
}
