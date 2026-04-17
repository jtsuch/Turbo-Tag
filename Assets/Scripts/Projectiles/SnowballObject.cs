using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

/// <summary>
/// Snowball projectile for the Snowball ThrowAbility.
/// On impact, broadcasts an RPC that temporarily applies a zero-friction PhysicsMaterial
/// to the nearest static surface.  The SlickRevertHelper companion class restores the
/// original material (and self-destructs) after slickDuration seconds.
///
/// Unity setup:
///  - Attach to the Snowball prefab (Resources/Object/Snowball).
///  - Prefab also needs: Rigidbody, Collider, PhotonView.
///  - Optionally assign hitVFX (a local particle-effect prefab).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SnowballObject : MonoBehaviourPun
{
    [Header("Slick Settings")]
    [SerializeField] private float slickDuration = 5f;
    [SerializeField] private float slickFriction = 0f;
    [SerializeField] private float slickSearchRadius = 2f;

    [Header("References")]
    [SerializeField] private GameObject hitVFX;

    private bool hasHit = false;

    // -------------------------------------------------------------------------
    // Called by Snowball.OnThrow immediately after the projectile is spawned
    // -------------------------------------------------------------------------

    public void IgnoreColliders(Collider[] toIgnore)
    {
        if (toIgnore == null) return;
        Collider[] myCols = GetComponentsInChildren<Collider>();
        foreach (Collider src in toIgnore)
        {
            if (src == null) continue;
            foreach (Collider dst in myCols)
                Physics.IgnoreCollision(src, dst);
        }
    }

    // -------------------------------------------------------------------------
    // Collision
    // -------------------------------------------------------------------------

    private void OnCollisionEnter(Collision collision)
    {
        if (!photonView.IsMine || hasHit) return;
        hasHit = true;

        Vector3 pos = transform.position;

        // Disable visuals immediately so the snowball vanishes on impact
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            r.enabled = false;

        if (TryGetComponent(out Rigidbody rb)) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }

        photonView.RPC(nameof(RPC_ApplySlick), RpcTarget.All, pos);

        PhotonNetwork.Destroy(gameObject);
        Destroy(gameObject);
    }

    // -------------------------------------------------------------------------
    // RPC
    // -------------------------------------------------------------------------

    [PunRPC]
    private void RPC_ApplySlick(Vector3 hitPos)
    {
        if (hitVFX != null)
            Instantiate(hitVFX, hitPos, Quaternion.identity);

        // Find the nearest static collider to apply the slick surface to
        Collider nearest = FindNearestStaticCollider(hitPos);
        if (nearest == null) return;

        SlickRevertHelper.ApplySlick(nearest, slickDuration, slickFriction);
    }

    private static readonly Collider[] overlapBuffer = new Collider[32];

    private Collider FindNearestStaticCollider(Vector3 pos)
    {
        int count = Physics.OverlapSphereNonAlloc(pos, slickSearchRadius, overlapBuffer);
        Collider best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];

            // Skip dynamic objects and players
            if (col.attachedRigidbody != null) continue;
            if (col.TryGetComponent<Player>(out _)) continue;

            float dist = Vector3.Distance(col.ClosestPoint(pos), pos);
            if (dist < bestDist) { bestDist = dist; best = col; }
        }
        return best;
    }
}

/// <summary>
/// Applies a slick PhysicsMaterial to a collider and reverts it after a set duration.
/// Uses a static registry so a second snowball hitting the same surface refreshes the
/// timer instead of creating a nested revert chain.
/// </summary>
public class SlickRevertHelper : MonoBehaviour
{
    private static readonly Dictionary<Collider, SlickRevertHelper> activeSlicks = new();

    private Collider         targetCollider;
    private PhysicsMaterial  originalMaterial;
    private PhysicsMaterial  slickMaterial;
    private float            remainingTime;

    // -------------------------------------------------------------------------
    // Static entry point
    // -------------------------------------------------------------------------

    public static void ApplySlick(Collider col, float duration, float friction)
    {
        // If already slick, just refresh the timer
        if (activeSlicks.TryGetValue(col, out SlickRevertHelper existing) && existing != null)
        {
            existing.remainingTime = duration;
            return;
        }

        GameObject go = new("SlickRevertHelper");
        SlickRevertHelper helper = go.AddComponent<SlickRevertHelper>();
        helper.Initialize(col, duration, friction);
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Initialize(Collider col, float duration, float friction)
    {
        targetCollider   = col;
        originalMaterial = col.sharedMaterial;
        remainingTime    = duration;

        slickMaterial = new PhysicsMaterial("Slick")
        {
            dynamicFriction  = friction,
            staticFriction   = friction,
            bounciness       = 0f,
            frictionCombine  = PhysicsMaterialCombine.Minimum,
            bounceCombine    = PhysicsMaterialCombine.Minimum,
        };

        targetCollider.sharedMaterial = slickMaterial;
        activeSlicks[col] = this;
    }

    private void Update()
    {
        remainingTime -= Time.deltaTime;
        if (remainingTime <= 0f)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (targetCollider != null)
        {
            targetCollider.sharedMaterial = originalMaterial;

            if (activeSlicks.TryGetValue(targetCollider, out SlickRevertHelper registered) && registered == this)
                activeSlicks.Remove(targetCollider);
        }

        if (slickMaterial != null)
            Destroy(slickMaterial);
    }
}
