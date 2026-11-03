using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Physics-based player locomotion: grounded movement, jumping, crouch/prone/slide,
/// wall-running, climbing, ledge hanging and picking up "Grabbable" objects.
/// All forces are applied in FixedUpdate; discrete inputs are handled in Update.
/// Only runs on the owning client — remote copies are kinematic and positioned by PlayerNetworkSync.
/// Gravity has a single owner here (<see cref="UpdateGravity"/>); abilities only set flags like IsDashing.
/// Attach to: ThePlayer prefab root.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PhotonView))]
public class PlayerMovement : MonoBehaviourPun
{
    // ─── References ───────────────────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private Player       player;
    [SerializeField] private InputHandler input;
    [SerializeField] private Rigidbody    rb;
    [SerializeField] private Camera       cam;
    [SerializeField] private LayerMask    structureLayer;
    [SerializeField] private Transform    throwPoint;

    [Header("Tuning")]
    [SerializeField] private float crouchJumpMultiplier = 1.6f;
    [SerializeField] private float proneDiveImpulse     = 10f;
    [SerializeField] private float dashSpeedCap         = 30f;
    [SerializeField] private float grabReach            = 4f;

    // ─── Surface Detection (read by camera, animation and abilities) ──────────
    public bool OnGround  { get; private set; }
    public bool WallLeft  { get; private set; }
    public bool WallRight { get; private set; }
    private bool wallForward;

    // Ground detection smoothing: grounded through short misses, and landings only after real air time
    private const float GroundGrace          = 0.1f;
    private const float MinAirTimeForLanding = 0.25f;
    private float lastTouchTime = float.NegativeInfinity;
    private float airborneSince;

    [Tooltip("Grip while iced (Ice Trap, Snowball) as a fraction of normal acceleration.")]
    [SerializeField] private float iceControl = 0.08f;

    /// <summary>On the ground while iced by an Ice Trap or Snowball (<see cref="Player.IsIced"/>): sliding.</summary>
    public bool OnIce => OnGround && player.IsIced;

    /// <summary>Prone on the ground with enough speed to coast (a slide rather than a crawl).</summary>
    public bool IsSliding =>
        State == Player.MovementState.Prone && OnGround && rb.linearVelocity.magnitude > SlideThreshold;

    private float SlideThreshold => player.Stats.proneSpeed + 1f;

    // ─── Wall-Run State ───────────────────────────────────────────────────────
    private float   timeOnWall;
    private Vector3 lastWallNormal;
    private Vector3 currentWallNormal;

    // ─── Climb / Hang State ───────────────────────────────────────────────────
    private float   currentClimbSpeed;
    private Vector3 ledgePosition;
    private Vector3 ledgeNormal;
    private bool    climbingLedge;
    private const float LedgeOffset = -1f;
    private const float MinClimbSpeed = 3f; // Below this a climb only slides down the wall
    private const float SteadyClimbReach = 1.8f; // Wall detection for Infinite Climb (normal climbs: 1 m)
    private const float SteadyClimbGrip  = 2f;   // Speed toward the wall that keeps an Infinite Climb on it

    // ─── Held Object State ────────────────────────────────────────────────────
    private GameObject heldObject;
    private Rigidbody  heldObjectRb;

    private Player.MovementState State => player.State;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (player == null) player = GetComponent<Player>();
        if (input  == null) input  = GetComponent<InputHandler>();
        if (rb     == null) rb     = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        if (photonView.IsMine) rb.interpolation = RigidbodyInterpolation.Interpolate;
        player.StateChanged += OnPlayerStateChanged;
    }

    private void OnDestroy()
    {
        if (player != null) player.StateChanged -= OnPlayerStateChanged;
    }

    private void Update()
    {
        if (!photonView.IsMine) return;
        HandleDiscreteInput();
    }

    private void FixedUpdate()
    {
        if (!photonView.IsMine) return;
        SurfaceCheck();
        UpdateGravity();

        // Stuck to a surface (Chameleon): no movement, no gravity
        if (player.IsAnchored)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        // Held by a trap: stand still (gravity still applies, so a held player can't float)
        if (player.IsHeld)
        {
            rb.linearVelocity  = new Vector3(0f, Mathf.Min(0f, rb.linearVelocity.y), 0f);
            rb.angularVelocity = Vector3.zero;
            return;
        }

        MovePlayer();
    }

    // The only place gravity is switched: off while dashing, wall-running or hanging, on otherwise.
    // One writer means no system can leave gravity off (or on) after another one ends.
    private void UpdateGravity()
    {
        bool off = player.IsDashing || player.IsAnchored
                || State == Player.MovementState.WallRun || State == Player.MovementState.Hang;
        if (rb.useGravity == off) rb.useGravity = !off;

        // Gravity stat (cheats): add or remove the difference from normal gravity
        float scale = player.Modifiers.Get(Stat.Gravity);
        if (!off && !Mathf.Approximately(scale, 1f))
            rb.AddForce(Physics.gravity * (scale - 1f), ForceMode.Acceleration);
    }

    private void OnPlayerStateChanged(Player.MovementState newState)
    {
        if (!photonView.IsMine) return;

        if (player.PreviousState == Player.MovementState.WallRun && newState != Player.MovementState.WallRun)
            timeOnWall = 0f;
    }

    // ─── Surface Detection ────────────────────────────────────────────────────

    private void SurfaceCheck()
    {
        bool wasGrounded = OnGround;

        // Cast from a little above the feet so a bump or seam under a slide can't start the ray below the surface
        const float lift = 0.3f;
        bool touching = Physics.Raycast(transform.position + Vector3.up * lift, Vector3.down, out _,
                                        player.Height * 0.2f + lift, ~0, QueryTriggerInteraction.Ignore);
        if (touching) lastTouchTime = Time.time;

        // Brief misses (sliding over uneven ground) don't count as leaving the ground, unless moving up (a jump)
        OnGround = touching || (Time.time - lastTouchTime < GroundGrace && rb.linearVelocity.y <= 0.1f);

        if (wasGrounded && !OnGround) airborneSince = Time.time;

        // Only a real fall counts as a landing (sound, animation), not a one-frame blip
        if (!wasGrounded && OnGround && Time.time - airborneSince >= MinAirTimeForLanding)
            player.NotifyLanded();

        if (State == Player.MovementState.Hang)
            return;

        // Free Spin: the body is tumbling, so its "forward" and "sideways" can point at the floor (which read
        // as a wall: wall-runs with gravity off froze the player mid-air). No walls or climbing while spinning.
        if (player.IsFreeSpinning)
        {
            wallForward = WallLeft = WallRight = false;
            currentWallNormal = Vector3.zero;
            if (State == Player.MovementState.WallRun) StopWallRun();
            else if (State == Player.MovementState.Climb) player.SetState(Player.MovementState.Idle);
            return;
        }

        Vector3 chest = transform.position + Vector3.up * (player.Height * 0.5f);
        // Infinite Climb looks further, so drifting off the wall or glancing sideways doesn't end the climb
        float reach = player.SteadyClimbSpeed > 0f ? SteadyClimbReach : 1f;
        wallForward = Physics.Raycast(chest, transform.forward, reach, structureLayer);

        WallLeft  = Physics.Raycast(transform.position, -transform.right, out RaycastHit hitLeft,  1f, structureLayer);
        WallRight = Physics.Raycast(transform.position,  transform.right, out RaycastHit hitRight, 1f, structureLayer);

        currentWallNormal = WallLeft ? hitLeft.normal : WallRight ? hitRight.normal : Vector3.zero;

        // Touching the ground lets the same wall be run on again
        if (OnGround) lastWallNormal = Vector3.zero;

        Vector2 move = input.MoveInput;
        bool pushingIntoWall = (WallLeft && move.x < -0.1f) || (WallRight && move.x > 0.1f);

        if (pushingIntoWall && !OnGround)
        {
            if (State != Player.MovementState.WallRun) StartWallRun(currentWallNormal);
        }
        else if (State == Player.MovementState.WallRun)
        {
            StopWallRun();
        }

        if (wallForward && move.y > 0.1f && State != Player.MovementState.Climb)
            StartClimb();
        else if (State == Player.MovementState.Climb && ShouldStopClimbing(move))
            player.SetState(Player.MovementState.Idle);
    }

    // ─── Discrete Input ───────────────────────────────────────────────────────

    private void HandleDiscreteInput()
    {
        if (State == Player.MovementState.Hang)
        {
            HandleHanging();
        }
        else if (input.GrabPressed)
        {
            if (player.IsHolding) DropHeldObject();
            else                  Grab();
        }

        if (input.JumpPressed)   Jump();
        if (input.CrouchPressed) ToggleCrouch();
        if (input.PronePressed)  ToggleProne();
    }

    // ─── Ground Movement ──────────────────────────────────────────────────────

    private void MovePlayer()
    {
        // During a dash, only brake if above the speed cap
        if (player.IsDashing)
        {
            if (rb.linearVelocity.magnitude > dashSpeedCap)
                rb.AddForce(-rb.linearVelocity / 5f, ForceMode.Acceleration);
            return;
        }

        // Hanging: held at the ledge (and following shimmies) in physics steps
        if (State == Player.MovementState.Hang)
        {
            if (!climbingLedge)
            {
                rb.linearVelocity = Vector3.zero;
                rb.MovePosition(Vector3.Lerp(rb.position, HangPosition, Time.fixedDeltaTime * 10f));
            }
            return;
        }

        // A prone slide coasts unless the player is also swinging
        bool coasting = State == Player.MovementState.Prone && !player.IsSwinging
                     && rb.linearVelocity.magnitude > SlideThreshold;
        if (coasting)
            return;

        if (State == Player.MovementState.Climb)
        {
            Climb();
            return;
        }

        if (State == Player.MovementState.WallRun)
            WallRun();

        Vector2 move        = input.MoveInput;
        Vector3 intendedDir = (Heading * move.y + HeadingRight * move.x) * player.TargetSpeed;
        Vector3 flatVel     = new(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        bool    hasInput    = intendedDir.sqrMagnitude > 0f;

        // No input on the ground: stop quickly. Skipped while swinging (the joint drives momentum), on ice (it
        // keeps sliding) and while an outside force pushes the player (GravBall, Gravity Well, explosions).
        bool onIce = OnIce;
        if (OnGround && !hasInput && !player.IsSwinging && !onIce && !player.IsBeingPushed)
        {
            if (flatVel.magnitude < 1f)
            {
                // Only the horizontal speed: zeroing the vertical too fought gravity every step (standing jitter)
                rb.linearVelocity  = new Vector3(0f, rb.linearVelocity.y, 0f);
                rb.angularVelocity = Vector3.zero;
            }
            else
            {
                rb.AddForce(-flatVel * 2f, ForceMode.Acceleration);
            }
        }

        // On ice with no input, keep the slide going
        if (onIce && !hasInput)
            intendedDir = flatVel;

        // No input in the air: preserve momentum
        if (!hasInput && !OnGround)
            intendedDir = flatVel;

        float control = player.IsSwinging ? player.Stats.swingControl
                      : !OnGround         ? player.Stats.airControl * player.Modifiers.Get(Stat.AirControl)
                      : onIce             ? iceControl
                      : 1f;

        float   maxAccel   = Mathf.Max(1f, player.Stats.acceleration * player.Modifiers.Get(Stat.Acceleration));
        Vector3 desiredAcc = (intendedDir - flatVel) / Time.fixedDeltaTime;
        desiredAcc = Vector3.ClampMagnitude(desiredAcc, maxAccel * Mathf.Max(control, 0.0001f));

        // Cancel slope-induced sliding while grounded
        if (OnGround && Physics.Raycast(transform.position, Vector3.down, out RaycastHit groundHit, player.Height + 0.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 n = groundHit.normal;
            Vector3 slopeCorrection = n * Physics.gravity.y / Mathf.Max(n.y, 0.0001f);
            slopeCorrection.y = 0f;
            desiredAcc += slopeCorrection;
        }

        rb.AddForce(desiredAcc, ForceMode.Acceleration);
    }

    // ─── Jump ─────────────────────────────────────────────────────────────────

    private void Jump()
    {
        if (State == Player.MovementState.Prone)
        {
            player.ToggleProne();
            return;
        }

        float   strength   = player.Stats.jumpStrength * player.Modifiers.Get(Stat.Jump);
        Vector3 jumpVector = Vector3.zero;

        if (WallLeft && !OnGround)
            jumpVector = (transform.up + transform.right) * strength;
        else if (WallRight && !OnGround)
            jumpVector = (transform.up - transform.right) * strength;
        else if (State == Player.MovementState.Climb && !OnGround)
            jumpVector = (transform.up - transform.forward) * strength;
        else if (OnGround)
        {
            if (State == Player.MovementState.Crouch)
            {
                ToggleCrouch();
                jumpVector = Vector3.up * (strength * crouchJumpMultiplier);
            }
            else
            {
                jumpVector = Vector3.up * strength;
            }
        }

        if (jumpVector == Vector3.zero) return;

        rb.AddForce(jumpVector, ForceMode.Impulse);
        player.NotifyJumped();
    }

    // ─── Crouch / Prone ───────────────────────────────────────────────────────

    private bool CanChangeStance =>
        !player.IsSwinging
        && State != Player.MovementState.WallRun
        && State != Player.MovementState.Climb
        && State != Player.MovementState.Hang;

    private void ToggleCrouch()
    {
        if (CanChangeStance) player.ToggleCrouch();
    }

    private void ToggleProne()
    {
        if (!CanChangeStance) return;

        player.ToggleProne();

        // Airborne dive
        if (State == Player.MovementState.Prone && !OnGround)
            rb.AddForce(transform.forward * proneDiveImpulse, ForceMode.Impulse);
    }

    // Flat movement directions. Normally the body's own (it is kept upright); during a Free Spin, where the
    // camera faces, flattened, so a tilted body never pushes the player up or down
    private Vector3 Heading
    {
        get
        {
            if (!player.IsFreeSpinning) return transform.forward;
            Vector3 look = cam != null ? cam.transform.forward : transform.forward;
            Vector3 flat = Vector3.ProjectOnPlane(look, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.ProjectOnPlane(cam != null ? cam.transform.up : transform.up, Vector3.up);
            return flat.sqrMagnitude < 0.0001f ? transform.forward : flat.normalized;
        }
    }

    private Vector3 HeadingRight => player.IsFreeSpinning ? Vector3.Cross(Vector3.up, Heading) : transform.right;

    // ─── Climbing ─────────────────────────────────────────────────────────────

    private void StartClimb()
    {
        if (State == Player.MovementState.Prone || State == Player.MovementState.Crouch) return;

        float speed = rb.linearVelocity.y < 0f
            ? 0f
            : Mathf.Min(rb.linearVelocity.magnitude * 1.7f, player.Stats.climbSpeed * player.Modifiers.Get(Stat.Speed));

        // Infinite Climb: always climbs, from a standstill too
        if (player.SteadyClimbSpeed > 0f) speed = Mathf.Max(speed, player.SteadyClimbSpeed);

        // Standing at the foot of a wall without momentum is not a climb (it used to lock the player in place)
        if (OnGround && speed <= MinClimbSpeed) return;

        currentClimbSpeed = speed;
        player.SetState(Player.MovementState.Climb);
    }

    // Climbing ends when the wall is gone, when the player pulls back (S), or on the floor whenever they
    // aren't actively climbing up (no forward input, or the climb has run out of speed). Never stuck on the floor.
    private bool ShouldStopClimbing(Vector2 move)
    {
        if (!wallForward || move.y < -0.1f) return true;
        return OnGround && (move.y <= 0.1f || Mathf.Abs(move.x) > 0.1f || currentClimbSpeed <= MinClimbSpeed);
    }

    private void Climb()
    {
        if (currentClimbSpeed <= MinClimbSpeed)
        {
            ClampFallSpeed();
            return;
        }

        Vector3 sideways = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        // Infinite Climb: hold the player against the wall so the climb can't drift off it
        Vector3 chest = transform.position + Vector3.up * (player.Height * 0.5f);
        if (player.SteadyClimbSpeed > 0f
            && Physics.Raycast(chest, transform.forward, out RaycastHit wall, SteadyClimbReach, structureLayer))
            sideways = -wall.normal * SteadyClimbGrip;

        rb.linearVelocity = new Vector3(sideways.x, currentClimbSpeed, sideways.z);

        // Climbs slow down, unless Infinite Climb holds the speed up
        currentClimbSpeed = player.SteadyClimbSpeed > 0f
            ? Mathf.Max(currentClimbSpeed - 0.5f, player.SteadyClimbSpeed)
            : currentClimbSpeed - 0.5f;
    }

    private void ClampFallSpeed()
    {
        float max = player.Stats.wallSlideDownMax;
        if (rb.linearVelocity.y < -max)
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, -max, rb.linearVelocity.z);
    }

    // ─── Wall-Run ─────────────────────────────────────────────────────────────

    private void StartWallRun(Vector3 wallNormal)
    {
        if (State == Player.MovementState.Prone || State == Player.MovementState.Crouch) return;
        if (wallNormal == lastWallNormal) return;

        player.SetState(Player.MovementState.WallRun);
        lastWallNormal = wallNormal;

        // Cancel downward velocity at the moment of contact
        if (timeOnWall == 0f)
            rb.AddForce(transform.up * (rb.linearVelocity.y / -2f), ForceMode.Impulse);
    }

    private void WallRun()
    {
        timeOnWall += Time.fixedDeltaTime;

        // Stick to the wall
        rb.AddForce(WallLeft ? -transform.right * 10f : transform.right * 10f);

        // Upward pull diminishes and inverts over time so the player gradually slides down
        float pull = -Mathf.Pow(4f, timeOnWall - 1f) - 2f;
        rb.AddForce(transform.up * pull);

        ClampFallSpeed();
    }

    private void StopWallRun()
    {
        player.SetState(Player.MovementState.Idle);
        timeOnWall = 0f;
    }

    // ─── Grab: objects first, then ledges ─────────────────────────────────────

    private void Grab()
    {
        bool canGrabLedge = !OnGround && State != Player.MovementState.Prone;
        if (!TryPickUpObject() && canGrabLedge)
            TryGrabLedge();
    }

    private bool TryPickUpObject()
    {
        if (cam == null) return false;
        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, grabReach)) return false;
        if (!hit.collider.CompareTag("Grabbable")) return false;
        if (!hit.collider.TryGetComponent(out PhotonView objectView)) return false;

        AttachHeldObject(objectView);
        photonView.RPC(nameof(RPC_SetHeldObject), RpcTarget.Others, objectView.ViewID);
        return true;
    }

    private void AttachHeldObject(PhotonView objectView)
    {
        heldObject   = objectView.gameObject;
        heldObjectRb = heldObject.GetComponent<Rigidbody>();
        player.IsHolding = true;

        if (heldObjectRb != null)
        {
            heldObjectRb.useGravity       = false;
            heldObjectRb.isKinematic      = true;
            heldObjectRb.detectCollisions = false;
            heldObjectRb.constraints      = RigidbodyConstraints.FreezeRotation;
        }

        if (throwPoint != null)
        {
            heldObject.transform.SetParent(throwPoint);
            heldObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
    }

    private void DropHeldObject()
    {
        if (heldObject == null) return;

        Rigidbody body = heldObjectRb;
        ReleaseHeldObject();
        if (body != null && cam != null)
            body.AddForce(cam.transform.forward * 5f, ForceMode.Impulse);

        photonView.RPC(nameof(RPC_DropHeldObject), RpcTarget.Others);
    }

    private void ReleaseHeldObject()
    {
        if (heldObject == null) return;

        heldObject.transform.SetParent(null);
        if (heldObjectRb != null)
        {
            heldObjectRb.useGravity       = true;
            heldObjectRb.isKinematic      = false;
            heldObjectRb.detectCollisions = true;
            heldObjectRb.constraints      = RigidbodyConstraints.None;
        }

        heldObject   = null;
        heldObjectRb = null;
        player.IsHolding = false;
    }

    // ─── Ledges ───────────────────────────────────────────────────────────────

    private void TryGrabLedge()
    {
        if (!Physics.Raycast(transform.position, transform.forward, out RaycastHit wallHit, 3f, structureLayer))
            return;

        Vector3 ledgeCheckOrigin = wallHit.point + Vector3.up * 6.2f + transform.forward * 0.1f;
        if (!Physics.Raycast(ledgeCheckOrigin, Vector3.down, out RaycastHit ledgeHit, 6.4f, structureLayer)) return;
        if (Vector3.Dot(ledgeHit.normal, Vector3.up) < 0.7f) return;

        // Needs headroom above the ledge and enough width either side
        if (Physics.Raycast(ledgeHit.point + Vector3.up * 0.1f, Vector3.up, 1.5f, structureLayer)) return;
        if (!HasLedgeWidth(ledgeHit.point, wallHit.normal)) return;

        ledgePosition = ledgeHit.point;
        ledgeNormal   = wallHit.normal;

        player.SetState(Player.MovementState.Hang);
        rb.linearVelocity = Vector3.zero;
        rb.position       = HangPosition;
    }

    private bool HasLedgeWidth(Vector3 ledgePoint, Vector3 wallNormal)
    {
        Vector3 right = Vector3.Cross(Vector3.up, wallNormal);
        Vector3 above = ledgePoint + Vector3.up * 0.5f;

        return Physics.Raycast(above - right * 0.6f, Vector3.down, 1f, structureLayer)
            && Physics.Raycast(above + right * 0.6f, Vector3.down, 1f, structureLayer);
    }

    private Vector3 HangPosition => ledgePosition - ledgeNormal * LedgeOffset - Vector3.up * 1.8f;

    private void HandleHanging()
    {
        if (climbingLedge) return;

        float vertical = input.MoveInput.y;
        if (vertical > 0.5f)
        {
            StartCoroutine(ClimbOntoLedge());
            return;
        }
        if (vertical < -0.5f || input.JumpPressed)
        {
            LetGoOfLedge();
            return;
        }

        // Shimmy along the ledge while there is ledge to hold
        float horizontal = input.MoveInput.x;
        if (Mathf.Abs(horizontal) < 0.01f) return;

        Vector3 left     = Vector3.Cross(Vector3.up, ledgeNormal);
        Vector3 proposed = ledgePosition + left * (-horizontal * player.TargetSpeed * Time.deltaTime);
        Vector3 probe    = proposed + Vector3.up * 0.5f - left * 1f;

        if (Physics.Raycast(probe, Vector3.down, out RaycastHit support, 1f, structureLayer)
            && Vector3.Dot(support.normal, Vector3.up) >= 0.7f)
            ledgePosition = proposed;
    }

    private IEnumerator ClimbOntoLedge()
    {
        climbingLedge = true;
        Vector3 start  = transform.position;
        Vector3 target = ledgePosition + Vector3.up * 0.5f - ledgeNormal * 0.3f;

        // Moved in physics steps through the Rigidbody, so interpolation keeps it smooth
        const float duration = 0.5f;
        for (float t = 0f; t < duration; t += Time.fixedDeltaTime)
        {
            rb.linearVelocity = Vector3.zero;
            rb.MovePosition(Vector3.Lerp(start, target, t / duration));
            yield return new WaitForFixedUpdate();
        }

        climbingLedge = false;
        player.SetState(Player.MovementState.Idle);
    }

    private void LetGoOfLedge()
    {
        player.SetState(Player.MovementState.Idle);
        rb.linearVelocity = -ledgeNormal * 2f;
    }

    // ─── Network RPCs ─────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_SetHeldObject(int objectViewId)
    {
        var objectView = PhotonView.Find(objectViewId);
        if (objectView != null) AttachHeldObject(objectView);
    }

    [PunRPC]
    private void RPC_DropHeldObject() => ReleaseHeldObject();
}
