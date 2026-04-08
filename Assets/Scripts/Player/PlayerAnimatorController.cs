using UnityEngine;
using Photon.Pun;
using System;

[RequireComponent(typeof(JimmyMove))]
[RequireComponent(typeof(Rigidbody))]
public class PlayerAnimatorController : MonoBehaviour
{
    [Header("References")]
    public Animator animator;
    public Player player;
    public JimmyMove pm;
    public InputHandler input;
    public Rigidbody rb;
    public Transform cameraHolder;
    public PhotonView view;
    public float verticalLook; // Synchronized variable for vertical look direction

    void Awake()
    {
        if (pm == null) pm = GetComponent<JimmyMove>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (rb == null) rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (!view.IsMine) return;
        // Update camera pitch for aiming animations
        float verticalPitch = cameraHolder.localEulerAngles.x;
        if (verticalPitch > 180) verticalPitch -= 360; // Convert to -180 to 180 range
        float clampedPitch = Mathf.Clamp(verticalPitch, -90f, 90f); // Clamp to avoid extreme values
        verticalLook = Mathf.InverseLerp(90, -90, clampedPitch); // Normalize to 0-1

        animator.SetFloat("VerticalDirectionLooking", verticalLook);
    }

    // This function is called automatically by Photon to sync variables
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting) // Local player sends data
        {
            stream.SendNext(verticalLook);
        }
        else // Remote players receive data
        {
            verticalLook = (float)stream.ReceiveNext();
        }
    }

    private void HandleJumped()
    {
        // Play jump trigger (assumes your Animator has a "Jump" trigger)
        if (animator != null)
            animator.SetTrigger("Jump");
    }

    public void UpdateAnimator()
    {
        if (animator == null) return;

        // Set bool parameters
        animator.SetBool("Prone", player.currentState == Player.MovementState.Prone);
        animator.SetBool("onGround", pm.onGround);
        animator.SetLayerWeight(4, 1f); // Can remove?

        // If wall running
        if (player.currentState == Player.MovementState.WallRun)
        {
            animator.SetLayerWeight(2, 1f);
            if (pm.wallLeft)
                animator.SetFloat("MovementSpeed", -1f);
            else if (pm.wallRight)
                animator.SetFloat("MovementSpeed", 1f);
            return;
        }
        else
            animator.SetLayerWeight(2, 0f);

        // If climbing
        if (player.currentState == Player.MovementState.Climb)
        {
            animator.SetLayerWeight(3, 1f);
            if (rb.linearVelocity.y > 0.1f)
                animator.SetFloat("MovementSpeed", 1f);
            else
                animator.SetFloat("MovementSpeed", -1f);
            return;
        }
        else
            animator.SetLayerWeight(3, 0f);

        float currentSpeed = rb.linearVelocity.magnitude;
        // If prone
        if (player.currentState == Player.MovementState.Prone)
        {
            animator.SetLayerWeight(4, 0f);
            if (currentSpeed < 0.3f)
                animator.SetFloat("MovementSpeed", 0f);
            else if (currentSpeed < player.ProneSpeed + 1f)
                animator.SetFloat("MovementSpeed", 0.5f);
            else
                animator.SetFloat("MovementSpeed", 1f);
            return;
        }
        else
            animator.SetLayerWeight(4, 1f);

        if (player.currentState == Player.MovementState.Idle)
        {
            if (currentSpeed < 0.3f)
                animator.SetFloat("MovementSpeed", 0f);
            else if (!input.Sprint)
                animator.SetFloat("MovementSpeed", 0.5f);
            else
                animator.SetFloat("MovementSpeed", 1f);
            return;
        }
    }
}
