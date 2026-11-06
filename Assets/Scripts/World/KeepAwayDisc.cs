using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// The Keep Away disc (Resources/Object/KeepAwayDisc, Frisbee-sized; a room object the host spawns). The host owns
/// and simulates it while it's loose (low gravity, so throws glide a long way; bouncy) and other screens follow
/// its snapshots (PlayerNetworkSync). Who holds it is the <see cref="RoomKeys.DiscHolder"/> room property, which
/// only the host writes; a held disc sits in its holder's left hand on every screen (lower left of the holder's view).
///   - Grab: any touch grabs a loose disc. It never bumps players (its solid collider ignores them); a slightly
///     larger trigger tells the host about every touch (a throw that reaches an enemy is theirs), and each client
///     also asks for its own player. The thrower can't re-grab it for a moment.
///   - Steal: an enemy touching the holder (KeepAwayMode) asks the host; a fresh holder is safe for a moment.
///   - Throw: the holder holds the Action key to charge (up to <see cref="ChargeSeconds"/>; everyone hears it tick
///     faster) and lets go; a click that places a trap never throws. Throw abilities are off while holding.
///   - Falling off the map, getting caught or leaving drops it; lost over the edge, it reappears at a random
///     spawn point.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class KeepAwayDisc : MonoBehaviourPun
{
    public const float ChargeSeconds = 2f;
    private const float MinThrowSpeed  = 14f;
    private const float MaxThrowSpeed  = 46f;
    private const float GravityScale   = 0.22f;
    private const float GrabRadius     = 1.2f;  // From the disc's centre to the nearest point of a body
    private const float RegrabDelay    = 0.6f;  // The thrower can't catch their own throw straight away
    private const float StealImmunity  = 1.5f;  // A new holder can't be robbed straight away

    private static KeepAwayDisc instance;

    /// <summary>The disc in this level, if any.</summary>
    public static KeepAwayDisc Instance => instance;

    /// <summary>True while the local player holds the disc (their throw abilities are off).</summary>
    public static bool LocalHolding => instance != null && instance.Holder == PhotonNetwork.LocalPlayer.ActorNumber && !instance.thrownLocally;

    /// <summary>The holder's actor number, or -1 while it's loose. On the host, a change it just made counts at once.</summary>
    public int Holder => pendingHolder ?? NetProps.Room(RoomKeys.DiscHolder, -1);

    /// <summary>The middle of the disc in the world (its pivot is off to one side of the model).</summary>
    public Vector3 Centre => transform.TransformPoint(localCentre);

    private int?  pendingHolder; // Host: written, not yet echoed back by Photon
    private float pendingSince;

    private Rigidbody body;
    private PlayerNetworkSync sync;
    private Collider[] solids;
    private SphereCollider touch;
    private Vector3 localCentre;
    private LineRenderer beam;
    private TrailRenderer trail;

    // Host
    private int    lastThrower = -1;
    private double throwTime, holderSince;
    private Vector3 lastHolderPosition;
    private int    trackedHolder = -1;
    private float  nextIgnoreRefresh;
    private readonly HashSet<Collider> ignoredPlayers = new();

    // Local
    private bool  charging, blockedUntilRelease, thrownLocally;
    private float chargeStart, lastRequest, thrownAt;

    // Every screen: the holder's charge (for the effect)
    private bool  holderCharging;
    private float holderChargeStart, nextSpark;
    private int   handOf = -1;
    private Transform hand;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        instance = this;
        body = GetComponent<Rigidbody>();
        sync = GetComponent<PlayerNetworkSync>();
        solids = GetComponentsInChildren<Collider>();
        body.useGravity = false; // Light gravity of its own (FixedUpdate)
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // Fast throws never tunnel into the floor
        var bouncy = new PhysicsMaterial("Disc") { bounciness = 0.55f, dynamicFriction = 0.3f, staticFriction = 0.3f, bounceCombine = PhysicsMaterialCombine.Maximum };
        foreach (var c in solids) c.sharedMaterial = bouncy;

        // The model's middle (its collider's centre), and a touch trigger around it a little bigger than the disc
        var box = GetComponent<BoxCollider>();
        localCentre = box != null ? box.center : Vector3.zero;
        touch = gameObject.AddComponent<SphereCollider>();
        touch.isTrigger = true;
        touch.center = localCentre;
        touch.radius = 0.75f / Mathf.Max(0.01f, transform.lossyScale.x); // ≈0.75 m in the world
        BuildEffects();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void BuildEffects()
    {
        beam = new GameObject("Beam").AddComponent<LineRenderer>();
        beam.transform.SetParent(transform, false);
        beam.useWorldSpace = true;
        beam.positionCount = 2;
        beam.widthCurve = new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(1f, 0.04f));
        beam.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.3f), 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) });
        beam.colorGradient = fade;
        beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.35f;
        trail.widthMultiplier = 0.18f;
        trail.sharedMaterial = beam.sharedMaterial;
        trail.startColor = new Color(1f, 0.85f, 0.3f, 0.7f);
        trail.endColor = new Color(1f, 1f, 1f, 0f);
        trail.emitting = false;
    }

    private void SetHolderProp(int actor)
    {
        pendingHolder = actor;
        pendingSince  = Time.time;
        NetProps.SetRoom(RoomKeys.DiscHolder, actor);
    }

    // ─── Every frame ──────────────────────────────────────────────────────────

    private void Update()
    {
        if (pendingHolder.HasValue && (NetProps.Room(RoomKeys.DiscHolder, -1) == pendingHolder.Value || Time.time - pendingSince > 2f))
            pendingHolder = null;
        int holder = Holder;
        if (thrownLocally && (holder != PhotonNetwork.LocalPlayer.ActorNumber || Time.time - thrownAt > 1.5f)) thrownLocally = false;
        bool held = holder >= 0 && !thrownLocally;
        if (!held) holderCharging = false;

        // Held: our own placement on every screen (not the host's snapshots)
        if (sync != null) sync.enabled = !held;
        if (held) body.isKinematic = true;
        else if (photonView.IsMine) body.isKinematic = false;
        body.interpolation = held ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
        foreach (var c in solids) c.enabled = !held;
        touch.enabled = !held;

        UpdateEffects(held, holder);
        if (PhotonNetwork.IsMasterClient) HostUpdate(holder);
        if (!held) CheckGrab();
        else if (holder == PhotonNetwork.LocalPlayer.ActorNumber) HolderInput();
    }

    private void LateUpdate()
    {
        int holder = Holder;
        if (holder < 0 || thrownLocally) return;
        var p = Player.Find(holder);
        if (p == null) return;

        if (p.IsLocal && PlayerCam.Local != null)
        {
            // Low in the bottom-left corner of your view, mostly out of the way
            var cam = PlayerCam.Local.Camera.transform;
            Vector3 at = cam.position + cam.forward * 1.0f - cam.right * 0.62f - cam.up * 0.55f;
            Place(at, cam.rotation * Quaternion.Euler(-70f, 0f, 0f));
        }
        else
        {
            // In the left hand
            if (handOf != holder) { hand = FindHand(p); handOf = holder; }
            Vector3 at = hand != null ? hand.position : p.transform.position + Vector3.up * (p.EyeHeight * 0.6f) - p.transform.right * 0.45f;
            Place(at, p.transform.rotation * Quaternion.Euler(-80f, 0f, 0f));
        }
    }

    // Puts the disc's middle (not its off-centre pivot) at a point
    private void Place(Vector3 centre, Quaternion rotation)
    {
        transform.rotation = rotation;
        transform.position = centre - (transform.TransformPoint(localCentre) - transform.position);
    }

    private static Transform FindHand(Player p)
    {
        Transform best = null;
        foreach (var t in p.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "LeftArm4_end") return t;
            if (t.name == "LeftArm3") best = t;
        }
        return best;
    }

    private int homingTarget = -1;

    // The host's gravity for a loose disc: light, so throws glide; a homing throw bends toward its catcher
    private void FixedUpdate()
    {
        if (!photonView.IsMine || body.isKinematic) return;
        body.AddForce(Physics.gravity * GravityScale, ForceMode.Acceleration);

        if (homingTarget < 0 || PhotonNetwork.Time - throwTime > 3.0) { homingTarget = -1; return; }
        var target = Player.Find(homingTarget);
        if (target == null) { homingTarget = -1; return; }
        Vector3 v = body.linearVelocity;
        Vector3 to = target.transform.position + Vector3.up * (target.EyeHeight * 0.6f) - Centre;
        body.linearVelocity = Vector3.RotateTowards(v, to.normalized * v.magnitude, 140f * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
    }

    private float Charge01 => holderCharging ? Mathf.Clamp01((Time.time - holderChargeStart) / ChargeSeconds) : 0f;

    private void UpdateEffects(bool held, int holder)
    {
        // A beam marks a loose disc lying still; a trail follows it while it flies; neither while it's held
        Vector3 c = Centre;
        float speed = (sync != null && !photonView.IsMine && sync.enabled) ? sync.Velocity.magnitude : body.linearVelocity.magnitude;
        bool flying = !held && speed > 2f;
        beam.enabled = !held && !flying;
        beam.SetPosition(0, c);
        beam.SetPosition(1, c + Vector3.up * 10f);
        trail.emitting = flying;

        // Charging: a tick that speeds up and rises in pitch as the throw powers up (the only tell)
        float charge = Charge01;
        if (holderCharging && Time.time >= nextSpark)
        {
            nextSpark = Time.time + Mathf.Lerp(0.18f, 0.05f, charge);
            Sfx.At(SfxKind.Tick, c, 0.25f + charge * 0.3f, 25f, Mathf.Lerp(0.9f, 2f, charge));
        }
    }

    // ─── Grab (each client, its own player) ───────────────────────────────────

    private void CheckGrab()
    {
        var local = Player.Local;
        if (local == null || local.IsCaught || local.IsDownAfterTag || Time.time - lastRequest < 0.2f) return;
        if (Time.time - thrownAt < RegrabDelay) return;
        if (!PlayerRoles.IsTeam(local.Role) || GameModeManager.Instance == null || GameModeManager.Instance.Phase != MatchPhase.Active) return;
        if (!local.TryGetComponent(out CapsuleCollider capsule)) return;
        Vector3 c = Centre;
        if (Vector3.Distance(capsule.ClosestPoint(c), c) > GrabRadius) return;

        lastRequest = Time.time;
        photonView.RPC(nameof(RPC_RequestGrab), RpcTarget.MasterClient);
    }

    // Host: any player the touch trigger meets grabs it (a throw flying into an enemy is theirs)
    private void OnTriggerEnter(Collider other) => HostTouch(other);
    private void OnTriggerStay(Collider other)  => HostTouch(other);

    private void HostTouch(Collider other)
    {
        if (!PhotonNetwork.IsMasterClient || Holder >= 0) return;
        var p = other.GetComponentInParent<Player>();
        if (p != null) TryGrant(p.ActorNumber);
    }

    /// <summary>Local: the holder <paramref name="holder"/> is touching the local player (an enemy): ask to steal.</summary>
    public void RequestSteal(Player holder)
    {
        if (Time.time - lastRequest < 0.25f || holder == null) return;
        lastRequest = Time.time;
        photonView.RPC(nameof(RPC_RequestSteal), RpcTarget.MasterClient, holder.ActorNumber);
    }

    // ─── Holding and throwing (the holder) ────────────────────────────────────

    private void HolderInput()
    {
        var local = Player.Local;
        if (local == null) return;
        KeyCode action = Keybinds.Get(InputAction.Action);

        // A click that places a trap never throws (wait for the key to be let go)
        if (local.Abilities != null && local.Abilities.IsAwaitingAction) { blockedUntilRelease = true; SetCharging(false); }
        else if (!Input.GetKey(action)) blockedUntilRelease = false;

        bool canThrow = !blockedUntilRelease && !PauseMenuManager.IsPaused && local.CanMove && !local.IsCaught;
        if (!charging && canThrow && Input.GetKeyDown(action)) { SetCharging(true); chargeStart = Time.time; }

        float charge = charging ? Mathf.Clamp01((Time.time - chargeStart) / ChargeSeconds) : 0f;
        string bar = new string('|', Mathf.RoundToInt(charge * 10f)).PadRight(10, '.');
        HUDManager.Prompt(action, charging ? $"Throw  {bar}" : "Hold to throw the disc");

        if (charging && (!Input.GetKey(action) || !canThrow))
        {
            SetCharging(false);
            if (canThrow) Throw(charge);
        }
    }

    private void SetCharging(bool on)
    {
        if (charging == on) return;
        charging = on;
        photonView.RPC(nameof(RPC_Charging), RpcTarget.All, on); // Everyone sees it power up
    }

    private void Throw(float charge)
    {
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera.transform : null;
        if (cam == null) return;
        var local = Player.Local;
        Vector3 dir = cam.forward;
        // Leave from just in front of the face, never from inside a wall or the floor
        Vector3 origin = cam.position + dir * 1f;
        if (Physics.SphereCast(cam.position, 0.2f, dir, out RaycastHit hit, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<Player>() == null)
            origin = cam.position + dir * Mathf.Max(0.1f, hit.distance - 0.3f);
        Vector3 velocity = dir * Mathf.Lerp(MinThrowSpeed, MaxThrowSpeed, charge) * ThrowBoost + local.Velocity * 0.5f;
        int homing = HomingThrow ? NearestTeammateAhead(local, dir) : -1;

        thrownLocally = true;
        thrownAt = Time.time;
        Place(origin, Quaternion.LookRotation(dir) * Quaternion.Euler(-90f, 0f, 0f)); // Gone from the hand at once
        Sfx.Here(SfxKind.Whoosh, 0.8f, Mathf.Lerp(1.2f, 0.85f, charge));
        photonView.RPC(nameof(RPC_Throw), RpcTarget.MasterClient, origin, velocity, homing);
    }

    /// <summary>Local: throw speed multiplier (Rocket Disc donut).</summary>
    public static float ThrowBoost { get; set; } = 1f;

    /// <summary>Local: throws curve toward the nearest teammate ahead (Homing Throw donut).</summary>
    public static bool HomingThrow { get; set; }

    private static int NearestTeammateAhead(Player local, Vector3 dir)
    {
        int best = -1;
        float bestScore = float.MaxValue;
        foreach (var p in Player.All)
        {
            if (p == null || p == local || p.Role != local.Role || p.IsDownAfterTag) continue;
            Vector3 to = p.transform.position - local.transform.position;
            if (Vector3.Angle(dir, to) > 75f) continue;
            float score = to.magnitude * (1f + Vector3.Angle(dir, to) / 45f);
            if (score < bestScore) { bestScore = score; best = p.ActorNumber; }
        }
        return best;
    }

    [PunRPC]
    private void RPC_Charging(bool on)
    {
        holderCharging = on;
        holderChargeStart = Time.time;
    }

    // ─── Host ─────────────────────────────────────────────────────────────────

    private void HostUpdate(int holder)
    {
        IgnorePlayerBumps();
        var bounds = LevelInfo.Bounds;

        if (holder >= 0)
        {
            var p = Player.Find(holder);
            // Gone, caught, or teleported (fell off and respawned): drop it, or reset it if it was lost
            if (p == null || p.IsCaught || p.IsDownAfterTag)
            {
                Drop(p != null ? p.transform.position + Vector3.up : lastHolderPosition, Vector3.zero);
                return;
            }
            Vector3 now = p.transform.position;
            if (trackedHolder == holder && Vector3.Distance(now, lastHolderPosition) > 15f) { ResetToSpawn(); return; }
            if (bounds.HasValue && now.y < bounds.Value.min.y - 3f) { ResetToSpawn(); return; }
            trackedHolder = holder;
            lastHolderPosition = now;
            return;
        }

        trackedHolder = -1;
        if (bounds.HasValue && Centre.y < bounds.Value.min.y - 8f) ResetToSpawn(); // Lost over the edge
    }

    // The disc never shoves players (any touch is a grab instead): its solid colliders ignore every player
    private void IgnorePlayerBumps()
    {
        if (Time.time < nextIgnoreRefresh) return;
        nextIgnoreRefresh = Time.time + 1f;
        foreach (var p in Player.All)
        {
            if (p == null) continue;
            foreach (var col in p.GetComponentsInChildren<Collider>(true))
                if (ignoredPlayers.Add(col))
                    foreach (var mine in solids) Physics.IgnoreCollision(mine, col, true);
        }
    }

    /// <summary>Host: puts the disc loose a little above a random spawn point (the level's middle if it has none).</summary>
    public void ResetToSpawn()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        SetHolderProp(-1);
        body.isKinematic = false;
        Place(SpawnSpot(), Quaternion.identity);
        body.position = transform.position;
        body.rotation = transform.rotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        photonView.RPC(nameof(RPC_Fx), RpcTarget.All, (byte)2, Centre);
    }

    /// <summary>
    /// Where the disc appears: a random spawn point (the same places players spawn, so always on solid, open
    /// ground), standing on what's under it; failing that, open ground in the level's middle.
    /// </summary>
    public static Vector3 SpawnSpot()
    {
        var points = SpawnPoint.All;
        if (points.Count > 0)
        {
            var point = points[Random.Range(0, points.Count)].transform.position;
            if (Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit floor, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return floor.point + Vector3.up * 1.3f;
            return point + Vector3.up * 1.3f;
        }

        var b = LevelInfo.Bounds;
        if (b.HasValue)
            for (int i = 0; i < 12; i++)
            {
                Vector2 jitter = Random.insideUnitCircle * (i * 2f);
                Vector3 top = new(b.Value.center.x + jitter.x, b.Value.max.y + 2f, b.Value.center.z + jitter.y);
                if (Physics.Raycast(top, Vector3.down, out RaycastHit hit, b.Value.size.y + 4f, 1 << LevelInfo.StructureLayer, QueryTriggerInteraction.Ignore)
                    && hit.normal.y > 0.7f && hit.collider.GetComponentInParent<DeathZone>() == null)
                    return hit.point + Vector3.up * 1.3f;
            }
        return Spawner.Instance != null ? Spawner.Instance.transform.position + Vector3.up * 2f : Vector3.up * 2f;
    }

    private void Drop(Vector3 at, Vector3 velocity)
    {
        SetHolderProp(-1);
        body.isKinematic = false;
        Place(at, transform.rotation);
        body.position = transform.position;
        body.linearVelocity = velocity;
    }

    private void TryGrant(int actor)
    {
        if (Holder >= 0) return;
        if (GameModeManager.Instance == null || GameModeManager.Instance.Phase != MatchPhase.Active) return;
        if (!PlayerRoles.IsTeam(PlayerRoles.Get(actor))) return;
        if (actor == lastThrower && PhotonNetwork.Time - throwTime < RegrabDelay) return;
        SetHolder(actor, steal: false);
    }

    private void SetHolder(int actor, bool steal)
    {
        SetHolderProp(actor);
        holderSince = PhotonNetwork.Time;
        trackedHolder = -1;
        body.isKinematic = true;
        var who = PhotonNetwork.CurrentRoom.GetPlayer(actor);
        if (who != null) MatchStats.Add(who, steal ? PlayerKeys.StatSteals : PlayerKeys.StatCatches);
        photonView.RPC(nameof(RPC_Fx), RpcTarget.All, steal ? (byte)1 : (byte)0, Centre);
    }

    [PunRPC]
    private void RPC_RequestGrab(PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null) return;
        TryGrant(info.Sender.ActorNumber);
    }

    [PunRPC]
    private void RPC_RequestSteal(int holder, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null || Holder != holder) return;
        if (PhotonNetwork.Time - holderSince < StealImmunity) return;
        var holding = Player.Find(holder);
        if (holding != null && holding.Has(PlayerFlag.Unstealable)) return; // Butter Hands donut
        var mine = PlayerRoles.Get(info.Sender);
        var theirs = PlayerRoles.Get(holder);
        if (!PlayerRoles.IsTeam(mine) || mine == theirs) return;
        SetHolder(info.Sender.ActorNumber, steal: true);
    }

    [PunRPC]
    private void RPC_Throw(Vector3 origin, Vector3 velocity, int homing, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null || Holder != info.Sender.ActorNumber) return;
        lastThrower = info.Sender.ActorNumber;
        homingTarget = homing;
        throwTime = PhotonNetwork.Time;
        holderCharging = false;

        SetHolderProp(-1);
        body.isKinematic = false;
        Place(origin, Quaternion.LookRotation(velocity.sqrMagnitude > 0.01f ? velocity : Vector3.forward) * Quaternion.Euler(-90f, 0f, 0f));
        body.position = transform.position;
        body.rotation = transform.rotation;
        body.linearVelocity = velocity;
        body.angularVelocity = Vector3.up * 25f;
    }

    // 0: grabbed, 1: stolen, 2: reset to a spawn point
    [PunRPC]
    private void RPC_Fx(byte kind, Vector3 at)
    {
        holderCharging = false;
        switch (kind)
        {
            case 0:
                Sfx.At(SfxKind.Pop, at, 1f, 40f, 1.3f);
                Vfx.Burst(at, new Color(1f, 0.85f, 0.3f), 14, 3f, 0.2f, 0.4f);
                break;
            case 1:
                Sfx.At(SfxKind.Snap, at, 1f, 50f);
                Sfx.At(SfxKind.Kill, at, 0.6f, 50f);
                Vfx.Burst(at, new Color(1f, 0.3f, 0.4f), 22, 5f, 0.25f, 0.5f);
                break;
            default:
                Sfx.At(SfxKind.Respawn, at, 1f, 60f);
                Vfx.Ring(at, new Color(1f, 0.85f, 0.3f), 24, 5f, 0.4f, 0.6f);
                break;
        }
    }
}
