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
    [SerializeField] private JimmyMove movement;

    [Header("Footstep Clips")]
    [SerializeField] private AudioClip[] footsteps;
    //[SerializeField] private AudioClip[] sprintFootsteps;

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

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        photonView = GetComponent<PhotonView>();

        // Auto-assign sibling components if not set in Inspector
        if (player == null) player = GetComponent<Player>();
        if (movement == null) movement = GetComponent<JimmyMove>();

        // 3D spatial audio so other players hear positional sound
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 1f;
        audioSource.maxDistance = 30f;
        audioSource.loop = false;
        audioSource.playOnAwake = false;
    }

    private void OnEnable()
    {
        player.OnJump += HandleJump;
        player.OnLand += HandleLand;
    }

    private void OnDisable()
    {
        player.OnJump -= HandleJump;
        player.OnLand -= HandleLand;
    }

    private void Update()
    {
        // Only drive audio logic for the local player.
        // Remote players hear audio via RPCs, not local Update.
        if (!photonView.IsMine) return;

        HandleFootsteps();
        HandleSlide();
    }

    // -------------------------------------------------------------------------
    // Footsteps
    // -------------------------------------------------------------------------

    private void HandleFootsteps()
    {
        // No footsteps if airborne, hanging, climbing, or sliding
        if (!movement.onGround) return;
        if (player.currentState == Player.MovementState.Hang) return;
        if (player.currentState == Player.MovementState.Climb) return;
        if (isSliding) return;

        // Check if the player is actually moving horizontally
        Vector3 flatVelocity = new Vector3(player.rb.linearVelocity.x, 0f, player.rb.linearVelocity.z);
        if (flatVelocity.magnitude < 2f) return;

        bool isSprinting = player.currentState == Player.MovementState.WallRun
                        || flatVelocity.magnitude >= player.SprintSpeed * 0.8f;

        float interval = isSprinting ? sprintStepInterval : walkStepInterval;

        footstepTimer -= Time.deltaTime;
        if (footstepTimer <= 0f)
        {
            PlayRandomFromPool(footsteps, footstepVolume);
            footstepTimer = interval;
        }
    }

    // -------------------------------------------------------------------------
    // Sliding
    // -------------------------------------------------------------------------

    private void HandleSlide()
    {
        // Sliding = prone + grounded + speed above threshold
        bool shouldSlide = player.currentState == Player.MovementState.Prone
                        && movement.onGround
                        && player.rb.linearVelocity.magnitude > player.ProneSpeed + 1f;

        if (shouldSlide && !isSliding)
            StartSlide();
        else if (!shouldSlide && isSliding)
            StopSlide();
    }

    private void StartSlide()
    {
        isSliding = true;
        if (slideSound == null) return;

        // Use a looping PlayOneShot workaround — assign clip and loop on the source
        audioSource.clip = slideSound;
        audioSource.loop = true;
        audioSource.volume = slideVolume;
        audioSource.Play();

        photonView.RPC("RPC_PlaySlide", RpcTarget.Others);
    }

    private void StopSlide()
    {
        isSliding = false;
        if (audioSource.loop)
        {
            audioSource.loop = false;
            audioSource.Stop();
        }

        photonView.RPC("RPC_StopSlide", RpcTarget.Others);
    }

    // -------------------------------------------------------------------------
    // Event Handlers (fired from Player.cs)
    // -------------------------------------------------------------------------

    private void HandleJump()
    {
        PlayOneShot(jumpSound, jumpVolume);
        photonView.RPC("RPC_PlayOneShot", RpcTarget.Others, "jump");
    }

    private void HandleLand()
    {
        PlayOneShot(landSound, landVolume);
        photonView.RPC("RPC_PlayOneShot", RpcTarget.Others, "land");
    }

    // -------------------------------------------------------------------------
    // Audio Helpers
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Photon RPCs — called on remote clients to play audio at this object's position
    // -------------------------------------------------------------------------

    [PunRPC]
    private void RPC_PlayOneShot(string clipKey)
    {
        switch (clipKey)
        {
            case "jump":
                PlayOneShot(jumpSound, jumpVolume);
                break;
            case "land":
                PlayOneShot(landSound, landVolume);
                break;
        }
    }

    [PunRPC]
    private void RPC_PlaySlide()
    {
        if (slideSound == null) return;
        audioSource.clip = slideSound;
        audioSource.loop = true;
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