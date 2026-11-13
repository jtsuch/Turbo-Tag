using UnityEngine;
using Photon.Pun;

/// <summary>
/// Plays positional audio for a player's footsteps, jumps, lands, and sliding.
/// Footsteps play on every screen from each copy's own (synced) movement, so you hear other players walking: they
/// used to be played only for the local player, which made everyone else silent. Others' steps are louder and
/// carry further (hunters listen for them); crouch walking makes no sound at all and crawling is quieter (sneaking). The step is a sound made
/// in code (<see cref="SfxKind.Step"/>); tick <c>useClassicFootsteps</c> to go back to the old clips.
/// Jumps, lands and the slide loop are sent to remote clients by RPC.
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
    [Tooltip("The old recorded footsteps (kept): used only when Use Classic Footsteps is on.")]
    [SerializeField] private AudioClip[] footsteps;
    [Tooltip("Use the old recorded clips instead of the synthesised penguin step.")]
    [SerializeField] private bool useClassicFootsteps;
    [Tooltip("Volume of other players' steps (yours use Footstep Volume).")]
    [SerializeField] private float remoteStepVolume = 0.9f;
    [Tooltip("How far other players' steps can be heard (m).")]
    [SerializeField] private float remoteStepRange = 38f;

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

    private PlayerAnimatorController anim;

    private void Update()
    {
        HandleFootsteps(); // Every copy: from its own synced movement
        HandleAirRush();
        if (!photonView.IsMine) return;
        HandleSlide();
    }

    // ─── Footsteps ────────────────────────────────────────────────────────────

    private bool leftFoot;

    private void HandleFootsteps()
    {
        bool mine = photonView.IsMine;
        if (!mine && anim == null) anim = GetComponent<PlayerAnimatorController>();
        bool grounded = mine ? movement.OnGround : anim != null && anim.GroundedParam;

        // No footsteps if airborne (wall-runs excepted: running steps on the wall), hanging, climbing, sliding, caught or a ghost
        var state = player.State;
        if (state == Player.MovementState.Climb) { HandleClimbScuffs(mine); return; }
        if ((!grounded && state != Player.MovementState.WallRun) || state == Player.MovementState.Hang) return;
        if (player.IsCaught || player.IsDownAfterTag || player.Role == PlayerRole.Ghost) return;
        if (mine ? isSliding : movement != null && state == Player.MovementState.Prone && player.Velocity.magnitude > player.Stats.proneSpeed + 1f) return;

        // Check if the player is actually moving horizontally
        Vector3 flatVelocity = Vector3.ProjectOnPlane(player.Velocity, Vector3.up);
        if (flatVelocity.magnitude < 2f) return;

        bool isSprinting = state == Player.MovementState.WallRun
                        || flatVelocity.magnitude >= player.Stats.sprintSpeed * 0.8f;

        float interval = isSprinting ? sprintStepInterval : walkStepInterval;

        footstepTimer -= Time.deltaTime;
        if (footstepTimer > 0f) return;
        footstepTimer = interval;

        // Crouch walking is silent (sneaking); crawling is quiet, sprinting a little louder
        if (state == Player.MovementState.Crouch) return;
        float stance = state == Player.MovementState.Prone ? 0.25f : isSprinting ? 1.15f : 1f;
        if (useClassicFootsteps)
        {
            PlayRandomFromPool(footsteps, footstepVolume * stance * (mine ? 1f : 2f));
            return;
        }
        leftFoot = !leftFoot;
        float pitch = (leftFoot ? 0.96f : 1.06f) * Random.Range(0.92f, 1.08f) / Mathf.Sqrt(Mathf.Max(0.3f, player.Size));
        float volume = (mine ? footstepVolume * 1.5f : remoteStepVolume) * stance;
        if (!mine && isSprinting && state != Player.MovementState.Crouch && state != Player.MovementState.Prone)
            SoundVisualizer.Notify(transform.position, 0.8f, player.ActorNumber); // Full running shows on the edge
        Sfx.At(SfxKind.Step, transform.position + Vector3.up * 0.1f, volume, mine ? 20f : remoteStepRange, pitch);
    }

    // Climbing: quieter scuffs, a little apart, that still show on others' sound visualizers
    private void HandleClimbScuffs(bool mine)
    {
        if (player.IsCaught || player.IsDownAfterTag || player.Role == PlayerRole.Ghost) return;
        if (Mathf.Abs(player.Velocity.y) < 1.5f) return;
        footstepTimer -= Time.deltaTime;
        if (footstepTimer > 0f) return;
        footstepTimer = 0.32f;
        leftFoot = !leftFoot;
        if (!mine) SoundVisualizer.Notify(transform.position, 0.6f, player.ActorNumber);
        Sfx.At(SfxKind.Step, transform.position + Vector3.up, (mine ? footstepVolume : remoteStepVolume) * 0.7f,
               mine ? 20f : remoteStepRange * 0.8f, leftFoot ? 1.2f : 1.3f);
    }

    // Others moving fast through the air (grapples, zips, dashes, launches): a rush of air you can hear coming,
    // shown on the sound visualizer too
    private float nextRush;
    private void HandleAirRush()
    {
        if (photonView.IsMine || player.IsCaught || player.IsDownAfterTag || player.Role == PlayerRole.Ghost) return;
        if (anim == null) anim = GetComponent<PlayerAnimatorController>();
        if (anim != null && anim.GroundedParam) return;
        float speed = player.Velocity.magnitude;
        if (speed < RushSpeed || Time.time < nextRush) return;
        nextRush = Time.time + 0.55f;
        float loud = Mathf.InverseLerp(RushSpeed, 35f, speed);
        Sfx.At(SfxKind.Whoosh, transform.position + Vector3.up, Mathf.Lerp(0.35f, 0.75f, loud), 40f, Mathf.Lerp(0.8f, 1.15f, loud));
        SoundVisualizer.Notify(transform.position, Mathf.Lerp(0.6f, 1f, loud), player.ActorNumber);
    }
    private const float RushSpeed = 17f;   // Above a sprint jump: only ability-driven speed

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
    private void RPC_PlaySlide()
    {
        SoundVisualizer.Notify(transform.position, 0.8f, player.ActorNumber);
        PlaySlideLoop();
    }

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