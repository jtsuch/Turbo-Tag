using UnityEngine;
using DG.Tweening;
using Photon.Pun;

/// <summary>
/// First-person camera controller. Handles mouse-look with smoothing, cursor lock management,
/// camera offset transitions between stances (standing/prone), and FOV tweens.
/// Attach to: CameraHolder child of ThePlayer prefab — must reference the player's Transform
/// and a PhotonView so it only runs on the owning client.
/// </summary>
public class PlayerCam : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    private static PlayerCam _instance;
    public static PlayerCam Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindFirstObjectByType<PlayerCam>();
            return _instance;
        }
        private set => _instance = value;
    }

    // ─── References ───────────────────────────────────────────────────────────
    [Header("References")]
    public Player player;
    public JimmyMove pm;
    public Transform playerTransform;
    public Transform camHolder;

    // ─── Sensitivity ──────────────────────────────────────────────────────────
    [Header("Camera Settings")]
    [SerializeField] private float _sensitivity;
    // Property fires an event so any UI sliders can react immediately
    public float sensitivity
    {
        get => _sensitivity;
        private set
        {
            _sensitivity = value;
            OnSensitivityChange?.Invoke(value);
        }
    }
    public static event System.Action<float> OnSensitivityChange;

    // ─── Camera Offsets / Smoothing ───────────────────────────────────────────
    public Vector3 normalCameraOffset = new(0, 2f, 0);
    public Vector3 crouchCameraOffset = new(0, 1.4f, 0);
    public Vector3 proneCameraOffset  = new(0, 1f, 0);
    private Vector3 currentCameraOffset;
    private Vector3 cameraVelocity = Vector3.zero;

    public float positionSmoothTime = 0.1f;
    // rotationSmoothTime feeds into an exponential smoothing formula, not a direct lerp speed
    public float rotationSmoothTime = 80f;

    // Accumulated euler angles; xRotation is pitch (clamped), yRotation is yaw
    private float xRotation;
    private float yRotation;

    // ─── Head Bob ─────────────────────────────────────────────────────────────
    [Header("Head Bob")]
    public float walkBobFreq    = 2.5f;
    public float walkBobAmplY   = 0.18f;
    public float walkBobAmplX   = 0.10f;
    public float crouchBobFreq  = 1.8f;
    public float crouchBobAmplY = 0.08f;
    public float crouchBobAmplX = 0.04f;

    private float   bobTimer  = 0f;
    private Vector3 bobOffset = Vector3.zero;

    // ─── Jump / Land Bump ─────────────────────────────────────────────────────
    [Header("Jump / Land")]
    public float jumpBumpAmount   = -0.12f;
    public float landBumpAmount   = -0.18f;
    public float bumpRecoverSpeed = 12f;

    private float bumpOffset = 0f;

    // ─── Wall Run Tilt ────────────────────────────────────────────────────────
    [Header("Wall Run Tilt")]
    public float wallTiltAngle = 8f;
    public float wallTiltSpeed = 8f;

    private float currentTilt = 0f;

    // ─── Caught Spectator Camera ──────────────────────────────────────────────
    private bool       caughtMode;
    private Transform  camOriginalParent;
    private Vector3    camOriginalLocalPos;
    private Quaternion camOriginalLocalRot;

    public PhotonView view;

    public Camera cam;
    private void Awake()
    {
        Instance = this;
        //cam = GetComponentInChildren<Camera>();
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        currentCameraOffset = normalCameraOffset;

        // Subscribe to Settings changes
        if (SettingsManager.Instance != null)
        {
            sensitivity = SettingsManager.Instance.Sensitivity;
            SettingsManager.OnSensitivityChanged += HandleSensitivityChanged;
        }
        else
        {
            Debug.LogWarning("SettingsManager not found! Falling back to default sensitivity.");
            sensitivity = 100f;
        }

        // Subscribe to Player state changes and events
        if (player != null)
        {
            player.OnStateChanged += HandlePlayerStateChanged;
            player.OnJump         += HandleJump;
            player.OnLand         += HandleLand;
        }
    }

    private void OnDestroy()
    {
        SettingsManager.OnSensitivityChanged -= HandleSensitivityChanged;
        if (player != null)
        {
            player.OnStateChanged -= HandlePlayerStateChanged;
            player.OnJump         -= HandleJump;
            player.OnLand         -= HandleLand;
        }
        DOTween.KillAll();
    }

    private void HandleJump() => bumpOffset = jumpBumpAmount;
    private void HandleLand() => bumpOffset = landBumpAmount;

    // Event handler for sensitivity changes
    private void HandleSensitivityChanged(float newSensitivity)
    {
        sensitivity = newSensitivity;
    }

    public void EnterCaughtMode()
    {
        if (cam == null) return;
        caughtMode          = true;
        camOriginalParent   = cam.transform.parent;
        camOriginalLocalPos = cam.transform.localPosition;
        camOriginalLocalRot = cam.transform.localRotation;
        cam.transform.SetParent(null, true);
    }

    public void ExitCaughtMode()
    {
        if (cam == null) return;
        caughtMode = false;
        cam.transform.SetParent(camOriginalParent, false);
        cam.transform.SetLocalPositionAndRotation(camOriginalLocalPos, camOriginalLocalRot);
    }

    private void LateUpdate()
    {
        if (!view.IsMine) return;

        bool paused = PauseMenuManager.Instance != null && PauseMenuManager.Instance.Paused;

        // Re-lock cursor every frame when not paused.
        if (!paused && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        if (paused) return;

        // Spectator camera while caught — orbits above the player for 3 s until respawn
        if (caughtMode && cam != null && player != null)
        {
            float    t          = Time.deltaTime * 5f;
            Vector3  targetPos  = player.transform.position + Vector3.up * 4f;
            Vector3  newPos     = Vector3.Lerp(cam.transform.position, targetPos, t);
            Quaternion newRot   = Quaternion.Lerp(cam.transform.rotation, Quaternion.Euler(75f, player.transform.eulerAngles.y, 0f), t);
            cam.transform.SetPositionAndRotation(newPos, newRot);
            return;
        }

        // Normalize sensitivity: 100 = 1:1 mouse delta
        float mouseX = Input.GetAxisRaw("Mouse X") * (sensitivity / 100f);
        float mouseY = Input.GetAxisRaw("Mouse Y") * (sensitivity / 100f);

        yRotation += mouseX;
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        // Stance height (standing / crouch / prone)
        transform.localPosition = Vector3.SmoothDamp(
            transform.localPosition,
            currentCameraOffset,
            ref cameraVelocity,
            positionSmoothTime);

        // Camera effects
        UpdateHeadBob();
        UpdateWallTilt();
        bumpOffset = Mathf.Lerp(bumpOffset, 0f, Time.deltaTime * bumpRecoverSpeed);

        if (cam != null)
            cam.transform.localPosition = bobOffset + Vector3.up * bumpOffset;

        // Rotation with wall tilt on Z
        Quaternion targetRotation = Quaternion.Euler(xRotation, yRotation, currentTilt);
        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            targetRotation,
            1f - Mathf.Exp(-rotationSmoothTime * Time.deltaTime));

        playerTransform.rotation = Quaternion.Euler(0, yRotation, 0);
    }

    private void UpdateHeadBob()
    {
        var state = player.currentState;

        // Use input rather than velocity — velocity stays non-zero for several frames after
        // releasing keys (friction braking), which would trigger the bob while "standing still".
        bool hasInput = player.Input != null && player.Input.MoveInput.magnitude > 0.1f;

        bool shouldBob = hasInput
                      && player.IsGrounded
                      && state != Player.MovementState.WallRun
                      && state != Player.MovementState.Climb
                      && state != Player.MovementState.Hang;

        if (!shouldBob)
        {
            bobTimer  = 0f;
            bobOffset = Vector3.Lerp(bobOffset, Vector3.zero, Time.deltaTime * 10f);
            return;
        }

        bool crouched = state == Player.MovementState.Crouch || state == Player.MovementState.Prone;
        float freq  = crouched ? crouchBobFreq  : walkBobFreq;
        float amplY = crouched ? crouchBobAmplY : walkBobAmplY;
        float amplX = crouched ? crouchBobAmplX : walkBobAmplX;

        bobTimer += Time.deltaTime * freq;
        bobOffset = new Vector3(
            Mathf.Sin(bobTimer)        * amplX,
            Mathf.Abs(Mathf.Sin(bobTimer * 2f)) * amplY,
            0f);
    }

    private void UpdateWallTilt()
    {
        float target = player.currentState == Player.MovementState.WallRun
            ? (pm.wallRight ? wallTiltAngle : -wallTiltAngle)
            : 0f;
        currentTilt = Mathf.Lerp(currentTilt, target, Time.deltaTime * wallTiltSpeed);
    }

    // Event handler for player state changes
    private void HandlePlayerStateChanged(Player.MovementState newState)
    {
        if (!view.IsMine) return;
        currentCameraOffset = newState switch
        {
            Player.MovementState.Prone  => proneCameraOffset,
            Player.MovementState.Crouch => crouchCameraOffset,
            _                           => normalCameraOffset,
        };
    }

    public void ChangeFov(float endValue)
    {
        if (cam != null)
            cam.DOFieldOfView(endValue, 0.25f);
    }

    private void UpdateSensitivityFromManager(float newSens)
    {
        sensitivity = newSens;
    }

    public void SetSensitivity(float newSensitivity)
    {
        // Update the SettingsManager which will trigger the OnSensitivityChanged event
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.Sensitivity = newSensitivity;
        }
        else
        {
            sensitivity = newSensitivity;
        }
    }
}