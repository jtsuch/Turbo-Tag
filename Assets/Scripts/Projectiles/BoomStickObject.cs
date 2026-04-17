using UnityEngine;
using Photon.Pun;

/// <summary>
/// Dynamite projectile for the BoomStick ability.
/// Auto-detonates on the first collision after a short grace period,
/// applying an explosion force to all nearby rigidbodies.
///
/// Unity setup:
///  - Attach to the BoomStick prefab (Resources/Object/BoomStick).
///  - The prefab also needs: Rigidbody, Collider, PhotonView.
///  - Assign explosionVFX to a local particle-effect prefab (optional).
///  - Set affectedLayers to include the Player layer (and anything else the blast should push).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BoomStickObject : MonoBehaviourPun
{
    [Header("Explosion Settings")]
    public float explosionRadius = 4f;
    public float explosionForce = 40f;
    public LayerMask affectedLayers = ~0;   // Defaults to Everything; narrow in Inspector if needed

    [Header("References")]
    [SerializeField] private GameObject explosionVFX;   // Optional local particle prefab
    [SerializeField] private AudioClip explosionSFX;    // Optional world-space audio clip

    [Header("VFX Settings")]
    [SerializeField] private float vfxScale = 0.3f;     // Multiplier applied to each particle system's start size

    [Header("Audio Settings")]
    [SerializeField] private float audioRadius = 20f;   // Distance at which the explosion becomes inaudible
    [SerializeField] private float audioVolume = 1f;

    private bool hasExploded = false;

    /// <summary>
    /// Called by BoomStick.OnThrow immediately after the dynamite is spawned.
    /// Ignores collisions with the thrower's own colliders so it doesn't detonate on spawn.
    /// </summary>
    public void IgnoreColliders(Collider[] toIgnore)
    {
        if (toIgnore == null) return;
        var myCols = GetComponentsInChildren<Collider>();
        foreach (var src in toIgnore)
        {
            if (src == null) continue;
            foreach (var dst in myCols)
                Physics.IgnoreCollision(src, dst);
        }
    }

    void OnCollisionEnter(Collision _)
    {
        // Only the owner triggers the explosion so it isn't called multiple times across clients
        if (!photonView.IsMine) return;
        if (hasExploded) return;

        TriggerExplosion();
    }

    private void TriggerExplosion()
    {
        hasExploded = true;

        // Immediately hide and freeze the object so it never lingers visually
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        var rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }

        // Capture position before the object is destroyed
        Vector3 pos = transform.position;

        // Broadcast the explosion to all clients, then destroy
        photonView.RPC(nameof(ExplodeRPC), RpcTarget.All, pos);

        // PhotonNetwork.Destroy handles networked cleanup; Destroy is the local fallback
        PhotonNetwork.Destroy(gameObject);
        Destroy(gameObject);
    }

    [PunRPC]
    private void ExplodeRPC(Vector3 pos)
    {
        if (explosionVFX != null)
        {
            GameObject vfx = Instantiate(explosionVFX, pos, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * vfxScale;
        }

        // Play explosion audio with a configurable world-space radius.
        // AudioSource.PlayClipAtPoint uses Unity's default max distance (~500), which is too large;
        // creating a temporary source lets us set exact rolloff and range.
        if (explosionSFX != null)
        {
            GameObject audioObj = new("ExplosionAudio");
            audioObj.transform.position = pos;
            AudioSource src = audioObj.AddComponent<AudioSource>();
            src.clip          = explosionSFX;
            src.spatialBlend  = 1f;                          // Full 3D attenuation
            src.rolloffMode   = AudioRolloffMode.Linear;     // Linear: smoothly fades to silence at maxDistance
            src.minDistance   = 1f;
            src.maxDistance   = audioRadius;
            src.volume        = audioVolume;
            src.Play();
            Destroy(audioObj, explosionSFX.length + 0.1f);  // Clean up after clip finishes
        }

        // Apply physics blast to all nearby rigidbodies.
        // Fall back to all layers if affectedLayers was left at 0 (Nothing) in the Inspector.
        Collider[] colliders = affectedLayers.value != 0
            ? Physics.OverlapSphere(pos, explosionRadius, affectedLayers)
            : Physics.OverlapSphere(pos, explosionRadius);
        foreach (Collider nearby in colliders)
        {
            if (nearby.attachedRigidbody != null)
            {
                nearby.attachedRigidbody.AddExplosionForce(
                    explosionForce, pos, explosionRadius, 1f, ForceMode.Impulse);
            }
        }
    }
}
