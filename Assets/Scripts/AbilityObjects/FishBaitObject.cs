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
/// A lured player within <see cref="eatRange"/> can press the Grab key (F) to eat it: the fish is gone for everyone.
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
    [Tooltip("How close a lured player must be to eat the fish (Grab key).")]
    [SerializeField] private float eatRange = 2f;

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
        if (!IsAuthority || landed || !RealContact(collision)) return;
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

    private AoeDebug aoe, aoeEat;

    private void Update()
    {
        if (!landed || Time.time > lureEndsAt) { if (aoe != null) Destroy(aoe.gameObject); return; }
        if (AoeDebug.On && aoe == null) aoe = AoeDebug.Sphere(transform.position, lureRadius, new Color(1f, 0.9f, 0.3f), "Fish Bait lure", 0f, transform);
        if (AoeDebug.On && aoeEat == null) aoeEat = AoeDebug.Sphere(transform.position, eatRange, new Color(1f, 0.6f, 0.15f), "Fish Bait eat (F)", 0f, transform);

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
        float distance = Vector3.Distance(local.transform.position, transform.position);
        if (distance > lureRadius * Mathf.Sqrt(Power)) return;
        if (PlayerCam.Local != null) PlayerCam.Local.Lure(transform.position);

        // Close enough: eat it and get your eyes back
        if (distance <= eatRange && !local.IsCaught)
        {
            KeyCode key = Keybinds.Get(InputAction.Grab);
            local.ClaimGrabKey(); // F eats, it doesn't also grab a ledge
            HUDManager.Prompt(key, "Eat the fish");
            if (!PauseMenuManager.IsPaused && Input.GetKeyDown(key)) photonView.RPC(nameof(RPC_Eaten), RpcTarget.All);
        }
    }

    [PunRPC]
    private void RPC_Eaten()
    {
        if (!landed || Time.time > lureEndsAt) return;
        lureEndsAt = 0f; // Nobody is lured any more
        Sfx.At(SfxKind.Pop, transform.position, 1f, 25f, 0.8f);
        Vfx.Burst(transform.position, new Color(1f, 0.85f, 0.4f), 14, 2.5f, 0.2f, 0.5f);
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        if (IsAuthority) DestroyNetworked();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, lureRadius);
    }
#endif
}
