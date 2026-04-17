using UnityEngine;
using Photon.Pun;

/// <summary>
/// Networked repulsion field spawned by CenterOfImpulseEffect.
/// Runs on ALL clients; each client pushes their own local Player.Instance away
/// unless they are the owner of this field (the player who triggered the effect).
/// The owner's client pushes nearby non-player Rigidbodies instead.
///
/// Unity setup:
///  - Attach to RepulseField prefab (Resources/Object/RepulseField).
///  - Prefab also needs: PhotonView, optional TransformView for position sync.
/// </summary>
public class RepulseField : MonoBehaviourPun
{
    [Header("Repulsion Settings")]
    [SerializeField] private float pushRadius = 7f;
    [SerializeField] private float pushForce  = 22f;

    private static readonly Collider[] overlapBuffer = new Collider[32];

    private void FixedUpdate()
    {
        bool isOwner = PhotonNetwork.LocalPlayer.ActorNumber == photonView.OwnerActorNr;

        // Every client repels their own local player (except the impulsing player)
        if (!isOwner)
            TryPushPlayer();

        // Owner repels nearby non-player rigidbodies
        if (isOwner)
            PushNearbyRigidbodies();
    }

    private void TryPushPlayer()
    {
        if (Player.Instance == null || Player.Instance.rb == null) return;

        Vector3 toPlayer = Player.Instance.rb.position - transform.position;
        float   dist     = toPlayer.magnitude;
        if (dist > pushRadius || dist < 0.1f) return;

        float strength = pushForce * (1f - dist / pushRadius);
        Player.Instance.rb.AddForce(toPlayer.normalized * strength, ForceMode.Force);
    }

    private void PushNearbyRigidbodies()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, pushRadius, overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col == null) continue;
            if (col.TryGetComponent<Player>(out _)) continue;

            Rigidbody rb = col.attachedRigidbody;
            if (rb == null || rb.isKinematic) continue;

            Vector3 toObj = rb.position - transform.position;
            float   dist  = toObj.magnitude;
            if (dist > pushRadius || dist < 0.1f) continue;

            float strength = pushForce * (1f - dist / pushRadius);
            rb.AddForce(toObj.normalized * strength, ForceMode.Force);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.2f);
        Gizmos.DrawSphere(transform.position, pushRadius);
    }
#endif
}
