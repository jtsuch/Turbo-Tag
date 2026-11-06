using UnityEngine;

/// <summary>
/// Places a security camera on a wall (vertical surfaces only). Once one is placed the ability key
/// toggles looking through it: the player's view moves to the camera, movement is locked, and the
/// mouse turns the camera head (everyone sees it turn). No cooldown between looks. If the camera is
/// gone (destroyed, or the owner left), the key places a new one. Pausing or being trapped only cancels
/// placing (Cancel); watching continues until the key is pressed again.
/// Standing next to your camera shows "F  Withdraw camera" (the Grab key): it comes back to you, usable again
/// after <see cref="withdrawCooldown"/>.
/// </summary>
[AbilityInfo("Place a camera on a wall. Press again to watch through it (you can't move while watching), and again to stop.",
             DisplayName = "Camera")]
public class SpyCamera : TrapAbility
{
    [Header("Camera")]
    [Tooltip("How far from vertical a surface may be and still take a camera (0 = only perfectly vertical walls).")]
    [SerializeField, Range(0f, 1f)] private float maxSurfaceTilt = 0.35f;
    [Tooltip("How close (m) you must be to pick your camera back up.")]
    [SerializeField] private float withdrawRange = 3f;
    [Tooltip("Seconds before a withdrawn camera can be placed again.")]
    [SerializeField] private float withdrawCooldown = 2f;

    private SpyCameraObject placed;
    private bool            viewing;

    public override void HandleInput(AbilityInputEvent inputEvent)
    {
        if (inputEvent != AbilityInputEvent.Down) return;
        if (placed != null) { SetViewing(!viewing); return; }
        base.HandleInput(inputEvent);
    }

    // Walls only: the normal is (close to) horizontal
    protected override bool IsValidSurface(Vector3 normal) => Mathf.Abs(normal.y) <= maxSurfaceTilt;

    // Upright, facing straight out of the wall
    protected override Quaternion PlacementRotation(RaycastHit hit, Transform view)
    {
        Vector3 outward = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
        if (outward.sqrMagnitude < 0.0001f) outward = -view.forward;
        return Quaternion.LookRotation(outward.normalized, Vector3.up);
    }

    protected override void OnPlaced(GameObject placedObject) =>
        placed = placedObject != null ? placedObject.GetComponent<SpyCameraObject>() : null;

    // ─── Viewing ──────────────────────────────────────────────────────────────

    private void SetViewing(bool on)
    {
        if (on && (placed == null || PlayerCam.Local == null || Owner.IsCaught)) on = false;
        if (on == viewing) return;
        viewing = on;

        Owner.MovementLocked = on;
        var initializer = Owner.GetComponent<PlayerInitializer>();
        if (initializer != null) initializer.SetOwnBodyVisible(on); // See yourself from the camera
        if (PlayerCam.Local != null)
            PlayerCam.Local.SetViewOverride(on ? placed.ViewPoint : null, on ? placed.Look : null);
    }

    // LateUpdate, not Update: TrapAbility's own Update moves the placement hologram
    private void LateUpdate()
    {
        if (viewing && (placed == null || Owner.IsCaught)) SetViewing(false);
        UpdateWithdraw();
    }

    // ─── Withdraw (pick it back up) ───────────────────────────────────────────

    private void UpdateWithdraw()
    {
        if (!IsLocal || placed == null || viewing || Owner.IsCaught) return;
        Vector3 chest = Owner.transform.position + Vector3.up * 1.2f;
        if (Vector3.Distance(chest, placed.transform.position) > withdrawRange) return;

        KeyCode key = Keybinds.Get(InputAction.Grab);
        HUDManager.Prompt(key, "Withdraw camera");
        if (!PauseMenuManager.IsPaused && Input.GetKeyDown(key)) Withdraw();
    }

    private void Withdraw()
    {
        Vector3 at = placed.transform.position;
        Photon.Pun.PhotonNetwork.Destroy(placed.gameObject);
        placed = null;
        RefundCharge(withdrawCooldown);
        Sfx.At(SfxKind.Pop, at, 0.8f, 20f);
        Vfx.Burst(at, new Color(0.13f, 0.83f, 0.93f), 12, 3f, 0.2f, 0.4f);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        SetViewing(false);
    }
}
