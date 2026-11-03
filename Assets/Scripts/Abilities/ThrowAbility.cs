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
    [SerializeField] private Transform    throwOrigin;
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
        Vector3 impulse = throwOrigin.forward * CurrentForce;
        state = ThrowState.Idle;
        PutAway();

        PhotonNetwork.Instantiate(ResourcePath, throwOrigin.position, throwOrigin.rotation, 0,
            SpawnData.Create(impulse, power, Owner.photonView.ViewID, ObjectTunableOverrides()));

        StartCooldown();
    }

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
            ? Vector3.forward * 0.5f
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
        Vector3 launch = throwOrigin.forward * CurrentForce;
        if (projectilePrefab != null) launch = projectilePrefab.LaunchVelocity(launch);
        int count = SimulateArc(throwOrigin.position, launch);

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
