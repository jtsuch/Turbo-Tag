using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Semtex: sticks to the first surface it hits, waits, beeps (with a glow pulse) a few times,
/// then explodes.
///   owner hit → RPC_Stick (all) → owner fuse → RPC_Beep × N (all) → RPC_Explode (all) → destroy
/// Attach to: Resources/Object/Semtex (with Rigidbody, Collider, PhotonView). Assign
/// glowRenderer to a mesh whose material supports _EmissionColor.
/// </summary>
public class SemtexObject : NetworkedProjectile
{
    [Header("Explosion")]
    [TunableParam("Blast Radius", 1f, 30f)]
    [SerializeField] private float     explosionRadius = 5f;
    [TunableParam("Blast Force", 5f, 300f)]
    [SerializeField] private float     explosionForce  = 60f;
    [SerializeField] private LayerMask affectedLayers  = ~0;

    [Header("Fuse")]
    [TunableParam("Fuse Delay", 0f, 10f)]
    [SerializeField] private float preFuseDuration = 2f;
    [SerializeField] private int   beepCount       = 3;
    [SerializeField] private float beepInterval    = 1f;

    [Header("Glow")]
    [SerializeField] private Renderer glowRenderer;
    [SerializeField] private Color    beepGlowColor    = Color.red;
    [SerializeField] private float    glowIntensity    = 3f;
    [SerializeField] private float    glowFadeDuration = 0.4f;

    [Header("Effects")]
    [SerializeField] private GameObject explosionVFX;
    [SerializeField] private AudioClip  beepSFX;
    [SerializeField] private AudioClip  explosionSFX;
    [SerializeField] private float      vfxScale    = 0.3f;
    [SerializeField] private float      audioRadius = 20f;
    [SerializeField] private float      audioVolume = 1f;

    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    private bool        stuck;
    private bool        exploded;
    private Material    glowMaterial;
    private Coroutine   glowRoutine;
    private AudioSource beepSource;

    protected override void Awake()
    {
        base.Awake();

        if (glowRenderer != null)
        {
            glowMaterial = glowRenderer.material; // Instanced so only this charge glows
            glowMaterial.EnableKeyword("_EMISSION");
            glowMaterial.SetColor(EmissionColor, Color.black);
        }

        beepSource = gameObject.AddComponent<AudioSource>();
        WorldFx.Configure3D(beepSource, audioRadius, audioVolume);
    }

    private void OnDestroy()
    {
        if (glowMaterial != null) Destroy(glowMaterial);
    }

    private void OnCollisionEnter(Collision _)
    {
        if (!IsAuthority || stuck) return;
        stuck = true;
        photonView.RPC(nameof(RPC_Stick), RpcTarget.All, transform.position, transform.rotation);
    }

    [PunRPC]
    private void RPC_Stick(Vector3 position, Quaternion rotation)
    {
        stuck = true;
        transform.SetPositionAndRotation(position, rotation);
        Body.linearVelocity  = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.isKinematic     = true;

        if (IsAuthority) StartCoroutine(Fuse());
    }

    private IEnumerator Fuse()
    {
        yield return new WaitForSeconds(preFuseDuration);

        for (int i = 0; i < beepCount; i++)
        {
            photonView.RPC(nameof(RPC_Beep), RpcTarget.All);
            yield return new WaitForSeconds(beepInterval);
        }

        HideAndFreeze();
        photonView.RPC(nameof(RPC_Explode), RpcTarget.All, transform.position);
        DestroyNetworked();
    }

    [PunRPC]
    private void RPC_Beep()
    {
        if (beepSFX != null) beepSource.PlayOneShot(beepSFX);

        if (glowMaterial == null) return;
        if (glowRoutine != null) StopCoroutine(glowRoutine);
        glowRoutine = StartCoroutine(GlowPulse());
    }

    private IEnumerator GlowPulse()
    {
        Color peak = beepGlowColor * glowIntensity;
        for (float t = 0f; t < glowFadeDuration; t += Time.deltaTime)
        {
            glowMaterial.SetColor(EmissionColor, Color.Lerp(peak, Color.black, t / glowFadeDuration));
            yield return null;
        }
        glowMaterial.SetColor(EmissionColor, Color.black);
        glowRoutine = null;
    }

    [PunRPC]
    private void RPC_Explode(Vector3 position)
    {
        if (exploded) return;
        exploded = true;

        WorldFx.SpawnVfx(explosionVFX, position, vfxScale);
        WorldFx.PlayClip(explosionSFX, position, audioRadius, audioVolume);
        WorldFx.Explode(position, explosionRadius * Mathf.Sqrt(Power), explosionForce * Power, affectedLayers);
    }
}
