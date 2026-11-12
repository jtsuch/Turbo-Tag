using Photon.Pun;
using UnityEngine;

/// <summary>
/// Placed crate from the Box ability: a real physics object that falls and settles where it lands (placed on a
/// ledge's edge it tips off, stacked on something it rests on it). The placer's client simulates it, heavy enough
/// that players don't shove it about; every other screen follows it through <see cref="PlayerNetworkSync"/>
/// (snapshot interpolation, remote copies kinematic), like the Ladder. Plays an impact sound when hit hard enough.
/// Attach to: Resources/Object/Box (with its Rigidbody, PhotonView and PlayerNetworkSync).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BoxObject : MonoBehaviourPun
{
    [Tooltip("Mass (kg): heavy, so a player walking into it doesn't push it around.")]
    [SerializeField] private float mass = 60f;

    [Header("Audio")]
    [SerializeField] private AudioClip impactSound;
    [SerializeField] private float minImpactSpeed = 2f;  // m/s — filters out gentle grazes
    [SerializeField] private float audioRadius    = 15f;
    [SerializeField] private float audioVolume    = 1f;

    private void Start()
    {
        if (photonView != null && !photonView.IsMine) return; // Other screens: kinematic, moved by PlayerNetworkSync

        var body = GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.useGravity = true;
        body.mass = mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.WakeUp();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (impactSound == null) return;
        if (collision.relativeVelocity.magnitude < minImpactSpeed) return;

        WorldFx.PlayClip(impactSound, transform.position, audioRadius, audioVolume);
    }
}
