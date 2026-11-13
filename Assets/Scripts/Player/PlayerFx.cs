using Photon.Pun;
using UnityEngine;

/// <summary>
/// Movement effects on penguins:
///   - jump puffs and hard-landing thuds other players see and hear (the owner sends one small RPC each);
///   - on other players' bodies (worked out on each screen from the synced movement, nothing extra sent): a
///     squash on landing and a stretch on take-off (a spring on the skeleton's scale, so they look bouncy),
///     dust puffs behind running feet and a trail behind slides.
/// Added to every copy by <see cref="Player"/>, so the RPC lines up.
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

    // ─── Squash and stretch, dust (other players' bodies) ─────────────────────

    private float     squash, squashVelocity; // + stretches up, − squashes down
    private float     lastVy, nextDust;
    private bool      squashing;

    private void Update()
    {
        if (player.IsLocal || player.IsDownAfterTag) return;

        // Take-off and landing, read from the synced velocity
        float vy = player.Velocity.y;
        if (lastVy < -6f && vy > -1f) Bounce(-Mathf.Clamp01((-lastVy - 4f) / 26f) * 0.35f - 0.08f); // Landed: squash
        else if (lastVy < 2f && vy > 9f) Bounce(0.22f);                                          // Jumped: stretch
        lastVy = vy;

        // Dust behind running feet, a trail behind slides
        Vector3 flat = Vector3.ProjectOnPlane(player.Velocity, Vector3.up);
        float speed = flat.magnitude;
        bool sliding = player.State == Player.MovementState.Prone && speed > 6f;
        if ((speed > 10f || sliding) && Time.time >= nextDust && OnGround())
        {
            nextDust = Time.time + (sliding ? 0.05f : 0.22f);
            Vector3 behind = transform.position - flat.normalized * 0.3f + Vector3.up * 0.1f;
            Vfx.Burst(behind, new Color(0.85f, 0.85f, 0.9f, sliding ? 0.6f : 0.45f), sliding ? 3 : 2, 1.2f, sliding ? 0.35f : 0.28f, 0.5f,
                direction: Vector3.up - flat.normalized * 0.5f, spread: 0.6f);
        }
    }

    private bool OnGround() =>
        Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, 0.45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

    private void Bounce(float amount)
    {
        squashing = true;
        squashVelocity += amount * 28f;
    }

    private void LateUpdate()
    {
        if (!squashing) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        squashVelocity += (-260f * squash - 16f * squashVelocity) * dt;
        squash += squashVelocity * dt;
        squash = Mathf.Clamp(squash, -0.4f, 0.4f);

        if (Mathf.Abs(squash) < 0.002f && Mathf.Abs(squashVelocity) < 0.02f)
        {
            squash = squashVelocity = 0f;
            squashing = false;
        }
        player.SetBounce(1f + squash); // Settled: exactly 1 again
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
        // Jumps and hard landings give someone away on the sound visualizer too
        if (player != null) SoundVisualizer.Notify(feet, landed ? Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(8f, 30f, speed)) : 0.5f, player.ActorNumber);
    }
}
