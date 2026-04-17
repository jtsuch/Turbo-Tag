using UnityEngine;
using Photon.Pun;
using System.Collections;

/// <summary>
/// Gravity Orb projectile.  Immediately spawns a trailing particle VFX parented to itself,
/// then applies a continuous inward pull to all nearby Rigidbodies for its entire lifetime.
///
/// Pull model: linear falloff — full <see cref="pullForce"/> at the orb's surface,
/// zero at <see cref="pullRadius"/>.
///
/// Network behaviour (mirrors GravityWellObject):
///   - All clients apply pull to their own local Player each FixedUpdate.
///   - Only the owning client applies pull to non-player Rigidbodies.
///   - Owner destroys the orb after <see cref="lifetime"/> seconds.
///
/// Unity setup:
///  - Attach to GravBall prefab (Resources/Object/GravBall).
///  - Prefab also needs: small SphereCollider, Rigidbody, PhotonView.
///  - Assign orbVFX (a looping particle system prefab) in the Inspector.
///    It will be instantiated as a child and destroyed automatically with the orb.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GravBallObject : MonoBehaviourPun
{
    [Header("Gravity Settings")]
    [SerializeField] private float pullRadius = 8f;
    [SerializeField] private float pullForce  = 18f;

    [Header("Lifetime")]
    [SerializeField] private float lifetime = 6f;

    [Header("VFX")]
    [SerializeField] private GameObject orbVFX;

    private static readonly Collider[] overlapBuffer = new Collider[32];

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        // Spawn VFX as a child so it follows and is destroyed with the orb
        if (orbVFX != null)
            Instantiate(orbVFX, transform.position, transform.rotation, transform);

        if (photonView.IsMine)
            StartCoroutine(LifetimeRoutine());
    }

    private IEnumerator LifetimeRoutine()
    {
        yield return new WaitForSeconds(lifetime);
        PhotonNetwork.Destroy(gameObject);
        Destroy(gameObject);
    }

    // -------------------------------------------------------------------------
    // Called by GravBall.OnThrow
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
    // Physics
    // -------------------------------------------------------------------------

    private void FixedUpdate()
    {
        ApplyPullToLocalPlayer();

        if (photonView.IsMine)
            ApplyPullToNearbyRigidbodies();
    }

    private void ApplyPullToLocalPlayer()
    {
        if (Player.Instance == null || Player.Instance.rb == null) return;
        TryApplyPull(Player.Instance.rb);
    }

    private void ApplyPullToNearbyRigidbodies()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, pullRadius, overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col == null) continue;
            if (col.TryGetComponent<Player>(out _)) continue; // Handled in ApplyPullToLocalPlayer
            Rigidbody rb = col.attachedRigidbody;
            if (rb == null || rb.isKinematic) continue;
            TryApplyPull(rb);
        }
    }

    private void TryApplyPull(Rigidbody rb)
    {
        Vector3 toOrb = transform.position - rb.position;
        float   dist  = toOrb.magnitude;

        if (dist > pullRadius || dist < 0.1f) return;

        float strength = pullForce * (1f - dist / pullRadius);
        rb.AddForce(toOrb.normalized * strength, ForceMode.Force);
    }

    // -------------------------------------------------------------------------
    // Debug gizmo
    // -------------------------------------------------------------------------

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0f, 1f, 0.25f);
        Gizmos.DrawSphere(transform.position, pullRadius);
    }
#endif
}
