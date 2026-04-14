using UnityEngine;
using Photon.Pun;

public abstract class ThrowAbility : Ability
{
    [Header("Throw Settings")]
    public float cooldownTime = 3f;
    public float minThrowForce = 10f;
    public float maxThrowForce = 40f;
    public float chargeTime = 2f;          // Seconds to reach max charge

    [Header("Trajectory Settings")]
    [SerializeField] private int trajectorySteps = 60;       // How many points on the arc
    [SerializeField] private float trajectoryTimeStep = 0.05f;

    [Header("References")]
    [SerializeField] private Transform throwOrigin;          // Assign to camera or hand transform
    [SerializeField] private LineRenderer trajectoryLine;    // Add LineRenderer component to player

    protected GameObject heldObject;                         // The visual object held before throwing

    private float lastUseTime = -999f;
    private float chargeStartTime;
    private float currentCharge => Mathf.Clamp01((Time.time - chargeStartTime) / chargeTime);
    public override bool IsAwaitingAction => 
        state == ThrowState.Aiming || state == ThrowState.Charging;

    private enum ThrowState { Idle, Aiming, Charging }
    private ThrowState state = ThrowState.Idle;

    // -------------------------------------------------------------------------
    // Ability Base Implementation
    // -------------------------------------------------------------------------

    public override void TryActivate(AbilityInputEvent inputEvent)
    {
        if (PauseMenuManager.Instance.Paused) return;

        switch (state)
        {
            case ThrowState.Idle:
                if (inputEvent == AbilityInputEvent.Down && CanActivate())
                    EnterAiming();
                break;

            case ThrowState.Aiming:
            case ThrowState.Charging:
                // Q pressed again while active = cancel
                if (inputEvent == AbilityInputEvent.Down)
                    CancelThrow();
                break;
        }
    }

    // LMB down = start charging
    public override void OnActionConfirm()
    {
        if (state == ThrowState.Aiming)
            EnterCharging();
    }

    // LMB up = release throw
    public override void OnActionConfirmUp()
    {
        if (state == ThrowState.Charging)
            ReleaseThrow();
    }

    // Called by InputHandler when Mouse1 is held during aiming
    // Separated from TryActivate because it's a different key
    public void TryCharge(bool down)
    {
        if (state != ThrowState.Aiming) return;
        if (down) EnterCharging();
    }

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (state == ThrowState.Charging)
        {
            UpdateTrajectory();
        }
    }

    // -------------------------------------------------------------------------
    // State Methods
    // -------------------------------------------------------------------------

    private void EnterAiming()
    {
        state = ThrowState.Aiming;
        SpawnHeldObject();
        OnEnterAiming();
    }

    private void EnterCharging()
    {
        state = ThrowState.Charging;
        chargeStartTime = Time.time;
        
        if (trajectoryLine != null)
            trajectoryLine.enabled = true;

        OnEnterCharging();
    }

    private void ReleaseThrow()
    {
        if (throwOrigin == null)
        {
            Debug.LogWarning($"{abilityName}: throwOrigin is not assigned.");
            return;
        }

        float throwForce = Mathf.Lerp(minThrowForce, maxThrowForce, currentCharge);
        Vector3 throwDirection = throwOrigin.forward;

        // Destroy held visual
        if (heldObject != null)
        {
            PhotonNetwork.Destroy(heldObject);
            heldObject = null;
        }

        // Hide trajectory
        if (trajectoryLine != null)
            trajectoryLine.enabled = false;

        // Spawn the actual thrown object and apply force
        GameObject thrown = PhotonNetwork.Instantiate(
            "Object/" + abilityName,
            throwOrigin.position,
            throwOrigin.rotation
        );

        Rigidbody thrownRb = thrown.GetComponent<Rigidbody>();
        if (thrownRb != null)
            thrownRb.AddForce(throwDirection * throwForce, ForceMode.Impulse);

        OnThrow(thrown, throwDirection, throwForce);

        lastUseTime = Time.time;
        state = ThrowState.Idle;
    }

    private void CancelThrow()
    {
        if (heldObject != null)
        {
            PhotonNetwork.Destroy(heldObject);
            heldObject = null;
        }

        if (trajectoryLine != null)
            trajectoryLine.enabled = false;

        state = ThrowState.Idle;
        OnCancelThrow();
    }

    // Virtual hook in case a child needs to react to cancellation
    protected virtual void OnCancelThrow() { }

    // -------------------------------------------------------------------------
    // Trajectory Arc
    // -------------------------------------------------------------------------

    private void UpdateTrajectory()
    {
        if (trajectoryLine == null || throwOrigin == null) return;

        Vector3[] points = CalculateTrajectory(
            throwOrigin.position,
            throwOrigin.forward * Mathf.Lerp(minThrowForce, maxThrowForce, currentCharge)
        );

        trajectoryLine.positionCount = points.Length;
        trajectoryLine.SetPositions(points);
    }

    private Vector3[] CalculateTrajectory(Vector3 origin, Vector3 initialVelocity)
    {
        Vector3[] points = new Vector3[trajectorySteps];
        Vector3 pos = origin;
        Vector3 vel = initialVelocity;

        for (int i = 0; i < trajectorySteps; i++)
        {
            points[i] = pos;

            // Simulate physics step
            vel += Physics.gravity * trajectoryTimeStep;
            pos += vel * trajectoryTimeStep;

            // Stop drawing if arc hits something
            if (Physics.Raycast(pos, vel.normalized, out RaycastHit hit, vel.magnitude * trajectoryTimeStep))
            {
                // Trim array to hit point
                Vector3[] trimmed = new Vector3[i + 1];
                System.Array.Copy(points, trimmed, i + 1);
                return trimmed;
            }
        }

        return points;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    public bool CanActivate() => Time.time >= lastUseTime + cooldownTime;
    public float CooldownRemaining() => Mathf.Max(0, (lastUseTime + cooldownTime) - Time.time);

    // Spawns a visual-only held object parented to the throw origin
    private void SpawnHeldObject()
    {
        if (throwOrigin == null) return;

        heldObject = PhotonNetwork.Instantiate(
            "Object/" + abilityName,
            throwOrigin.position,
            throwOrigin.rotation
        );

        // Disable physics on held object — it's just visual
        if (heldObject.TryGetComponent<Rigidbody>(out var heldRb))
        {
            heldRb.isKinematic = true;
            heldRb.useGravity = false;
        }

        foreach (var col in heldObject.GetComponentsInChildren<Collider>())
            col.enabled = false;

        // Parent to throw origin so it follows the camera
        heldObject.transform.SetParent(throwOrigin);
        heldObject.transform.SetLocalPositionAndRotation(Vector3.forward * 0.5f, Quaternion.identity);
    }

    private void OnDisable()
    {
        // Clean up if ability is interrupted
        if (heldObject != null)
        {
            PhotonNetwork.Destroy(heldObject);
            heldObject = null;
        }

        if (trajectoryLine != null)
            trajectoryLine.enabled = false;

        state = ThrowState.Idle;
    }

    // -------------------------------------------------------------------------
    // Virtual hooks for child classes
    // -------------------------------------------------------------------------

    protected virtual void OnEnterAiming() { }     // Called when player pulls out the object
    protected virtual void OnEnterCharging() { }   // Called when player starts holding mouse1
    protected virtual void OnThrow(GameObject thrown, Vector3 direction, float force) { } // Called after throw
}