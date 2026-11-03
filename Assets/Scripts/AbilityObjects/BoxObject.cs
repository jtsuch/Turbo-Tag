using UnityEngine;

/// <summary>
/// Placed crate from the Box ability. Plays an impact sound when hit hard enough.
/// Attach to: Resources/Object/Box.
/// </summary>
public class BoxObject : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip impactSound;
    [SerializeField] private float minImpactSpeed = 2f;  // m/s — filters out gentle grazes
    [SerializeField] private float audioRadius    = 15f;
    [SerializeField] private float audioVolume    = 1f;

    void OnCollisionEnter(Collision collision)
    {
        if (impactSound == null) return;
        if (collision.relativeVelocity.magnitude < minImpactSpeed) return;

        WorldFx.PlayClip(impactSound, transform.position, audioRadius, audioVolume);
    }
}
