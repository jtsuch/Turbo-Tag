using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Flashbang: pops after a fuse. Every client then checks its own camera — distance, line of
/// sight and how directly it is looking at the flash — and blinds itself accordingly.
///   intensity = look × distance falloff
///   look: 1 when looking within about 35° of the flash, fading to 0 at about 70°
///   distance: 1 (complete blindness) up to fullBlindRange, fading to 0 at maxBlindRange
/// Attach to: Resources/Object/Flashbang (with Rigidbody, Collider, PhotonView).
/// Set obstacleMask to world layers only (not players) so the thrower's body doesn't block it.
/// </summary>
public class FlashbangObject : NetworkedProjectile
{
    [Header("Fuse")]
    [TunableParam("Fuse Time", 0.2f, 10f)]
    [SerializeField] private float fuseTime = 2f;

    [Header("Blind")]
    [Tooltip("Looking at the flash within this distance blinds you completely (m).")]
    [TunableParam("Full Blind Range", 1f, 60f)]
    [SerializeField] private float     fullBlindRange   = 15f;
    [Tooltip("No effect beyond this distance (m).")]
    [FormerlySerializedAs("maxBlindRange")]
    [TunableParam("Max Blind Range", 1f, 100f)]
    [SerializeField] private float     maxBlindRange    = 30f;
    [Tooltip("How long a complete blind lasts (s); weaker blinds are shorter.")]
    [TunableParam("Blind Duration", 0.5f, 15f)]
    [SerializeField] private float     maxBlindDuration = 4f;
    [Tooltip("Layers that block line of sight to the flash. Exclude the Player layer.")]
    [SerializeField] private LayerMask obstacleMask;

    [Header("Effects")]
    [SerializeField] private GameObject popVFX;
    [SerializeField] private AudioClip  popSFX;
    [SerializeField] private float      audioRadius = 30f;
    [SerializeField] private float      audioVolume = 1f;

    protected override void OnLaunched()
    {
        if (IsAuthority) StartCoroutine(Fuse());
    }

    private IEnumerator Fuse()
    {
        yield return new WaitForSeconds(fuseTime);
        HideAndFreeze();
        photonView.RPC(nameof(RPC_Pop), RpcTarget.All, transform.position);
        DestroyNetworked();
    }

    [PunRPC]
    private void RPC_Pop(Vector3 flashPos)
    {
        WorldFx.SpawnVfx(popVFX, flashPos);
        WorldFx.PlayClip(popSFX, flashPos, audioRadius, audioVolume);

        Camera cam = PlayerCam.Local != null ? PlayerCam.Local.Camera : Camera.main;
        if (cam == null) return;

        float   range   = maxBlindRange * Mathf.Sqrt(Power);
        float   full    = fullBlindRange * Mathf.Sqrt(Power);
        Vector3 toFlash = flashPos - cam.transform.position;
        float   dist    = toFlash.magnitude;
        if (dist > range) return;

        // Start the ray slightly ahead of the camera to skip the player's own colliders
        Vector3 dir = toFlash.normalized;
        if (dist > 0.4f && Physics.Raycast(cam.transform.position + dir * 0.4f, dir, dist - 0.4f, obstacleMask))
            return;

        float dot = Vector3.Dot(cam.transform.forward, dir);
        if (dot <= 0f) return; // Looking away

        float look      = Mathf.Clamp01((dot - 0.35f) / (0.82f - 0.35f));        // ~70° → 0, ~35° → 1
        float distance  = 1f - Mathf.Clamp01((dist - full) / Mathf.Max(0.01f, range - full));
        float intensity = look * distance;
        if (intensity > 0.02f)
            ScreenFlash.Apply(intensity, maxBlindDuration * intensity);
    }
}
