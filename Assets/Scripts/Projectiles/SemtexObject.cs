using UnityEngine;
using Photon.Pun;
using System.Collections;

/// <summary>
/// Semtex charge projectile.  Sticks to the first surface it touches, waits a fuse
/// duration, beeps three times (with a glow pulse each beep), then detonates.
///
/// Network flow:
///   owner OnCollisionEnter → RPC_Stick (all) → owner runs fuse coroutine
///   → RPC_Beep ×3 (all: SFX + glow) → RPC_Explode (all: VFX + physics) → destroy
///
/// Unity setup:
///  - Attach to Semtex prefab (Resources/Object/Semtex).
///  - Prefab also needs: Rigidbody, Collider, PhotonView.
///  - Assign glowRenderer to the mesh renderer whose material should pulse.
///    The material MUST have emission enabled or use a shader with _EmissionColor.
///  - Assign explosionVFX, beepSFX, and explosionSFX in the Inspector.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SemtexObject : MonoBehaviourPun
{
    [Header("Explosion Settings")]
    [SerializeField] private float explosionRadius = 5f;
    [SerializeField] private float explosionForce  = 60f;
    [SerializeField] public  LayerMask affectedLayers = ~0;

    [Header("Fuse Settings")]
    [SerializeField] private float preFuseDuration = 2f;
    [SerializeField] private int   beepCount       = 3;
    [SerializeField] private float beepInterval    = 1f;

    [Header("Glow Settings")]
    [Tooltip("HDR colour used for the emission pulse.  Set higher intensity in the colour picker.")]
    [SerializeField] private Color beepGlowColor   = Color.red;
    [SerializeField] private float glowIntensity   = 3f;
    [SerializeField] private float glowFadeDuration = 0.4f;
    [SerializeField] private Renderer glowRenderer;

    [Header("References")]
    [SerializeField] private GameObject explosionVFX;
    [SerializeField] private AudioClip  beepSFX;
    [SerializeField] private AudioClip  explosionSFX;

    [Header("VFX Settings")]
    [SerializeField] private float vfxScale = 0.3f;

    [Header("Audio Settings")]
    [SerializeField] private float audioRadius = 20f;
    [SerializeField] private float audioVolume = 1f;

    private bool      hasStuck    = false;
    private bool      hasExploded = false;
    private Material  glowMaterial;
    private Coroutine glowCoroutine;
    private AudioSource beepAudioSource;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        // Create an instance material so we only modify this object's emission
        if (glowRenderer != null)
        {
            glowMaterial = glowRenderer.material;
            glowMaterial.EnableKeyword("_EMISSION");
            glowMaterial.SetColor("_EmissionColor", Color.black);
        }

        // Persistent world-space AudioSource for beeps (cheaper than spawning temps)
        beepAudioSource              = gameObject.AddComponent<AudioSource>();
        beepAudioSource.spatialBlend = 1f;
        beepAudioSource.rolloffMode  = AudioRolloffMode.Linear;
        beepAudioSource.minDistance  = 1f;
        beepAudioSource.maxDistance  = audioRadius;
        beepAudioSource.volume       = audioVolume;
        beepAudioSource.playOnAwake  = false;
    }

    // -------------------------------------------------------------------------
    // Called by Semtex.OnThrow immediately after spawning
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
    // Collision — owner sticks on first hit
    // -------------------------------------------------------------------------

    private void OnCollisionEnter(Collision _)
    {
        if (!photonView.IsMine || hasStuck) return;
        hasStuck = true;

        photonView.RPC(nameof(RPC_Stick), RpcTarget.All,
            transform.position, transform.rotation);
    }

    // -------------------------------------------------------------------------
    // RPCs
    // -------------------------------------------------------------------------

    [PunRPC]
    private void RPC_Stick(Vector3 pos, Quaternion rot)
    {
        hasStuck             = true;
        transform.position   = pos;
        transform.rotation   = rot;

        // Freeze physics on the owner so it stays in place
        if (photonView.IsMine)
        {
            if (TryGetComponent(out Rigidbody rb))
            {
                rb.linearVelocity  = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic     = true;
            }
            StartCoroutine(FuseRoutine());
        }
    }

    private IEnumerator FuseRoutine()
    {
        yield return new WaitForSeconds(preFuseDuration);

        for (int i = 0; i < beepCount; i++)
        {
            photonView.RPC(nameof(RPC_Beep), RpcTarget.All);
            yield return new WaitForSeconds(beepInterval);
        }

        Vector3 pos = transform.position;

        // Suppress visuals before the RPC so there is no frame-flicker
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;

        photonView.RPC(nameof(RPC_Explode), RpcTarget.All, pos);

        PhotonNetwork.Destroy(gameObject);
        Destroy(gameObject);
    }

    [PunRPC]
    private void RPC_Beep()
    {
        // SFX
        if (beepSFX != null)
            beepAudioSource.PlayOneShot(beepSFX);

        // Glow pulse — stop any in-progress fade before starting a new one
        if (glowMaterial != null)
        {
            if (glowCoroutine != null) StopCoroutine(glowCoroutine);
            glowCoroutine = StartCoroutine(GlowPulse());
        }
    }

    private IEnumerator GlowPulse()
    {
        // Instant flash at full intensity
        glowMaterial.SetColor("_EmissionColor", beepGlowColor * glowIntensity);

        // Fade back to off over glowFadeDuration
        float elapsed = 0f;
        while (elapsed < glowFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / glowFadeDuration;
            glowMaterial.SetColor("_EmissionColor",
                Color.Lerp(beepGlowColor * glowIntensity, Color.black, t));
            yield return null;
        }

        glowMaterial.SetColor("_EmissionColor", Color.black);
        glowCoroutine = null;
    }

    [PunRPC]
    private void RPC_Explode(Vector3 pos)
    {
        if (hasExploded) return;
        hasExploded = true;

        // VFX
        if (explosionVFX != null)
        {
            GameObject vfx = Instantiate(explosionVFX, pos, Quaternion.identity);
            foreach (ParticleSystem ps in vfx.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.startSizeMultiplier *= vfxScale;
            }
        }

        // SFX — temporary world-space source with configurable radius
        if (explosionSFX != null)
        {
            GameObject audioObj = new("SemtexExplosionAudio");
            audioObj.transform.position = pos;
            AudioSource src = audioObj.AddComponent<AudioSource>();
            src.clip         = explosionSFX;
            src.spatialBlend = 1f;
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.minDistance  = 1f;
            src.maxDistance  = audioRadius;
            src.volume       = audioVolume;
            src.Play();
            Destroy(audioObj, explosionSFX.length + 0.1f);
        }

        // Physics blast
        Collider[] hits = affectedLayers.value != 0
            ? Physics.OverlapSphere(pos, explosionRadius, affectedLayers)
            : Physics.OverlapSphere(pos, explosionRadius);

        foreach (Collider nearby in hits)
        {
            if (nearby.attachedRigidbody != null)
                nearby.attachedRigidbody.AddExplosionForce(
                    explosionForce, pos, explosionRadius, 1f, ForceMode.Impulse);
        }
    }
}
