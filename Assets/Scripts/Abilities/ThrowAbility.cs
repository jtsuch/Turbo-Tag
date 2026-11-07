using Photon.Pun;
using UnityEngine;

/// <summary>
/// Base for throwables with an aim → charge → release flow:
///   ability key: pull out the object (Aiming) · hold Action: charge · release Action: throw.
///   Pressing the ability key again while aiming or charging puts it away.
///
/// The thrown object is spawned from Resources/Object/{Id} with its launch impulse and power in
/// the instantiation data, so it flies identically on every client. While aiming, a local-only
/// visual copy is held in hand (mirrored on remote players via AbilityHandler).
/// Most throwables need no code beyond an empty subclass — tuning lives in these fields.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public abstract class ThrowAbility : Ability
{
    [Header("Throw")]
    [SerializeField] protected float cooldown = 3f;
    [TunableParam("Min Force", 1f, 100f)]
    [SerializeField] protected float minForce = 10f;
    [TunableParam("Max Force", 1f, 200f)]
    [SerializeField] protected float maxForce = 60f;
    [TunableParam("Charge Time", 0.1f, 5f, HardMin = 0.01f)]
    [SerializeField] protected float chargeSeconds = 1f;
    [Tooltip("Multiplier passed to the thrown object (explosion force, pull strength…).")]
    [TunableParam("Power", 0.1f, 10f)]
    [SerializeField] protected float power = 1f;

    [Header("Trajectory")]
    [SerializeField] private int   trajectorySteps    = 60;
    [SerializeField] private float trajectoryTimeStep = 0.05f;

    [Header("References")]
    [Tooltip("Where throws originate (the first-person ThrowPoint).")]
    [SerializeField] protected Transform  throwOrigin;
    [SerializeField] private LineRenderer trajectoryLine;
    [Tooltip("Where remote players see the object held. Defaults to in front of the chest.")]
    [SerializeField] private Transform    remoteHoldPoint;

    private enum ThrowState { Idle, Aiming, Charging }
    private ThrowState state = ThrowState.Idle;
    private float      chargeStartTime;
    private GameObject heldVisual;
    private Vector3[]  trajectoryBuffer;

    public override AbilitySlot Slot             => AbilitySlot.Throw;
    public override float       BaseCooldown     => cooldown;
    public override bool        UsesActionKey    => true;
    public override bool        IsAwaitingAction => state != ThrowState.Idle;

    private float Charge01 => state == ThrowState.Charging
        ? Mathf.Clamp01((Time.time - chargeStartTime) / Mathf.Max(0.01f, chargeSeconds))
        : 0f;

    private float CurrentForce => Mathf.Lerp(minForce, maxForce, Charge01);

    // ─── Input ────────────────────────────────────────────────────────────────

    public override void HandleInput(AbilityInputEvent inputEvent)
    {
        if (inputEvent != AbilityInputEvent.Down) return;

        if (state == ThrowState.Idle)
        {
            if (IsReady) EnterAiming();
        }
        else
        {
            Cancel();
        }
    }

    public override void OnActionDown()
    {
        if (state != ThrowState.Aiming) return;
        state           = ThrowState.Charging;
        chargeStartTime = Time.time;
    }

    public override void OnActionUp()
    {
        if (state == ThrowState.Charging) Release();
    }

    public override void Cancel()
    {
        if (state == ThrowState.Idle) return;
        state = ThrowState.Idle;
        PutAway();
    }

    // ─── Flow ─────────────────────────────────────────────────────────────────

    private void EnterAiming()
    {
        if (throwOrigin == null)
        {
            Debug.LogWarning($"[{Id}] throwOrigin is not assigned.");
            return;
        }

        state = ThrowState.Aiming;
        SetHeldVisual(true);
        Handler.BroadcastHeldVisual(Id, true);
        if (trajectoryLine != null) trajectoryLine.enabled = true;
    }

    private void Release()
    {
        Launch(out Vector3 start, out Vector3 direction);
        Vector3 impulse = direction * CurrentForce;
        state = ThrowState.Idle;
        PutAway();

        PhotonNetwork.Instantiate(ResourcePath, start, Quaternion.LookRotation(direction, throwOrigin.up), 0,
            SpawnData.Create(impulse, power * Owner.AbilityPower, Owner.photonView.ViewID, ObjectTunableOverrides()));

        StartCooldown();
    }

    /// <summary>
    /// Where a throw starts and which way it goes. The hand (throwOrigin) sits low and to the side of the eyes, so
    /// a throw straight along its forward missed the crosshair by half a metre and clipped door frames, and next
    /// to a wall the hand could be inside or behind it. Now the throw aims at what's under the crosshair (keeping
    /// the hand's own tilt), and starts where the held copy is (so the arc and the throw leave from the object, not
    /// half a metre behind it), or just in front of anything between the eyes and it.
    /// </summary>
    protected void Launch(out Vector3 start, out Vector3 direction)
    {
        Transform eye = throwOrigin.parent != null ? throwOrigin.parent : throwOrigin;
        Vector3 eyePos = eye.position, forward = eye.forward;
        start = throwOrigin.TransformPoint(HeldOffset);

        Vector3 toHand = start - eyePos;
        if (toHand.sqrMagnitude > 0.0001f && FirstHit(eyePos, toHand.normalized, toHand.magnitude + 0.2f, out RaycastHit wall))
            start = eyePos + toHand.normalized * Mathf.Max(0f, wall.distance - 0.25f);

        Vector3 aim = FirstHit(eyePos, forward, 150f, out RaycastHit target) ? target.point : eyePos + forward * 150f;
        Vector3 toAim = aim - start;
        Quaternion tilt = Quaternion.FromToRotation(forward, throwOrigin.forward); // Lobs keep their lift
        direction = toAim.magnitude > 2f && Vector3.Dot(toAim, forward) > 0f ? tilt * toAim.normalized : throwOrigin.forward;
    }

    // The nearest solid thing along a ray, ignoring the thrower's own colliders and triggers
    private bool FirstHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
    {
        nearest = default;
        float best = float.MaxValue;
        int count = Physics.RaycastNonAlloc(origin, direction, hitBuffer, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = hitBuffer[i];
            if (hit.distance >= best || hit.collider.transform.IsChildOf(Owner.transform)) continue;
            best = hit.distance;
            nearest = hit;
        }
        return best < float.MaxValue;
    }

    private static readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    private void PutAway()
    {
        SetHeldVisual(false);
        if (IsLocal) Handler.BroadcastHeldVisual(Id, false);
        if (trajectoryLine != null) trajectoryLine.enabled = false;
    }

    private void Update()
    {
        if (state != ThrowState.Idle) UpdateTrajectory();
    }

    // ─── Held visual ──────────────────────────────────────────────────────────

    // Where the held copy sits in front of the hand (throwOrigin space); throws leave from here
    private static readonly Vector3 HeldOffset = Vector3.forward * 0.5f;

    /// <summary>Shows or hides a local-only copy of the throwable in this player's hand.</summary>
    public void SetHeldVisual(bool visible)
    {
        if (!visible)
        {
            if (heldVisual != null) Destroy(heldVisual);
            heldVisual = null;
            return;
        }
        if (heldVisual != null) return;

        var prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null) return;

        Transform parent = IsLocal ? throwOrigin : remoteHoldPoint != null ? remoteHoldPoint : transform;
        heldVisual = VisualProxy.Create(prefab, parent);

        Vector3 offset = IsLocal || remoteHoldPoint != null
            ? HeldOffset
            : new Vector3(0.4f, Owner.Height * 0.75f, 0.6f);
        // Held the way it will leave the hand (a frisbee lies flat)
        var projectile = prefab.GetComponent<NetworkedProjectile>();
        heldVisual.transform.SetLocalPositionAndRotation(offset, projectile != null ? projectile.LaunchRotation : Quaternion.identity);
    }

    // ─── Trajectory preview ───────────────────────────────────────────────────

    private void UpdateTrajectory()
    {
        if (trajectoryLine == null || throwOrigin == null) return;

        trajectoryBuffer ??= new Vector3[trajectorySteps];
        if (projectilePrefab == null) projectilePrefab = Resources.Load<GameObject>(ResourcePath)?.GetComponent<NetworkedProjectile>();
        Launch(out Vector3 start, out Vector3 direction);
        Vector3 launch = direction * CurrentForce;
        if (projectilePrefab != null) launch = projectilePrefab.LaunchVelocity(launch);
        int count = SimulateArc(start, launch);

        trajectoryLine.positionCount = count;
        trajectoryLine.SetPositions(trajectoryBuffer);
    }

    // The prefab's projectile script: how it launches and whether it falls (for the preview)
    private NetworkedProjectile projectilePrefab;

    // The launch value is a velocity (VelocityChange), matching how throws are launched
    private int SimulateArc(Vector3 position, Vector3 velocity)
    {
        Vector3 gravity = Physics.gravity * (projectilePrefab != null ? projectilePrefab.GravityScale : 1f);
        for (int i = 0; i < trajectorySteps; i++)
        {
            trajectoryBuffer[i] = position;

            velocity += gravity * trajectoryTimeStep;
            Vector3 step = velocity * trajectoryTimeStep;
            if (Physics.Raycast(position, step.normalized, step.magnitude, ~0, QueryTriggerInteraction.Ignore))
                return i + 1;
            position += step;
        }
        return trajectorySteps;
    }
}
