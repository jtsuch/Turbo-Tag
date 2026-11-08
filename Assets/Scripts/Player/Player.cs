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

    // ─── Components ───────────────────────────────────────────────────────────
    public PlayerMovement  Movement  { get; private set; }
    public InputHandler    Input     { get; private set; }
    public AbilityHandler  Abilities { get; private set; }
    public Rigidbody       Body      { get; private set; }
    public PlayerNetworkSync NetSync { get; private set; }
    public PlayerHealth    Health    { get; private set; }
    public PlayerGun       Gun       { get; private set; }
    public PlayerAnimatorController Animation { get; private set; }

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

    /// <summary>
    /// Owner: something else uses the Grab key right now (the Spy Camera's withdraw prompt), so movement doesn't
    /// also grab a ledge or an object with it. Call <see cref="ClaimGrabKey"/> every frame it applies.
    /// </summary>
    public bool GrabKeyClaimed => Time.frameCount - grabClaimFrame <= 1;
    public void ClaimGrabKey() => grabClaimFrame = Time.frameCount;
    private int grabClaimFrame = -10;

    /// <summary>Free Spin (PlayerCam): the body is tumbling, so movement ignores its tilt.</summary>
    public bool IsFreeSpinning { get; set; }

    /// <summary>Stuck in place on a surface (Chameleon): no movement and no gravity until cleared.</summary>
    public bool IsAnchored { get; set; }

    /// <summary>
    /// Wall Magnet (owner): the "up" of the surface being walked on (a wall's or ceiling's normal), or null for
    /// normal gravity. PlayerMovement walks along that surface; PlayerCam turns the body and view to match.
    /// </summary>
    public Vector3? MagnetUp { get; set; }
    public bool IsMagnetized => MagnetUp.HasValue;

    /// <summary>Owner: the turn from world up to the surface the body stands on, smoothed by PlayerCam (identity normally).</summary>
    public Quaternion SurfaceFrame { get; set; } = Quaternion.identity;

    /// <summary>Zero Gravity (owner): no gravity while set. PlayerMovement switches it (it owns gravity).</summary>
    public bool ZeroGravity { get; set; }

    /// <summary>Owner: an admin turned this player's abilities off ("name ability 0" in the admin console).</summary>
    public bool AdminAbilitiesOff { get; set; }

    /// <summary>Owner: an admin made this player immune to gun damage ("name god 1").</summary>
    public bool AdminGod { get; set; }

    /// <summary>Owner: the Shield ability is held up.</summary>
    public bool ShieldHeld { get; set; }

    /// <summary>Owner: the Bubble donut effect is on.</summary>
    public bool ShieldBubble { get; set; }

    /// <summary>
    /// Shielded (synced): immune to other players' abilities (blasts, pulls, ice, traps, flashes, Honk, Fish Bait…).
    /// Drawn on every screen by <see cref="ShieldBubble"/>.
    /// </summary>
    public bool IsShielded => IsLocal ? ShieldHeld || ShieldBubble : remoteShielded;

    /// <summary>Synced: everyone else sees this player through walls (the Beacon donut curse).</summary>
    public bool IsBeacon
    {
        get => IsLocal ? beacon : remoteBeacon;
        set => beacon = value;
    }

    /// <summary>Owner: abilities are switched off by an effect (the Butterfingers donut curse).</summary>
    public bool AbilitiesLockedByEffect { get; set; }

    private bool beacon, remoteBeacon, remoteShielded;
    private int  localFlags, remoteFlags;

    /// <summary>A synced donut flag (golden, phantom, repel…): the owner's own value, or the last one received.</summary>
    public bool Has(PlayerFlag flag) => ((IsLocal ? localFlags : remoteFlags) & (int)flag) != 0;

    /// <summary>Owner: turns a synced donut flag on or off (other screens see it within a moment).</summary>
    public void SetFlag(PlayerFlag flag, bool on)
    {
        if (on) localFlags |= (int)flag;
        else    localFlags &= ~(int)flag;
    }

    /// <summary>Owner: the next fatal hit leaves you on 50 health instead (Second Life donut).</summary>
    public bool SecondLife { get; set; }

    /// <summary>Owner: multiplier on ability strength (Ability Overdrive donut): throws, traps, dash, launch, blower, honk, ground pound.</summary>
    public float AbilityPower { get; set; } = 1f;

    /// <summary>
    /// True when an ability from <paramref name="sourceActor"/> may affect this player: always, unless shielded
    /// (then only their own). Actor 0 means the world (fans, pads), which shields don't block.
    /// </summary>
    public bool AffectedBy(int sourceActor) =>
        Role != PlayerRole.Ghost && (!IsShielded || sourceActor == 0 || sourceActor == ActorNumber);

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

    /// <summary>
    /// Called by anything applying an outside force (see <see cref="WorldFx.Push"/>). With another player as the
    /// source, they're remembered as the last pusher (with the ability that did it), so a fall soon after counts
    /// as their knock-off.
    /// </summary>
    public void NotePushed(int sourceActor = 0, string cause = null)
    {
        lastPushTime = Time.time;
        if (sourceActor <= 0 || sourceActor == ActorNumber) return;
        lastPusher = sourceActor;
        lastPushCause = cause ?? "";
        lastPusherTime = Time.time;
    }

    private int    lastPusher;
    private string lastPushCause = "";
    private float  lastPusherTime = float.NegativeInfinity;

    /// <summary>Who last pushed this player (and with what ability id, "" if unknown), within the last <paramref name="seconds"/>.</summary>
    public bool TryGetPusher(float seconds, out int actor, out string cause)
    {
        actor = lastPusher;
        cause = lastPushCause;
        return lastPusher > 0 && Time.time - lastPusherTime <= seconds;
    }

    /// <summary>Movement off while abilities still work (looking through a placed Camera).</summary>
    public bool MovementLocked { get; set; }

    /// <summary>Owner: looking through a placed Spy Camera. Only the camera's own key works (to stop watching).</summary>
    public bool ViewingCamera { get; set; }

    public bool CanMove =>
        !IsCaught && !IsHeld && !MovementLocked && (GameModeManager.Instance == null || GameModeManager.Instance.CanMove(this));

    public bool CanUseAbilities =>
        !IsCaught && !IsHeld && !AdminAbilitiesOff && !AbilitiesLockedByEffect && (GameModeManager.Instance == null || GameModeManager.Instance.CanUseAbilities(this));

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
            float modeSpeed = GameModeManager.Instance != null ? GameModeManager.Instance.SpeedMultiplier(this) : 1f;
            return baseSpeed * Modifiers.Get(Stat.Speed) * modeSpeed;
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
        Animation  = GetComponent<PlayerAnimatorController>();

        // Gun-mode parts, idle unless the mode uses guns. Added here on every copy, so their RPCs line up.
        Health = TryGetComponent(out PlayerHealth health) ? health : gameObject.AddComponent<PlayerHealth>();
        Gun    = TryGetComponent(out PlayerGun gun)       ? gun    : gameObject.AddComponent<PlayerGun>();
        if (!TryGetComponent(out PlayerFx _)) gameObject.AddComponent<PlayerFx>(); // Jump/land dust for others
        if (!TryGetComponent(out ShieldBubble _)) gameObject.AddComponent<ShieldBubble>(); // Shield / Bubble, every screen
        if (!TryGetComponent(out HitFlinch _)) gameObject.AddComponent<HitFlinch>();       // Shot reactions on the body
        if (!TryGetComponent(out PlayerEmotes _)) gameObject.AddComponent<PlayerEmotes>(); // Emote bubbles and moves
        if (!TryGetComponent(out PlayerCallouts _)) gameObject.AddComponent<PlayerCallouts>(); // Taunts and team pings
        if (!TryGetComponent(out HunterKit _)) gameObject.AddComponent<HunterKit>();           // Hunter classes and their tools
        if (!TryGetComponent(out GhostBody _)) gameObject.AddComponent<GhostBody>();       // Hunter: ghosts pass through players
        if (!TryGetComponent(out DonutNet _)) gameObject.AddComponent<DonutNet>();         // Donut effects others see

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
        if (!IsLocal) return;
        // Tutorial: arrive with abilities off until an admin turns them on (all ability 1)
        var mode = GameModeManager.Instance != null ? GameModeManager.Instance.Mode : null;
        if (mode != null && mode.AbilitiesStartOff) AdminAbilitiesOff = true;
        LocalPlayerSpawned?.Invoke(this);
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

    private float bounce = 1f;

    /// <summary>
    /// Visual squash (below 1) or stretch (above 1) of the body along its height, volume kept (landing and
    /// take-off bounce, <see cref="PlayerFx"/>). Looks only: the collider doesn't change.
    /// </summary>
    public void SetBounce(float stretch)
    {
        if (Mathf.Approximately(stretch, bounce)) return;
        bounce = stretch;
        ApplyBodyScale(visualOnly: true);
    }

    private void ApplyBodyScale() => ApplyBodyScale(visualOnly: false);

    /// <summary>
    /// Scales the visible mesh and collider for the current stance and size. Prone keeps the model's
    /// shape (the prone animation lays it down) and turns the collider into a capsule lying along the
    /// body; crouch squashes the model and collider vertically (to CrouchScale), as there is no crouch animation.
    /// </summary>
    private void ApplyBodyScale(bool visualOnly)
    {
        float size   = Size;
        bool  prone  = State == MovementState.Prone;
        float squash = prone ? 1f : StanceScale;

        // Position scales too: the model sits at an offset from the root, and scaling only the model
        // moved its feet off the collider's bottom (into the ground when grown)
        for (int i = 0; i < visualRoots.Length; i++)
        {
            var root = visualRoots[i];
            if (root == null) continue;

            // Squash the root's own axis that points up. Models from Blender are imported turned 270° on X,
            // so their Y points forward: squashing Y made a crouching penguin thinner, not shorter.
            Vector3 localScale = baseVisualScales[i] * size;
            int up = UpAxis(root.localRotation);
            float across = 1f / Mathf.Sqrt(Mathf.Max(0.3f, bounce)); // Squash and stretch keep the volume
            for (int a = 0; a < 3; a++) localScale[a] *= a == up ? squash * bounce : across;
            root.localScale    = localScale;
            root.localPosition = Vector3.Scale(baseVisualPositions[i], new Vector3(size, size * squash * bounce, size));
        }

        if (visualOnly || col == null) return;

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

    // Which local axis (0 x, 1 y, 2 z) of a child with this rotation points (most nearly) up
    private static int UpAxis(Quaternion localRotation)
    {
        Vector3 up = Quaternion.Inverse(localRotation) * Vector3.up;
        Vector3 a  = new(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z));
        return a.x >= a.y && a.x >= a.z ? 0 : a.y >= a.z ? 1 : 2;
    }

    /// <summary>Height of the eyes above the feet (matches PlayerCam's stance offsets), for any copy.</summary>
    public float EyeHeight => Size * State switch
    {
        MovementState.Crouch => 1.4f,
        MovementState.Prone  => 1f,
        _                    => 2f,
    };

    /// <summary>World height of the top of the body (the collider's top: stance and size included).</summary>
    public float TopY => col != null && col.enabled ? col.bounds.max.y : transform.position.y + Height;

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
        if (!TagRespawns) return; // Caught (caught camera) until the scene changes

        var mode = GameModeManager.Instance != null ? GameModeManager.Instance.Mode : null;
        float delay = mode != null ? mode.RespawnDelayAfter(tagger, Owner) : MatchRules.RespawnDelay;
        StartCoroutine(RespawnAfterDelay(delay));
    }

    private bool respawnWaitEnded;

    /// <summary>Local player: respawns now instead of waiting out the rest of the delay (killcam skipped or over).</summary>
    public void EndRespawnWait() => respawnWaitEnded = true;

    // Whether the current mode brings tagged players back (Protect the President doesn't: the tag ended the match)
    private static bool TagRespawns
    {
        get
        {
            var manager = GameModeManager.Instance;
            return manager == null || manager.Mode == null || manager.Mode.RespawnsAfterTag;
        }
    }

    /// <summary>True while this player's body is hidden after a tag (the ragdoll stands in for it).</summary>
    public bool IsDownAfterTag { get; private set; }

    private IEnumerator RespawnAfterDelay(float delay)
    {
        respawnWaitEnded = false;
        float until = Time.time + delay;
        while (Time.time < until && !respawnWaitEnded) yield return null;

        if (Spawner.Instance != null) Spawner.Instance.RespawnLocalPlayer();
        IsCaught = false;
        Respawned?.Invoke();
        Fx.Respawn(transform.position);
        photonView.RPC(nameof(RPC_Respawned), RpcTarget.Others, transform.position);
    }

    // Other screens show the body again once its owner says it respawned (the wait can be cut short)
    private bool respawnSignal;

    [PunRPC]
    private void RPC_Respawned(Vector3 feet)
    {
        respawnSignal = true;
        Fx.Respawn(feet);
    }

    /// <summary>The ragdoll left by the last tag (its torso), followed by the caught camera; null if none.</summary>
    public Transform Corpse { get; private set; }

    /// <summary>
    /// Every client, when this player is tagged: leaves a ragdoll (see <see cref="PlayerCorpse"/>) and, on other
    /// screens, hides the real body (and its collider) until the respawn, so only the ragdoll is seen.
    /// </summary>
    public void LeaveCorpse()
    {
        // Shot dead: thrown along the shot (a headshot snaps back harder); tagged: carried by its momentum
        Vector3 velocity = Velocity + Vector3.up * 2f;
        Vector3? shot = null;
        if (Health != null && Time.time - Health.LastHitTime < 1.5f)
        {
            shot = Health.LastHitDirection;
            velocity += Vector3.ProjectOnPlane(shot.Value, Vector3.up).normalized * 4f;
        }
        Corpse = PlayerCorpse.Create(this, velocity, shot, Health != null && Health.LastHitHeadshot);
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
        IsDownAfterTag = true;

        // No respawn coming (the tag ended the match): stay hidden until the scene changes
        if (!TagRespawns) yield break;

        // Until the owner respawns (or a long safety limit), and a little longer, so the body reappears
        // at the spawn point, not where it fell
        respawnSignal = false;
        float limit = Time.time + 30f;
        while (!respawnSignal && Time.time < limit) yield return null;
        yield return new WaitForSeconds(0.3f);

        foreach (var r in hidden) if (r != null) r.enabled = true;
        if (col != null) col.enabled = true;
        IsDownAfterTag = false;
    }

    /// <summary>Moves the player instantly, clearing momentum and stance.</summary>
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        Abilities.CancelAll();
        MagnetUp     = null;                 // Arrive upright, not still turned to a wall (Wall Magnet)
        ZeroGravity  = false;
        SurfaceFrame = Quaternion.identity;
        Body.linearVelocity  = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.position        = position;
        Body.rotation        = rotation;
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
            stream.SendNext((IsShielded ? 1 : 0) | (IsBeacon ? 2 : 0) | localFlags);
        }
        else
        {
            var   state = (MovementState)(byte)stream.ReceiveNext();
            float size  = (float)stream.ReceiveNext();
            remoteOnIce = (bool)stream.ReceiveNext();
            int flags      = (int)stream.ReceiveNext();
            remoteShielded = (flags & 1) != 0;
            remoteBeacon   = (flags & 2) != 0;
            remoteFlags    = flags & ~3;

            bool sizeChanged = !Mathf.Approximately(size, remoteSize);
            remoteSize = size;

            if (state != State) SetState(state);
            else if (sizeChanged) ApplyBodyScale();
        }
    }
}

/// <summary>Synced donut flags (bits above the shield and beacon bits in Player's sync).</summary>
[System.Flags]
public enum PlayerFlag
{
    None        = 0,
    Golden      = 1 << 2, // Invulnerable: glows gold on every screen
    Phantom     = 1 << 3, // Passes through players, can't be tagged
    Repel       = 1 << 4, // Pushes away anyone who comes close
    Magnet      = 1 << 5, // Pulls everyone nearby in
    Unstealable = 1 << 6, // Keep Away: the disc can't be stolen from you
    DoubleScore = 1 << 7, // Keep Away: your team scores double while you hold the disc
    Jetpack     = 1 << 8, // Flames under your feet while flying
    Jammed      = 1 << 9, // Hunter tools and the radar can't find you (Jammer donut)
}
