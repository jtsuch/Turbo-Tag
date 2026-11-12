using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Physics-based player locomotion: grounded movement, jumping, crouch/prone/slide,
/// wall-running, climbing, ledge hanging and picking up "Grabbable" objects.
///
/// Feel ("fast but precise"): raw input; on the ground the velocity is steered straight toward the wanted
/// velocity along the ground plane (quick to top speed, quicker to stop or turn), while speed above the run
/// speed (dashes, slides, pads, explosions) bleeds off slowly so physics abilities keep their punch. In the air
/// momentum is kept and the player can steer it (air control) without gaining speed beyond what they had.
/// Ground is found with a sphere under the feet (standing on an edge counts), small crests and steps down
/// are snapped to, and jumps set the vertical speed (consistent heights) with a jump buffer, coyote time and
/// short hops (let go early). All physics runs in FixedUpdate; discrete inputs are read in Update.
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
    [Tooltip("Seconds between airborne dive boosts: jump + prone spam can't keep adding speed.")]
    [SerializeField] private float proneDiveCooldown    = 1.5f;
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
    private const float MaxWalkableSlope     = 0.6f;  // Ground normal's up component (about 53 degrees)
    private const float GroundProbe          = 0.12f; // How far below the feet still counts as standing (m)
    private const float SnapDistance         = 0.6f;  // Crests and small steps down within this stay grounded (m)
    private const float JumpLockout          = 0.25f; // No second ground jump this soon (no stacked super jumps)
    private float lastTouchTime = float.NegativeInfinity;
    private float airborneSince;

    // Real contact this physics step, and the walkable surface under the feet
    private bool    touching;
    private Vector3 groundNormal = Vector3.up;
    private CapsuleCollider body;

    // Jumping
    private float jumpPressedAt = float.NegativeInfinity;
    private float lastJumpTime  = float.NegativeInfinity;
    private bool  jumpCutReady;  // The current rise is our own jump, so letting go early may shorten it
    private bool  jumpedThisStep;

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

    /// <summary>Owner, while hanging: the way the body faces (into the wall).</summary>
    public Vector3 LedgeFacing => -ledgeNormal;

    /// <summary>Owner, while hanging: pulling up onto the ledge (the climb-up animation).</summary>
    public bool ClimbingLedge => climbingLedge;

    /// <summary>Owner, while hanging: -1 shimmying left, +1 right, 0 still (or blocked: no ledge that way).</summary>
    public float LedgeShimmy { get; private set; }
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
        body = GetComponent<CapsuleCollider>();
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

        // Wall Magnet: walking on whatever surface is under the feet, walls and ceilings included
        if (player.IsMagnetized && !player.IsAnchored && !player.IsHeld)
        {
            UpdateGravity();
            MagnetMove();
            return;
        }
        magnetGrounded = false;

        SurfaceCheck();
        UpdateGravity();
        jumpedThisStep = false;

        // On a zip line: the line carries the body
        if (player.OnZipLine) return;

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

        TryBufferedJump();
        UpdateJumpCut();
        MovePlayer();
    }

    // The only place gravity is switched: off while dashing, wall-running or hanging, on otherwise.
    // One writer means no system can leave gravity off (or on) after another one ends.
    private void UpdateGravity()
    {
        bool off = player.IsDashing || player.IsAnchored || player.OnZipLine || player.IsMagnetized || player.ZeroGravity
                || State == Player.MovementState.WallRun || State == Player.MovementState.Hang
                || (State == Player.MovementState.Climb && onLadder); // Resting on a ladder doesn't slide down
        if (rb.useGravity == off) rb.useGravity = !off;

        // Gravity stat (cheats): add or remove the difference from normal gravity
        float scale = player.Modifiers.Get(Stat.Gravity);
        if (!off && !Mathf.Approximately(scale, 1f))
            rb.AddForce(Physics.gravity * (scale - 1f), ForceMode.Acceleration);
    }

    // ─── Wall Magnet ──────────────────────────────────────────────────────────
    // While magnetised, "down" is toward the surface under the feet. Walking into a wall climbs onto it
    // (its normal becomes up); walking off an edge wraps around onto the face below. Jumps go away from the
    // surface and fall back to it. With no surface for a moment, up returns to the sky (a normal fall).

    private const float MagnetStick     = 40f;   // Pull toward the surface (m/s², like gravity)
    private const float MagnetProbe     = 0.45f; // How far past the feet still counts as standing (m)
    private const float MagnetWallReach = 0.9f;  // Walls this close ahead (from the body) are climbed onto (m)
    private const float MagnetLetGo     = 1.2f;  // Seconds with no surface before up returns to the sky

    private bool    magnetGrounded;
    private float   lastMagnetTouch;
    private Vector3 lastMagnetMove;

    /// <summary>On a surface with Wall Magnet (any angle). Wall Magnet's meter rests while standing still on one.</summary>
    public bool MagnetGrounded => player.IsMagnetized && magnetGrounded;

    private void MagnetMove()
    {
        Vector3 up     = player.MagnetUp.Value;
        Vector3 bodyUp = rb.rotation * Vector3.up;
        float   half   = player.Height * 0.5f;
        float   radius = body != null ? body.radius * 0.9f : 0.35f;
        Vector3 center = rb.position + bodyUp * half;

        // Wanted direction along the surface, from the camera
        Vector2   move    = player.CanMove ? input.MoveInput : Vector2.zero;
        Transform view    = cam != null ? cam.transform : transform;
        Vector3   forward = Vector3.ProjectOnPlane(view.forward, up);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(view.up, up);
        forward.Normalize();
        Vector3 right = Vector3.Cross(up, forward);
        Vector3 wish  = Vector3.ClampMagnitude(forward * move.y + right * move.x, 1f);
        if (wish.sqrMagnitude > 0.01f) lastMagnetMove = wish.normalized;

        // The surface under the feet
        bool justJumped = Time.time - lastJumpTime < 0.2f;
        RaycastHit ground = default;
        bool grounded = !justJumped && Physics.SphereCast(center, radius, -up, out ground,
            half - radius + MagnetProbe, structureLayer, QueryTriggerInteraction.Ignore);
        if (grounded)
        {
            lastMagnetTouch = Time.time;
            if (Vector3.Angle(ground.normal, up) < 60f) up = ground.normal; // Follow curves and slopes
        }

        // A wall ahead: walk up onto it
        if (wish.sqrMagnitude > 0.01f
            && Physics.SphereCast(center, radius, wish.normalized, out RaycastHit wall, MagnetWallReach, structureLayer, QueryTriggerInteraction.Ignore)
            && Vector3.Angle(wall.normal, up) > 35f)
        {
            up = wall.normal;
            lastMagnetTouch = Time.time;
        }
        // Just walked off an edge: wrap around onto the face below it
        else if (!grounded && !justJumped && magnetGrounded && lastMagnetMove != Vector3.zero)
        {
            Vector3 below = center - up * (half + 0.7f);
            if (Physics.SphereCast(below, 0.3f, -lastMagnetMove, out RaycastHit edge, 2f, structureLayer, QueryTriggerInteraction.Ignore)
                && Vector3.Angle(edge.normal, up) > 35f)
            {
                up = edge.normal;
                grounded = true;
                lastMagnetTouch = Time.time;
            }
        }

        // Too long without a surface: back to the sky's up (a normal fall, still magnetised for the next wall)
        if (!grounded && Time.time - lastMagnetTouch > MagnetLetGo) up = Vector3.up;

        player.MagnetUp = up.normalized;
        bool wasGrounded = magnetGrounded;
        magnetGrounded = grounded;
        OnGround = grounded;
        if (grounded && !wasGrounded) player.NotifyLanded();

        // Steer along the surface, keep (or set) speed across it
        Vector3 v      = rb.linearVelocity;
        Vector3 across = Vector3.Project(v, up);
        Vector3 along  = v - across;
        Vector3 target = wish * player.TargetSpeed;
        float   accel  = grounded ? 90f : 40f;
        if (grounded || wish.sqrMagnitude > 0.01f) along = Vector3.MoveTowards(along, target, accel * Time.fixedDeltaTime);
        if (grounded && Vector3.Dot(across, up) > 0f) across = Vector3.zero; // Stay on it

        if (grounded && Time.time - jumpPressedAt <= 0.15f && player.CanMove)
        {
            across = up * (player.Stats.jumpStrength * player.Modifiers.Get(Stat.Jump));
            jumpPressedAt  = float.NegativeInfinity;
            lastJumpTime   = Time.time;
            magnetGrounded = false;
            OnGround       = false;
            player.NotifyJumped();
        }

        rb.linearVelocity = along + across;
        rb.AddForce(-up * MagnetStick, ForceMode.Acceleration);
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

        touching = FindGround(GroundProbe, out groundNormal, out _);

        // Just ran over a crest or off a small step: pull down onto the ground instead of floating off it
        if (!touching && wasGrounded && CanSnap()
            && FindGround(SnapDistance, out Vector3 snapNormal, out float gap))
        {
            Vector3 v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(v.x, Mathf.Min(v.y, -gap / Time.fixedDeltaTime), v.z);
            touching     = true;
            groundNormal = snapNormal;
        }

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
        wallForward = Physics.Raycast(chest, transform.forward, out RaycastHit ahead, reach, structureLayer);
        // A placed Ladder (the only TippingProp) at any lean: climbed from a standstill, and you can rest on it
        onLadder = wallForward && ahead.collider.GetComponentInParent<TippingProp>() != null;
        if (!onLadder && State == Player.MovementState.Climb && ladderGrip > 0f && Time.time - ladderGrip < 0.15f)
            onLadder = true; // A rung's gap for a moment doesn't drop you
        if (onLadder && wallForward) ladderGrip = Time.time;

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

        bool steady = player.SteadyClimbSpeed > 0f; // Infinite Climb held: facing a wall is enough
        if (wallForward && (move.y > 0.1f || steady) && State != Player.MovementState.Climb)
            StartClimb();
        else if (State == Player.MovementState.Climb && ShouldStopClimbing(move))
        {
            // Ran out of wall while going up: climb onto the top instead of flying off into the sky
            bool topped = !wallForward && rb.linearVelocity.y > 0.5f && TryMantle();
            if (!topped) player.SetState(Player.MovementState.Idle);
        }
    }

    // ─── Mantle (over the top of a climbed wall) ──────────────────────────────

    [Header("Mantle")]
    [Tooltip("Share of the climb's upward speed turned into forward speed when climbing over the top of a wall.")]
    [SerializeField] private float mantleForwardShare = 0.6f;
    [Tooltip("Extra clearance (m) above the top the hop aims for, so the feet clear the edge.")]
    [SerializeField] private float mantleClearance = 0.35f;
    [Tooltip("Cap on the forward speed a mantle can give (m/s).")]
    [SerializeField] private float mantleMaxForward = 14f;

    /// <summary>
    /// The climb reached the top of a wall: if there's floor just past the edge (within reach of the body's
    /// height), swap the upward rush for a small hop that just clears the edge plus forward speed (a share of the
    /// climb speed), so the player lands on top of the wall moving on, instead of shooting metres into the air.
    /// </summary>
    private bool TryMantle()
    {
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f) return false;
        forward.Normalize();

        float height = player.Height;
        Vector3 from = transform.position + Vector3.up * (height + 1f) + forward * 1.1f;
        if (!Physics.Raycast(from, Vector3.down, out RaycastHit top, height + 1.5f, structureLayer, QueryTriggerInteraction.Ignore))
            return false;
        if (top.normal.y < 0.7f) return false;
        float rise = top.point.y - transform.position.y; // How far the feet still have to go up
        if (rise < -0.2f || rise > height + 0.5f) return false;
        if (Physics.Raycast(top.point + Vector3.up * 0.1f, Vector3.up, height * 0.7f, structureLayer)) return false; // No room on top

        float g = -Physics.gravity.y * Mathf.Max(0.1f, player.Modifiers.Get(Stat.Gravity));
        float needUp = Mathf.Sqrt(2f * g * Mathf.Max(0.05f, rise + mantleClearance));
        float up = rb.linearVelocity.y;
        Vector3 flat = new(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        float along = Mathf.Max(Vector3.Dot(flat, forward), 0f);
        float gain = Mathf.Max(0f, up - needUp) * mantleForwardShare;
        float forwardSpeed = Mathf.Min(Mathf.Max(along, 3f) + gain, Mathf.Max(mantleMaxForward, along));

        rb.linearVelocity = forward * forwardSpeed + Vector3.up * Mathf.Min(up, needUp);
        player.SetState(Player.MovementState.Idle);
        return true;
    }

    // A sphere just inside the capsule's bottom, swept down: finds ground under any part of the feet (standing
    // on an edge counts), and reads the face below the contact so an edge doesn't look like a slope.
    private bool FindGround(float distance, out Vector3 normal, out float gap)
    {
        normal = Vector3.up;
        gap    = 0f;
        float radius = (body != null ? body.radius : 0.5f) * 0.9f;
        const float lift = 0.1f;
        Vector3 origin = rb.position + Vector3.up * (radius + lift);

        if (!Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, lift + distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;

        normal = hit.normal;
        if (Physics.Raycast(hit.point + Vector3.up * 0.05f, Vector3.down, out RaycastHit face, 0.15f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            normal = face.normal;

        gap = Mathf.Max(0f, hit.distance - lift);
        return normal.y >= MaxWalkableSlope;
    }

    private bool CanSnap() =>
        Time.time - lastJumpTime > 0.3f && rb.linearVelocity.y < 1f
        && !player.IsBeingPushed && !player.IsDashing && !player.IsSwinging
        && State != Player.MovementState.Climb && State != Player.MovementState.WallRun;

    // ─── Discrete Input ───────────────────────────────────────────────────────

    private void HandleDiscreteInput()
    {
        if (State == Player.MovementState.Hang)
        {
            HandleHanging();
        }
        else if (input.GrabPressed && !player.GrabKeyClaimed) // The key may be withdrawing a spy camera instead
        {
            if (player.IsHolding) DropHeldObject();
            else                  Grab();
        }

        // Jumps are buffered and done in the physics step (letting go of a ledge uses the key itself)
        if (input.JumpPressed && State != Player.MovementState.Hang)
        {
            // From prone: stand up and jump straight away (it used to only stand up)
            if (State == Player.MovementState.Prone && CanChangeStance) player.ToggleProne();
            jumpPressedAt = Time.time;
        }
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

        // A prone slide coasts (with a little friction) unless the player is also swinging
        bool coasting = State == Player.MovementState.Prone && !player.IsSwinging
                     && rb.linearVelocity.magnitude > SlideThreshold;
        if (coasting)
        {
            if (touching && !player.IsBeingPushed) SlideFriction();
            return;
        }

        if (State == Player.MovementState.Climb)
        {
            Climb();
            return;
        }

        if (State == Player.MovementState.WallRun)
            WallRun();

        Vector2 move    = input.MoveInput;
        Vector3 wishDir = Heading * move.y + HeadingRight * move.x;
        if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();
        float speed      = player.TargetSpeed * wishDir.magnitude;
        float accelScale = Mathf.Max(0.05f, player.Modifiers.Get(Stat.Acceleration));

        if (player.IsSwinging)
            SwingSteer(wishDir, speed, accelScale);
        else if (touching && !jumpedThisStep && State != Player.MovementState.WallRun)
            GroundMove(wishDir, speed, accelScale);
        else
            AirMove(wishDir, speed, accelScale);
    }

    // On the ground: steer the velocity along the ground plane straight toward the wanted one. Speeding up
    // uses acceleration; stopping and turning use the (stronger) deceleration. Speed above the run speed only
    // bleeds off slowly while the player keeps moving that way. Upward speed (a pad, a jump) is never touched.
    private void GroundMove(Vector3 wishDir, float speed, float accelScale)
    {
        var     stats    = player.Stats;
        Vector3 n        = groundNormal;
        Vector3 v        = rb.linearVelocity;
        float   vn       = Vector3.Dot(v, n);
        Vector3 vp       = v - n * vn;
        float   current  = vp.magnitude;
        bool    hasInput = speed > 0.01f;
        bool    pushed   = player.IsBeingPushed;
        float   dt       = Time.fixedDeltaTime;

        Vector3 target;
        float   rate;
        if (!hasInput)
        {
            target = Vector3.zero;
            rate   = pushed ? 0f : stats.deceleration;
        }
        else
        {
            Vector3 dir     = Vector3.ProjectOnPlane(wishDir, n).normalized;
            bool    aligned = Vector3.Dot(dir, vp) > 0f;
            float   want    = speed;
            if (aligned && current > speed)
            {
                // Faster than running (dash, slide, pad, explosion): keep the speed, bleed it off slowly, steer it
                want = pushed ? current : Mathf.Max(speed, current - stats.overSpeedFriction * dt);
                rate = stats.acceleration;
            }
            else
            {
                // Speeding up toward the input uses acceleration; turning across or against it, deceleration
                rate = Vector3.Dot(dir, vp.normalized) > 0.7f || current < 0.5f ? stats.acceleration : stats.deceleration;
            }
            target = dir * want;
            if (pushed) rate *= 0.5f;
        }

        if (OnIce) rate *= iceControl;
        vp = Vector3.MoveTowards(vp, target, rate * accelScale * dt);

        rb.linearVelocity  = vp + n * Mathf.Max(vn, 0f);
        rb.angularVelocity = Vector3.zero;
    }

    // In the air: momentum is kept with no input. With input, the flat velocity is steered toward it without
    // gaining speed beyond what the player already had (or their run speed), so jumps can be corrected and
    // curved precisely, but air control is never a way to go faster.
    private void AirMove(Vector3 wishDir, float speed, float accelScale)
    {
        if (speed <= 0.01f) return;

        Vector3 v    = rb.linearVelocity;
        Vector3 flat = new(v.x, 0f, v.z);
        Vector3 dir  = wishDir.normalized;

        float cap     = Vector3.Dot(flat, dir) > 0f ? Mathf.Max(speed, flat.magnitude) : speed;
        float control = Mathf.Min(1f, player.Stats.airControl * player.Modifiers.Get(Stat.AirControl));
        if (player.IsBeingPushed) control *= 0.3f;

        flat = Vector3.MoveTowards(flat, dir * cap, player.Stats.acceleration * accelScale * control * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(flat.x, v.y, flat.z);
    }

    // On a grapple: only a gentle push (the rope drives the motion)
    private void SwingSteer(Vector3 wishDir, float speed, float accelScale)
    {
        Vector3 flat   = new(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        Vector3 wanted = speed > 0.01f ? wishDir.normalized * speed : flat;
        Vector3 acc    = (wanted - flat) / Time.fixedDeltaTime;
        acc = Vector3.ClampMagnitude(acc, player.Stats.acceleration * accelScale * player.Stats.swingControl);
        rb.AddForce(acc, ForceMode.Acceleration);
    }

    // A prone slide on the ground slows a little each step (downhill, gravity keeps it going)
    private void SlideFriction()
    {
        Vector3 v    = rb.linearVelocity;
        Vector3 flat = new(v.x, 0f, v.z);
        flat = Vector3.MoveTowards(flat, Vector3.zero, player.Stats.slideFriction * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(flat.x, v.y, flat.z);
    }

    // ─── Jump ─────────────────────────────────────────────────────────────────

    // A jump pressed shortly before it became possible (landing, reaching a wall) still happens
    private void TryBufferedJump()
    {
        if (Time.time - jumpPressedAt > player.Stats.jumpBuffer) return;
        if (Jump()) jumpPressedAt = float.NegativeInfinity;
    }

    // Letting go of the key while still rising from a jump cuts it short (short hops for precise platforming)
    private void UpdateJumpCut()
    {
        if (!jumpCutReady) return;
        Vector3 v = rb.linearVelocity;
        if (v.y <= 0f || player.IsBeingPushed) { jumpCutReady = false; return; }
        if (input.JumpHeld) return;

        rb.linearVelocity = new Vector3(v.x, v.y * player.Stats.jumpCut, v.z);
        jumpCutReady = false;
    }

    // Jumps set the vertical speed rather than adding to it, so every jump is the same height
    private bool Jump()
    {
        float   strength = player.Stats.jumpStrength * player.Modifiers.Get(Stat.Jump);
        bool    grounded = touching || Time.time - lastTouchTime <= player.Stats.coyoteTime;
        bool    fresh    = Time.time - lastJumpTime > JumpLockout;
        Vector3 v        = rb.linearVelocity;

        if (!grounded && (WallLeft || WallRight) && currentWallNormal != Vector3.zero)
        {
            // Off a wall: away from it and up, keeping the speed along the wall
            Vector3 along = Vector3.ProjectOnPlane(new Vector3(v.x, 0f, v.z), currentWallNormal);
            rb.linearVelocity = along + currentWallNormal * (strength * 0.75f) + Vector3.up * strength;
            if (State == Player.MovementState.WallRun) StopWallRun();
        }
        else if (!grounded && State == Player.MovementState.Climb)
        {
            // Off a climb: up and back from the wall
            Vector3 back = -Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            rb.linearVelocity = back * (strength * 0.7f) + Vector3.up * strength;
            player.SetState(Player.MovementState.Idle);
        }
        else if (grounded && fresh)
        {
            float height = strength;
            if (State == Player.MovementState.Crouch)
            {
                ToggleCrouch();
                height *= crouchJumpMultiplier;
            }
            rb.linearVelocity = new Vector3(v.x, Mathf.Max(v.y, 0f) + height, v.z);
            lastTouchTime = float.NegativeInfinity; // Coyote time is spent
        }
        else return false;

        lastJumpTime   = Time.time;
        jumpCutReady   = true;
        jumpedThisStep = true;
        touching       = false;
        player.NotifyJumped();
        return true;
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

    private float nextDiveTime;

    private void ToggleProne()
    {
        if (!CanChangeStance) return;

        player.ToggleProne();

        // Airborne dive
        if (State == Player.MovementState.Prone && !OnGround && Time.time >= nextDiveTime)
        {
            rb.AddForce(transform.forward * proneDiveImpulse, ForceMode.Impulse);
            nextDiveTime = Time.time + proneDiveCooldown; // Invisible cooldown: going prone still works, the boost waits
        }
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

    private bool  onLadder;
    private float ladderGrip;

    [Header("Climbing")]
    [Tooltip("Climb speed (m/s) from any jump at a wall, whatever the run-up.")]
    [SerializeField] private float climbBaseSpeed = 23f;
    [Tooltip("Extra climb speed (m/s) from running at the wall, full at sprint speed.")]
    [SerializeField] private float climbMomentumBonus = 3f;
    [Tooltip("Falling faster than this (m/s) into a wall only slides down it.")]
    [SerializeField] private float climbFallLimit = 4f;

    private void StartClimb()
    {
        if (State == Player.MovementState.Prone || State == Player.MovementState.Crouch) return;
        // Less momentum-based (Nov 11): any jump at a wall climbs about as high as a running one. In the air a climb
        // starts at climbBaseSpeed, plus up to climbMomentumBonus for run speed toward it (≈10.4 m of climb from a
        // standing jump, ≈13.3 m from a sprint, at 25 m/s² of climb decay; it was ≈14 and ≈18 m, and a dash got
        // the full 18); falling hard into a wall only slides. From the
        // ground it needs speed toward the wall (a walk-up gives a short climb, never more than the base)
        Vector3 v = rb.linearVelocity;
        float flat = new Vector2(v.x, v.z).magnitude;
        float runShare = Mathf.Clamp01(flat / player.Stats.sprintSpeed);
        float speed;
        if (OnGround) speed = Mathf.Min(flat * 1.4f, climbBaseSpeed);
        else speed = v.y < -climbFallLimit ? 0f : climbBaseSpeed + climbMomentumBonus * runShare;
        speed = Mathf.Min(speed * player.Modifiers.Get(Stat.Speed), player.Stats.climbSpeed * player.Modifiers.Get(Stat.Speed));

        // Infinite Climb: always its own steady speed, from a standstill too; momentum plays no part
        if (player.SteadyClimbSpeed > 0f) speed = player.SteadyClimbSpeed;

        // Standing at the foot of a wall without momentum is not a climb (it used to lock the player in place)
        if (OnGround && speed <= MinClimbSpeed) return;

        currentClimbSpeed = speed;
        player.SetState(Player.MovementState.Climb);
    }

    // Climbing ends when the wall is gone, when the player pulls back (S), or on the floor whenever they
    // aren't actively climbing up (no forward input, or the climb has run out of speed). Never stuck on the floor.
    private bool ShouldStopClimbing(Vector2 move)
    {
        if (player.SteadyClimbSpeed > 0f) return !wallForward || move.y < -0.1f; // Infinite Climb: up while held, S lets go
        if (onLadder) return move.y < -0.1f || (OnGround && move.y <= 0.1f); // Rest anywhere on it; S lets go
        if (!wallForward || move.y < -0.1f) return true;
        return OnGround && (move.y <= 0.1f || Mathf.Abs(move.x) > 0.1f || currentClimbSpeed <= MinClimbSpeed);
    }

    private void Climb()
    {
        if (onLadder)
        {
            // A ladder climbs exactly like a wall (same start speed, same slowing down; no boost), but where the climb
            // runs out, or when W is let go, you stay put instead of sliding down. Only a light hold against it, so
            // the climber doesn't shove a standing ladder over
            Vector3 middle = transform.position + Vector3.up * (player.Height * 0.5f);
            Vector3 grip = Physics.Raycast(middle, transform.forward, out RaycastHit rung, SteadyClimbReach, structureLayer)
                ? -rung.normal * 0.4f : Vector3.zero;
            bool climbing = input.MoveInput.y > 0.1f && currentClimbSpeed > MinClimbSpeed;
            rb.linearVelocity = new Vector3(grip.x, climbing ? currentClimbSpeed : 0f, grip.z);
            if (climbing) currentClimbSpeed -= 0.5f;
            else currentClimbSpeed = 0f; // Resting spends the climb: like a wall, it doesn't come back
            return;
        }
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
            ? player.SteadyClimbSpeed
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

    // Ledge detection is deliberately forgiving: a fat sphere cast (not a thin ray) at three heights and three
    // angles either side of where you face, reaching a little further, up to LedgeReach above the feet.
    private const float LedgeProbeRadius = 0.45f;
    private const float LedgeProbeReach  = 3.5f;
    private const float LedgeReach       = 6.8f;   // Highest ledge top above the feet that can be grabbed
    private const float LedgeHalfWidth   = 0.4f;   // Ledge needed either side of the hands
    private static readonly float[] LedgeProbeHeights = { 0.6f, 1.6f, 2.6f };
    private static readonly float[] LedgeProbeAngles  = { 0f, -30f, 30f };

    private void TryGrabLedge()
    {
        foreach (float angle in LedgeProbeAngles)
        {
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) continue;
            dir.Normalize();

            foreach (float height in LedgeProbeHeights)
            {
                Vector3 origin = transform.position + Vector3.up * height;
                if (!Physics.SphereCast(origin, LedgeProbeRadius, dir, out RaycastHit wallHit, LedgeProbeReach, structureLayer, QueryTriggerInteraction.Ignore))
                    continue;
                if (Mathf.Abs(wallHit.normal.y) > 0.5f) continue; // A floor or ceiling, not a wall
                if (TryLedgeAt(wallHit)) return;
            }
        }
    }

    [Tooltip("After grabbing a ledge, how long (s) W is ignored before it pulls you up, so a held W doesn't climb at once.")]
    [SerializeField] private float ledgeClimbDelay = 0.3f;
    private float hangStartedAt;

    private bool TryLedgeAt(RaycastHit wallHit)
    {
        Vector3 into = -new Vector3(wallHit.normal.x, 0f, wallHit.normal.z).normalized;
        float   feet = transform.position.y;
        Vector3 top  = new(wallHit.point.x, feet + LedgeReach, wallHit.point.z);
        top += into * 0.25f;

        if (!Physics.Raycast(top, Vector3.down, out RaycastHit ledgeHit, LedgeReach - 0.3f, structureLayer, QueryTriggerInteraction.Ignore)) return false;
        if (Vector3.Dot(ledgeHit.normal, Vector3.up) < 0.7f) return false;

        // Needs headroom above the ledge and enough width either side
        if (Physics.Raycast(ledgeHit.point + Vector3.up * 0.1f, Vector3.up, 1.5f, structureLayer)) return false;
        if (!HasLedgeWidth(ledgeHit.point, wallHit.normal)) return false;

        ledgePosition = ledgeHit.point;
        ledgeNormal   = new Vector3(wallHit.normal.x, 0f, wallHit.normal.z).normalized;

        player.SetState(Player.MovementState.Hang);
        hangStartedAt     = Time.time;
        rb.linearVelocity = Vector3.zero;
        rb.position       = HangPosition;
        return true;
    }

    private bool HasLedgeWidth(Vector3 ledgePoint, Vector3 wallNormal)
    {
        Vector3 right = Vector3.Cross(Vector3.up, wallNormal).normalized;
        Vector3 above = ledgePoint + Vector3.up * 0.5f;

        return Physics.Raycast(above - right * LedgeHalfWidth, Vector3.down, 1f, structureLayer)
            && Physics.Raycast(above + right * LedgeHalfWidth, Vector3.down, 1f, structureLayer);
    }

    private Vector3 HangPosition => ledgePosition - ledgeNormal * LedgeOffset - Vector3.up * 1.8f;

    private void HandleHanging()
    {
        if (climbingLedge) return;

        float vertical = input.MoveInput.y;
        if (vertical > 0.5f && Time.time - hangStartedAt >= ledgeClimbDelay)
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
        LedgeShimmy = 0f;
        if (Mathf.Abs(horizontal) < 0.01f) return;

        Vector3 left     = Vector3.Cross(Vector3.up, ledgeNormal);
        Vector3 proposed = ledgePosition + left * (-horizontal * player.TargetSpeed * Time.deltaTime);
        Vector3 probe    = proposed + Vector3.up * 0.5f - left * 1f;

        if (Physics.Raycast(probe, Vector3.down, out RaycastHit support, 1f, structureLayer)
            && Vector3.Dot(support.normal, Vector3.up) >= 0.7f)
        {
            ledgePosition = proposed;
            LedgeShimmy = Mathf.Sign(horizontal);
        }
    }

    private IEnumerator ClimbOntoLedge()
    {
        climbingLedge = true;
        LedgeShimmy = 0f;
        Vector3 start  = transform.position;
        Vector3 target = ledgePosition + Vector3.up * 0.5f - ledgeNormal * 0.3f;

        // Moved in physics steps through the Rigidbody, so interpolation keeps it smooth; as long as the
        // climb-up animation takes at its state speed (LedgeAnimationSetup: 1.33 s clip × 1.8)
        const float duration = 0.75f;
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
