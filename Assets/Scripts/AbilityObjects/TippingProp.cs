using Photon.Pun;
using UnityEngine;

/// <summary>
/// Makes a placed object a real, top-heavy physics object that topples easily (Ladder): the placer's client
/// simulates it (light, centre of mass high up), and every other screen follows it through
/// <see cref="PlayerNetworkSync"/> (snapshot interpolation; remote copies stay kinematic). Players pushing or
/// climbing it knock it over. Attach to the prefab root next to its Rigidbody, PhotonView and PlayerNetworkSync.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PhotonView))]
public class TippingProp : MonoBehaviourPun
{
    [Tooltip("Mass (kg). Light enough that a player bumping into it tips it.")]
    [SerializeField] private float mass = 3f;
    [Tooltip("Centre of mass height as a fraction of the object's height (0 = base, 1 = top). Higher tips sooner.")]
    [SerializeField] private float centreOfMassHeight = 0.7f;

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

        // Top-heavy: the centre of mass well up the object's height (in world space, whatever its rotation)
        var colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length == 0) return;
        Bounds bounds = colliders[0].bounds;
        foreach (var c in colliders) bounds.Encapsulate(c.bounds);
        Vector3 worldCom = new(bounds.center.x, bounds.min.y + bounds.size.y * centreOfMassHeight, bounds.center.z);
        body.centerOfMass = transform.InverseTransformPoint(worldCom);
        body.WakeUp();
    }
}
