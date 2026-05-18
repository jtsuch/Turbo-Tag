using System.Collections;
using UnityEngine;
using Photon.Pun;

/// <summary>
/// Core player movement: grounded locomotion, wall-running, climbing, ledge-hanging,
/// and physics-based acceleration model. All movement is driven through Rigidbody forces
/// in FixedUpdate; input sampling happens in Update to avoid missed key events.
/// Attach to: ThePlayer prefab — requires Player, Rigidbody, and PhotonView on the same object.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PhotonView))]
public class JimmyMove : MonoBehaviour
{
    // ─── References ───────────────────────────────────────────────────────────
    [Header("References")]
    public Player       player;
    public InputHandler input;
    public Rigidbody    rb;
    public Camera       cam;
    public LayerMask    structureLayer;
    public Transform    throwPoint;

    private PhotonView view;

    // ─── Wall-Run State ───────────────────────────────────────────────────────
    private float   timeOnWall       = 0f;
    private Vector3 lastWallNormal   = Vector3.zero;
    private Vector3 currentWallNormal = Vector3.zero;

    // ─── Surface Detection ────────────────────────────────────────────────────
    [Header("Don't Change")]
    public bool onGround;
    public bool wallLeft;
    public bool wallRight;
    private bool wallForward;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        view = GetComponent<PhotonView>();
        if (rb == null && view.IsMine)
        {
            rb = GetComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
        if (player != null)
            player.OnStateChanged += OnPlayerStateChanged;
    }

    void OnDestroy()
    {
        if (player != null)
            player.OnStateChanged -= OnPlayerStateChanged;
    }

    // Restores physics when any code forces a state change away from WallRun or Hang
    // (e.g. an ability calling player.SetState directly without going through StopWallRun)
    private void OnPlayerStateChanged(Player.MovementState newState)
    {
        if (player.lastState == Player.MovementState.WallRun && newState != Player.MovementState.WallRun)
        {
            rb.useGravity = true;
            timeOnWall    = 0f;
        }
        if (player.lastState == Player.MovementState.Hang && newState != Player.MovementState.Hang)
            rb.useGravity = true;
    }

    void FixedUpdate()
    {
        if (!view.IsMine) return;
        SurfaceCheck();
        MovePlayer();
    }

    void Update()
    {
        if (!view.IsMine) return;
        HandleInput();
    }

    // ─── Surface Detection ────────────────────────────────────────────────────

    void SurfaceCheck()
    {
        bool wasGrounded = onGround;
        onGround = Physics.Raycast(transform.position, Vector3.down, player.height * 0.2f, ~0);

        if (!wasGrounded && onGround)
            player.TriggerLand();

        if (player.currentState == Player.MovementState.Hang)
            return;

        Vector3 playerCenter = new(transform.position.x, transform.position.y + player.height * 0.5f, transform.position.z);
        wallForward = Physics.Raycast(playerCenter, transform.forward, 1f, structureLayer);

        wallLeft  = Physics.Raycast(transform.position, -transform.right, out RaycastHit wallHitLeft,  1f, structureLayer);
        wallRight = Physics.Raycast(transform.position,  transform.right, out RaycastHit wallHitRight, 1f, structureLayer);

        if      (wallLeft)  currentWallNormal = wallHitLeft.normal;
        else if (wallRight) currentWallNormal = wallHitRight.normal;
        else                currentWallNormal = Vector3.zero;

        // Reset wall memory on landing so the same wall can be re-used after touching the ground
        if (onGround) lastWallNormal = Vector3.zero;

        // Wall-run start/stop
        if (((wallLeft && Input.GetKey(KeyCode.A)) || (wallRight && Input.GetKey(KeyCode.D))) && !onGround)
        {
            if (player.currentState != Player.MovementState.WallRun)
                StartWallRun(currentWallNormal);
        }
        else if (player.currentState == Player.MovementState.WallRun)
            StopWallRun();

        // Climb start/stop
        if (wallForward  && Input.GetKey(KeyCode.W) && player.currentState != Player.MovementState.Climb)
            StartClimb();
        if (!wallForward && player.currentState == Player.MovementState.Climb)
            EndClimb();
    }

    // ─── Input ────────────────────────────────────────────────────────────────

    void HandleInput()
    {
        if (player.currentState == Player.MovementState.Hang)
        {
            HandleHangingState();
        }
        else if (input.Grab && !player.IsHolding)
        {
            Grab();
        }
        else if (input.Grab && player.IsHolding)
        {
            DropHeldObject();
        }

        if (input.Jump)   Jump();
        if (input.Crouch) ToggleCrouch();
        if (input.Prone)  ToggleProne();
    }

    // ─── Ground Movement ──────────────────────────────────────────────────────

    void MovePlayer()
    {
        // During a dash, brake if above speed cap
        if (player.IsDashing && rb.linearVelocity.magnitude > 30f)
        {
            rb.AddForce(-rb.linearVelocity / 5f, ForceMode.Acceleration);
            return;
        }

        // Hang is self-managed; prone-slide bypasses steering unless the player is also swinging
        if (player.currentState == Player.MovementState.Hang ||
           (player.currentState == Player.MovementState.Prone && !player.IsSwinging && rb.linearVelocity.magnitude > player.ProneSpeed + 1f))
            return;

        if (player.currentState == Player.MovementState.Climb)
        {
            Climb();
            return;
        }

        if (player.currentState == Player.MovementState.WallRun)
            WallRun();

        float moveH = Input.GetAxis("Horizontal") * player.MovementScale.x;
        float moveV = Input.GetAxis("Vertical")   * player.MovementScale.y;

        if (PauseMenuManager.Instance.Paused || !player.CanMove)
            moveH = moveV = 0f;

        Vector3 intendedDir = (transform.forward * moveV + transform.right * moveH) * player.targetSpeed;
        Vector3 flatVel     = new(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        // No input on ground: stop quickly or zero out tiny jitter.
        // Skip while swinging — the grapple joint drives momentum and braking fights it.
        if (onGround && intendedDir.magnitude == 0 && !player.IsSwinging)
        {
            if (rb.linearVelocity.magnitude < 1f)
            {
                rb.linearVelocity  = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            else
            {
                rb.AddForce(-rb.linearVelocity * 2f, ForceMode.Acceleration);
            }
        }

        // No input in air: preserve current momentum
        if (intendedDir.magnitude == 0 && !onGround)
            intendedDir = flatVel;

        Vector3 velDiff    = intendedDir - flatVel;
        Vector3 desiredAcc = velDiff / Time.fixedDeltaTime;
        float   maxAccel   = Mathf.Max(1f, player.Acceleration);

        float controlMult = player.IsSwinging ? player.SwingControlMult
                          : !onGround         ? player.AirControlMult
                          : 1f;

        desiredAcc = Vector3.ClampMagnitude(desiredAcc, maxAccel * Mathf.Max(controlMult, 0.0001f));

        // Cancel slope-induced sliding while grounded
        if (onGround && Physics.Raycast(transform.position, Vector3.down, out RaycastHit groundHit, player.height + 0.5f, ~0))
        {
            Vector3 n = groundHit.normal;
            Vector3 slopeCorrection = n * Physics.gravity.y / Mathf.Max(n.y, 0.0001f);
            slopeCorrection.y = 0f;
            desiredAcc += slopeCorrection;
        }

        rb.AddForce(desiredAcc, ForceMode.Acceleration);
    }

    // ─── Jump ─────────────────────────────────────────────────────────────────

    void Jump()
    {
        if (player.currentState == Player.MovementState.Prone)
        {
            player.ToggleProneHeight();
            return;
        }

        Vector3 jumpVector = Vector3.zero;

        if (wallLeft && !onGround)
            jumpVector = (transform.up + transform.right) * player.JumpStrength;
        else if (wallRight && !onGround)
            jumpVector = (transform.up - transform.right) * player.JumpStrength;
        else if (player.currentState == Player.MovementState.Climb && !onGround)
            jumpVector = (transform.up - transform.forward) * player.JumpStrength;
        else if (onGround)
        {
            if (player.currentState == Player.MovementState.Crouch)
            {
                ToggleCrouch();
                jumpVector = new Vector3(0f, player.JumpStrength * 1.6f, 0f);
            }
            else
                jumpVector = new Vector3(0f, player.JumpStrength, 0f);
        }

        if (jumpVector != Vector3.zero)
        {
            rb.AddForce(jumpVector, ForceMode.Impulse);
            player.TriggerJump();
        }
    }

    // ─── Crouch / Prone ───────────────────────────────────────────────────────

    public void ToggleCrouch()
    {
        if (player.IsSwinging ||
            player.currentState == Player.MovementState.WallRun ||
            player.currentState == Player.MovementState.Climb  ||
            player.currentState == Player.MovementState.Hang) return;

        player.ToggleCrouchHeight();
    }

    void ToggleProne()
    {
        if (player.IsSwinging ||
            player.currentState == Player.MovementState.WallRun ||
            player.currentState == Player.MovementState.Climb  ||
            player.currentState == Player.MovementState.Hang) return;

        player.ToggleProneHeight();

        // Airborne dive: add a forward impulse when going prone mid-air
        if (player.currentState == Player.MovementState.Prone && !onGround)
            rb.AddForce(transform.forward * 10f, ForceMode.Impulse);
    }

    // ─── Climbing ─────────────────────────────────────────────────────────────

    private float currentClimbSpeed = 0f;

    void StartClimb()
    {
        if (player.currentState == Player.MovementState.Prone ||
            player.currentState == Player.MovementState.Crouch) return;

        currentClimbSpeed = rb.linearVelocity.y < 0f ? 0f
                          : Mathf.Min(rb.linearVelocity.magnitude * 1.7f, player.ClimbSpeed);
        player.SetState(Player.MovementState.Climb);
    }

    void Climb()
    {
        if (currentClimbSpeed <= 3f)
        {
            if (rb.linearVelocity.y < -player.WallSlideDownMax)
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, -player.WallSlideDownMax, rb.linearVelocity.z);
            return;
        }

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, currentClimbSpeed, rb.linearVelocity.z);
        currentClimbSpeed -= 0.5f;
    }

    void EndClimb() => player.SetState(Player.MovementState.Idle);

    // ─── Wall-Run ─────────────────────────────────────────────────────────────

    void StartWallRun(Vector3 wall)
    {
        if (player.currentState == Player.MovementState.Prone ||
            player.currentState == Player.MovementState.Crouch) return;
        if (wall == lastWallNormal) return;

        player.SetState(Player.MovementState.WallRun);
        rb.useGravity  = false;
        lastWallNormal = currentWallNormal;

        // Cancel downward velocity at the moment of contact
        if (timeOnWall == 0)
            rb.AddForce(transform.up * (rb.linearVelocity.y / -2f), ForceMode.Impulse);
    }

    void WallRun()
    {
        timeOnWall += Time.deltaTime;

        // Stick the player to the wall surface
        rb.AddForce(wallLeft ? -transform.right * 10f : transform.right * 10f);

        // Upward pull diminishes and inverts over time so the player gradually slides down
        float pullForce = -1f * Mathf.Pow(4f, timeOnWall - 1f) - 2f;
        rb.AddForce(transform.up * pullForce);

        if (rb.linearVelocity.y < -player.WallSlideDownMax)
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, -player.WallSlideDownMax, rb.linearVelocity.z);
    }

    void StopWallRun()
    {
        player.SetState(Player.MovementState.Idle);
        rb.useGravity = true;
        timeOnWall    = 0f;
    }

    // ─── Grab / Hold / Throw ──────────────────────────────────────────────────

    private GameObject heldObject;
    private Rigidbody  heldObjectRb;
    private Vector3    ledgePosition;
    private Vector3    ledgeNormal;
    private readonly float ledgeOffset = -1f;

    void Grab()
    {
        bool canGrabLedge = !onGround && player.currentState != Player.MovementState.Prone;
        if (!PickUpObject() && canGrabLedge)
            CheckForLedge();
    }

    private bool PickUpObject()
    {
        if (cam == null) return false;

        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 4f))
            return false;

        if (!hit.collider.CompareTag("Grabbable")) return false;
        if (!hit.collider.TryGetComponent<Rigidbody>(out var objRb)) return false;

        heldObject   = hit.collider.gameObject;
        heldObjectRb = objRb;
        player.IsHolding = true;

        heldObjectRb.useGravity       = false;
        heldObjectRb.isKinematic      = true;
        heldObjectRb.detectCollisions = false;
        heldObjectRb.linearDamping    = 10f;
        heldObjectRb.constraints      = RigidbodyConstraints.FreezeRotation;

        if (throwPoint != null)
        {
            heldObject.transform.parent = throwPoint;
            heldObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        if (view.IsMine)
            view.RPC("RPC_SetHeldObject", RpcTarget.Others, heldObject.GetComponent<PhotonView>().ViewID);

        return true;
    }

    private void DropHeldObject()
    {
        if (heldObject == null) return;

        heldObjectRb.useGravity       = true;
        heldObjectRb.isKinematic      = false;
        heldObjectRb.detectCollisions = true;
        heldObjectRb.linearDamping    = 0f;
        heldObjectRb.constraints      = RigidbodyConstraints.None;

        heldObject.transform.parent = null;
        heldObjectRb.AddForce(cam.transform.forward * 5f, ForceMode.Impulse);

        if (view.IsMine)
            view.RPC("RPC_DropHeldObject", RpcTarget.Others);

        heldObject   = null;
        heldObjectRb = null;
        player.IsHolding = false;
    }

    // ─── Ledge Detection & Hanging ────────────────────────────────────────────

    private void CheckForLedge()
    {
        if (!Physics.Raycast(transform.position, transform.forward, out RaycastHit wallHit, 3f, structureLayer))
            return;

        Vector3 ledgeCheckOrigin = wallHit.point + Vector3.up * 6.2f + transform.forward * 0.1f;
        if (!Physics.Raycast(ledgeCheckOrigin, Vector3.down, out RaycastHit ledgeHit, 6.4f, structureLayer))
            return;

        if (Vector3.Dot(ledgeHit.normal, Vector3.up) < 0.7f) return;

        Vector3 spaceCheck = ledgeHit.point + Vector3.up * 0.1f;
        if (Physics.Raycast(spaceCheck, Vector3.up, 1.5f, structureLayer)) return;

        if (!CheckLedgeWidth(ledgeHit.point, wallHit.normal)) return;

        GrabLedge(ledgeHit.point, wallHit.normal);
    }

    private bool CheckLedgeWidth(Vector3 ledgePoint, Vector3 wallNormal)
    {
        Vector3 right = Vector3.Cross(Vector3.up, wallNormal);
        Vector3 base_ = ledgePoint + Vector3.up * 0.5f;

        bool leftOk  = Physics.Raycast(base_ - right * 0.6f, Vector3.down, 1f, structureLayer);
        bool rightOk = Physics.Raycast(base_ + right * 0.6f, Vector3.down, 1f, structureLayer);
        return leftOk && rightOk;
    }

    private void GrabLedge(Vector3 ledge, Vector3 normal)
    {
        player.SetState(Player.MovementState.Hang);
        ledgePosition = ledge;
        ledgeNormal   = normal;

        rb.linearVelocity = Vector3.zero;
        rb.useGravity     = false;

        transform.position = ledgePosition - normal * ledgeOffset - Vector3.up * 1.8f;

        if (view.IsMine)
            view.RPC("RPC_SetHangingState", RpcTarget.Others, true, ledgePosition, ledgeNormal);
    }

    private void HandleHangingState()
    {
        if (Input.GetKeyDown(KeyCode.W))      ClimbUp();
        else if (Input.GetKeyDown(KeyCode.S)) LetGo();

        // Lerp to hang position each frame
        transform.position = Vector3.Lerp(
            transform.position,
            ledgePosition - ledgeNormal * ledgeOffset - Vector3.up * 1.8f,
            Time.deltaTime * 10f);

        // Shimmy along ledge
        float horizontal = Input.GetAxis("Horizontal");
        if (Mathf.Abs(horizontal) > 0.01f)
        {
            Vector3 left        = Vector3.Cross(Vector3.up, ledgeNormal);
            Vector3 proposed    = ledgePosition + left * (-horizontal * player.ShimmySpeed * Time.deltaTime);
            Vector3 checkOrigin = proposed + Vector3.up * 0.5f - left * 1f;

            if (Physics.Raycast(checkOrigin, Vector3.down, out RaycastHit supportHit, 1f, structureLayer)
                && Vector3.Dot(supportHit.normal, Vector3.up) >= 0.7f)
                ledgePosition = proposed;
        }
    }

    private void ClimbUp()
    {
        Vector3 target = ledgePosition + Vector3.up * 0.5f - ledgeNormal * 0.3f;
        StartCoroutine(ClimbCoroutine(target));
    }

    private IEnumerator ClimbCoroutine(Vector3 target)
    {
        float elapsed = 0f, duration = 0.5f;
        Vector3 start = transform.position;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(start, target, elapsed / duration);
            yield return null;
        }

        ReleaseLedge();
    }

    private void LetGo()
    {
        ReleaseLedge();
        rb.linearVelocity = -ledgeNormal * 2f;
    }

    private void ReleaseLedge()
    {
        player.SetState(Player.MovementState.Idle);
        rb.useGravity = true;

        if (view.IsMine)
            view.RPC("RPC_SetHangingState", RpcTarget.Others, false, Vector3.zero, Vector3.zero);
    }

    // ─── Network RPCs ─────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_SetHeldObject(int objectViewId)
    {
        PhotonView objectView = PhotonView.Find(objectViewId);
        if (objectView == null) return;

        heldObject   = objectView.gameObject;
        heldObjectRb = heldObject.GetComponent<Rigidbody>();
        player.IsHolding = true;

        if (throwPoint != null)
        {
            heldObject.transform.parent = throwPoint;
            heldObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
    }

    [PunRPC]
    private void RPC_DropHeldObject()
    {
        if (heldObject == null) return;

        heldObject.transform.parent = null;
        if (heldObjectRb != null)
        {
            heldObjectRb.useGravity   = true;
            heldObjectRb.linearDamping = 0f;
            heldObjectRb.constraints  = RigidbodyConstraints.None;
        }

        heldObject   = null;
        heldObjectRb = null;
        player.IsHolding = false;
    }

    [PunRPC]
    private void RPC_SetHangingState(bool hanging, Vector3 ledgePos, Vector3 ledgeNorm)
    {
        player.currentState = hanging ? Player.MovementState.Hang : Player.MovementState.Idle;
        ledgePosition = ledgePos;
        ledgeNormal   = ledgeNorm;
        rb.useGravity = !hanging;
    }
}
