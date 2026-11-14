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
/// Ledges: while hanging, the "Ledge Layer" (full body, built by Editor/LedgeAnimationSetup) plays LedgeIdle,
/// LedgeShimmy (backwards for the other way, via LedgeShimmySpeed), LedgeClimbUp, or LedgeLookLeft/Right when the
/// view is turned more than 90° from the wall (the side flips as the view passes straight behind). The body
/// faces the wall (PlayerCam). The pose is chosen by the owner and synced.
///
/// Hands: guns and held objects (gun-mode guns, the S&D bomb, the Keep Away disc) go in the RIGHT hand (the Right
/// Arm layer raises it while a gun is out); abilities use the LEFT hand. The grapple's pistol is a mirrored copy of
/// the built-in pistol made at Start: under the camera on the left for the owner, and in the left hand for everyone
/// else, where the left arm is raised procedurally toward the aim (<see cref="AimLeftArm"/>, after the Animator; no
/// clip yet, so it works with any pose). <see cref="Ability.ShowsGrappleGun"/> brings it out; <see cref="Ability.RaisesArm"/>
/// raises the left arm for abilities with their own model (Leaf Blower: <see cref="LeftHand"/>, <see cref="AimRotation"/>).
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

    // ─── Layer Indices (found by name: NewMoveAnimationSetup inserts layers under these) ───
    private int LayerArm = -1, LayerWallRun = -1, LayerClimb = -1, LayerHead = -1;

    // New moves (Editor/NewMoveAnimationSetup): crouching, the Bulldozer charge, planting/defusing, the left-hand throw
    private int crouchLayer = -1, actionLayer = -1, throwLayer = -1;
    private float crouchWeight, actionWeight, throwWeight, throwUntil = -10f;
    private string shownAction = "";
    private const float ThrowLength = 1f;   // ThrowLeft.anim

    /// <summary>The Animator has a crouch animation (the body isn't squashed to crouch, <see cref="Player"/>).</summary>
    public bool AnimatesCrouch => crouchLayer >= 0;

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

    // Ledge (the pose is synced; the layer is found by name)
    private enum LedgePose : byte { None, Idle, Shimmy, LookLeft, LookRight, ClimbUp }
    private static readonly string[] LedgeStates = { "Empty", "LedgeIdle", "LedgeShimmy", "LedgeLookLeft", "LedgeLookRight", "LedgeClimbUp" };
    private static readonly int H_ShimmySpeed = Animator.StringToHash("LedgeShimmySpeed");
    private const string LedgeLayerName = "Ledge Layer";
    private const float  LookAwayAngle  = 90f;
    private const float  LookHysteresis = 10f;   // Past 90 + this to start a look pose, back under 90 - this to end it
    private const float  LedgeFade      = 0.3f;  // Cross-fade between ledge poses (s)
    [Header("Ledge")]
    [Tooltip("The shimmy clip moves to the right; flip if the penguin shimmies the wrong way.")]
    [SerializeField] private bool shimmyClipGoesRight = true;
    [Tooltip("Most the head (and neck) turn up or down with the view while hanging (degrees). Looking right down tucks the head under the ledge.")]
    [SerializeField] private float ledgeHeadPitch = 80f;
    private Transform neckBone;
    private float     ledgeHeadAngle;
    private int       ledgeLayer = -1;
    private LedgePose ledgePose, shownLedgePose;
    private float     shimmyDirection = 1f;
    private float     ledgeWeight;

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
        BuildLeftHand();

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
        ledgeLayer  = animator.GetLayerIndex(LedgeLayerName);
        LayerArm     = animator.GetLayerIndex("RightArm");
        LayerWallRun = animator.GetLayerIndex("Wall Run Layer");
        LayerClimb   = animator.GetLayerIndex("Climb Layer");
        LayerHead    = animator.GetLayerIndex("HeadLayer");
        crouchLayer  = animator.GetLayerIndex("Crouch Layer");
        actionLayer  = animator.GetLayerIndex("Action Layer");
        throwLayer   = animator.GetLayerIndex("Throw Layer");
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
        ledgePose = SampleLedgePose();
    }

    // Which ledge animation: climbing up, looking far round (beyond 90° from the wall, by side), shimmying, idle
    private LedgePose SampleLedgePose()
    {
        if (player.State != Player.MovementState.Hang) return LedgePose.None;
        if (pm.ClimbingLedge) return LedgePose.ClimbUp;

        Vector3 view = Vector3.ProjectOnPlane(cameraHolder.forward, Vector3.up);
        float look = view.sqrMagnitude > 0.001f ? Vector3.SignedAngle(pm.LedgeFacing, view, Vector3.up) : 0f;
        // A little hysteresis, so hovering around 90 degrees doesn't flick between poses
        bool lookingAway = ledgePose == LedgePose.LookLeft || ledgePose == LedgePose.LookRight;
        float threshold = LookAwayAngle + (lookingAway ? -LookHysteresis : LookHysteresis);
        if (look >  threshold) return LedgePose.LookRight;
        if (look < -threshold) return LedgePose.LookLeft;

        if (Mathf.Abs(pm.LedgeShimmy) > 0.1f)
        {
            shimmyDirection = pm.LedgeShimmy > 0f == shimmyClipGoesRight ? 1f : -1f;
            return LedgePose.Shimmy;
        }
        return LedgePose.Idle;
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
        if (LayerArm >= 0) animator.SetLayerWeight(LayerArm, gunWeight);

        flappyWeight = Mathf.MoveTowards(flappyWeight, gliding ? 1f : 0f, Time.deltaTime * 8f);
        if (flappyLayer >= 0) animator.SetLayerWeight(flappyLayer, flappyWeight);
        if (LayerHead >= 0) animator.SetLayerWeight(LayerHead, 1f);

        float blend = Time.deltaTime * 8f;
        if (LayerWallRun >= 0)
            animator.SetLayerWeight(LayerWallRun, Mathf.MoveTowards(
                animator.GetLayerWeight(LayerWallRun), state == Player.MovementState.WallRun ? 1f : 0f, blend));
        if (LayerClimb >= 0)
            animator.SetLayerWeight(LayerClimb, Mathf.MoveTowards(
                animator.GetLayerWeight(LayerClimb), state == Player.MovementState.Climb ? 1f : 0f, blend));

        UpdateLedgeLayer();
        UpdateMoveLayers(state, blend);
    }

    // Crouching (synced state), the charge and planting/defusing (synced flags), the throw (an RPC per throw)
    private void UpdateMoveLayers(Player.MovementState state, float blend)
    {
        if (crouchLayer >= 0)
        {
            crouchWeight = Mathf.MoveTowards(crouchWeight, state == Player.MovementState.Crouch ? 1f : 0f, blend);
            animator.SetLayerWeight(crouchLayer, crouchWeight);
        }
        if (actionLayer >= 0)
        {
            string want = player.Has(PlayerFlag.Charging) ? "Charge"
                        : player.Has(PlayerFlag.Planting) ? "PlantBomb"
                        : player.Has(PlayerFlag.Defusing) ? "DefuseBomb"
                        : player.Has(PlayerFlag.OnLadder) ? (player.Has(PlayerFlag.LadderMoving) ? "LadderClimb" : "LadderHold") : "";
            if (player.IsCaught) want = "";
            if (want != shownAction && want.Length > 0) animator.CrossFadeInFixedTime(want, shownAction.Length > 0 ? 0.15f : 0f, actionLayer);
            if (want.Length > 0 || actionWeight <= 0f) shownAction = want;
            actionWeight = Mathf.MoveTowards(actionWeight, want.Length > 0 ? 1f : 0f, blend);
            animator.SetLayerWeight(actionLayer, actionWeight);
        }
        if (throwLayer >= 0)
        {
            bool throwing = Time.time < throwUntil - 0.2f;
            throwWeight = Mathf.MoveTowards(throwWeight, throwing ? 1f : 0f, Time.deltaTime * (throwing ? 14f : 5f));
            animator.SetLayerWeight(throwLayer, throwWeight);
        }
    }

    /// <summary>Owner: a throw just left the hand; every screen plays the left-arm throw.</summary>
    public void PlayThrow()
    {
        if (!photonView.IsMine) return;
        photonView.RPC(nameof(RPC_AnimThrow), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_AnimThrow()
    {
        if (animator == null || throwLayer < 0) return;
        animator.CrossFadeInFixedTime("ThrowLeft", 0.05f, throwLayer, 0f);
        throwUntil = Time.time + ThrowLength;
    }

    // Cross-fades the Ledge Layer to the synced pose and blends the layer in while hanging (and out after)
    private void UpdateLedgeLayer()
    {
        if (ledgeLayer < 0) return;
        var pose = player.State == Player.MovementState.Hang ? ledgePose : LedgePose.None;
        if (pose != shownLedgePose)
        {
            // Out of a climb-up the pose holds while the layer fades, so it doesn't snap back to hanging
            if (pose != LedgePose.None)
                animator.CrossFadeInFixedTime(LedgeStates[(int)pose], shownLedgePose == LedgePose.None ? 0f : LedgeFade, ledgeLayer);
            shownLedgePose = pose;
        }
        if (validHashes.Contains(H_ShimmySpeed)) animator.SetFloat(H_ShimmySpeed, shimmyDirection);
        ledgeWeight = Mathf.MoveTowards(ledgeWeight, pose != LedgePose.None ? 1f : 0f, Time.deltaTime * 5f); // ~0.2 s in and out
        animator.SetLayerWeight(ledgeLayer, ledgeWeight);
    }

    // ─── Head tilt ────────────────────────────────────────────────────────────

    // After the Animator has posed the body. While prone the head is lifted by a constant amount (the prone
    // pose faces the ground); while sliding it also follows how far the player looks up (only up, capped).
    // Uses synced values, so every client sees the same tilt.
    private void LateUpdate()
    {
        AimLeftArm();
        if (headBone == null) return;

        // Prone with the "fast" speed value is a slide (see ComputeMovementSpeed)
        bool  prone   = player.State == Player.MovementState.Prone;
        bool  sliding = prone && movementSpeed > 0.75f;
        float lookUp  = Mathf.Clamp((verticalLook - 0.5f) * 180f, 0f, maxSlideHeadTilt);
        float target  = (prone ? proneHeadLift : 0f) + (sliding ? lookUp : 0f);
        headTilt = Mathf.MoveTowards(headTilt, target, headTiltSpeed * Time.deltaTime);

        if (headTilt > 0.01f)
            headBone.rotation = Quaternion.AngleAxis(-headTilt, transform.right) * headBone.rotation;

        LedgeHeadLook();
    }

    // Hanging, the Ledge Layer poses the whole body, head included: put the view's up/down back on top, split
    // between the neck and the head (looking right down bends the neck too, tucking the head under the ledge)
    private void LedgeHeadLook()
    {
        float up = Mathf.Clamp((verticalLook - 0.5f) * 180f, -ledgeHeadPitch, ledgeHeadPitch); // + up, - down
        float target = ledgeWeight > 0.01f && shownLedgePose != LedgePose.ClimbUp ? up * ledgeWeight : 0f;
        ledgeHeadAngle = Mathf.MoveTowards(ledgeHeadAngle, target, 360f * Time.deltaTime);
        if (Mathf.Abs(ledgeHeadAngle) < 0.01f) return;

        if (neckBone == null && headBone != null) neckBone = headBone.parent;
        float neck = ledgeHeadAngle < 0f ? 0.45f : 0.25f; // Down bends the neck more
        if (neckBone != null)
            neckBone.rotation = Quaternion.AngleAxis(-ledgeHeadAngle * neck, transform.right) * neckBone.rotation;
        if (headBone != null)
            headBone.rotation = Quaternion.AngleAxis(-ledgeHeadAngle * (1f - neck), transform.right) * headBone.rotation;
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

    // The right arm rises for a gun; the left for an ability that shows its gun (grapples) or raises the arm
    // (Leaf Blower). Only an equipped (enabled) ability counts; Flappy and others never do.
    private void UpdateGun()
    {
        bool armUp = HoldingGun, gunOut = HoldingGun && HoldingPistol;
        bool leftUp = false, leftGun = false;
        foreach (var ability in abilities)
        {
            if (ability == null || !ability.enabled) continue;
            leftUp  |= ability.RaisesArm || ability.ShowsGrappleGun;
            leftGun |= ability.ShowsGrappleGun;
        }
        leftWeight = Mathf.MoveTowards(leftWeight, leftUp ? 1f : 0f, gunBlendSpeed * Time.deltaTime);
        leftGunWasOut = leftGun || (leftGunWasOut && leftWeight > 0.4f);
        ShowLeftGun(leftGunWasOut);

        // The arm raises as the gun comes out and lowers before it is put away
        gunWeight = Mathf.MoveTowards(gunWeight, armUp ? 1f : 0f, gunBlendSpeed * Time.deltaTime);
        gunWasOut = gunOut || (gunWasOut && gunWeight > 0.4f);
        if (HoldingGun && !HoldingPistol) gunWasOut = false; // Another gun's model is in hand
        ShowGun(gunWasOut);
    }

    private void ShowGun(bool show)
    {
        // Looking through a placed Spy Camera, the main camera sits on it: no first-person pistol floating in that view
        bool watchingCamera = !ForceFirstPersonGun && PlayerCam.Local != null && PlayerCam.Local.HasViewOverride && !Killcam.IsPlaying;
        bool firstPerson = (show || ForceFirstPersonGun) && !watchingCamera;
        if (firstPersonGun != null && firstPersonGun.activeSelf != firstPerson) firstPersonGun.SetActive(firstPerson);
        if (thirdPersonGun != null && thirdPersonGun.activeSelf != show) thirdPersonGun.SetActive(show);
    }

    // ─── Left hand (abilities) ────────────────────────────────────────────────

    private GameObject leftGunFirstPerson, leftGunThirdPerson;
    private Transform  leftUpperArm, leftForearm, leftHandEnd;
    private Quaternion leftGunViewRotation = Quaternion.identity; // The pistol's angle relative to the view
    private float      leftWeight;
    private bool       leftGunWasOut;

    /// <summary>How far the left arm is raised toward the aim (0–1).</summary>
    public float LeftArmWeight => leftWeight;

    /// <summary>The end of the left hand (null if the rig has none): where left-hand ability models sit.</summary>
    public Transform LeftHand => leftHandEnd;

    /// <summary>Where this player aims: the body's facing pitched by the synced look (every client).</summary>
    public Quaternion AimRotation => transform.rotation * Quaternion.Euler(LookPitch, 0f, 0f);

    /// <summary>The grapple pistol's muzzle (left hand): first person for the owner, third person for others.</summary>
    public Transform GrappleTip(bool firstPerson)
    {
        var gun = firstPerson ? leftGunFirstPerson : leftGunThirdPerson;
        if (gun == null) return null;
        var tip = FindChild(gun.transform, "GunTip");
        return tip != null ? tip.transform : gun.transform;
    }

    // Mirrored copies of the built-in pistol for the left hand, and the left arm's bones
    private void BuildLeftHand()
    {
        var armature = transform.Find("Armature");
        if (armature != null)
        {
            foreach (var t in armature.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "LeftArm1") leftUpperArm = t;
                else if (t.name == "LeftArm3") leftForearm = t;
                else if (t.name == "LeftArm4_end") leftHandEnd = t;
            }
            if (leftHandEnd == null) leftHandEnd = leftForearm;
        }

        if (firstPersonGun != null)
        {
            leftGunFirstPerson = Instantiate(firstPersonGun, firstPersonGun.transform.parent, false);
            leftGunFirstPerson.name = "Grapple Pistol (left)";
            var t = leftGunFirstPerson.transform;
            Vector3 p = firstPersonGun.transform.localPosition;
            Quaternion r = firstPersonGun.transform.localRotation;
            t.localPosition = new Vector3(-p.x, p.y, p.z);
            t.localRotation = new Quaternion(r.x, -r.y, -r.z, r.w); // Mirrored across the view's middle
            leftGunViewRotation = t.localRotation;
            leftGunFirstPerson.SetActive(false);
        }
        if (thirdPersonGun != null && leftHandEnd != null)
        {
            leftGunThirdPerson = Instantiate(thirdPersonGun, leftHandEnd, false);
            leftGunThirdPerson.name = "Grapple Pistol (left hand)";
            leftGunThirdPerson.transform.localScale = Vector3.one;
            float scale = thirdPersonGun.transform.lossyScale.x / Mathf.Max(0.0001f, leftHandEnd.lossyScale.x);
            leftGunThirdPerson.transform.localScale = Vector3.one * scale;
            leftGunThirdPerson.SetActive(false);
        }
    }

    private void ShowLeftGun(bool show)
    {
        bool watchingCamera = PlayerCam.Local != null && PlayerCam.Local.HasViewOverride && !Killcam.IsPlaying;
        if (leftGunFirstPerson != null)
        {
            bool fp = show && photonView.IsMine && !watchingCamera;
            if (leftGunFirstPerson.activeSelf != fp) leftGunFirstPerson.SetActive(fp);
        }
        if (leftGunThirdPerson != null && leftGunThirdPerson.activeSelf != show) leftGunThirdPerson.SetActive(show);
    }

    // After the Animator: swing the left arm (upper arm, then forearm) so the hand points along the aim, by the
    // left arm's weight, and hold the pistol in that hand pointing where the player looks
    private void AimLeftArm()
    {
        if (leftWeight <= 0.001f || leftUpperArm == null || leftHandEnd == null) return;
        Quaternion aim = AimRotation;
        Vector3 dir = aim * Vector3.forward;
        Vector3 want = (dir + transform.right * -0.15f).normalized; // A little across the body, like holding it out

        Vector3 now = leftHandEnd.position - leftUpperArm.position;
        if (now.sqrMagnitude > 0.0001f)
            leftUpperArm.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(now, want), leftWeight) * leftUpperArm.rotation;
        if (leftForearm != null)
        {
            Vector3 fore = leftHandEnd.position - leftForearm.position;
            if (fore.sqrMagnitude > 0.0001f)
                leftForearm.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(fore, want), leftWeight) * leftForearm.rotation;
        }

        if (leftGunThirdPerson != null && leftGunThirdPerson.activeSelf)
            leftGunThirdPerson.transform.SetPositionAndRotation(leftHandEnd.position, aim * leftGunViewRotation);
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
            stream.SendNext((byte)ledgePose);
            stream.SendNext(shimmyDirection > 0f);
        }
        else
        {
            movementSpeed = (float)stream.ReceiveNext();
            verticalLook  = (float)stream.ReceiveNext();
            onGround      = (bool) stream.ReceiveNext();
            gliding       = (bool) stream.ReceiveNext();
            ledgePose     = (LedgePose)(byte)stream.ReceiveNext();
            shimmyDirection = (bool)stream.ReceiveNext() ? 1f : -1f;
        }
    }
}
