using DG.Tweening;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// First-person camera for the local player: mouse-look with smoothing, stance height changes,
/// head bob, jump/land bumps, wall-run tilt, FOV tweens and a spectator view while caught.
/// Camera offsets scale with the player's size so shrinking also lowers the view.
/// Attach to: CameraHolder child of ThePlayer prefab. Disabled on remote player instances.
/// </summary>
public class PlayerCam : MonoBehaviour
{
    // ─── Singleton (local player only) ────────────────────────────────────────
    private static PlayerCam _local;
    public static PlayerCam Local => _local;

    // ─── References ───────────────────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private Player         player;
    [SerializeField] private PlayerMovement pm;
    [SerializeField] private Transform      playerTransform;
    [SerializeField] private PhotonView     view;
    [SerializeField] private Camera         cam;

    // ─── Stance Offsets / Smoothing ───────────────────────────────────────────
    [Header("Camera Settings")]
    [SerializeField] private Vector3 normalCameraOffset = new(0, 2f, 0);
    [SerializeField] private Vector3 crouchCameraOffset = new(0, 1.4f, 0);
    [SerializeField] private Vector3 proneCameraOffset  = new(0, 1f, 0);
    [SerializeField] private float   positionSmoothTime = 0.1f;
    [Tooltip("How high the caught spectator view floats above the ragdoll (m).")]
    [SerializeField] private float   caughtCameraHeight = 8f;
    [Tooltip("Exponential smoothing rate for rotation (higher = snappier).")]
    [SerializeField] private float   rotationSmoothTime = 80f;
    [Tooltip("How fast the body swings back upright after a Free Spin (higher = snappier).")]
    [SerializeField] private float   spinRecoverSpeed = 12f;

    // ─── Head Bob ─────────────────────────────────────────────────────────────
    [Header("Head Bob")]
    [SerializeField] private float walkBobFreq    = 2.5f;
    [SerializeField] private float walkBobAmplY   = 0.18f;
    [SerializeField] private float walkBobAmplX   = 0.10f;
    [SerializeField] private float crouchBobFreq  = 1.8f;
    [SerializeField] private float crouchBobAmplY = 0.08f;
    [SerializeField] private float crouchBobAmplX = 0.04f;

    // ─── Jump / Land Bump ─────────────────────────────────────────────────────
    [Header("Jump / Land")]
    [SerializeField] private float jumpBumpAmount   = -0.12f;
    [SerializeField] private float landBumpAmount   = -0.18f;
    [SerializeField] private float bumpRecoverSpeed = 12f;

    // ─── Wall Run Tilt ────────────────────────────────────────────────────────
    [Header("Wall Run Tilt")]
    [SerializeField] private float wallTiltAngle = 8f;
    [SerializeField] private float wallTiltSpeed = 8f;

    // ─── Runtime ──────────────────────────────────────────────────────────────
    public Camera Camera     => cam;
    public float  DefaultFov { get; private set; } = 60f;

    private float   sensitivity = SettingsManager.DefaultSensitivity;
    private Vector3 currentCameraOffset;
    private Vector3 cameraVelocity;
    private float   xRotation, yRotation;
    private float   bobTimer;
    private Vector3 bobOffset;
    private float   bumpOffset;
    private float   currentTilt;

    private bool       caughtMode;
    private Transform  camOriginalParent;
    private Vector3    camOriginalLocalPos;
    private Quaternion camOriginalLocalRot;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (view != null && !view.IsMine)
        {
            enabled = false;
            return;
        }
        _local = this;
    }

    private void Start()
    {
        if (!enabled) return;

        currentCameraOffset = normalCameraOffset;
        yRotation = playerTransform.eulerAngles.y;
        if (cam != null) DefaultFov = cam.fieldOfView;

        if (SettingsManager.Instance != null) sensitivity = SettingsManager.Instance.Sensitivity;
        SettingsManager.SensitivityChanged += HandleSensitivityChanged;

        player.StateChanged += HandlePlayerStateChanged;
        player.Jumped       += HandleJump;
        player.Landed       += HandleLand;
        player.Caught       += EnterCaughtMode;
        player.Respawned    += ExitCaughtMode;
    }

    private void OnDestroy()
    {
        if (_local == this) _local = null;
        SettingsManager.SensitivityChanged -= HandleSensitivityChanged;

        if (player != null)
        {
            player.StateChanged -= HandlePlayerStateChanged;
            player.Jumped       -= HandleJump;
            player.Landed       -= HandleLand;
            player.Caught       -= EnterCaughtMode;
            player.Respawned    -= ExitCaughtMode;
        }
        if (cam != null) cam.DOKill();
    }

    private void LateUpdate()
    {
        bool paused = PauseMenuManager.IsPaused;

        // Re-lock every frame when not paused (avoids same-frame races with the EventSystem)
        if (!paused && (Cursor.lockState != CursorLockMode.Locked || Cursor.visible))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        if (caughtMode)
        {
            UpdateCaughtCamera();
            return;
        }

        // Looking through something else (a placed Camera): the mouse turns that, and the view sits on it
        if (viewOverride != null)
        {
            if (!paused)
            {
                float scale = sensitivity / 100f;
                overrideLook?.Invoke(Input.GetAxisRaw("Mouse X") * scale, Input.GetAxisRaw("Mouse Y") * scale);
            }
            if (cam != null) cam.transform.SetPositionAndRotation(viewOverride.position, viewOverride.rotation);
            return;
        }

        if (!paused) ReadMouse();

        float size = player.Size;
        transform.localPosition = Vector3.SmoothDamp(
            transform.localPosition, currentCameraOffset * size, ref cameraVelocity, positionSmoothTime);

        UpdateHeadBob(size);
        UpdateWallTilt();
        bumpOffset = Mathf.Lerp(bumpOffset, 0f, Time.deltaTime * bumpRecoverSpeed);

        if (cam != null)
            cam.transform.localPosition = bobOffset + Vector3.up * (bumpOffset * size);

        // Free Spin (hold R): the mouse turns the whole body on every axis (backflips, barrel turns)
        bool spinHeld = !paused && player.CanMove && Input.GetKey(Keybinds.Get(InputAction.FreeSpin));
        if (spinHeld)
        {
            if (!spinning) BeginSpin();
            UpdateSpin();
            return;
        }
        if (spinning) EndSpin();

        Quaternion targetRotation = Quaternion.Euler(xRotation, yRotation, currentTilt);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation,
            1f - Mathf.Exp(-rotationSmoothTime * Time.deltaTime));

        Quaternion upright = Quaternion.Euler(0f, yRotation, 0f);
        if (recoveringFromSpin)
        {
            // Swing back upright around the body's middle, quickly but not in one frame
            Quaternion next = Quaternion.Slerp(player.Body.rotation, upright, 1f - Mathf.Exp(-spinRecoverSpeed * Time.deltaTime));
            if (Quaternion.Angle(next, upright) < 1f) { next = upright; recoveringFromSpin = false; player.IsFreeSpinning = false; }
            RotateBodyAroundCenter(next);
        }
        else
        {
            playerTransform.rotation = upright;
        }
    }

    // ─── View override ────────────────────────────────────────────────────────

    private Transform                 viewOverride;
    private System.Action<float, float> overrideLook;

    /// <summary>
    /// Shows the view from <paramref name="viewPoint"/> instead of the player's head; mouse deltas (degrees,
    /// after sensitivity) go to <paramref name="look"/>. Null puts the camera back on the player.
    /// </summary>
    public void SetViewOverride(Transform viewPoint, System.Action<float, float> look = null)
    {
        viewOverride = viewPoint;
        overrideLook = viewPoint != null ? look : null;
        if (viewPoint == null && cam != null)
            cam.transform.SetLocalPositionAndRotation(bobOffset, Quaternion.identity);
    }

    /// <summary>True while the view comes from somewhere other than the player's head.</summary>
    public bool HasViewOverride => viewOverride != null;

    // ─── Free Spin ────────────────────────────────────────────────────────────

    private bool  spinning;
    private bool  recoveringFromSpin;
    private float spinCameraPitch; // The camera's pitch relative to the body, kept while spinning

    /// <summary>True while the player is free-spinning or swinging back upright afterwards.</summary>
    public bool IsFreeSpinning => spinning || recoveringFromSpin;

    private void BeginSpin()
    {
        player.IsFreeSpinning = true;
        spinning           = true;
        recoveringFromSpin = false;
        spinCameraPitch    = xRotation;
    }

    private void UpdateSpin()
    {
        float scale = sensitivity / 100f;
        float yaw   = Input.GetAxisRaw("Mouse X") * scale;
        float pitch = Input.GetAxisRaw("Mouse Y") * scale;

        // Local axes: looking up pitches the body backward, so keep looking up and it flips over
        RotateBodyAroundCenter(player.Body.rotation * Quaternion.Euler(-pitch, yaw, 0f));
        transform.rotation = playerTransform.rotation * Quaternion.Euler(spinCameraPitch, 0f, 0f);
    }

    // Let go: keep looking where the camera points, and swing the body back upright
    private void EndSpin()
    {
        spinning           = false;
        recoveringFromSpin = true;

        Vector3 look = transform.forward;
        Vector3 flat = Vector3.ProjectOnPlane(look, Vector3.up);
        if (flat.sqrMagnitude < 0.0001f) flat = Vector3.ProjectOnPlane(transform.up, Vector3.up) * -Mathf.Sign(look.y);
        if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
        yRotation = Quaternion.LookRotation(flat).eulerAngles.y;
        xRotation = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg, -90f, 90f);
    }

    // Turning around the feet (the root) would swing the body through the floor; turn around the middle.
    // Works from the Rigidbody's own pose and only nudges it by the pivot shift: the local body is
    // interpolated, so its transform trails the physics position, and copying the transform back each
    // frame dragged the player backwards (a slowed fall).
    private void RotateBodyAroundCenter(Quaternion rotation)
    {
        var        body         = player.Body;
        Vector3    centerOffset = Vector3.up * (player.Height * 0.5f);
        Vector3    pivotShift   = body.rotation * centerOffset - rotation * centerOffset;

        body.position += pivotShift;
        body.rotation  = rotation;
        playerTransform.rotation = rotation; // Show it this frame (interpolation catches up on position)
    }

    // ─── Look ─────────────────────────────────────────────────────────────────

    private void ReadMouse()
    {
        // Sensitivity 100 = raw 1:1 mouse delta
        float scale = sensitivity / 100f;
        yRotation += Input.GetAxisRaw("Mouse X") * scale;
        xRotation  = Mathf.Clamp(xRotation - Input.GetAxisRaw("Mouse Y") * scale, -90f, 90f);
    }

    private void UpdateHeadBob(float size)
    {
        var state = player.State;

        // Input, not velocity: velocity lingers for a few frames after releasing the keys.
        // No bob while sliding — the body is gliding, not stepping.
        bool shouldBob = player.Input.MoveInput.magnitude > 0.1f
                      && player.IsGrounded
                      && !pm.IsSliding
                      && state != Player.MovementState.WallRun
                      && state != Player.MovementState.Climb
                      && state != Player.MovementState.Hang;

        if (!shouldBob)
        {
            bobTimer  = 0f;
            bobOffset = Vector3.Lerp(bobOffset, Vector3.zero, Time.deltaTime * 10f);
            return;
        }

        bool  low   = state == Player.MovementState.Crouch || state == Player.MovementState.Prone;
        float freq  = low ? crouchBobFreq  : walkBobFreq;
        float amplY = (low ? crouchBobAmplY : walkBobAmplY) * size;
        float amplX = (low ? crouchBobAmplX : walkBobAmplX) * size;

        bobTimer += Time.deltaTime * freq;
        bobOffset = new Vector3(Mathf.Sin(bobTimer) * amplX, Mathf.Abs(Mathf.Sin(bobTimer * 2f)) * amplY, 0f);
    }

    private void UpdateWallTilt()
    {
        float target = player.State == Player.MovementState.WallRun
            ? (pm.WallRight ? wallTiltAngle : -wallTiltAngle)
            : 0f;
        currentTilt = Mathf.Lerp(currentTilt, target, Time.deltaTime * wallTiltSpeed);
    }

    // ─── Event Handlers ───────────────────────────────────────────────────────

    private void HandleJump() => bumpOffset = jumpBumpAmount;
    private void HandleLand() => bumpOffset = landBumpAmount;
    private void HandleSensitivityChanged(float value) => sensitivity = value;

    private void HandlePlayerStateChanged(Player.MovementState newState)
    {
        currentCameraOffset = newState switch
        {
            Player.MovementState.Prone  => proneCameraOffset,
            Player.MovementState.Crouch => crouchCameraOffset,
            _                           => normalCameraOffset,
        };
    }

    // ─── Caught Spectator View ────────────────────────────────────────────────

    private void EnterCaughtMode()
    {
        if (cam == null) return;
        caughtMode          = true;
        camOriginalParent   = cam.transform.parent;
        camOriginalLocalPos = cam.transform.localPosition;
        camOriginalLocalRot = cam.transform.localRotation;
        cam.transform.SetParent(null, true);
    }

    private void ExitCaughtMode()
    {
        if (cam == null || !caughtMode) return;
        caughtMode = false;
        cam.transform.SetParent(camOriginalParent, false);
        cam.transform.SetLocalPositionAndRotation(camOriginalLocalPos, camOriginalLocalRot);
        yRotation = playerTransform.eulerAngles.y;
    }

    // Floats above the ragdoll (or the player) looking down until respawn
    private void UpdateCaughtCamera()
    {
        if (cam == null) return;
        Transform  focus     = player.Corpse != null ? player.Corpse : playerTransform;
        float      t         = Time.deltaTime * 5f;
        Vector3    targetPos = focus.position + Vector3.up * caughtCameraHeight;
        Quaternion targetRot = Quaternion.Euler(75f, playerTransform.eulerAngles.y, 0f);
        cam.transform.SetPositionAndRotation(
            Vector3.Lerp(cam.transform.position, targetPos, t),
            Quaternion.Lerp(cam.transform.rotation, targetRot, t));
    }

    /// <summary>Points the view along <paramref name="yaw"/> (used after teleports).</summary>
    public void SnapTo(float yaw)
    {
        yRotation = yaw;
        xRotation = 0f;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    // ─── FOV ──────────────────────────────────────────────────────────────────

    public void ChangeFov(float endValue, float duration = 0.25f)
    {
        if (cam == null) return;
        cam.DOKill();
        cam.DOFieldOfView(endValue, duration);
    }

    public void ResetFov(float duration = 0.25f) => ChangeFov(DefaultFov, duration);
}
