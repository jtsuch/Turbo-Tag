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
    /// <summary>The resting field of view: the setting, times any effect's scale (Tunnel Vision). FOV kicks return here.</summary>
    public float  DefaultFov => baseFov * fovScale;
    private float baseFov  = 60f;
    private float fovScale = 1f;
    private float fovTween = 60f;   // The FOV kicks (dash, teleport) tween this; the camera adds the speed stretch on top
    private float speedFov;         // Extra degrees while moving fast

    /// <summary>0 at running speed, 1 when really flying (≈35 m/s): drives the FOV stretch, speed lines and wind.</summary>
    public float SpeedFeel { get; private set; }

    private float   sensitivity = SettingsManager.DefaultSensitivity;
    private Vector3 currentCameraOffset;
    private Vector3 cameraVelocity;
    private float   xRotation, yRotation;
    private float   bobTimer;
    private Vector3 bobOffset;
    private float   bumpOffset;
    private float   shake;       // Screen shake (0–1 trauma), decays each frame
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
        if (SettingsManager.Instance != null)
        {
            sensitivity = SettingsManager.Instance.Sensitivity;
            invertY     = SettingsManager.Instance.InvertY;
            HandleFovChanged(SettingsManager.Instance.FieldOfView);
        }
        else if (cam != null) baseFov = cam.fieldOfView;
        fovTween = DefaultFov;
        SettingsManager.SensitivityChanged += HandleSensitivityChanged;
        SettingsManager.FieldOfViewChanged += HandleFovChanged;
        SettingsManager.InvertYChanged     += HandleInvertChanged;

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
        SettingsManager.FieldOfViewChanged -= HandleFovChanged;
        SettingsManager.InvertYChanged     -= HandleInvertChanged;

        if (player != null)
        {
            player.StateChanged -= HandlePlayerStateChanged;
            player.Jumped       -= HandleJump;
            player.Landed       -= HandleLand;
            player.Caught       -= EnterCaughtMode;
            player.Respawned    -= ExitCaughtMode;
        }
        DOTween.Kill(this);
    }

    private void LateUpdate()
    {
        UpdateSpeedFeel();
        bool paused = PauseMenuManager.IsPaused;

        // The gun picker frees the mouse to click a gun, the admin console to scroll its log (and the view stops turning)
        if ((GunPicker.IsOpen && !paused) || AdminConsole.IsOpen)
        {
            if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
            }
            paused = true;
        }

        // Re-lock every frame when not paused (avoids same-frame races with the EventSystem)
        else if (!paused && (Cursor.lockState != CursorLockMode.Locked || Cursor.visible))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        // Looking through something else (a placed Camera, the killcam): the mouse turns that, and the view
        // sits on it. Checked before the caught view, so the killcam shows while caught.
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

        if (caughtMode)
        {
            UpdateCaughtCamera();
            return;
        }

        if (!paused) ReadMouse();
        ApplyLure();
        float adminRoll = ApplyAdminEffects();
        punchRoll = Mathf.Lerp(punchRoll, 0f, 1f - Mathf.Exp(-10f * Time.deltaTime));
        adminRoll += punchRoll;
        flipRoll = Mathf.MoveTowards(flipRoll, upsideDown ? 180f : 0f, 360f * Time.deltaTime);
        adminRoll += flipRoll;

        float size = player.Size;
        transform.localPosition = Vector3.SmoothDamp(
            transform.localPosition, currentCameraOffset * size, ref cameraVelocity, positionSmoothTime);

        UpdateHeadBob(size);
        UpdateWallTilt();
        bumpOffset = Mathf.Lerp(bumpOffset, 0f, Time.deltaTime * bumpRecoverSpeed);

        shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 1.8f);
        Vector3 shakeOffset = shake > 0f ? Random.insideUnitSphere * (shake * shake * 0.22f * size) : Vector3.zero;
        if (cam != null)
            cam.transform.localPosition = bobOffset + Vector3.up * (bumpOffset * size) + shakeOffset;

        // Free Spin (hold the Free Spin key, default 4): the mouse turns the whole body on every axis (backflips, barrel turns)
        bool spinHeld = !paused && player.CanMove && !IsLured && !EmoteMenu.IsOpen && !player.IsMagnetized && player.SurfaceFrame == Quaternion.identity
                     && Input.GetKey(Keybinds.Get(InputAction.FreeSpin));
        if (spinHeld)
        {
            if (!spinning) BeginSpin();
            UpdateSpin();
            return;
        }
        if (spinning) EndSpin();

        Quaternion frame = UpdateSurfaceFrame();
        Quaternion targetRotation = frame * Quaternion.Euler(xRotation, yRotation, currentTilt + adminRoll);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation,
            1f - Mathf.Exp(-rotationSmoothTime * Time.deltaTime));

        Quaternion upright = frame * Quaternion.Euler(0f, yRotation, 0f);
        if (frame != Quaternion.identity)
        {
            RotateBodyAroundCenter(upright); // Standing on a wall or ceiling (Wall Magnet), or turning back upright
        }
        else if (recoveringFromSpin)
        {
            // Swing back upright around the body's middle, quickly but not in one frame
            Quaternion next = Quaternion.Slerp(player.Body.rotation, upright, 1f - Mathf.Exp(-spinRecoverSpeed * Time.deltaTime));
            if (Quaternion.Angle(next, upright) < 1f) { next = upright; recoveringFromSpin = false; player.IsFreeSpinning = false; }
            RotateBodyAroundCenter(next);
        }
        else if (player.State == Player.MovementState.Hang && movement != null && movement.LedgeFacing.sqrMagnitude > 0.5f)
        {
            // Hanging: the body keeps facing the wall (the ledge animations), the view still turns all the way round
            Quaternion facing = Quaternion.LookRotation(movement.LedgeFacing, Vector3.up);
            playerTransform.rotation = Quaternion.Slerp(playerTransform.rotation, facing, 1f - Mathf.Exp(-14f * Time.deltaTime)); // Turns in, no snap
        }
        else
        {
            playerTransform.rotation = upright;
        }
    }

    private PlayerMovement movementCache;
    private PlayerMovement movement => movementCache != null ? movementCache : movementCache = player != null ? player.GetComponent<PlayerMovement>() : null;

    // ─── Lure (Fish Bait) ─────────────────────────────────────────────────────

    private Vector3 lurePoint;
    private float   lureUntil;

    /// <summary>True while something (Fish Bait) holds this player's gaze.</summary>
    public bool IsLured => Time.time < lureUntil;

    /// <summary>
    /// Locks the view onto <paramref name="point"/> for a moment; call it every frame to keep it locked. The mouse
    /// can't turn away, but movement still works (and "forward" is toward the point).
    /// </summary>
    public void Lure(Vector3 point)
    {
        lurePoint = point;
        lureUntil = Time.time + 0.15f;
    }

    private void ApplyLure()
    {
        if (!IsLured) return;
        Vector3 dir = lurePoint - transform.position;
        if (dir.sqrMagnitude < 0.01f) return;

        // In the frame of the surface being stood on (normally the world)
        dir = Quaternion.Inverse(player.SurfaceFrame) * dir.normalized;
        float yaw   = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg, -90f, 90f);
        float k     = 1f - Mathf.Exp(-14f * Time.deltaTime);
        yRotation = Mathf.LerpAngle(yRotation, yaw, k);
        xRotation = Mathf.Lerp(xRotation, pitch, k);
    }

    // ─── Admin teases (AdminCommands) ─────────────────────────────────────────

    private float dizzyUntil, spinUntil;
    private bool  upsideDown;
    private float flipRoll;

    /// <summary>Turns the view upside down (the Upside Down donut), rolling over smoothly.</summary>
    public void SetUpsideDown(bool on) => upsideDown = on;

    /// <summary>A wobbly, drifting, tilting view for <paramref name="seconds"/> (admin "dizzy").</summary>
    public void Dizzy(float seconds) => dizzyUntil = Time.time + Mathf.Clamp(seconds, 0f, 120f);

    /// <summary>The view spins round for <paramref name="seconds"/> (admin "spin").</summary>
    public void SpinAround(float seconds) => spinUntil = Time.time + Mathf.Clamp(seconds, 0f, 60f);

    public void StopAdminEffects() => dizzyUntil = spinUntil = 0f;

    // Returns the extra camera roll (degrees)
    private float ApplyAdminEffects()
    {
        float now = Time.time;
        if (now < spinUntil) yRotation += 540f * Time.deltaTime;
        if (now >= dizzyUntil) return 0f;

        float fade = Mathf.Clamp01((dizzyUntil - now) / 1.5f); // Sobers up over the last moment
        yRotation += Mathf.Sin(now * 1.3f) * 35f * fade * Time.deltaTime;
        xRotation  = Mathf.Clamp(xRotation + Mathf.Cos(now * 1.7f) * 18f * fade * Time.deltaTime, -90f, 90f);
        return Mathf.Sin(now * 0.9f) * 22f * fade;
    }

    // ─── Wall Magnet surface frame ────────────────────────────────────────────

    // Turns the view's "up" toward the surface being walked on (Wall Magnet) by the smallest turn each frame,
    // so the heading carries over smoothly. Back on normal gravity, the leftover turn around the vertical is
    // folded into the yaw and the frame resets to identity.
    private Quaternion UpdateSurfaceFrame()
    {
        Quaternion frame = player.SurfaceFrame;
        if (!player.IsMagnetized && frame == Quaternion.identity) return frame;

        Vector3    goalUp  = player.MagnetUp ?? Vector3.up;
        Vector3    frameUp = frame * Vector3.up;
        Quaternion goal    = Quaternion.FromToRotation(frameUp, goalUp) * frame;
        frame = Quaternion.Slerp(frame, goal, 1f - Mathf.Exp(-12f * Time.deltaTime));

        if (!player.IsMagnetized && Vector3.Angle(frame * Vector3.up, Vector3.up) < 0.5f)
        {
            Vector3 frameForward = Vector3.ProjectOnPlane(frame * Vector3.forward, Vector3.up);
            if (frameForward.sqrMagnitude > 0.0001f)
                yRotation += Vector3.SignedAngle(Vector3.forward, frameForward, Vector3.up);
            frame = Quaternion.identity;
        }
        player.SurfaceFrame = frame;
        return frame;
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
        if (viewPoint == null && cam != null && !caughtMode)
            cam.transform.SetLocalPositionAndRotation(bobOffset, Quaternion.identity);
    }

    /// <summary>Shakes the view (explosions, hard hits): 0–1, adds up, fades in about half a second.</summary>
    public void AddShake(float amount) => shake = Mathf.Clamp01(Mathf.Max(shake, 0f) + amount);

    /// <summary>
    /// Getting shot: the view jolts away from the shot (pitch and a quick roll toward the side it came from),
    /// harder for bigger hits, plus a little shake.
    /// </summary>
    public void Punch(Vector3 shotDirection, float damage)
    {
        float k = Mathf.Clamp01(damage / 60f);
        Vector3 local = transform.InverseTransformDirection(shotDirection);
        xRotation = Mathf.Clamp(xRotation - Mathf.Lerp(1.5f, 6f, k) * Mathf.Sign(local.z + 0.01f), -90f, 90f);
        punchRoll += Mathf.Lerp(4f, 12f, k) * -Mathf.Sign(local.x + 0.001f);
        AddShake(Mathf.Lerp(0.15f, 0.45f, k));
    }

    private float punchRoll; // Degrees, springs back to 0

    /// <summary>Kicks the view up by <paramref name="degrees"/> (gun recoil).</summary>
    public void AddRecoil(float degrees) => xRotation = Mathf.Clamp(xRotation - degrees, -90f, 90f);

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
        // The clip recorder sets the view directly
        if (SimInput.Active)
        {
            yRotation = SimInput.Yaw;
            xRotation = Mathf.Clamp(SimInput.Pitch, -90f, 90f);
            return;
        }

        // Sensitivity 100 = raw 1:1 mouse delta
        float scale = sensitivity / 100f;
        yRotation += Input.GetAxisRaw("Mouse X") * scale;
        float mouseY = Input.GetAxisRaw("Mouse Y") * (invertY ? -1f : 1f);
        xRotation  = Mathf.Clamp(xRotation - mouseY * scale, -90f, 90f);
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
    private void HandleInvertChanged(bool value)       => invertY = value;

    // The field of view setting: the resting FOV that FOV effects (dash, teleport) return to
    private void HandleFovChanged(float value)
    {
        baseFov = value;
        DOTween.Kill(this);
        fovTween = DefaultFov;
    }

    private bool invertY;

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

    // Going fast stretches the view a little (on top of the FOV setting and any kick)
    private void UpdateSpeedFeel()
    {
        float speed = player != null && player.Body != null && !player.IsCaught ? player.Body.linearVelocity.magnitude : 0f;
        float feel  = Mathf.InverseLerp(15f, 35f, speed);
        SpeedFeel   = Mathf.Lerp(SpeedFeel, feel, 1f - Mathf.Exp(-6f * Time.deltaTime));
        speedFov    = SpeedFeel * 12f;
        if (cam != null) cam.fieldOfView = fovTween + speedFov;
    }

    public void ChangeFov(float endValue, float duration = 0.25f)
    {
        if (cam == null) return;
        DOTween.Kill(this);
        DOTween.To(() => fovTween, v => fovTween = v, endValue, duration).SetTarget(this);
    }

    public void ResetFov(float duration = 0.25f) => ChangeFov(DefaultFov, duration);

    /// <summary>Scales the resting field of view (1 = the setting) and eases to it.</summary>
    public void SetFovScale(float scale)
    {
        fovScale = Mathf.Clamp(scale, 0.2f, 2f);
        ResetFov(0.4f);
    }
}
