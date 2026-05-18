using UnityEngine;
using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System;

/// <summary>
/// Central data hub for a single player: holds stats, movement state, health, and references
/// to all sibling components. Also acts as a singleton for the local player instance.
/// Attach to: ThePlayer prefab — requires JimmyMove, Rigidbody, PhotonView, InputHandler,
/// and PlayerAnimatorController on the same object.
/// </summary>
[RequireComponent(typeof(JimmyMove))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PhotonView))]
[RequireComponent(typeof(InputHandler))]
[RequireComponent(typeof(PlayerAnimatorController))]
public class Player : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    // Only the local player becomes the singleton; remote instances are separate objects.
    private static Player _instance;
    public static Player Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindFirstObjectByType<Player>();
            return _instance;
        }
        private set => _instance = value;
    }

    // ─── Identity / References ────────────────────────────────────────────────
    public string PlayerID { get; private set; }
    public string PlayerName { get; private set; }

    public JimmyMove Movement { get; private set; }
    public InputHandler Input { get; private set; }
    public PlayerAnimatorController Animator { get; private set; }
    public Rigidbody rb;

    // ─── Game Mode ────────────────────────────────────────────────────────────
    public bool isHunter;
    public int Score;

    // True only for the player instance owned by this client
    public bool IsLocalPlayer;

    // ─── Visual / Collider ────────────────────────────────────────────────────
    [Header("Visual")]
    [Tooltip("Mesh/rig roots to squish on crouch/prone. Assign Armature and Jimmy here.")]
    [SerializeField] private Transform[] visualRoots;

    private CapsuleCollider col;
    private float baseColHeight;
    private Vector3 baseColCenter;

    // ─── Character Attributes ─────────────────────────────────────────────────
    public float height = 1.8f;
    public int maxHealth = 100;
    private int currentHealth;

    // ─── Movement Speeds ──────────────────────────────────────────────────────
    public float WalkSpeed = 8f;
    public float SprintSpeed = 12f;
    public float CrouchSpeed = 5f;
    public float ProneSpeed = 3f;
    public float SlideSpeed = 15f;
    public float AirSpeed = 20f;
    public float ClimbSpeed = 30f;
    public float WallRunSpeed = 20f;
    public float ShimmySpeed = 4f;
    public float DashSpeed = 30f;
    public float SwingSpeed = 20f;

    // ─── Physics / Movement Modifiers ────────────────────────────────────────
    public float   SpeedMultiplier    = 1f;
    public float   CooldownMultiplier = 1f;  // Multiplies all ability cooldown durations (cheats)
    public Vector2 MovementScale   = Vector2.one;   // Per-axis input scale (effects can clamp to 0)
    public float Acceleration = 50f;
    public float JumpStrength = 16;
    public float currentXScale = 1f;
    public float currentYScale = 1f;
    public float currentZScale = 1f;
    public float CrouchHeight = 0.5f;
    public float WallSlideDownMax = 4f;
    public float AirControlMult = 0.1f;       // Reduces steering force while airborne
    public float SwingControlMult = 0.01f;    // Near-zero control while on grapple swing
    public float DamageReductionPercent = 0f;

    // ─── Settings ─────────────────────────────────────────────────────────────
    public float Sensitivity = 100f;

    // ─── Status Effects ───────────────────────────────────────────────────────
    public bool IsInvincible = false;
    public bool CanMove = true;

    // ─── Movement State ───────────────────────────────────────────────────────
    public enum MovementState
    {
        Idle,                   // Covers walking and sprinting (speed set by Sprint input)
        Crouch,
        Prone,                  // Also covers sliding when speed > ProneSpeed
        Climb, WallRun, Hang,   // Wall-contact states
    }
    public MovementState currentState = MovementState.Idle;
    public MovementState lastState = MovementState.Idle;
    public event System.Action OnJump;
    public event System.Action OnLand;
    public float targetSpeed = 8f;

    public event System.Action<MovementState> OnStateChanged;

    public bool IsAlive => currentHealth > 0;
    public bool IsGrounded => Movement != null && Movement.onGround;
    public bool IsSwinging = false;
    public bool IsHolding = false;
    public bool IsDashing = false;

    // ─── Abilities ────────────────────────────────────────────────────────────
    // Indices: 0=Basic, 1=Quick, 2=Throw, 3=Trap — filled from Photon custom properties
    public string[] abilityList = new string[4];

    private void Awake()
    {
        // ─── Ownership check ──────────────────────────────────────────────────
        // Determine whether this GameObject belongs to the local client.
        if (TryGetComponent<PhotonView>(out var view))
        {
            IsLocalPlayer = view.IsMine;
        }
        else
        {
            // No PhotonView — treat as local (singleplayer / offline testing)
            IsLocalPlayer = true;
        }

        // Only the local player becomes the singleton; remote players should not be destroyed.
        if (IsLocalPlayer)
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // Cache component references
        Movement = GetComponent<JimmyMove>();
        rb = GetComponent<Rigidbody>();
        Input = GetComponent<InputHandler>();
        Animator = GetComponent<PlayerAnimatorController>();
        currentHealth = maxHealth;

        col = GetComponent<CapsuleCollider>();
        if (col != null)
        {
            baseColHeight = col.height;
            baseColCenter = col.center;
        }

        // Optional: generate unique ID if needed
        //PlayerID = System.Guid.NewGuid().ToString();

        // ONLY ONCE pull information from Photon's LocalPlayer
        LoadAllProperties();

    }

    public void LoadAllProperties()
    {
        var props = PhotonNetwork.LocalPlayer.CustomProperties;
        if (props == null)
        {
            Debug.LogWarning("Could not load player properties — using defaults.");
            SetDefaultAbilities();
            return;
        }

        isHunter = props.TryGetValue("IsHunter", out object isHunterVal) ? (bool)isHunterVal : false;

        // Use defaults for any missing slot so the ability system never gets null names
        abilityList[0] = props.TryGetValue("BasicAbility", out object b) ? (string)b : "BasicGrapple";
        abilityList[1] = props.TryGetValue("QuickAbility",  out object q) ? (string)q : "Dash";
        abilityList[2] = props.TryGetValue("ThrowAbility",  out object t) ? (string)t : "BoomStick";
        abilityList[3] = props.TryGetValue("TrapAbility",   out object r) ? (string)r : "Box";
    }

    private void SetDefaultAbilities()
    {
        abilityList[0] = "BasicGrapple";
        abilityList[1] = "Dash";
        abilityList[2] = "BoomStick";
        abilityList[3] = "Box";
    }

    // Jump event
    public void TriggerJump()
    {
        //Debug.Log("Jump Event");
        OnJump?.Invoke();
    }

    // Landing event
    public void TriggerLand()
    {
        //Debug.Log("Land Event");
        OnLand?.Invoke();
    }

    // ─── State Management ─────────────────────────────────────────────────────

    /// <summary>
    /// Transitions to a new movement state, updates targetSpeed (factoring in SpeedMultiplier),
    /// refreshes the animator, and fires OnStateChanged for subscribers.
    /// </summary>
    public void SetState(MovementState newState)
    {
        switch (newState)
        {
            case MovementState.Idle:
                if (Input.Sprint)
                    targetSpeed = SprintSpeed;
                else
                    targetSpeed = WalkSpeed;
                break;
            case MovementState.Crouch:
                targetSpeed = CrouchSpeed;
                break;
            case MovementState.Prone:
                // When entering prone with enough momentum, use the faster slide speed
                if (rb.linearVelocity.magnitude > ProneSpeed)
                    targetSpeed = SlideSpeed;
                else
                    targetSpeed = ProneSpeed;
                break;
            case MovementState.Climb:
                targetSpeed = ClimbSpeed;
                break;
            case MovementState.WallRun:
                targetSpeed = WallRunSpeed;
                break;
            case MovementState.Hang:
                targetSpeed = ShimmySpeed;
                break;
        }
        targetSpeed *= SpeedMultiplier;
        lastState = currentState;

        // Auto-restore standing scale if something forces us out of crouch/prone
        if ((lastState == MovementState.Prone || lastState == MovementState.Crouch)
            && newState != MovementState.Prone && newState != MovementState.Crouch)
            ApplyScale(1f);

        currentState = newState;
        Animator.UpdateAnimator();
        OnStateChanged?.Invoke(newState);
    }

    // ─── Data / Stat Management ───────────────────────────────────────────────

    // Scales only the visual mesh children and resizes the collider.
    // The root transform is never scaled so the camera/gun are unaffected.
    public void ApplyScale(float yFactor)
    {
        foreach (var t in visualRoots)
            if (t != null)
                t.localScale = new Vector3(currentXScale, currentYScale * yFactor, currentZScale);

        if (col != null)
        {
            col.height   = baseColHeight * yFactor;
            col.center   = new Vector3(baseColCenter.x, baseColCenter.y * yFactor, baseColCenter.z);
        }
    }

    public void SetPlayerScale()
    {
        ApplyScale(1f);
    }

    public void ToggleCrouchHeight()
    {
        if (currentState == MovementState.Crouch)
        {
            ApplyScale(1f);
            SetState(MovementState.Idle);
        }
        else
        {
            ApplyScale(0.7f);
            SetState(MovementState.Crouch);
        }
    }

    public void ToggleProneHeight()
    {
        if (currentState == MovementState.Prone)
        {
            ApplyScale(1f);
            SetState(MovementState.Idle);
        }
        else
        {
            ApplyScale(0.35f);
            SetState(MovementState.Prone);
        }
    }

    public void ModifyHealth(int amount)
    {
        currentHealth = Mathf.Clamp(currentHealth + amount, 0, maxHealth);
        if (currentHealth <= 0)
            Die();
    }

    public void SetMaxHealth(int amount)
    {
        maxHealth = Mathf.Max(1, amount);
    }

    public void SetSensitivity(float amount)
    {
        Sensitivity = amount;
    }

    private void Die()
    {
        Debug.Log($"{PlayerName} has died.");
    }

    // ─── Caught / Respawn ─────────────────────────────────────────────────────

    /// <summary>
    /// Called on the caught player's client via RPC. Disables movement, fakes a ragdoll
    /// physics bump, hands off to caught-camera mode, updates the HUD, then respawns after 3 s.
    /// </summary>
    public void EnterCaughtState(float hideTime)
    {
        CanMove = false;

        if (rb != null)
        {
            rb.AddForce(Vector3.up * 3f + UnityEngine.Random.insideUnitSphere * 2f, ForceMode.Impulse);
            rb.AddTorque(UnityEngine.Random.insideUnitSphere * 4f, ForceMode.Impulse);
        }

        if (PlayerCam.Instance != null)  PlayerCam.Instance.EnterCaughtMode();
        if (HUDManager.Instance != null) HUDManager.Instance.ShowCaught(hideTime);
        StartCoroutine(RespawnAfterDelay(3f));
    }

    private IEnumerator RespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        RespawnAsHunter();
    }

    private void RespawnAsHunter()
    {
        Vector3 spawnPos = Spawner.Instance != null
            ? Spawner.Instance.GetHunterSpawnPosition()
            : transform.position; // Stay put if no Spawner found — beats spawning at the origin

        transform.position = spawnPos;
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        CanMove = true;
        SetState(MovementState.Idle);

        if (HUDManager.Instance != null) HUDManager.Instance.HideCaught();
        if (PlayerCam.Instance != null)  PlayerCam.Instance.ExitCaughtMode();
    }
}
