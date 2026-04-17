using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using System.Collections;

/// <summary>
/// Flashbang projectile for the Flashbang ThrowAbility.
/// After StartFuse() is called (by the ability on throw), waits fuseTime seconds then pops.
/// The pop broadcasts an RPC so every client evaluates its own camera angle and distance
/// to determine how long and how intensely it is blinded.
///
/// Blind formula: intensity = angleFactor² × distanceFalloff
///   angleFactor  — dot product of camera forward and direction-to-flash (1 = direct, 0 = perpendicular)
///   distFactor   — 1 at epicenter, 0 at maxBlindRange
///
/// Unity setup:
///  - Attach to the Flashbang prefab (Resources/Object/Flashbang).
///  - Prefab also needs: Rigidbody, Collider, PhotonView.
///  - Assign popVFX (local particle prefab) and popSFX (AudioClip) in the Inspector.
///  - Set obstacleMask to the layers that should block the flash (usually Default / World).
///    Do NOT include the player layer, otherwise close-range flashes may be absorbed by the
///    thrower's own collider.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FlashbangObject : MonoBehaviourPun
{
    [Header("Fuse")]
    [SerializeField] private float fuseTime = 2f;

    [Header("Blind Settings")]
    [SerializeField] private float maxBlindRange    = 20f;
    [SerializeField] private float maxBlindDuration = 3f;
    [Tooltip("Layers that block line-of-sight to the flash.  Exclude the Player layer.")]
    [SerializeField] private LayerMask obstacleMask;

    [Header("References")]
    [SerializeField] private GameObject popVFX;
    [SerializeField] private AudioClip  popSFX;

    [Header("Audio Settings")]
    [SerializeField] private float audioRadius = 30f;
    [SerializeField] private float audioVolume = 1f;

    private Coroutine fuseCoroutine;

    // -------------------------------------------------------------------------
    // Called by Flashbang.OnThrow after the projectile is launched
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

    /// <summary>Starts the fuse countdown.  Only called on the owner client.</summary>
    public void StartFuse()
    {
        if (!photonView.IsMine || fuseCoroutine != null) return;
        fuseCoroutine = StartCoroutine(FuseRoutine());
    }

    // -------------------------------------------------------------------------
    // Fuse
    // -------------------------------------------------------------------------

    private IEnumerator FuseRoutine()
    {
        yield return new WaitForSeconds(fuseTime);

        Vector3 pos = transform.position;

        // Immediately hide and freeze so it doesn't linger visually
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        if (TryGetComponent(out Rigidbody rb)) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }

        photonView.RPC(nameof(RPC_Pop), RpcTarget.All, pos);

        PhotonNetwork.Destroy(gameObject);
        Destroy(gameObject);
    }

    // -------------------------------------------------------------------------
    // RPC — runs on every client
    // -------------------------------------------------------------------------

    [PunRPC]
    private void RPC_Pop(Vector3 flashPos)
    {
        // VFX (local instantiation, no network)
        if (popVFX != null)
            Instantiate(popVFX, flashPos, Quaternion.identity);

        // SFX with configurable world-space falloff
        if (popSFX != null)
        {
            GameObject audioObj = new("FlashbangAudio");
            audioObj.transform.position = flashPos;
            AudioSource src = audioObj.AddComponent<AudioSource>();
            src.clip         = popSFX;
            src.spatialBlend = 1f;
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.minDistance  = 1f;
            src.maxDistance  = audioRadius;
            src.volume       = audioVolume;
            src.Play();
            Destroy(audioObj, popSFX.length + 0.1f);
        }

        // Evaluate local client's camera
        Camera cam = PlayerCam.Instance != null ? PlayerCam.Instance.cam : Camera.main;
        if (cam == null) return;

        Vector3 camPos  = cam.transform.position;
        Vector3 toFlash = flashPos - camPos;
        float   dist    = toFlash.magnitude;

        if (dist > maxBlindRange) return;

        // Line-of-sight check.
        // Start the ray 0.4 m in front of the camera to skip the player's own colliders.
        Vector3 rayStart = camPos + toFlash.normalized * 0.4f;
        float   rayDist  = dist - 0.4f;
        if (rayDist > 0f && Physics.Raycast(rayStart, toFlash.normalized, rayDist, obstacleMask))
            return;

        // Angle factor: how directly the camera is aimed at the flash
        //   dot = 1  → looking straight at it
        //   dot = 0  → perpendicular (edge of vision)
        float dot = Vector3.Dot(cam.transform.forward, toFlash.normalized);
        if (dot <= 0f) return;  // Looking away

        float angleFactor = dot * dot;                               // Square for sharper falloff at glancing angles
        float distFactor  = 1f - Mathf.Clamp01(dist / maxBlindRange);
        float intensity   = angleFactor * distFactor;

        if (intensity > 0.02f)
            FlashEffect.Apply(intensity, maxBlindDuration * intensity);
    }
}

// =============================================================================
// FlashEffect — companion class
// Creates a temporary full-screen white overlay that fades out.
// Manages its own GameObject lifetime.
// =============================================================================

/// <summary>
/// Spawns a full-screen white Canvas overlay and fades it out over <paramref name="duration"/> seconds.
/// Call <see cref="Apply"/> from any context; no pre-existing UI setup required.
/// </summary>
public class FlashEffect : MonoBehaviour
{
    /// <summary>
    /// Applies a flash blind effect to the local screen.
    /// </summary>
    /// <param name="intensity">Alpha of the white overlay at peak (0–1).</param>
    /// <param name="duration">Total seconds before the screen is fully clear.</param>
    public static void Apply(float intensity, float duration)
    {
        if (intensity <= 0f || duration <= 0f) return;

        GameObject go = new("FlashEffect");
        DontDestroyOnLoad(go);
        go.AddComponent<FlashEffect>().Init(Mathf.Clamp01(intensity), duration);
    }

    private void Init(float intensity, float duration)
    {
        // Canvas
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        gameObject.AddComponent<CanvasScaler>();
        gameObject.AddComponent<GraphicRaycaster>();

        // Full-screen white image
        GameObject imgGO = new("WhiteOverlay");
        imgGO.transform.SetParent(transform, false);
        Image img = imgGO.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, intensity);

        RectTransform rt = img.rectTransform;
        rt.anchorMin  = Vector2.zero;
        rt.anchorMax  = Vector2.one;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;

        StartCoroutine(FadeRoutine(img, intensity, duration));
    }

    private IEnumerator FadeRoutine(Image img, float peakAlpha, float duration)
    {
        // Hold at peak for 10 % of the duration
        float holdTime = duration * 0.1f;
        yield return new WaitForSeconds(holdTime);

        // Fade to transparent over the remaining 90 %
        float fadeTime = duration - holdTime;
        float elapsed  = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(peakAlpha, 0f, elapsed / fadeTime);
            img.color = new Color(1f, 1f, 1f, alpha);
            yield return null;
        }

        Destroy(gameObject);
    }
}
