using UnityEngine;
using Photon.Pun;

/// <summary>
/// Networked repulsion field spawned by CenterOfImpulseEffect. Each client pushes the bodies
/// it has authority over away from the field, except the owner's own player (the source).
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
    private readonly System.Collections.Generic.HashSet<Rigidbody> pushed = new();

    private void FixedUpdate()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, pushRadius, overlapBuffer,
                                                  Physics.AllLayers, QueryTriggerInteraction.Ignore);
        pushed.Clear();
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = overlapBuffer[i].attachedRigidbody;
            if (rb == null || rb.isKinematic || !pushed.Add(rb) || !WorldFx.HasAuthority(rb)) continue;

            // The source player is never pushed by their own field
            var player = rb.GetComponent<Player>();
            if (player != null && player.ActorNumber == photonView.OwnerActorNr) continue;

            Vector3 away = rb.position - transform.position;
            float   dist = away.magnitude;
            if (dist > pushRadius || dist < 0.1f) continue;

            rb.AddForce(away.normalized * (pushForce * (1f - dist / pushRadius)), ForceMode.Force);
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
