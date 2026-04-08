
using UnityEngine;
using DG.Tweening;
using Photon.Pun;

public class PlayerCam : MonoBehaviour
{
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

    [Header("References")]
    public Player player;
    public JimmyMove pm;
    public Transform playerTransform;
    public Transform camHolder;

    [Header("Camera Settings")]
    [SerializeField] private float _sensitivity;
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

    public Vector3 normalCameraOffset = new(0, 2f, 0);
    public Vector3 proneCameraOffset = new(0, 1f, 0);
    private Vector3 currentCameraOffset;
    private Vector3 cameraVelocity = Vector3.zero;

    public float positionSmoothTime = 0.1f;
    public float rotationSmoothTime = 80f;

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
        if (PauseMenuManager.Instance != null && PauseMenuManager.Instance.Paused) return;

        float mouseX = Input.GetAxisRaw("Mouse X") * (sensitivity / 100f);
        float mouseY = Input.GetAxisRaw("Mouse Y") * (sensitivity / 100f);

        yRotation += mouseX;
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        // Smooth camera offset (e.g., between standing/prone)
        transform.localPosition = Vector3.SmoothDamp(
            transform.localPosition,
            currentCameraOffset,
            ref cameraVelocity,
            positionSmoothTime
        );

        // Smooth camera rotation
        Quaternion targetRotation = Quaternion.Euler(xRotation, yRotation, 0);
        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            targetRotation,
            1f - Mathf.Exp(-rotationSmoothTime * Time.deltaTime)
        );

        // Rotate player body horizontally
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