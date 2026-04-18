using UnityEngine;
using Photon.Pun;
using System;

/// <summary>
/// Drives the player's Animator parameters based on movement state, velocity, and vertical
/// camera pitch. Also syncs the pitch value to remote clients via OnPhotonSerializeView so
/// aiming animations look correct for all players.
/// Attach to: ThePlayer prefab — requires JimmyMove, Rigidbody, and an Animator in children.
/// </summary>
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

        // Unity euler angles are 0–360; remap to -180–180 so negative pitch (looking up) works correctly
        float verticalPitch = cameraHolder.localEulerAngles.x;
        if (verticalPitch > 180) verticalPitch -= 360;
        float clampedPitch = Mathf.Clamp(verticalPitch, -90f, 90f);
        // Map pitch to 0–1: 0 = looking fully down, 0.5 = forward, 1 = looking fully up
        verticalLook = Mathf.InverseLerp(90, -90, clampedPitch);

        animator.SetFloat("VerticalDirectionLooking", verticalLook);
    }

    // Photon calls this every network tick to sync verticalLook to remote clients
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

    /// <summary>
    /// Called by Player.SetState whenever the movement state changes.
    /// Sets Animator layer weights and MovementSpeed based on current state and velocity.
    /// MovementSpeed convention: 0 = idle, 0.5 = walk, 1 = sprint/slide/climb-up, -1 = climb-down/wall-left.
    /// </summary>
    public void UpdateAnimator()
    {
        if (animator == null) return;

        animator.SetBool("Prone", player.currentState == Player.MovementState.Prone);
        animator.SetBool("onGround", pm.onGround);
        animator.SetLayerWeight(4, 1f);

        // Layer 2 = wall-run; negative speed = left wall, positive = right wall
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

        // Layer 3 = climb; speed direction matches vertical velocity sign
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

        // Prone: layer 4 disabled; speed buckets drive the blend tree
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
        }
    }
}
