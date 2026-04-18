using UnityEngine;
using Photon.Pun;
using System.Collections;

public class Dash : QuickAbility
{
    [Header("References")]
    public Transform cam;
    public Player player;

    [Header("Modifiers")]
    [TunableParam("Dash Strength", 5f, 100f)]
    public float dashStrength = 40f;
    [TunableParam("Duration", 0.05f, 2f)]
    public float dashDuration = 0.25f;
    [TunableParam("Cooldown", 0.1f, 15f)]
    public float dashCooldown = 1f;
    private Coroutine dashCoroutine;

    protected override void Awake()
    {
        base.Awake(); // assigns rb and pm in Ability.Awake()

        // ensure cooldownTime uses the inspector value
        cooldownTime = dashCooldown;
    }

    void OnDisable()
    {
        // If component is disabled while dashing, stop the dash and restore state
        CancelDash();
    }

    void OnDestroy()
    {
        CancelDash();
    }

    protected override void OnKeyDown()
    {
        // If we are already dashing, reset the existing dash
        if (dashCoroutine != null)
        {
            StopCoroutine(dashCoroutine);
            dashCoroutine = null;
        }

        PlayerCam.Instance.ChangeFov(75f);
        dashCoroutine = StartCoroutine(DashRoutine());
    }

    private IEnumerator DashRoutine()
    {
        // Capture strong local references so we don't rely on potentially changed fields later
        Rigidbody rbLocal = rb;
        Transform camLocal = cam;

        if (rbLocal == null || camLocal == null)
        {
            Debug.LogWarning("Dashing aborted: missing components");
            yield break;
        }

        // First, cut off momentum
        rbLocal.linearVelocity = Vector3.zero;

        // Read input for direction (owner only)
        float moveHorizontal = Input.GetAxis("Horizontal");
        float moveVertical = Input.GetAxis("Vertical");

        // Calculate dash direction relative to camera
        Vector3 dashDirection;
        if (moveHorizontal != 0f || moveVertical != 0f)
            dashDirection = (camLocal.forward * moveVertical + camLocal.right * moveHorizontal).normalized;
        else
            dashDirection = camLocal.forward.normalized;

        // If dashing upwards, dash less far
        float dashScale = 1f;
        if (dashDirection.y > 0f)
        {
            dashScale = Mathf.Lerp(1.0f, 0.5f, dashDirection.y);
        }

        // Apply dash state + physics
        player.IsDashing = true;
        rbLocal.useGravity = false;
        rbLocal.AddForce(dashDirection * (dashStrength * dashScale), ForceMode.Impulse);

        // Wait the dash duration but bail early if object is destroyed/disabled
        float timer = 0f;
        while (timer < dashDuration)
        {
            // If either component has been destroyed, exit early
            if (rbLocal == null) yield break;
            timer += Time.deltaTime;
            yield return null;
        }

        // End the dash (final safety checks)
        if (rbLocal != null)
        {
            player.IsDashing = false;
            rbLocal.useGravity = true;
            PlayerCam.Instance.ChangeFov(60f);
        }

        dashCoroutine = null;
    }

    private void CancelDash()
    {
        if (dashCoroutine != null)
        {
            StopCoroutine(dashCoroutine);
            dashCoroutine = null;
        }
        //PlayerCam.Instance.ChangeFov(60f);
        // Try to safely restore state if possible
        if (pm != null) player.IsDashing = false;
        if (rb != null) rb.useGravity = true;
    }
}
