using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System;

[RequireComponent(typeof(JimmyMove))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PhotonView))]
[RequireComponent(typeof(InputHandler))]
[RequireComponent(typeof(PlayerAnimatorController))]
public class Player : MonoBehaviour
{
    // Singleton instance
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

    // --- Identity / Profile ---
    public string PlayerID { get; private set; }
    public string PlayerName { get; private set; }
    
    // --- References ---
    public JimmyMove Movement { get; private set; }
    public InputHandler Input { get; private set; }
    public PlayerAnimatorController Animator { get; private set; }
    public Rigidbody rb;

    // --- Game Mode Related ---
    public bool isHunter;
    public int Score;

    // --- Multiplayer Logic
    public bool IsLocalPlayer;

    // --- Character Attributes ---
    public float height = 1.8f;
    public int maxHealth = 100;
    private int currentHealth;

    // --- Movement Speeds ---
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

    // --- Modifiers ---
    public float SpeedMultiplier = 1f;
    public float Acceleration = 50f;
    public float JumpStrength = 16;
    public float currentXScale = 1f;
    public float currentYScale = 1f;
    public float currentZScale = 1f;
    public float CrouchHeight = 0.5f;
    public float WallSlideDownMax = 4f;
    public float AirControlMult = 0.1f;
    public float SwingControlMult = 0.01f;
    public float DamageReductionPercent = 0f;

    // --- Settings ---
    public float Sensitivity = 100f;

    // --- Status Effects ---
    public bool IsInvincible = false;
    public bool CanMove = true;

    // --- State ---
    public enum MovementState
    {
        Idle,                   // Idle now handles walking and sprinting too 
        Crouch,                 // Crouched state
        Prone,                  // Prone and slide states
        //Air,                  // In-air state
        Climb, WallRun, Hang,   // Wall states
    }
    public MovementState currentState = MovementState.Idle;
    public MovementState lastState = MovementState.Idle;
    public event System.Action OnJump;
    public event System.Action OnLand;
    public float targetSpeed = 8f;

    // Event for state changes
    public event System.Action<MovementState> OnStateChanged;

    public bool IsAlive => currentHealth > 0;
    public bool IsGrounded => Movement != null && Movement.onGround;
    public bool IsSwinging = false;
    public bool IsHolding = false;
    public bool IsDashing = false;

    //public Dictionary<string, Object> customProperties = new Dictionary<string, Object>();

    public string[] abilityList = new string[4];

    private void Awake()
    {
        // Determine ownership. Only the local/owned player should become the singleton.
        if (TryGetComponent<PhotonView>(out var view))
        {
            IsLocalPlayer = view.IsMine;
        }
        else
        {
            // If there's no PhotonView, treat this as the local player (singleplayer or non-networked)
            IsLocalPlayer = true;
        }

        // Only set the global Instance for the local player. Do NOT destroy remote player objects.
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
            Debug.LogWarning("Could not load player properties in Player class");
            return;
        } 

        // First, label player as hunter or not
        isHunter = props.ContainsKey("IsHunter") ? (bool)props["IsHunter"] : false;

        // Fill string list of player's starting abilities
        if (!props.ContainsKey("BasicAbility") || !props.ContainsKey("QuickAbility") || !props.ContainsKey("ThrowAbility") || !props.ContainsKey("TrapAbility"))
        {
            Debug.LogWarning("Could not load player's abilities because no matching key was found");
            return;
        }
        abilityList[0] = (string)props["BasicAbility"];
        abilityList[1] = (string)props["QuickAbility"];
        abilityList[2] = (string)props["ThrowAbility"];
        abilityList[3] = (string)props["TrapAbility"];

        // In order to get whole dictionary, use the below code
        //customProperties.Clear();
        //foreach (var kvp in PhotonNetwork.LocalPlayer.CustomProperties)
        //{
        //    customProperties[kvp.Key.ToString()] = (Object)kvp.Value;
        //}
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

    /*
     * Determine the player's current movement state based on input and environment.
     */
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
        currentState = newState;
        Animator.UpdateAnimator();
        OnStateChanged?.Invoke(newState);
    }

    // --- Data/Stat Management ---
    public void SetPlayerScale()
    {
        //currentYScale = Mathf.Max(0.1f, newY);
        transform.localScale = new Vector3(currentXScale, currentYScale, currentZScale);
    }

    public void ToggleCrouchHeight() 
    {
        if (currentState == MovementState.Crouch)
        {
            transform.localScale = new Vector3(currentXScale, currentYScale, currentZScale);
            SetState(MovementState.Idle);
        }
        else
        {
            transform.localScale = new Vector3(currentXScale, currentYScale * 0.7f, currentZScale);
            SetState(MovementState.Crouch);
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
        maxHealth = Mathf.Max(1, amount);;
    }

    public void SetSensitivity(float amount)
    {
        Sensitivity = amount;
    }

    private void Die()
    {
        Debug.Log($"{PlayerName} has died.");
        // Trigger death animation, disable input, etc.
    }
}
