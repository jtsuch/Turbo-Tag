using Photon.Pun;
using UnityEngine;

/// <summary>
/// Jump and landing effects other players see and hear (a puff and a thud under this penguin's feet). The owner
/// tells the others when it jumps or lands hard (one small RPC each); a hard landing also kicks up dust on the
/// owner's own screen. Added to every copy by <see cref="Player"/>, so the RPC lines up.
/// </summary>
[RequireComponent(typeof(Player))]
public class PlayerFx : MonoBehaviourPun
{
    private const float HardLanding = 14f; // m/s falling: below this a landing is quiet for others

    private Player player;
    private float  fallSpeed; // Fastest fall since leaving the ground (owner)

    private void Awake() => player = GetComponent<Player>();

    private void OnEnable()
    {
        if (player == null) return;
        player.Jumped += HandleJumped;
        player.Landed += HandleLanded;
    }

    private void OnDisable()
    {
        if (player == null) return;
        player.Jumped -= HandleJumped;
        player.Landed -= HandleLanded;
    }

    private void FixedUpdate()
    {
        if (!player.IsLocal) return;
        if (player.IsGrounded) return;
        fallSpeed = Mathf.Max(fallSpeed, -player.Body.linearVelocity.y);
    }

    private void HandleJumped()
    {
        if (!player.IsLocal) return;
        fallSpeed = 0f;
        if (!PhotonNetwork.InRoom) return;
        photonView.RPC(nameof(RPC_MoveFx), RpcTarget.Others, false, transform.position, 0f);
    }

    private void HandleLanded()
    {
        if (!player.IsLocal) return;
        float speed = fallSpeed;
        fallSpeed = 0f;
        if (speed < HardLanding) return;

        Vfx.Ring(transform.position, new Color(0.85f, 0.85f, 0.9f, 0.8f), 18, 5f, 0.45f, 0.45f);
        if (speed > 28f) Fx.Shake(0.2f);
        if (PhotonNetwork.InRoom) photonView.RPC(nameof(RPC_MoveFx), RpcTarget.Others, true, transform.position, speed);
    }

    [PunRPC]
    private void RPC_MoveFx(bool landed, Vector3 feet, float speed)
    {
        if (player != null && player.IsDownAfterTag) return;
        if (landed) Fx.Land(feet, speed);
        else        Fx.Jump(feet);
    }
}
