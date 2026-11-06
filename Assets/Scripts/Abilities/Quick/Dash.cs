using System.Collections;
using UnityEngine;

/// <summary>
/// Bursts the player in the input direction (relative to the camera), or straight where they
/// are looking with no input. Gravity is suspended for the dash; upward dashes travel less far.
/// </summary>
[AbilityInfo("A quick burst in the direction you're moving (or looking).")]
public class Dash : QuickAbility
{
    [Header("References")]
    [SerializeField] private Transform cam;

    [Header("Dash")]
    [TunableParam("Dash Strength", 5f, 100f)]
    [SerializeField] private float dashStrength = 40f;
    [TunableParam("Duration", 0.05f, 2f)]
    [SerializeField] private float dashDuration = 0.25f;
    [SerializeField] private float dashFovBoost = 15f;

    private Coroutine dashRoutine;

    protected override void Activate()
    {
        if (cam == null) return;
        if (dashRoutine != null) StopCoroutine(dashRoutine);
        dashRoutine = StartCoroutine(DashRoutine());
    }

    private IEnumerator DashRoutine()
    {
        Vector2 move = Owner.Input.MoveInput;
        Vector3 direction = move.sqrMagnitude > 0f
            ? (cam.forward * move.y + cam.right * move.x).normalized
            : cam.forward;

        float scale = direction.y > 0f ? Mathf.Lerp(1f, 0.5f, direction.y) : 1f;

        // IsDashing switches gravity off (PlayerMovement owns gravity)
        Body.linearVelocity = Vector3.zero;
        Body.useGravity     = false;
        Owner.IsDashing     = true;
        Body.AddForce(direction * (dashStrength * scale * Mathf.Sqrt(Owner.AbilityPower)), ForceMode.Impulse);

        if (PlayerCam.Local != null) PlayerCam.Local.ChangeFov(PlayerCam.Local.DefaultFov + dashFovBoost);

        yield return new WaitForSeconds(dashDuration);
        EndDash();
    }

    public override void Cancel()
    {
        if (dashRoutine == null) return;
        StopCoroutine(dashRoutine);
        EndDash();
    }

    private void EndDash()
    {
        dashRoutine = null;
        if (Owner == null) return;
        Owner.IsDashing = false;
        if (PlayerCam.Local != null) PlayerCam.Local.ResetFov();
    }
}
