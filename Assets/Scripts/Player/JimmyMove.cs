using System;
using UnityEngine;
using Photon.Pun;
// Note: UnityEditorInternal is an editor-only namespace and is not used at runtime here.


/*
 * 
 * Turbo Tag Player Movement Script
 * Covers all core movement abilties for the player that they will have at any given point in the game.
 * 
 */
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PhotonView))]
public class JimmyMove : MonoBehaviour
{
    [Header("References")]
    public Player player;
    public InputHandler input;
    public Rigidbody rb;
    public Camera cam;
    public LayerMask structureLayer;
    public Transform throwPoint; // Reference to ThrowPoint child transform

    PhotonView view;

    // Wall Running
    private float timeOnWall = 0f;
    private Vector3 lastWallNormal = new(0, 0, 0);
    private Vector3 currentWallNormal = new(0, 0, 0);

    [Header("Don't Change")]
    // Raycasts
    public bool onGround;
    public bool wallLeft;
    public bool wallRight;
    private bool wallForward;

    /*
    * When starting the game, initialize variables
    */
    void Start()
    {
        view = GetComponent<PhotonView>();
        if (rb == null && view.IsMine)
        {
            rb = GetComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

    }

    /*
    * For every fixed amount of time, check for surfaces, input, and state to move the player accordingly.
    * Display stats with a call to UIController. (FixedUpdate is used for anything physics related)
    */
    void FixedUpdate()
    {
        if (!view.IsMine) return;
        SurfaceCheck();
        MovePlayer();
    }

    /*
     * For every frame, check for input because FixedUpdate is not called every frame.
     */
    void Update()
    {
        if (!view.IsMine) return;
        InputHandler();
    }

    /*
     * Check if touching the floor or walls with raycasts. Set global bools accordingly.
     */
    void SurfaceCheck()
    {
        // Track previous grounded state so we can fire landing events
        bool wasGrounded = onGround;

        // Raycasts that checks for ground
        onGround = Physics.Raycast(transform.position, Vector3.down, player.height * 0.2f, ~0);

        // Fire event when player lands on ground
        if (!wasGrounded && onGround)
            player.TriggerLand();

        // Don't do checks if hanging
        if (player.currentState == Player.MovementState.Hang)
            return;

        // Raycasts that check for walls
        Vector3 playerCenter = new(transform.position.x, transform.position.y + player.height * 0.5f, transform.position.z);
        wallForward = Physics.Raycast(playerCenter, transform.forward, 1, structureLayer);
        //Debug.DrawRay(playerCenter, transform.forward * 1, Color.yellow);
        //Debug.DrawRay(transform.position, -transform.right * 1f, Color.red);

        wallLeft = Physics.Raycast(transform.position, -transform.right, out RaycastHit wallHitLeft, 1, structureLayer);
        wallRight = Physics.Raycast(transform.position, transform.right, out RaycastHit wallHitRight, 1, structureLayer);

        // Captures the 'normal' of the wall we are touching so we can't reset on the same / similar walls
        if (wallLeft)
            currentWallNormal = wallHitLeft.normal;
        else if (wallRight)
            currentWallNormal = wallHitRight.normal;
        else
            currentWallNormal = new Vector3(0, 0, 0);

        // Allows us to use the last wall if we touch the ground
        if (onGround)
            lastWallNormal = new Vector3(0, 0, 0);

            // Wall run
        if (((wallLeft && Input.GetKey(KeyCode.A)) || (wallRight && Input.GetKey(KeyCode.D))) && !onGround)
        {
            // If we are wall running for the first time
            if (player.currentState != Player.MovementState.WallRun)
                StartWallRun(currentWallNormal);
        }
        // If we stop wall running
        else if (player.currentState == Player.MovementState.WallRun)
            StopWallRun();

        // First instance of climbing
        if (wallForward && Input.GetKey(KeyCode.W) && player.currentState != Player.MovementState.Climb)
            StartClimb();

        // Last instance of climbing
        if (!wallForward && player.currentState == Player.MovementState.Climb)
            EndClimb();
    }

    /*
    * 
    * Check for relevent movement input keys.
    * 
    */
    void InputHandler()
    {
        // Grab
        if (player.currentState == Player.MovementState.Hang)
        {
            HandleHangingState();
        }
        else if (input.Grab && !player.IsHolding) // Only try to grab if not already holding
        {
            Grab();
        }
        else if (input.Grab && player.IsHolding) // If holding and press grab again, drop/throw
        {
            DropHeldObject();
        }

        // Jump
        if (input.Jump)
            Jump();

        if (input.Crouch)
            ToggleCrouch();

        // Prone
        if (input.Prone)
            ToggleProne();

    }

    /*
     * Apply forces to the player based on state and input.
     */
    void MovePlayer()
    {
        // When dashing, don't execute other movement AND limit speed
        if (player.IsDashing && rb.linearVelocity.magnitude > 30f)
        {
            Vector3 holdyourhorses = -rb.linearVelocity / 5f;
            rb.AddForce(holdyourhorses, ForceMode.Acceleration);
            return;
        }

        // If hanging or sliding
        if (player.currentState == Player.MovementState.Hang || (player.currentState == Player.MovementState.Prone && rb.linearVelocity.magnitude > player.ProneSpeed + 1f))
        {
            return;
        }

        // If in the climb state, only allow climbing movement
        if (player.currentState == Player.MovementState.Climb)
        {
            Climb();
            return;
        }

        // If wallrunning, add wallrun forces
        if (player.currentState == Player.MovementState.WallRun)
            WallRun();


        float moveHorizontal = Input.GetAxis("Horizontal") * player.MovementScale.x;
        float moveVertical   = Input.GetAxis("Vertical")   * player.MovementScale.y;

        // If paused, zero out input
        if (PauseMenuManager.Instance.Paused || !player.CanMove)
        {
            moveHorizontal = 0f;
            moveVertical = 0f;
        }
        
        // Vector of intended direction at the magnitude of targetSpeed
        Vector3 intendedDir = (transform.forward * moveVertical + transform.right * moveHorizontal) * (float)player.targetSpeed;
        
        // When no input
        if (onGround && intendedDir.magnitude == 0)
        {
            if (rb.linearVelocity.magnitude < 1f)
            {
                // Keep player from JITTERING on ground when no input is given
                rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0); // Zero out horizontal velocity, keep vertical (for gravity)
                rb.angularVelocity = Vector3.zero; // Transform was glitchy and would rotate after swinging
            }
            else
            {
                // Deccelerate quickly when no input
                Vector3 quickStop = -rb.linearVelocity * 2f;
                rb.AddForce(quickStop, ForceMode.Acceleration);
            }
        }

        // The player's current velocity in the X and Z coordinates
        Vector3 flatVel = new(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        // Allows player to move freely through the air without being pulled to a stop
        if (intendedDir.magnitude == 0 && !onGround)
        {
            intendedDir = flatVel;
        }

        Vector3 velDiff = intendedDir - flatVel; // how much horizontal velocity we need to add

        // Desired acceleration to correct velocity this FixedUpdate
        Vector3 desiredAcc = velDiff / Time.fixedDeltaTime;

        // Maximum acceleration (m/s^2) from player settings
        float maxAccel = Mathf.Max(1f, player.Acceleration);

        // Control multiplier (reduced control in air / swinging)
        float controlMult = 1f;
        if (player.IsSwinging) controlMult = player.SwingControlMult;
        else if (!onGround) controlMult = player.AirControlMult;

        float cappedAccel = maxAccel * Mathf.Max(controlMult, 0.0001f);

        // Cap desired acceleration but preserve small magnitudes (use ClampMagnitude instead of normalization)
        desiredAcc = Vector3.ClampMagnitude(desiredAcc, cappedAccel);

        // Slope correction when on ground (mitigate gravity on slopes)
        if (onGround)
        {
            Vector3 groundNormal = Vector3.up;
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit groundHit, player.height + 0.5f, ~0))
                groundNormal = groundHit.normal;

            Vector3 slopeCorrection = groundNormal * Physics.gravity.y / Mathf.Max(groundNormal.y, 0.0001f);
            slopeCorrection.y = 0f;

            desiredAcc += slopeCorrection;
        }

        // Apply acceleration (already adjusted for ground/air/swing control)
        rb.AddForce(desiredAcc, ForceMode.Acceleration);
    }





    /*
     *
     * ---------------------------------------------------------------------------------
     * BELOW ARE CORE MOVEMENT TYPE ACTIONS
     * ---------------------------------------------------------------------------------
     *
     */






    /*
     * Jump. Away from walls if wallrunning or climbing, straight up otherwise.
     */
    void Jump()
    {
        Vector3 jumpVector = new();
        if (player.currentState == Player.MovementState.Prone)
        {
            ToggleProne();
            return;
        }
        if (wallLeft && !onGround)
        {
            jumpVector = transform.up * player.JumpStrength + transform.right * player.JumpStrength;
        }
        else if (wallRight && !onGround)
        {
            jumpVector = transform.up * player.JumpStrength + -transform.right * player.JumpStrength;
        }
        else if (player.currentState == Player.MovementState.Climb && !onGround)
        {
            jumpVector = transform.up * player.JumpStrength + -transform.forward * player.JumpStrength;
        }
        else if (onGround)
        {
            if (player.currentState == Player.MovementState.Crouch)
            {
                ToggleCrouch();
                jumpVector = new(0, player.JumpStrength * 1.6f, 0);
            }
            else
                jumpVector = new(0, player.JumpStrength, 0);
        }
        //Debug.DrawRay(transform.position, jumpVector, Color.red);
        rb.AddForce(jumpVector, ForceMode.Impulse);
        player.TriggerJump();
    }

    public void ToggleCrouch()
    {
        if (player.IsSwinging || player.currentState == Player.MovementState.WallRun || player.currentState == Player.MovementState.Climb) return;
        player.ToggleCrouchHeight();
    }
            

    /*
     * Prone. Toggle between prone and normal stance.
     */
    void ToggleProne()
    {
        if (player.IsSwinging || player.currentState == Player.MovementState.WallRun || player.currentState == Player.MovementState.Climb) return;
        if (player.currentState != Player.MovementState.Prone)
        {
            player.SetState(Player.MovementState.Prone);
            if (!onGround)
                rb.AddForce(transform.forward * 10f, ForceMode.Impulse);
        }
        else
        {
            player.SetState(Player.MovementState.Idle);
        }
    }

    /*
     *
     * ---------------------------------------------------------------------------------
     * BELOW ARE ALL WALL RELATED FUNCTIONS
     * ---------------------------------------------------------------------------------
     *
     */



    /*
     * Start Climb. Set initial climb speed based on current momentum. Between zero and ClimbSpeed.
     */
    private float currentClimbSpeed = 0f;
    void StartClimb()
    {
        if (player.currentState == Player.MovementState.Prone || player.currentState == Player.MovementState.Crouch) return;
        currentClimbSpeed = rb.linearVelocity.magnitude * 1.7f;
        if (rb.linearVelocity.y < 0f) currentClimbSpeed = 0f;
        else if (currentClimbSpeed > player.ClimbSpeed) currentClimbSpeed = player.ClimbSpeed;
        player.SetState(Player.MovementState.Climb);
    }

    /*
     * Climb. Move player upwards at currentClimbSpeed.
     */
    void Climb()
    {
        // As they lose momentum
        if (currentClimbSpeed <= 3f)
        {
            // Can't fall faster than wallSlideDownMax
            if (rb.linearVelocity.y < -player.WallSlideDownMax)
            {
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, -player.WallSlideDownMax, rb.linearVelocity.z);
            }
            return; // Don't continue climbing
        }
        // Move upwards at current climb speed, then reduce climb speed
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, currentClimbSpeed, rb.linearVelocity.z);
        currentClimbSpeed -= 0.5f;
    }

    void EndClimb()
    {
        player.SetState(Player.MovementState.Idle);
    }
    
    /*
     * Start Wall Run. Disable gravity and set IsWallRunning to true.
     */
    void StartWallRun(Vector3 wall)
    {
        
        if (player.currentState == Player.MovementState.Prone || player.currentState == Player.MovementState.Crouch) return;
        // Can't reuse the same wall right away
        if (wall == lastWallNormal) return;
        player.SetState(Player.MovementState.WallRun);
        rb.useGravity = false;
        lastWallNormal = currentWallNormal;

        // Add inital force to negate any falling
        if (timeOnWall == 0)
            rb.AddForce(transform.up * (rb.linearVelocity.y / -2f), ForceMode.Impulse);
    }

    /*
     * Wall Run. Stick to wall and move upwards slightly, losing upward momentum over time.
     */
    void WallRun()
    {
        timeOnWall += 1 * Time.deltaTime;

        // Stick to wall
        if (wallLeft)
            rb.AddForce(-transform.right * 10); // * Time.deltaTime);
        else
            rb.AddForce(transform.right * 10); // * Time.deltaTime);

        // The force pulling up on the player. At first it's zero, then exponentially decreases
        float pullForce = -1f * Mathf.Pow(4f, timeOnWall - 1f) - 2f;
        rb.AddForce(transform.up * pullForce); // * Time.deltaTime);

        // Can't fall faster than wallSlideDownMax
        if (rb.linearVelocity.y < -player.WallSlideDownMax)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, -player.WallSlideDownMax, rb.linearVelocity.z);
        }
    }

    /*
     * Stop Wall Run. Re-enable gravity and set IsWallRunning to false.
     */
    void StopWallRun()
    {
        player.SetState(Player.MovementState.Idle);
        rb.useGravity = true;
        timeOnWall = 0f;
    }


    /*
     *
     * ---------------------------------------------------------------------------------
     * BELOW ARE ALL GRAB FUNCTIONS
     * ---------------------------------------------------------------------------------
     *
     */


    /*
     * Grab ledge or object in front of player.
     */
    private Vector3 ledgePosition;
    private Vector3 ledgeNormal;
    private readonly float ledgeOffset = -1f;
    void Grab()
    {
        if (!PickUpObject() && !onGround) // Try to pick up an object first
            CheckForLedge(); // If no object, check for ledge
    }

    /*
     * Check for a grabbable object in front of the player and pick it up if found
     */
    private GameObject heldObject; // Currently held object
    private Rigidbody heldObjectRb; // Rigidbody of held object
    private bool PickUpObject()
    {
        // Use camera's forward direction for the raycast
        if (cam == null) return false;

        Vector3 origin = cam.transform.position;
        Vector3 direction = cam.transform.forward;

        // Debug ray to see pickup range
        //Debug.DrawRay(origin, direction * 4f, Color.yellow, 5f);

        if (Physics.Raycast(origin, direction, out RaycastHit objectHit, 4f))
        {
            Debug.Log("Raycast hit: " + objectHit.collider.name);
            // Check if the object has a "Grabbable" tag
            if (objectHit.collider.CompareTag("Grabbable"))
            {
                GameObject grabbedObject = objectHit.collider.gameObject;
                
                if (grabbedObject.TryGetComponent<Rigidbody>(out var objectRb))
                {
                    // Store references
                    heldObject = grabbedObject;
                    heldObjectRb = objectRb;
                    player.IsHolding = true;

                    // Configure held object physics
                    heldObjectRb.useGravity = false;
                    heldObjectRb.isKinematic = true;   // Disable physics simulation
                    heldObjectRb.detectCollisions = false; // Optional: prevents weird physics bumps while held
                    heldObjectRb.linearDamping = 10f; // High damping to prevent jittering
                    heldObjectRb.constraints = RigidbodyConstraints.FreezeRotation; // Optional: prevent rotation

                    // Parent to throw point
                    if (throwPoint != null)
                    {
                        heldObject.transform.parent = throwPoint;
                        // Move to throw point position
                        heldObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    }
                    else
                    {
                        Debug.LogWarning("ThrowPoint not assigned! Object will not be positioned correctly.");
                    }

                    // Sync over network if needed
                    if (view.IsMine)
                    {
                        view.RPC("RPC_SetHeldObject", RpcTarget.Others, grabbedObject.GetComponent<PhotonView>().ViewID);
                    }

                    Debug.Log($"Picked up: {grabbedObject.name}");
                    return true;
                }
            }
        }
        return false;
    }

    /*
     * Drop or throw the currently held object
     */
    private void DropHeldObject()
    {
        if (heldObject != null && heldObjectRb != null)
        {
            // Restore physics state
            heldObjectRb.useGravity = true;
            heldObjectRb.isKinematic = false;   // Disable physics simulation
            heldObjectRb.detectCollisions = true; // Optional: prevents weird physics bumps while held
            heldObjectRb.linearDamping = 0f;
            heldObjectRb.constraints = RigidbodyConstraints.None;

            // Unparent (preserve world position)
            heldObject.transform.parent = null;

            // Add throwing force (optional)
            Camera playerCam = Camera.main;
            if (playerCam != null)
            {
                heldObjectRb.AddForce(playerCam.transform.forward * 5f, ForceMode.Impulse);
            }

            // Sync over network if needed
            if (view.IsMine)
            {
                view.RPC("RPC_DropHeldObject", RpcTarget.Others);
            }

            // Clear references
            heldObject = null;
            heldObjectRb = null;
            player.IsHolding = false;

            Debug.Log("Dropped held object");
        }
    }

    // Networking RPCs for object syncing
    [PunRPC]
    private void RPC_SetHeldObject(int objectViewId)
    {
        PhotonView objectView = PhotonView.Find(objectViewId);
        if (objectView != null)
        {
            GameObject syncedObject = objectView.gameObject;
            heldObject = syncedObject;
            heldObjectRb = syncedObject.GetComponent<Rigidbody>();
            player.IsHolding = true;

            if (throwPoint != null)
            {
                syncedObject.transform.parent = throwPoint;
                syncedObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }
        }
    }

    [PunRPC]
    private void RPC_DropHeldObject()
    {
        if (heldObject != null)
        {
            heldObject.transform.parent = null;
            if (heldObjectRb != null)
            {
                heldObjectRb.useGravity = true;
                heldObjectRb.linearDamping = 0f;
                heldObjectRb.constraints = RigidbodyConstraints.None;
            }
            heldObject = null;
            heldObjectRb = null;
            player.IsHolding = false;
        }
    }

    /*
     * Detects if there's a valid ledge in front of the player
     */
    private void CheckForLedge()
    {

        Vector3 origin = transform.position; // At foot level
        Vector3 forward = transform.forward;

        // 1. Check if there's a wall in front
        if (!Physics.Raycast(origin, forward, out RaycastHit wallHit, 3f, structureLayer))
            return;
        //Debug.DrawRay(origin, forward * 3f, Color.red);

        // 2. Check if there's a horizontal surface above the wall hit point
        Vector3 ledgeCheckOrigin = wallHit.point + Vector3.up * 6.2f + forward * 0.1f;
        //Debug.DrawRay(ledgeCheckOrigin, Vector3.down * ledgeCheckDistance, Color.yellow, 80f);
        if (!Physics.Raycast(ledgeCheckOrigin, Vector3.down, out RaycastHit ledgeHit, 6.4f, structureLayer))
            return;

        // 2.5. Verify it's reasonably horizontal (normal points upward)
        if (Vector3.Dot(ledgeHit.normal, Vector3.up) < 0.7f)
            return;

        // 3. Check if there's empty space above the ledge (so player can climb up)
        Vector3 spaceCheckOrigin = ledgeHit.point + Vector3.up * 0.1f;
        //Debug.DrawLine(spaceCheckOrigin, spaceCheckOrigin + Vector3.up * 1.5f, Color.green, 80f);
        if (Physics.Raycast(spaceCheckOrigin, Vector3.up, 1.5f, structureLayer))
            return;

        // 4. Check ledge width (optional but prevents grabbing thin edges)
        if (!CheckLedgeWidth(ledgeHit.point, wallHit.normal))
            return;
            
        // Valid ledge found! 
        GrabLedge(ledgeHit.point, wallHit.normal);
    }
    
    /*
    * Checks if the ledge is wide enough to grab (by 0.6f on both sides)
    */
    private bool CheckLedgeWidth(Vector3 ledgePoint, Vector3 wallNormal)
    {
        Vector3 right = Vector3.Cross(Vector3.up, wallNormal);
        
        // Check both sides
        bool leftCheck = Physics.Raycast(ledgePoint + Vector3.up * 0.5f - right * 0.6f, Vector3.down, 1f, structureLayer);
        bool rightCheck = Physics.Raycast(ledgePoint + Vector3.up * 0.5f - right * 0.6f, Vector3.down, 1f, structureLayer);
        //Debug.DrawRay(ledgePoint + Vector3.up * 0.5f - right * 0.6f, Vector3.down * 1f, Color.green, 80f);
        //Debug.DrawRay(ledgePoint + Vector3.up * 0.5f + right * 0.6f, Vector3.down * 1f, Color.green, 80f);
        
        return leftCheck && rightCheck;
    }

    /*
    * Initiates the ledge grab
    */
    private void GrabLedge(Vector3 ledge, Vector3 normal)
    {
        player.SetState(Player.MovementState.Hang);
        ledgePosition = ledge;
        ledgeNormal = normal;

        // Stop physics
        rb.linearVelocity = Vector3.zero;
        rb.useGravity = false;

        // Position player at hang position
        Vector3 hangPos = ledgePosition - normal * ledgeOffset - Vector3.up * 1.8f;
        //Debug.DrawRay(hangPos, Vector3.up * 2f, Color.blue, 60f);
        transform.position = hangPos;
        //transform.forward = -normal;

        // Sync over network
        if (view.IsMine)
        {
            view.RPC("RPC_SetHangingState", RpcTarget.Others, true, ledgePosition, ledgeNormal);
        }
    }
    
    /*
    * Handles player input while hanging
    *
    * NEED TO HANDLE WALLS ON THE SIDES & CURVING LEDGES
    */
    private void HandleHangingState()
    {
        // Don't move this to input handler
        if (Input.GetKeyDown(KeyCode.W))
        {
            ClimbUp();
        }
        else if (Input.GetKeyDown(KeyCode.S))
        {
            LetGo();
        }
        
        // Keeps transform at ledge position
        Vector3 newPosition = ledgePosition - ledgeNormal * ledgeOffset - Vector3.up * 1.8f;
        transform.position = Vector3.Lerp(transform.position, newPosition, Time.deltaTime * 10f);

        // Prevent shimmying past the ledge by verifying there is still a valid ledge below
        float horizontal = Input.GetAxis("Horizontal");
        if (Mathf.Abs(horizontal) > 0.01f)
        {
            Vector3 left = Vector3.Cross(Vector3.up, ledgeNormal);

            // Use shimmySpeed for movement amount
            float moveAmount = -horizontal * player.ShimmySpeed * Time.deltaTime;
            Vector3 proposedLedge = ledgePosition + left * moveAmount;

            // Add a buffer so player can't get too close to the edge
            float buffer = 1f; // Minimum distance from edge
            Vector3 checkOrigin = proposedLedge + Vector3.up * 0.5f - left * buffer;
            bool hasSupport = Physics.Raycast(checkOrigin, Vector3.down, out RaycastHit supportHit, 1.0f, structureLayer);

            // Ensure the surface below is reasonably horizontal (prevent shimmying onto walls)
            if (hasSupport && Vector3.Dot(supportHit.normal, Vector3.up) >= 0.7f)
            {
                ledgePosition = proposedLedge;
            }
            // else: no valid ledge at proposed position
        }
    }
    
    /*
    * Climbs up onto the ledge
    */
    private void ClimbUp()
    {
        // Move player up and forward
        Vector3 climbTarget = ledgePosition + Vector3.up * 0.5f - ledgeNormal * 0.3f;
        StartCoroutine(ClimbCoroutine(climbTarget));
    }
    
    private System.Collections.IEnumerator ClimbCoroutine(Vector3 target)
    {
        float elapsed = 0f;
        float duration = 0.5f;
        Vector3 startPos = transform.position;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(startPos, target, elapsed / duration);
            yield return null;
        }
        
        ReleaseLedge();
    }
    
    /*
    * Lets go of the ledge
    */
    private void LetGo()
    {
        Debug.Log("Letting go of ledge");
        ReleaseLedge();
        rb.linearVelocity = -ledgeNormal * 2f; // Push away from wall
    }
    
    /*
    * Releases the ledge grab state
    */
    private void ReleaseLedge()
    {
        player.SetState(Player.MovementState.Idle);
        rb.useGravity = true;
        
        // Sync over network
        if (view.IsMine)
        {
            view.RPC("RPC_SetHangingState", RpcTarget.Others, false, Vector3.zero, Vector3.zero);
        }
    }
    
    // Photon RPC for syncing hang state
    [PunRPC]
    private void RPC_SetHangingState(bool hanging, Vector3 ledgePos, Vector3 ledgeNorm)
    {
        player.currentState = hanging ? Player.MovementState.Hang : Player.MovementState.Idle;
        ledgePosition = ledgePos;
        ledgeNormal = ledgeNorm;
        rb.useGravity = !hanging;
    }
}