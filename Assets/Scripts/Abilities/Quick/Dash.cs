using System.Collections;
using UnityEngine;

/// <summary>
/// Bursts the player in the input direction (relative to the camera), or straight where they
/// are looking with no input. Gravity is suspended for the dash; upward dashes travel less far.
/// </summary>
[AbilityInfo("A quick burst in the direction you're moving (or looking).")]
public class Dash : QuickAbility
{
    // What every screen plays when this is used (each ability owns its own: Ability.PlayUseFx)
    public override void PlayUseFx(Player player, Vector3 feet, Vector3 chest, bool mine)
    {
        Sfx.At(SfxKind.Whoosh, chest, 1f);
        Vfx.Burst(chest, Fx.Cyan, 16, 4f, 0.25f, 0.45f, direction: -player.Velocity, spread: 0.5f);
        if (mine) Fx.Shake(0.15f);
    }

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
        Body.AddForce(direction * (dashStrength * scale * Mathf.Sqrt(Owner.AbilityPower)), ForceMode.VelocityChange);

        if (PlayerCam.Local != null) PlayerCam.Local.ChangeFov(PlayerCam.Local.DefaultFov + dashFovBoost);

        // Counted in physics steps, so every dash covers the same distance. Waiting in seconds ended it on whichever
        // frame came next (gravity and braking back up to a frame late: ±0.7 m at 60 fps, more at lower frame rates)
        var step = new WaitForFixedUpdate();
        int steps = Mathf.Max(1, Mathf.RoundToInt(dashDuration / Time.fixedDeltaTime));
        for (int i = 0; i < steps; i++) yield return step;
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
