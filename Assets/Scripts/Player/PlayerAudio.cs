using UnityEngine;
using Photon.Pun;

/// <summary>
/// Plays positional audio for the local player's footsteps, jumps, lands, and sliding.
/// The local client drives its own audio in Update; RPCs replicate one-shot and looping
/// sounds to remote clients so they hear positional audio at this object's world position.
/// Attach to: ThePlayer prefab — requires AudioSource and PhotonView on the same object.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(PhotonView))]
public class PlayerAudio : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Player player;
    [SerializeField] private PlayerMovement movement;

    [Header("Footstep Clips")]
    [SerializeField] private AudioClip[] footsteps;

    [Header("Action Clips")]
    [SerializeField] private AudioClip jumpSound;
    [SerializeField] private AudioClip landSound;
    [SerializeField] private AudioClip slideSound;      // Looping slide audio

    [Header("Footstep Timing")]
    [SerializeField] private float walkStepInterval = 0.3f;
    [SerializeField] private float sprintStepInterval = 0.22f;

    [Header("Volume")]
    [SerializeField] private float footstepVolume = 0.2f;
    [SerializeField] private float jumpVolume = 0.8f;
    [SerializeField] private float landVolume = 0.9f;
    [SerializeField] private float slideVolume = 0.7f;

    private AudioSource audioSource;
    private PhotonView photonView;

    private float footstepTimer;
    private bool isSliding;
    private int lastFootstepIndex = -1; // Prevents same clip twice in a row

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        photonView = GetComponent<PhotonView>();

        // Auto-assign sibling components if not set in Inspector
        if (player == null) player = GetComponent<Player>();
        if (movement == null) movement = GetComponent<PlayerMovement>();

        // 3D spatial audio so other players hear positional sound
        WorldFx.Configure3D(audioSource, maxDistance: 30f);
        audioSource.loop = false;
    }

    private void OnEnable()
    {
        player.Jumped += HandleJump;
        player.Landed += HandleLand;
    }

    private void OnDisable()
    {
        player.Jumped -= HandleJump;
        player.Landed -= HandleLand;
    }

    private void Update()
    {
        // Only drive audio logic for the local player.
        // Remote players hear audio via RPCs, not local Update.
        if (!photonView.IsMine) return;

        HandleFootsteps();
        HandleSlide();
    }

    // ─── Footsteps ────────────────────────────────────────────────────────────

    private void HandleFootsteps()
    {
        // No footsteps if airborne, hanging, climbing, or sliding
        if (!movement.OnGround) return;
        if (player.State == Player.MovementState.Hang) return;
        if (player.State == Player.MovementState.Climb) return;
        if (isSliding) return;

        // Check if the player is actually moving horizontally
        Vector3 flatVelocity = Vector3.ProjectOnPlane(player.Velocity, Vector3.up);
        if (flatVelocity.magnitude < 2f) return;

        bool isSprinting = player.State == Player.MovementState.WallRun
                        || flatVelocity.magnitude >= player.Stats.sprintSpeed * 0.8f;

        float interval = isSprinting ? sprintStepInterval : walkStepInterval;

        footstepTimer -= Time.deltaTime;
        if (footstepTimer <= 0f)
        {
            PlayRandomFromPool(footsteps, footstepVolume);
            footstepTimer = interval;
        }
    }

    // ─── Sliding ──────────────────────────────────────────────────────────────

    private void HandleSlide()
    {
        // Sliding = prone + grounded + speed above threshold
        bool shouldSlide = movement.IsSliding;

        if (shouldSlide && !isSliding)
            StartSlide();
        else if (!shouldSlide && isSliding)
            StopSlide();
    }

    private void StartSlide()
    {
        isSliding = true;
        if (slideSound == null) return;

        PlaySlideLoop();
        photonView.RPC(nameof(RPC_PlaySlide), RpcTarget.Others);
    }

    private void StopSlide()
    {
        isSliding = false;
        if (audioSource.loop)
        {
            audioSource.loop = false;
            audioSource.Stop();
        }

        photonView.RPC(nameof(RPC_StopSlide), RpcTarget.Others);
    }

    // ─── Event Handlers (fired from Player) ───────────────────────────────────

    private const byte ClipJump = 0;
    private const byte ClipLand = 1;

    private void HandleJump()
    {
        if (!photonView.IsMine) return;
        PlayOneShot(jumpSound, jumpVolume);
        photonView.RPC(nameof(RPC_PlayOneShot), RpcTarget.Others, ClipJump);
    }

    private void HandleLand()
    {
        if (!photonView.IsMine) return;
        PlayOneShot(landSound, landVolume);
        photonView.RPC(nameof(RPC_PlayOneShot), RpcTarget.Others, ClipLand);
    }

    // ─── Audio Helpers ────────────────────────────────────────────────────────

    private void PlayOneShot(AudioClip clip, float volume)
    {
        if (clip == null) return;
        audioSource.PlayOneShot(clip, volume);
    }

    private void PlayRandomFromPool(AudioClip[] pool, float volume)
    {
        if (pool == null || pool.Length == 0) return;

        int index;
        do
        {
            index = Random.Range(0, pool.Length);
        } while (pool.Length > 1 && index == lastFootstepIndex);

        lastFootstepIndex = index;
        audioSource.PlayOneShot(pool[index], volume);
    }

    // ─── RPCs (remote clients play audio at this object's position) ───────────

    [PunRPC]
    private void RPC_PlayOneShot(byte clip)
    {
        if      (clip == ClipJump) PlayOneShot(jumpSound, jumpVolume);
        else if (clip == ClipLand) PlayOneShot(landSound, landVolume);
    }

    [PunRPC]
    private void RPC_PlaySlide() => PlaySlideLoop();

    private void PlaySlideLoop()
    {
        if (slideSound == null) return;
        audioSource.clip   = slideSound;
        audioSource.loop   = true;
        audioSource.volume = slideVolume;
        audioSource.Play();
    }

    [PunRPC]
    private void RPC_StopSlide()
    {
        audioSource.loop = false;
        audioSource.Stop();
    }
}