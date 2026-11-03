using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Semtex: sticks to the first thing it hits (players, boxes and moving platforms included: it rides
/// along with them), waits, beeps (with a glow pulse) a few times, then explodes.
///   owner hit → RPC_Stick (all: same anchor + offset everywhere) → owner fuse → RPC_Beep × N → RPC_Explode → destroy
/// The anchor is found the same way on every client: by PhotonView id for networked objects (players,
/// boxes), otherwise by its scene path. While stuck it follows the anchor each frame, colliders off.
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
    private Transform   anchor;          // What it's stuck to (null = fixed in place)
    private Vector3     anchorPosition;  // Pose in the anchor's space
    private Quaternion  anchorRotation;
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

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsAuthority || stuck) return;
        stuck = true;

        // Networked things (players, boxes) by view id; everything else by scene path
        Transform hit  = collision.rigidbody != null ? collision.rigidbody.transform : collision.collider.transform;
        var       view = hit.GetComponentInParent<PhotonView>();
        int       viewId = view != null ? view.ViewID : 0;
        Transform target = view != null ? view.transform : hit;
        string    path   = view != null ? "" : PathOf(target);

        Vector3    localPos = target.InverseTransformPoint(transform.position);
        Quaternion localRot = Quaternion.Inverse(target.rotation) * transform.rotation;
        photonView.RPC(nameof(RPC_Stick), RpcTarget.All, viewId, path, localPos, localRot, transform.position, transform.rotation);
    }

    [PunRPC]
    private void RPC_Stick(int viewId, string path, Vector3 localPos, Quaternion localRot, Vector3 position, Quaternion rotation)
    {
        stuck = true;
        Body.linearVelocity  = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.isKinematic     = true;
        foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false; // Never shoves what it's on

        var view = viewId != 0 ? PhotonView.Find(viewId) : null;
        anchor = view != null ? view.transform : !string.IsNullOrEmpty(path) ? GameObject.Find(path)?.transform : null;
        anchorPosition = localPos;
        anchorRotation = localRot;
        if (anchor == null) transform.SetPositionAndRotation(position, rotation);
        else                FollowAnchor();

        if (IsAuthority) StartCoroutine(Fuse());
    }

    private void LateUpdate()
    {
        if (stuck && anchor != null) FollowAnchor();
    }

    private void FollowAnchor() =>
        transform.SetPositionAndRotation(anchor.TransformPoint(anchorPosition), anchor.rotation * anchorRotation);

    private static string PathOf(Transform t) => t.parent == null ? "/" + t.name : PathOf(t.parent) + "/" + t.name;

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
