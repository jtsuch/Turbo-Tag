using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Central hub for one player instance (local or remote): identity, stats, movement state,
/// body size and the caught/respawn flow. Sibling components read state from here.
///
/// <see cref="Local"/> is the instance owned by this client; <see cref="All"/> contains every
/// spawned player. Movement state and body size are synced to remote copies through
/// OnPhotonSerializeView so stances and shrinking look right for everyone.
/// Attach to: ThePlayer prefab root.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PhotonView))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(InputHandler))]
[RequireComponent(typeof(AbilityHandler))]
public class Player : MonoBehaviourPun, IPunObservable
{
    // ─── Registry ─────────────────────────────────────────────────────────────
    private static Player _local;
    public static Player Local => _local;

    private static readonly List<Player> all = new();
    public static IReadOnlyList<Player> All => all;

    /// <summary>Raised once the local player has finished spawning.</summary>
    public static event Action<Player> LocalPlayerSpawned;

    public static Player Find(int actorNumber)
    {
        foreach (var p in all)
            if (p.ActorNumber == actorNumber) return p;
        return null;
    }

    // ─── Inspector ────────────────────────────────────────────────────────────
    [SerializeField] private PlayerStats stats = new();

    [Header("Visual")]
    [Tooltip("Mesh/rig roots scaled for crouch, prone and size changes (Armature and Jimmy).")]
    [SerializeField] private Transform[] visualRoots;
    [Tooltip("Prone collider length as a fraction of standing height (the body lying down).")]
    [SerializeField] private float proneLength = 0.8f;

    [Header("Caught")]
    [SerializeField] private float respawnDelay = 3f;

    // ─── Components ───────────────────────────────────────────────────────────
    public PlayerMovement  Movement  { get; private set; }
    public InputHandler    Input     { get; private set; }
    public AbilityHandler  Abilities { get; private set; }
    public Rigidbody       Body      { get; private set; }
    public PlayerNetworkSync NetSync { get; private set; }

    /// <summary>Velocity on every client: the real one for the owner, the synced one for remote copies (which are kinematic).</summary>
    public Vector3 Velocity => NetSync != null ? NetSync.Velocity : Body.linearVelocity;

    // ─── Identity ─────────────────────────────────────────────────────────────
    public PhotonPlayer Owner       => photonView.Owner;
    public int          ActorNumber => Owner != null ? Owner.ActorNumber : -1;
    public bool         IsLocal     => photonView.IsMine;
    public string       DisplayName => string.IsNullOrEmpty(Owner?.NickName) ? $"Player {ActorNumber}" : Owner.NickName;
    public PlayerRole   Role        => PlayerRoles.Get(Owner);
    public bool         IsHunter    => Role == PlayerRole.Hunter;

    // ─── Stats ────────────────────────────────────────────────────────────────
    public PlayerStats   Stats     => stats;
    public StatModifiers Modifiers { get; } = new();

    /// <summary>Uniform body scale (1 = normal). Synced to remote copies.</summary>
    public float Size => IsLocal ? Modifiers.Get(Stat.Size) : remoteSize;
    public float Height => stats.height * Size;

    /// <summary>Everything that scales this player's ability cooldowns: cheats, mode and rules.</summary>
    public float CooldownMultiplier =>
        Modifiers.Get(Stat.Cooldown) * (GameModeManager.Instance != null ? GameModeManager.Instance.CooldownMultiplier(this) : 1f);

    // ─── Movement State ───────────────────────────────────────────────────────
    public enum MovementState
    {
        Idle,                   // Walking and sprinting (speed chosen by the Sprint input)
        Crouch,
        Prone,                  // Also covers sliding when entered with momentum
        Climb, WallRun, Hang,   // Wall-contact states
    }

    public MovementState State         { get; private set; } = MovementState.Idle;
    public MovementState PreviousState { get; private set; } = MovementState.Idle;

    public event Action<MovementState> StateChanged;
    public event Action Jumped;
    public event Action Landed;
    public event Action Caught;
    public event Action Respawned;

    public bool IsGrounded => Movement != null && Movement.OnGround;
    public bool IsSwinging { get; set; }
    public bool IsHolding  { get; set; }
    public bool IsDashing  { get; set; }
    public bool IsCaught   { get; private set; }

    /// <summary>Free Spin (PlayerCam): the body is tumbling, so movement ignores its tilt.</summary>
    public bool IsFreeSpinning { get; set; }

    /// <summary>Stuck in place on a surface (Chameleon): no movement and no gravity until cleared.</summary>
    public bool IsAnchored { get; set; }

    /// <summary>Climb speed that never runs out while above 0 (Infinite Climb); 0 = normal climbing.</summary>
    public float SteadyClimbSpeed { get; set; }

    /// <summary>Synced look changes (cloak, camouflage, ice tint).</summary>
    public PlayerAppearance Appearance { get; private set; }

    /// <summary>True for a moment after an outside force pushed this player; movement then doesn't brake.</summary>
    public bool IsBeingPushed => Time.time - lastPushTime < PushGrace;

    /// <summary>Sliding on ice (iced and on the ground). Synced, so remote copies can show it (PlayerAppearance).</summary>
    public bool IsOnIce => IsLocal ? Movement != null && Movement.OnIce : remoteOnIce;
    private bool remoteOnIce;
    private float lastPushTime = float.NegativeInfinity;
    private const float PushGrace = 0.2f;

    /// <summary>Called by anything applying an outside force (see <see cref="WorldFx.Push"/>).</summary>
    public void NotePushed() => lastPushTime = Time.time;

    /// <summary>Movement off while abilities still work (looking through a placed Camera).</summary>
    public bool MovementLocked { get; set; }

    public bool CanMove =>
        !IsCaught && !IsHeld && !MovementLocked && (GameModeManager.Instance == null || GameModeManager.Instance.CanMove(this));

    public bool CanUseAbilities =>
        !IsCaught && !IsHeld && (GameModeManager.Instance == null || GameModeManager.Instance.CanUseAbilities(this));

    // ─── Timed stat changes (Adrenaline, Grow Ray…) ───────────────────────────
    private readonly Dictionary<string, (Stat stat, float until)> timedModifiers = new();

    /// <summary>
    /// Local player: multiplies <paramref name="stat"/> for <paramref name="seconds"/>. Using the same
    /// <paramref name="source"/> again refreshes the timer instead of stacking.
    /// </summary>
    public void ApplyTimedModifier(Stat stat, string source, float multiplier, float seconds)
    {
        if (!IsLocal) return;
        Modifiers.Set(stat, source, multiplier);
        timedModifiers[source] = (stat, Time.time + seconds);
    }

    /// <summary>Any client: asks this player's owner to apply a timed stat change (stats are owner-side).</summary>
    public void RequestTimedModifier(Stat stat, string source, float multiplier, float seconds) =>
        photonView.RPC(nameof(RPC_TimedModifier), photonView.Owner, (int)stat, source, multiplier, seconds);

    [PunRPC]
    private void RPC_TimedModifier(int stat, string source, float multiplier, float seconds) =>
        ApplyTimedModifier((Stat)stat, source, multiplier, seconds);

    private void Update()
    {
        if (timedModifiers.Count == 0) return;
        List<string> expired = null;
        foreach (var pair in timedModifiers)
            if (Time.time >= pair.Value.until) (expired ??= new List<string>()).Add(pair.Key);
        if (expired == null) return;
        foreach (var source in expired)
        {
            Modifiers.Remove(timedModifiers[source].stat, source);
            timedModifiers.Remove(source);
        }
    }

    // ─── Ice (Snowball, Ice Trap) ─────────────────────────────────────────────

    /// <summary>Iced: this player slides with almost no grip on any ground until the time runs out.</summary>
    public bool IsIced => Time.time < icedUntil;
    private float icedUntil = float.NegativeInfinity;

    /// <summary>Local player: ices this player for <paramref name="seconds"/> (refreshes, never shortens).</summary>
    public void IceFor(float seconds)
    {
        if (!IsLocal) return;
        icedUntil = Mathf.Max(icedUntil, Time.time + seconds);
    }

    /// <summary>Any client: asks this player's owner to ice them.</summary>
    public void RequestIce(float seconds) =>
        photonView.RPC(nameof(RPC_Ice), photonView.Owner, seconds);

    [PunRPC]
    private void RPC_Ice(float seconds) => IceFor(seconds);

    /// <summary>Held in place by a trap (Mouse Trap): no moving, no abilities, until the hold ends.</summary>
    public bool IsHeld => Time.time < heldUntil;
    private float heldUntil = float.NegativeInfinity;

    /// <summary>Local player: holds this player still for <paramref name="seconds"/> (ends any ability in use).</summary>
    public void HoldFor(float seconds)
    {
        if (!IsLocal) return;
        heldUntil = Mathf.Max(heldUntil, Time.time + seconds);
        Abilities.CancelAll();
    }

    /// <summary>Speed the movement system steers toward in the current state.</summary>
    public float TargetSpeed
    {
        get
        {
            float baseSpeed = State switch
            {
                MovementState.Crouch  => stats.crouchSpeed,
                MovementState.Prone   => proneEnteredSliding ? stats.slideSpeed : stats.proneSpeed,
                MovementState.Climb   => stats.climbSpeed,
                MovementState.WallRun => stats.wallRunSpeed,
                MovementState.Hang    => stats.shimmySpeed,
                _                     => Input != null && Input.SprintHeld ? stats.sprintSpeed : stats.walkSpeed,
            };
            return baseSpeed * Modifiers.Get(Stat.Speed);
        }
    }

    // ─── Private ──────────────────────────────────────────────────────────────
    private Vector3[]       baseVisualScales;
    private Vector3[]       baseVisualPositions;
    private CapsuleCollider col;
    private float   baseColHeight;
    private float   baseColRadius;
    private Vector3 baseColCenter;
    private bool    proneEnteredSliding;
    private float   remoteSize = 1f;

    private const float CrouchScale = 0.7f;
    private const float ProneScale  = 0.35f; // Prone body height for the camera, not a model squash

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        Movement  = GetComponent<PlayerMovement>();
        Input     = GetComponent<InputHandler>();
        Abilities = GetComponent<AbilityHandler>();
        Body      = GetComponent<Rigidbody>();
        NetSync   = GetComponent<PlayerNetworkSync>();
        Appearance = GetComponent<PlayerAppearance>();

        // Model roots are often imported with a non-unit scale; stance/size multiply it
        baseVisualScales    = new Vector3[visualRoots.Length];
        baseVisualPositions = new Vector3[visualRoots.Length];
        for (int i = 0; i < visualRoots.Length; i++)
        {
            baseVisualScales[i]    = visualRoots[i] != null ? visualRoots[i].localScale : Vector3.one;
            baseVisualPositions[i] = visualRoots[i] != null ? visualRoots[i].localPosition : Vector3.zero;
        }

        col = GetComponent<CapsuleCollider>();
        if (col != null)
        {
            baseColHeight = col.height;
            baseColRadius = col.radius;
            baseColCenter = col.center;
        }

        all.Add(this);
        if (IsLocal)
        {
            if (_local != null && _local != this)
                Debug.LogWarning("[Player] A second local player was spawned; replacing the previous one.");
            _local = this;
        }

        Modifiers.Changed += OnModifierChanged;

        if (!photonView.ObservedComponents.Contains(this))
            photonView.ObservedComponents.Add(this);
    }

    private void Start()
    {
        if (IsLocal) LocalPlayerSpawned?.Invoke(this);
    }

    private void OnDestroy()
    {
        Modifiers.Changed -= OnModifierChanged;
        all.Remove(this);
        if (_local == this) _local = null;
    }

    // ─── Events (raised by PlayerMovement) ────────────────────────────────────

    public void NotifyJumped() => Jumped?.Invoke();
    public void NotifyLanded() => Landed?.Invoke();

    // ─── State Management ─────────────────────────────────────────────────────

    public void SetState(MovementState newState)
    {
        if (newState == MovementState.Prone && State != MovementState.Prone)
            proneEnteredSliding = Velocity.magnitude > stats.proneSpeed;

        PreviousState = State;
        State         = newState;

        if (PreviousState != newState) ApplyBodyScale();
        StateChanged?.Invoke(newState);
    }

    public void ToggleCrouch() =>
        SetState(State == MovementState.Crouch ? MovementState.Idle : MovementState.Crouch);

    public void ToggleProne() =>
        SetState(State == MovementState.Prone ? MovementState.Idle : MovementState.Prone);

    // ─── Body Scale ───────────────────────────────────────────────────────────

    private float StanceScale => State switch
    {
        MovementState.Crouch => CrouchScale,
        MovementState.Prone  => ProneScale,
        _                    => 1f,
    };

    /// <summary>
    /// Scales the visible mesh and collider for the current stance and size. Prone keeps the model's
    /// shape (the prone animation lays it down) and turns the collider into a capsule lying along the
    /// body; crouch still squashes the model, as there is no crouch animation.
    /// </summary>
    private void ApplyBodyScale()
    {
        float size   = Size;
        bool  prone  = State == MovementState.Prone;
        float squash = prone ? 1f : StanceScale;

        var scale = new Vector3(size, size * squash, size);
        // Position scales too: the model sits at an offset from the root, and scaling only the model
        // moved its feet off the collider's bottom (into the ground when grown)
        for (int i = 0; i < visualRoots.Length; i++)
        {
            if (visualRoots[i] == null) continue;
            visualRoots[i].localScale    = Vector3.Scale(baseVisualScales[i], scale);
            visualRoots[i].localPosition = Vector3.Scale(baseVisualPositions[i], scale);
        }

        if (col == null) return;

        float radius = baseColRadius * size;
        col.radius = radius;
        if (prone)
        {
            // Lying down: as long as the body, along the facing direction, resting on the ground
            col.direction = 2;
            col.height    = Mathf.Max(radius * 2f, baseColHeight * size * proneLength);
            col.center    = new Vector3(baseColCenter.x * size, radius, baseColCenter.z * size);
        }
        else
        {
            col.direction = 1;
            col.height    = baseColHeight * size * StanceScale;
            col.center    = new Vector3(baseColCenter.x * size, baseColCenter.y * size * StanceScale, baseColCenter.z * size);
        }
    }

    private void OnModifierChanged(Stat stat)
    {
        if (stat == Stat.Size) ApplyBodyScale();
    }

    // ─── Contact ──────────────────────────────────────────────────────────────

    private void OnCollisionEnter(Collision collision) => HandleContact(collision.collider);
    private void OnTriggerEnter(Collider other)        => HandleContact(other);

    private void HandleContact(Collider other)
    {
        if (!IsLocal || GameModeManager.Instance == null || GameModeManager.Instance.Mode == null) return;

        var otherPlayer = other.GetComponentInParent<Player>();
        if (otherPlayer == null || otherPlayer == this) return;

        GameModeManager.Instance.Mode.OnLocalPlayerContact(this, otherPlayer);
    }

    // ─── Caught / Respawn ─────────────────────────────────────────────────────

    /// <summary>Local player only: called when the master confirms this player was tagged.</summary>
    public void OnTagged(PhotonPlayer tagger)
    {
        if (!IsLocal || IsCaught) return;

        IsCaught = true;
        Abilities.CancelAll();

        // Fake a knock-back so the tag feels physical
        Body.AddForce(Vector3.up * 3f + UnityEngine.Random.insideUnitSphere * 2f, ForceMode.Impulse);

        Caught?.Invoke();
        StartCoroutine(RespawnAfterDelay());
    }

    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);
        if (Spawner.Instance != null) Spawner.Instance.RespawnLocalPlayer();
        IsCaught = false;
        Respawned?.Invoke();
    }

    /// <summary>The ragdoll left by the last tag (its torso), followed by the caught camera; null if none.</summary>
    public Transform Corpse { get; private set; }

    /// <summary>
    /// Every client, when this player is tagged: leaves a ragdoll (see <see cref="PlayerCorpse"/>) and, on other
    /// screens, hides the real body (and its collider) until the respawn, so only the ragdoll is seen.
    /// </summary>
    public void LeaveCorpse()
    {
        Corpse = PlayerCorpse.Create(this, Velocity + Vector3.up * 3f);
        if (!IsLocal) StartCoroutine(HideBodyUntilRespawn());
    }

    private IEnumerator HideBodyUntilRespawn()
    {
        var hidden = new List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is LineRenderer) continue;
            r.enabled = false;
            hidden.Add(r);
        }
        if (col != null) col.enabled = false;

        // A little longer than the respawn, so the body reappears at the spawn point, not where it fell
        yield return new WaitForSeconds(respawnDelay + 0.3f);

        foreach (var r in hidden) if (r != null) r.enabled = true;
        if (col != null) col.enabled = true;
    }

    /// <summary>Moves the player instantly, clearing momentum and stance.</summary>
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        Abilities.CancelAll();
        Body.linearVelocity  = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.position        = position;
        transform.SetPositionAndRotation(position, rotation);
        SetState(MovementState.Idle);
    }

    // ─── Network Sync ─────────────────────────────────────────────────────────

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext((byte)State);
            stream.SendNext(Size);
            stream.SendNext(IsOnIce);
        }
        else
        {
            var   state = (MovementState)(byte)stream.ReceiveNext();
            float size  = (float)stream.ReceiveNext();
            remoteOnIce = (bool)stream.ReceiveNext();

            bool sizeChanged = !Mathf.Approximately(size, remoteSize);
            remoteSize = size;

            if (state != State) SetState(state);
            else if (sizeChanged) ApplyBodyScale();
        }
    }
}
