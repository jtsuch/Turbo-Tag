using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

[RequireComponent(typeof(PhotonView))]
public class Flappy : BasicAbility
{
    [Header("References")]
    private PhotonView view;

    [Header("Modifiers")]
    private float glideFallSpeed = 2f;

    private bool isGliding = false;

    protected override void Awake()
    {
        base.Awake();
        view = GetComponent<PhotonView>();
    }

    protected override void OnKeyDown()
    {
        if (view == null || rb == null) return;
        isGliding = true;
        if (!Player.Instance.IsGrounded)
            isActive = true;
    }

    protected override void OnKeyUp()
    {
        StopAbility();
    }

    private void Update()
    {
        if (view == null || rb == null) return;
        
        if (isGliding)
        {
            if (Player.Instance.IsGrounded) isActive = false; // Regenerate the timer, but remain in glide state
            else 
            {
                isActive = true; // Bring the timer back when air borne 
                HUDManager.Instance.timer.SetActive(true);
            }

            // Only limit downward velocity (negative y), not upward
            if (rb.linearVelocity.y < -glideFallSpeed)
            {
                Player.Instance.AirControlMult = 0.5f;
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, -glideFallSpeed, rb.linearVelocity.z);
            }

        }
        else
        {
            Player.Instance.AirControlMult = 0.1f;
        }
    }

    protected override void StopAbility()
    {
        if (view == null || rb == null) return;
        isGliding = false;
        isActive = false;
    }
}
