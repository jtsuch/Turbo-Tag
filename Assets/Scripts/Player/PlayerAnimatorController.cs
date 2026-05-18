using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

/// <summary>
/// Drives the player Animator every frame and syncs all relevant parameters to remote clients
/// via OnPhotonSerializeView. The owner samples real state each Update; remote clients receive
/// the packed values and apply them identically.
///
/// Animator parameter contract (set these up in the Animator Controller):
///   Floats  : MovementSpeed (-1..1), VerticalDirectionLooking (0..1)
///   Bools   : OnGround, IsWallRunning, IsClimbing, IsProned, IsCrouching, IsHanging
///   Triggers: Jump, Land
///
/// Layer weight contract (layers must match these indices):
///   0 Base Layer     — always weight 1 (locomotion)
///   1 RightArm       — always weight 1 (gun-arm pose, masked)
///   2 Wall Run Layer — blends to 1 only while wall-running
///   3 Climb Layer    — blends to 1 only while climbing
///   4 HeadLayer      — always weight 1 (vertical aim, masked)
///
/// MovementSpeed convention:
///   Wall-run  : -1 = left wall,  +1 = right wall
///   Climb     : -1 = descending, +1 = ascending
///   Locomotion: 0 = idle, 0.5 = walk/crouch-move, 1 = sprint/slide
/// </summary>
[RequireComponent(typeof(JimmyMove))]
[RequireComponent(typeof(Rigidbody))]
public class PlayerAnimatorController : MonoBehaviour, IPunObservable
{
    // ─── Inspector References ─────────────────────────────────────────────────
    [Header("References")]
    public Animator   animator;
    public Player     player;
    public JimmyMove  pm;
    public Rigidbody  rb;
    public Transform  cameraHolder;
    public PhotonView view;

    // ─── Layer Indices ────────────────────────────────────────────────────────
    private const int L_ARM     = 1;
    private const int L_WALLRUN = 2;
    private const int L_CLIMB   = 3;
    private const int L_HEAD    = 4;

    // ─── Animator Parameter Hashes ────────────────────────────────────────────
    private static readonly int H_MoveSpeed   = Animator.StringToHash("MovementSpeed");
    private static readonly int H_VertLook    = Animator.StringToHash("VerticalDirectionLooking");
    private static readonly int H_OnGround    = Animator.StringToHash("onGround");    // lowercase 'o'
    private static readonly int H_WallRunning = Animator.StringToHash("IsWallRunning");
    private static readonly int H_Climbing    = Animator.StringToHash("IsClimbing");
    private static readonly int H_Proned      = Animator.StringToHash("Prone");       // was "IsProned"
    private static readonly int H_Crouching   = Animator.StringToHash("IsCrouching");
    private static readonly int H_Hanging     = Animator.StringToHash("IsHanging");
    private static readonly int H_Jump        = Animator.StringToHash("Jump");
    private static readonly int H_Land        = Animator.StringToHash("Land");

    // ─── Valid Parameter Cache ────────────────────────────────────────────────
    // Populated from the Animator Controller's actual parameters at Start.
    // Setters are no-ops for any hash not in this set, preventing per-frame
    // "Parameter does not exist" spam when the Animator Controller is incomplete.
    private readonly HashSet<int> validHashes = new();
    private int validLayerCount = 0;

    // ─── Synced Parameters ────────────────────────────────────────────────────
    private float netMovementSpeed;
    private float netVerticalLook;
    private bool  netOnGround;
    private bool  netIsWallRunning;
    private bool  netIsClimbing;
    private bool  netIsProned;
    private bool  netIsCrouching;
    private bool  netIsHanging;

    // ─── Remote Scale Tracking ────────────────────────────────────────────────
    private bool _prevIsProned;
    private bool _prevIsCrouching;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (pm       == null) pm       = GetComponent<JimmyMove>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (rb       == null) rb       = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        CacheAnimatorInfo();

        // Auto-register so PUN2 calls OnPhotonSerializeView without needing Inspector setup.
        if (view != null && !view.ObservedComponents.Contains(this))
            view.ObservedComponents.Add(this);

        if (player == null) return;
        player.OnJump += HandleJump;
        player.OnLand += HandleLand;
    }

    private void OnDestroy()
    {
        if (player == null) return;
        player.OnJump -= HandleJump;
        player.OnLand -= HandleLand;
    }

    private void CacheAnimatorInfo()
    {
        validHashes.Clear();
        validLayerCount = 0;
        if (animator == null) return;

        foreach (var param in animator.parameters)
            validHashes.Add(param.nameHash);

        validLayerCount = animator.layerCount;
    }

    private void Update()
    {
        if (animator == null) return;

        if (view.IsMine)
            SampleLocalState();
        else
            SyncRemoteScale();

        PushToAnimator();
        UpdateLayerWeights();
    }

    // Drives visual scale on remote clients when their synced stance changes.
    // The owner calls Player.ApplyScale directly; remote clients mirror it here.
    private void SyncRemoteScale()
    {
        if (player == null) return;
        if (netIsProned == _prevIsProned && netIsCrouching == _prevIsCrouching) return;

        _prevIsProned    = netIsProned;
        _prevIsCrouching = netIsCrouching;

        if (netIsProned)
            player.ApplyScale(0.35f);
        else if (netIsCrouching)
            player.ApplyScale(0.7f);
        else
            player.ApplyScale(1f);
    }

    // ─── Local Sampling ───────────────────────────────────────────────────────

    private void SampleLocalState()
    {
        var   state = player.currentState;
        float speed = rb.linearVelocity.magnitude;

        netOnGround      = pm.onGround;
        netIsWallRunning = state == Player.MovementState.WallRun;
        netIsClimbing    = state == Player.MovementState.Climb;
        netIsProned      = state == Player.MovementState.Prone;
        netIsCrouching   = state == Player.MovementState.Crouch;
        netIsHanging     = state == Player.MovementState.Hang;
        netMovementSpeed = ComputeMovementSpeed(state, speed);

        float raw = cameraHolder.localEulerAngles.x;
        if (raw > 180f) raw -= 360f;
        netVerticalLook = Mathf.InverseLerp(90f, -90f, Mathf.Clamp(raw, -90f, 90f));
    }

    private float ComputeMovementSpeed(Player.MovementState state, float speed)
    {
        switch (state)
        {
            case Player.MovementState.WallRun:
                return pm.wallRight ? 1f : -1f;

            case Player.MovementState.Climb:
                return rb.linearVelocity.y > 0.1f ? 1f : -1f;

            case Player.MovementState.Prone:
                if (speed < 0.3f) return 0f;
                return speed < player.ProneSpeed + 1f ? 0.5f : 1f;

            default:
                // When grounded with no movement input, return idle regardless of residual velocity.
                // This prevents floor-contact micro-forces from triggering the walk animation.
                bool hasInput = player.Input != null && player.Input.MoveInput.magnitude > 0.1f;
                if (pm.onGround && !hasInput) return 0f;
                if (speed < 0.3f) return 0f;
                return (player.Input != null && player.Input.Sprint) ? 1f : 0.5f;
        }
    }

    // ─── Animator Push ────────────────────────────────────────────────────────

    private void PushToAnimator()
    {
        SafeSetFloat(H_MoveSpeed, netMovementSpeed, 0.08f, Time.deltaTime);
        SafeSetFloat(H_VertLook,  netVerticalLook,  0.08f, Time.deltaTime);

        SafeSetBool(H_OnGround,    netOnGround);
        SafeSetBool(H_WallRunning, netIsWallRunning);
        SafeSetBool(H_Climbing,    netIsClimbing);
        SafeSetBool(H_Proned,      netIsProned);
        SafeSetBool(H_Crouching,   netIsCrouching);
        SafeSetBool(H_Hanging,     netIsHanging);
    }

    private void UpdateLayerWeights()
    {
        if (validLayerCount > L_ARM)  animator.SetLayerWeight(L_ARM,  1f);
        if (validLayerCount > L_HEAD) animator.SetLayerWeight(L_HEAD, 1f);

        float blend = Time.deltaTime * 8f;
        if (validLayerCount > L_WALLRUN)
            animator.SetLayerWeight(L_WALLRUN,
                Mathf.MoveTowards(animator.GetLayerWeight(L_WALLRUN), netIsWallRunning ? 1f : 0f, blend));
        if (validLayerCount > L_CLIMB)
            animator.SetLayerWeight(L_CLIMB,
                Mathf.MoveTowards(animator.GetLayerWeight(L_CLIMB), netIsClimbing ? 1f : 0f, blend));
    }

    // ─── Safe Animator Helpers ────────────────────────────────────────────────
    // No-ops when the Animator Controller doesn't declare the parameter.
    // Prevents per-frame "Parameter 'Hash XXXX' does not exist" spam.

    private void SafeSetFloat(int hash, float value, float damp, float dt)
    {
        if (validHashes.Contains(hash)) animator.SetFloat(hash, value, damp, dt);
    }

    private void SafeSetBool(int hash, bool value)
    {
        if (validHashes.Contains(hash)) animator.SetBool(hash, value);
    }

    private void SafeSetTrigger(int hash)
    {
        if (validHashes.Contains(hash)) animator.SetTrigger(hash);
    }

    // ─── Events ───────────────────────────────────────────────────────────────

    private void HandleJump()
    {
        if (animator == null) return;
        SafeSetTrigger(H_Jump);
        if (view.IsMine)
            view.RPC(nameof(RPC_AnimTrigger), RpcTarget.Others, "Jump");
    }

    private void HandleLand()
    {
        if (animator == null) return;
        SafeSetTrigger(H_Land);
        if (view.IsMine)
            view.RPC(nameof(RPC_AnimTrigger), RpcTarget.Others, "Land");
    }

    [PunRPC]
    private void RPC_AnimTrigger(string triggerName)
    {
        if (animator == null) return;
        int hash = Animator.StringToHash(triggerName);
        SafeSetTrigger(hash);
    }

    public void UpdateAnimator() { }

    // ─── Network Sync ─────────────────────────────────────────────────────────

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo _)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(netMovementSpeed);
            stream.SendNext(netVerticalLook);
            stream.SendNext(netOnGround);

            byte bits = 0;
            if (netIsWallRunning) bits |= 1;
            if (netIsClimbing)    bits |= 2;
            if (netIsProned)      bits |= 4;
            if (netIsCrouching)   bits |= 8;
            if (netIsHanging)     bits |= 16;
            stream.SendNext(bits);
        }
        else
        {
            netMovementSpeed = (float)stream.ReceiveNext();
            netVerticalLook  = (float)stream.ReceiveNext();
            netOnGround      = (bool) stream.ReceiveNext();
            byte bits        = (byte) stream.ReceiveNext();
            netIsWallRunning = (bits & 1)  != 0;
            netIsClimbing    = (bits & 2)  != 0;
            netIsProned      = (bits & 4)  != 0;
            netIsCrouching   = (bits & 8)  != 0;
            netIsHanging     = (bits & 16) != 0;
        }
    }
}
