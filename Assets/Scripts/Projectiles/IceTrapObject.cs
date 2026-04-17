using UnityEngine;
using Photon.Pun;

/// <summary>
/// Placed Ice Trap object.  Every <see cref="iceApplyInterval"/> seconds it checks whether
/// the local player is within <see cref="triggerRadius"/> metres.  If so, it applies a
/// zero-friction <see cref="SlickRevertHelper"/> to every static collider found directly
/// beneath and around the player's feet, removing traction for <see cref="iceDuration"/> seconds.
///
/// Network behaviour:
///   - Runs on ALL clients; each client is responsible for icing the ground under their
///     own player.  This avoids needing to sync ground-material changes over the network.
///   - SlickRevertHelper's static registry ensures the same surface collider is never
///     double-applied; subsequent hits simply refresh the timer.
///
/// Unity setup:
///  - Attach to IceTrap prefab (Resources/Object/IceTrap).
///  - Prefab also needs: Rigidbody (Kinematic), Collider (Is Trigger ✓), PhotonView.
///  - Optional: assign an ambient humSFX that plays in a loop while the trap is active.
///  - Assign icingVFX (particle prefab) if you want a frost effect spawned at the player's feet.
/// </summary>
public class IceTrapObject : MonoBehaviourPun
{
    [Header("Detection")]
    [SerializeField] private float triggerRadius = 5f;

    [Header("Ice Settings")]
    [SerializeField] private float iceDuration       = 5f;   // How long the slick lasts after application
    [SerializeField] private float iceSearchRadius   = 2.5f; // Radius around the player's feet to search for ground
    [SerializeField] private float iceApplyInterval  = 0.75f; // Seconds between re-applications (refresh timer)
    [SerializeField] private float iceFriction       = 0f;   // PhysicsMaterial friction value (0 = perfectly slick)

    [Header("Optional")]
    [SerializeField] private float    lifespan = 0f;         // 0 = infinite; > 0 = self-destructs after this many seconds
    [SerializeField] private AudioClip humSFX;
    [SerializeField] private float     humVolume = 0.4f;
    [SerializeField] private GameObject icingVFX;            // Spawned at player's feet while icing

    private float lastApplyTime = -999f;

    private static readonly Collider[] groundBuffer = new Collider[16];

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        if (humSFX != null)
        {
            AudioSource hum  = gameObject.AddComponent<AudioSource>();
            hum.clip         = humSFX;
            hum.loop         = true;
            hum.spatialBlend = 1f;
            hum.rolloffMode  = AudioRolloffMode.Linear;
            hum.minDistance  = 1f;
            hum.maxDistance  = triggerRadius * 1.5f;
            hum.volume       = humVolume;
            hum.Play();
        }

        if (lifespan > 0f && (photonView.IsMine || PhotonNetwork.IsMasterClient))
            Invoke(nameof(DestroySelf), lifespan);
    }

    private void DestroySelf()
    {
        PhotonNetwork.Destroy(gameObject);
        Destroy(gameObject);
    }

    // -------------------------------------------------------------------------
    // Per-frame trigger check (runs on every client for their local player)
    // -------------------------------------------------------------------------

    private void FixedUpdate()
    {
        if (Player.Instance == null) return;

        float dist = Vector3.Distance(transform.position, Player.Instance.transform.position);
        if (dist > triggerRadius) return;

        if (Time.time < lastApplyTime + iceApplyInterval) return;
        lastApplyTime = Time.time;

        ApplyIceUnderPlayer(Player.Instance);
    }

    // -------------------------------------------------------------------------
    // Ice application
    // -------------------------------------------------------------------------

    private void ApplyIceUnderPlayer(Player player)
    {
        // Use the player's feet position as the search centre
        Vector3 feetPos = player.transform.position;

        // Optional spawn point effect
        if (icingVFX != null)
            Instantiate(icingVFX, feetPos, Quaternion.identity);

        // Find all static colliders in the search radius (floor, ramps, platforms)
        int count = Physics.OverlapSphereNonAlloc(feetPos, iceSearchRadius, groundBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider col = groundBuffer[i];
            if (col == null) continue;

            // Skip dynamic objects and players — only affect static world geometry
            if (col.attachedRigidbody != null) continue;
            if (col.TryGetComponent<Player>(out _)) continue;

            // SlickRevertHelper handles de-duplication and timer refreshing automatically
            SlickRevertHelper.ApplySlick(col, iceDuration, iceFriction);
        }
    }

    // -------------------------------------------------------------------------
    // Debug gizmo
    // -------------------------------------------------------------------------

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Detection radius
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.2f);
        Gizmos.DrawSphere(transform.position, triggerRadius);

        // Ice search radius indicator (shown at trap's feet level)
        Gizmos.color = new Color(0.8f, 0.95f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, iceSearchRadius);
    }
#endif
}
