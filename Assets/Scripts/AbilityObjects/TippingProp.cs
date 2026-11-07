using Photon.Pun;
using UnityEngine;

/// <summary>
/// Makes a placed object a real, top-heavy physics object that topples easily (Ladder): the placer's client
/// simulates it (light, centre of mass high up), and every other screen follows it through
/// <see cref="PlayerNetworkSync"/> (snapshot interpolation; remote copies stay kinematic). Players pushing or
/// climbing it knock it over. It starts leaning <c>startTilt</c> degrees away from the placer, so it falls sooner.
/// Attach to the prefab root next to its Rigidbody, PhotonView and PlayerNetworkSync.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PhotonView))]
public class TippingProp : MonoBehaviourPun
{
    [Tooltip("Mass (kg). Light enough that a player bumping into it tips it.")]
    [SerializeField] private float mass = 3f;
    [Tooltip("Centre of mass height as a fraction of the object's height (0 = base, 1 = top). Higher tips sooner.")]
    [SerializeField] private float centreOfMassHeight = 0.7f;
    [Tooltip("Lean (degrees) away from the placer when it appears, around its base, so it topples faster.")]
    [SerializeField] private float startTilt = 20f;

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

        var colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length == 0) return;
        Bounds bounds = colliders[0].bounds;
        foreach (var c in colliders) bounds.Encapsulate(c.bounds);

        // Lean it forward (the way the placer looks) around the middle of its base
        var view = Camera.main != null ? Camera.main.transform : null;
        Vector3 away = view != null ? Vector3.ProjectOnPlane(view.forward, Vector3.up) : transform.forward;
        if (startTilt != 0f && away.sqrMagnitude > 0.01f)
        {
            Vector3 basePoint = new(bounds.center.x, bounds.min.y, bounds.center.z);
            transform.RotateAround(basePoint, Vector3.Cross(Vector3.up, away.normalized), startTilt);
            body.position = transform.position;
            body.rotation = transform.rotation;
            Physics.SyncTransforms();
            bounds = colliders[0].bounds;
            foreach (var c in colliders) bounds.Encapsulate(c.bounds);
        }

        // Top-heavy: the centre of mass well up the object's height (in world space, whatever its rotation)
        Vector3 worldCom = new(bounds.center.x, bounds.min.y + bounds.size.y * centreOfMassHeight, bounds.center.z);
        body.centerOfMass = transform.InverseTransformPoint(worldCom);
        body.WakeUp();
    }
}
