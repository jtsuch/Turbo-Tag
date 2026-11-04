using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Drives the player Animator from Player/PlayerMovement state. The owner samples real values;
/// remote copies receive speed, look angle and grounded state via OnPhotonSerializeView, while
/// the movement state itself comes from Player's own sync.
///
/// Animator contract (names are case-sensitive):
///   Floats  : MovementSpeed (-1..1), VerticalDirectionLooking (0..1)
///   Bools   : onGround, Prone, IsWallRunning, IsClimbing, IsCrouching, IsHanging
///   Triggers: Jump, Land
/// Parameters missing from the controller are skipped silently.
///
/// Layers: 0 Base, 1 Right Arm (blended in only while grappling: the gun is out), 2 Wall Run (blended),
/// 3 Climb (blended), 4 Head (always on).
///
/// Flappy: while the player glides with Flappy, the "Flappy Layer" (both arms) plays the Flappy clip.
///
/// The gun: hands are empty except while grappling. Both pistols (the first-person one under the camera
/// and the third-person one in the right hand) are shown only then, on every client.
///
/// MovementSpeed convention:
///   Wall-run: -1 left wall / +1 right wall · Climb: -1 down / +1 up · Ground: 0 idle, 0.5 walk, 1 sprint/slide
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerAnimatorController : MonoBehaviourPun, IPunObservable
{
    [Header("References")]
    [SerializeField] private Animator       animator;
    [SerializeField] private Player         player;
    [SerializeField] private PlayerMovement pm;
    [SerializeField] private Rigidbody      rb;
    [SerializeField] private Transform      cameraHolder;

    [Header("Gun (out only while grappling)")]
    [Tooltip("First-person pistol under the camera. Found by name if empty.")]
    [SerializeField] private GameObject firstPersonGun;
    [Tooltip("Third-person pistol in the right hand. Found by name if empty.")]
    [SerializeField] private GameObject thirdPersonGun;
    [Tooltip("How fast the aiming arm blends in and out (weight per second).")]
    [SerializeField] private float gunBlendSpeed = 8f;

    [Header("Head tilt while prone")]
    [Tooltip("Constant lift of the head while prone (crawling or sliding), so a player looking ahead faces ahead (degrees).")]
    [SerializeField] private float proneHeadLift = 70f;
    [Tooltip("The head bone. Found by name ('Head' under the Armature) if empty.")]
    [SerializeField] private Transform headBone;
    [Tooltip("Most the head tilts up while sliding (degrees). It follows how far the player looks up.")]
    [SerializeField] private float maxSlideHeadTilt = 50f;
    [Tooltip("How fast the head tilt follows (degrees per second).")]
    [SerializeField] private float headTiltSpeed = 240f;

    // ─── Layer Indices ────────────────────────────────────────────────────────
    private const int LayerArm     = 1;
    private const int LayerWallRun = 2;
    private const int LayerClimb   = 3;
    private const int LayerHead    = 4;

    // ─── Parameter Hashes ─────────────────────────────────────────────────────
    private static readonly int H_MoveSpeed   = Animator.StringToHash("MovementSpeed");
    private static readonly int H_VertLook    = Animator.StringToHash("VerticalDirectionLooking");
    private static readonly int H_OnGround    = Animator.StringToHash("onGround");
    private static readonly int H_Prone       = Animator.StringToHash("Prone");
    private static readonly int H_WallRunning = Animator.StringToHash("IsWallRunning");
    private static readonly int H_Climbing    = Animator.StringToHash("IsClimbing");
    private static readonly int H_Crouching   = Animator.StringToHash("IsCrouching");
    private static readonly int H_Hanging     = Animator.StringToHash("IsHanging");
    private static readonly int H_Jump        = Animator.StringToHash("Jump");
    private static readonly int H_Land        = Animator.StringToHash("Land");

    private readonly HashSet<int> validHashes = new();
    private int layerCount;
    private Ability[] abilities;
    private float gunWeight;
    private float headTilt;

    // Flappy (gliding is synced; the layer is found by name)
    private const string FlappyLayerName = "Flappy Layer";
    private Flappy flappy;
    private int    flappyLayer = -1;
    private bool   gliding;
    private float  flappyWeight;

    // ─── Synced values ────────────────────────────────────────────────────────
    private float movementSpeed;
    private float verticalLook;
    private bool  onGround;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (player   == null) player   = GetComponent<Player>();
        if (pm       == null) pm       = GetComponent<PlayerMovement>();
        if (rb       == null) rb       = GetComponent<Rigidbody>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        // The Rigidbody moves the player; animation must never move the root too (the two fighting made
        // players vibrate on every screen). The model's imported Animator may have root motion on.
        if (animator != null) animator.applyRootMotion = false;

        abilities = GetComponents<Ability>();
        flappy   = GetComponent<Flappy>();
        if (firstPersonGun == null && cameraHolder != null) firstPersonGun = FindChild(cameraHolder, "Pistol");
        if (thirdPersonGun == null)
        {
            var armature = transform.Find("Armature");
            if (armature != null) thirdPersonGun = FindChild(armature, "Pistol");
        }

        if (headBone == null)
        {
            var armature = transform.Find("Armature");
            var head = armature != null ? FindChild(armature, "Head") : null;
            if (head != null) headBone = head.transform;
        }

        ShowGun(false);
    }

    private static GameObject FindChild(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child.gameObject;
        return null;
    }

    private void Start()
    {
        CacheAnimatorInfo();

        if (!photonView.ObservedComponents.Contains(this))
            photonView.ObservedComponents.Add(this);

        player.Jumped += HandleJump;
        player.Landed += HandleLand;
    }

    private void OnDestroy()
    {
        if (player == null) return;
        player.Jumped -= HandleJump;
        player.Landed -= HandleLand;
    }

    private void CacheAnimatorInfo()
    {
        validHashes.Clear();
        if (animator == null) return;
        foreach (var param in animator.parameters) validHashes.Add(param.nameHash);
        layerCount = animator.layerCount;
        flappyLayer = animator.GetLayerIndex(FlappyLayerName);
    }

    private void Update()
    {
        if (animator == null) return;
        if (photonView.IsMine) SampleLocalState();
        PushToAnimator();
    }

    // ─── Sampling ─────────────────────────────────────────────────────────────

    private void SampleLocalState()
    {
        gliding       = flappy != null && flappy.enabled && flappy.IsGliding;
        onGround      = pm.OnGround;
        movementSpeed = ComputeMovementSpeed(player.State, rb.linearVelocity.magnitude);

        float pitch = cameraHolder.localEulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        verticalLook = Mathf.InverseLerp(90f, -90f, Mathf.Clamp(pitch, -90f, 90f));
    }

    private float ComputeMovementSpeed(Player.MovementState state, float speed)
    {
        switch (state)
        {
            case Player.MovementState.WallRun:
                return pm.WallRight ? 1f : -1f;

            case Player.MovementState.Climb:
                return rb.linearVelocity.y > 0.1f ? 1f : -1f;

            case Player.MovementState.Prone:
                if (speed < 0.3f) return 0f;
                return speed < player.Stats.proneSpeed + 1f ? 0.5f : 1f;

            default:
                // Idle when grounded without input, regardless of residual contact velocity
                bool hasInput = player.Input.MoveInput.magnitude > 0.1f;
                if ((pm.OnGround && !hasInput) || speed < 0.3f) return 0f;
                return player.Input.SprintHeld ? 1f : 0.5f;
        }
    }

    // ─── Animator ─────────────────────────────────────────────────────────────

    private void PushToAnimator()
    {
        var state = player.State;

        SetFloat(H_MoveSpeed, movementSpeed);
        SetFloat(H_VertLook,  verticalLook);
        SetBool(H_OnGround,    onGround);
        SetBool(H_Prone,       state == Player.MovementState.Prone);
        SetBool(H_WallRunning, state == Player.MovementState.WallRun);
        SetBool(H_Climbing,    state == Player.MovementState.Climb);
        SetBool(H_Crouching,   state == Player.MovementState.Crouch);
        SetBool(H_Hanging,     state == Player.MovementState.Hang);

        UpdateGun();
        if (layerCount > LayerArm)  animator.SetLayerWeight(LayerArm,  gunWeight);

        flappyWeight = Mathf.MoveTowards(flappyWeight, gliding ? 1f : 0f, Time.deltaTime * 8f);
        if (flappyLayer >= 0) animator.SetLayerWeight(flappyLayer, flappyWeight);
        if (layerCount > LayerHead) animator.SetLayerWeight(LayerHead, 1f);

        float blend = Time.deltaTime * 8f;
        if (layerCount > LayerWallRun)
            animator.SetLayerWeight(LayerWallRun, Mathf.MoveTowards(
                animator.GetLayerWeight(LayerWallRun), state == Player.MovementState.WallRun ? 1f : 0f, blend));
        if (layerCount > LayerClimb)
            animator.SetLayerWeight(LayerClimb, Mathf.MoveTowards(
                animator.GetLayerWeight(LayerClimb), state == Player.MovementState.Climb ? 1f : 0f, blend));
    }

    // ─── Head tilt ────────────────────────────────────────────────────────────

    // After the Animator has posed the body. While prone the head is lifted by a constant amount (the prone
    // pose faces the ground); while sliding it also follows how far the player looks up (only up, capped).
    // Uses synced values, so every client sees the same tilt.
    private void LateUpdate()
    {
        if (headBone == null) return;

        // Prone with the "fast" speed value is a slide (see ComputeMovementSpeed)
        bool  prone   = player.State == Player.MovementState.Prone;
        bool  sliding = prone && movementSpeed > 0.75f;
        float lookUp  = Mathf.Clamp((verticalLook - 0.5f) * 180f, 0f, maxSlideHeadTilt);
        float target  = (prone ? proneHeadLift : 0f) + (sliding ? lookUp : 0f);
        headTilt = Mathf.MoveTowards(headTilt, target, headTiltSpeed * Time.deltaTime);

        if (headTilt > 0.01f)
            headBone.rotation = Quaternion.AngleAxis(-headTilt, transform.right) * headBone.rotation;
    }

    // ─── Gun ──────────────────────────────────────────────────────────────────

    private bool gunWasOut;

    /// <summary>Where this player looks up/down in degrees (positive = down), synced; for the killcam.</summary>
    public float LookPitch => Mathf.Lerp(90f, -90f, verticalLook);

    /// <summary>A gun-mode gun is in hand (PlayerGun, every copy): the arm is raised.</summary>
    public bool HoldingGun { get; set; }

    /// <summary>The gun in hand is the built-in pistol, so the pistols show (other guns bring their own model).</summary>
    public bool HoldingPistol { get; set; }

    /// <summary>Shows the first-person pistol whatever else is going on (the killcam's view model).</summary>
    public bool ForceFirstPersonGun { get; set; }

    public GameObject FirstPersonPistol => firstPersonGun;
    public GameObject ThirdPersonPistol => thirdPersonGun;

    // ─── Pose readout (killcam recording) ─────────────────────────────────────
    public Animator Animator      => animator;
    public float    MoveSpeedParam => movementSpeed;
    public float    VerticalLook  => verticalLook;
    public bool     GroundedParam => onGround;
    public float    ArmWeight     => gunWeight;

    /// <summary>Muzzle of the first-person or third-person pistol (the gun itself if it has no GunTip).</summary>
    public Transform GunTip(bool firstPerson)
    {
        var gun = firstPerson ? firstPersonGun : thirdPersonGun;
        if (gun == null) return null;
        var tip = FindChild(gun.transform, "GunTip");
        return tip != null ? tip.transform : gun.transform;
    }

    // Only an equipped (enabled) ability brings the gun out or raises the arm; Flappy and others never do.
    // The arm can rise without the gun (the leaf blower carries its own model).
    private void UpdateGun()
    {
        bool armUp = HoldingGun, gunOut = HoldingGun && HoldingPistol;
        foreach (var ability in abilities)
        {
            if (ability == null || !ability.enabled) continue;
            armUp  |= ability.RaisesArm;
            gunOut |= ability.ShowsGrappleGun;
        }

        // The arm raises as the gun comes out and lowers before it is put away
        gunWeight = Mathf.MoveTowards(gunWeight, armUp ? 1f : 0f, gunBlendSpeed * Time.deltaTime);
        gunWasOut = gunOut || (gunWasOut && gunWeight > 0.4f);
        if (HoldingGun && !HoldingPistol) gunWasOut = false; // Another gun's model is in hand
        ShowGun(gunWasOut);
    }

    private void ShowGun(bool show)
    {
        bool firstPerson = show || ForceFirstPersonGun;
        if (firstPersonGun != null && firstPersonGun.activeSelf != firstPerson) firstPersonGun.SetActive(firstPerson);
        if (thirdPersonGun != null && thirdPersonGun.activeSelf != show) thirdPersonGun.SetActive(show);
    }

    private void SetFloat(int hash, float value)
    {
        if (validHashes.Contains(hash)) animator.SetFloat(hash, value, 0.08f, Time.deltaTime);
    }

    private void SetBool(int hash, bool value)
    {
        if (validHashes.Contains(hash)) animator.SetBool(hash, value);
    }

    private void SetTrigger(int hash)
    {
        if (animator != null && validHashes.Contains(hash)) animator.SetTrigger(hash);
    }

    // ─── Triggers ─────────────────────────────────────────────────────────────

    private void HandleJump() => FireTrigger(H_Jump);
    private void HandleLand() => FireTrigger(H_Land);

    private void FireTrigger(int hash)
    {
        SetTrigger(hash);
        if (photonView.IsMine)
            photonView.RPC(nameof(RPC_AnimTrigger), RpcTarget.Others, hash);
    }

    [PunRPC]
    private void RPC_AnimTrigger(int hash) => SetTrigger(hash);

    // ─── Network Sync ─────────────────────────────────────────────────────────

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(movementSpeed);
            stream.SendNext(verticalLook);
            stream.SendNext(onGround);
            stream.SendNext(gliding);
        }
        else
        {
            movementSpeed = (float)stream.ReceiveNext();
            verticalLook  = (float)stream.ReceiveNext();
            onGround      = (bool) stream.ReceiveNext();
            gliding       = (bool) stream.ReceiveNext();
        }
    }
}
