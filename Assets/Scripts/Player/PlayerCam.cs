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
    public Vector3 proneCameraOffset = new(0, 1f, 0);
    private Vector3 currentCameraOffset;
    private Vector3 cameraVelocity = Vector3.zero;

    public float positionSmoothTime = 0.1f;
    // rotationSmoothTime feeds into an exponential smoothing formula, not a direct lerp speed
    public float rotationSmoothTime = 80f;

    // Accumulated euler angles; xRotation is pitch (clamped), yRotation is yaw
    private float xRotation;
    private float yRotation;

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

        // Subscribe to Player state changes
        if (player != null)
        {
            player.OnStateChanged += HandlePlayerStateChanged;
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe from settings changes
        SettingsManager.OnSensitivityChanged -= HandleSensitivityChanged;
        if (player != null)
        {
            player.OnStateChanged -= HandlePlayerStateChanged;
        }
        DOTween.KillAll();
    }

    // Event handler for sensitivity changes
    private void HandleSensitivityChanged(float newSensitivity)
    {
        sensitivity = newSensitivity;
    }

    private void LateUpdate()
    {
        if (!view.IsMine) return;

        bool paused = PauseMenuManager.Instance != null && PauseMenuManager.Instance.Paused;

        // Re-lock cursor every frame when not paused. This handles the frame after Resume()
        // where the EventSystem or editor may have left the cursor in the wrong state.
        if (!paused && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        if (paused) return;

        // Normalize sensitivity: 100 = 1:1 mouse delta
        float mouseX = Input.GetAxisRaw("Mouse X") * (sensitivity / 100f);
        float mouseY = Input.GetAxisRaw("Mouse Y") * (sensitivity / 100f);

        yRotation += mouseX;
        xRotation -= mouseY;                               // Subtract so moving mouse up pitches camera up
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);    // Prevent over-rotation past vertical

        // Smooth camera height when switching stances (e.g. prone lowers the camera)
        transform.localPosition = Vector3.SmoothDamp(
            transform.localPosition,
            currentCameraOffset,
            ref cameraVelocity,
            positionSmoothTime
        );

        // Exponential smoothing: higher rotationSmoothTime = snappier feel
        Quaternion targetRotation = Quaternion.Euler(xRotation, yRotation, 0);
        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            targetRotation,
            1f - Mathf.Exp(-rotationSmoothTime * Time.deltaTime)
        );

        // Player body only rotates horizontally; vertical look stays on the camera
        playerTransform.rotation = Quaternion.Euler(0, yRotation, 0);
    }

    // Event handler for player state changes
    private void HandlePlayerStateChanged(Player.MovementState newState)
    {
        if (!view.IsMine) return;
        currentCameraOffset = newState == Player.MovementState.Prone
            ? proneCameraOffset
            : normalCameraOffset;
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