using Photon.Pun;
using UnityEngine;

/// <summary>
/// Makes a placed object a real physics object (the Ladder): the placer's client simulates it, and every other screen
/// follows it through <see cref="PlayerNetworkSync"/> (snapshot interpolation; remote copies stay kinematic). Since
/// Nov 14 (Justin: "revert the ladder to a normal object") it stands upright where placed (no starting lean), with
/// a normal weight and balance, and players climb it with F (<see cref="LadderClimb"/>, added here on every screen).
/// Attach to the prefab root next to its Rigidbody, PhotonView and PlayerNetworkSync.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PhotonView))]
public class TippingProp : MonoBehaviourPun
{
    [Tooltip("Mass (kg). Light enough that a player bumping into it tips it.")]
    [SerializeField] private float mass = 15f;
    [Tooltip("Centre of mass height as a fraction of the object's height (0 = base, 1 = top). Higher tips sooner.")]
    [SerializeField] private float centreOfMassHeight = 0.45f;
    [Tooltip("Lean (degrees) away from the placer when it appears, around its base, so it topples faster.")]
    [SerializeField] private float startTilt = 0f;

    private void Awake()
    {
        if (!TryGetComponent(out LadderClimb _))
        {
            gameObject.AddComponent<LadderClimb>();
            photonView.RefreshRpcMonoBehaviourCache();   // It receives the riders' RPCs on this PhotonView
        }
    }

    private void Start()
    {
        if (!photonView.IsMine) return; // Other screens: kinematic, moved by PlayerNetworkSync

        var body = GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.useGravity = true;
        body.mass = mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.angularDamping = 0.05f;
        body.sleepThreshold = 0f; // Never sleeps: one resting on something that moves away must still fall

        var colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length == 0) return;
        Bounds bounds = colliders[0].bounds;
        foreach (var c in colliders) bounds.Encapsulate(c.bounds);

        // Lean it forward, over its own face (the ladder is placed facing the way the placer looks: its local +X),
        // around the middle of its base. Only when it stands up (placed on a floor)
        Vector3 away = Vector3.ProjectOnPlane(transform.right, Vector3.up);
        if (startTilt != 0f && transform.forward.y > 0.7f && away.sqrMagnitude > 0.01f)
        {
            Vector3 basePoint = new(bounds.center.x, bounds.min.y, bounds.center.z);
            transform.RotateAround(basePoint, Vector3.Cross(Vector3.up, away.normalized), startTilt);
            body.position = transform.position;
            body.rotation = transform.rotation;
            Physics.SyncTransforms();
            bounds = colliders[0].bounds;
            foreach (var c in colliders) bounds.Encapsulate(c.bounds);
        }

        // Normal physics (Justin, Nov 14: "just let it fall with normal gravity"): the centre of mass follows its colliders
        // (and any climbers' capsules, LadderClimb), never set by hand, which made it right itself
        body.ResetCenterOfMass();
        body.WakeUp();
        _ = centreOfMassHeight;
    }
}
