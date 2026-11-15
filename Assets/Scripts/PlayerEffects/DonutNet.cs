using Photon.Pun;
using UnityEngine;

/// <summary>
/// The networked side of donut effects, on every player copy (added by Player, so RPCs line up):
///   - moments everyone sees: decoys running off, a smoke bomb, a teleport blink;
///   - effects on other players: Freeze Everyone Else, Time Warp (they apply it to themselves; shields block it),
///     Giant Slayer flings (sent to the flung player's owner, who pushes their own body);
///   - fields from synced flags (<see cref="PlayerFlag"/>): every screen pulls its own player toward a Magnet and
///     pushes it away from a Repel Field, and draws the rings, the gold sparkle and jetpack flames.
/// </summary>
[RequireComponent(typeof(Player))]
public class DonutNet : MonoBehaviourPun
{
    private const float MagnetRange = 15f, MagnetPull = 30f;
    private const float RepelRange  = 5f,  RepelPush  = 80f;

    private Player player;
    private float nextVisual;

    private void Awake() => player = GetComponent<Player>();

    // ─── Owner calls ──────────────────────────────────────────────────────────

    public void Decoys()  => photonView.RPC(nameof(RPC_Decoys), RpcTarget.All, transform.position, transform.eulerAngles.y);
    public void Smoke()   => photonView.RPC(nameof(RPC_Smoke), RpcTarget.All, transform.position + Vector3.up);
    public void FreezeOthers(float seconds) => photonView.RPC(nameof(RPC_FreezeOthers), RpcTarget.All, seconds);
    public void TimeWarp(float seconds)     => photonView.RPC(nameof(RPC_TimeWarp), RpcTarget.All, seconds);
    public void Blink(Vector3 from, Vector3 to) => photonView.RPC(nameof(RPC_Blink), RpcTarget.All, from, to);

    /// <summary>Owner: throws <paramref name="target"/> along <paramref name="velocity"/> (their own screen does the push).</summary>
    public void Fling(Player target, Vector3 velocity)
    {
        if (target == null || target.Owner == null) return;
        target.GetComponent<DonutNet>().photonView.RPC(nameof(RPC_Fling), target.Owner, velocity, player.ActorNumber);
    }

    // ─── RPCs ─────────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_Decoys(Vector3 at, float yaw)
    {
        Sfx.At(SfxKind.Pop, at + Vector3.up, 1f, 40f, 0.8f);
        Vfx.Burst(at + Vector3.up, Color.white, 30, 4f, 0.4f, 0.6f);
        for (int i = 0; i < 3; i++)
        {
            float angle = yaw + 90f * (i - 1) + Random.Range(-20f, 20f);
            Decoy.Create(player, at, Quaternion.Euler(0f, angle, 0f) * Vector3.forward);
        }
    }

    [PunRPC]
    private void RPC_Smoke(Vector3 at)
    {
        Sfx.At(SfxKind.Blow, at, 1f, 40f, 0.7f);
        Sfx.At(SfxKind.Pop, at, 0.8f, 40f, 0.6f);
        for (int i = 0; i < 4; i++)
            Vfx.Burst(at + Random.insideUnitSphere, new Color(0.75f, 0.75f, 0.8f, 0.85f), 30, 3.5f, 2.2f, 6f);
    }

    [PunRPC]
    private void RPC_FreezeOthers(float seconds, PhotonMessageInfo info)
    {
        Sfx.At(SfxKind.Ice, transform.position, 1f, 200f, 0.7f);
        var local = Player.Local;
        if (local == null || local == player || local.IsCaught || !local.AffectedBy(player.ActorNumber)) return;
        local.HoldFor(seconds);
        local.IceFor(seconds);
        PlayerEffect.Apply(typeof(FrozenEffect), local, seconds);
        Fx.Frost(local.transform.position + Vector3.up);
    }

    [PunRPC]
    private void RPC_TimeWarp(float seconds)
    {
        Sfx.Here(SfxKind.ZapDown, 0.7f, 0.5f);
        var local = Player.Local;
        if (local == null || local == player || !local.AffectedBy(player.ActorNumber)) return;
        PlayerEffect.Apply(typeof(SlowedEffect), local, seconds);
    }

    [PunRPC]
    private void RPC_Fling(Vector3 velocity, int fromActor)
    {
        if (!player.IsLocal || player.IsCaught || !player.AffectedBy(fromActor)) return;
        WorldFx.Push(player.Body, velocity, ForceMode.VelocityChange, fromActor);
        Fx.Shake(0.5f);
        Sfx.Here(SfxKind.Boing, 0.8f, 0.8f);
    }

    [PunRPC]
    private void RPC_Blink(Vector3 from, Vector3 to)
    {
        Sfx.At(SfxKind.Warp, from, 1f, 40f);
        Sfx.At(SfxKind.Warp, to, 1f, 40f, 1.2f);
        Vfx.Burst(from + Vector3.up, new Color(0.55f, 0.36f, 0.96f), 24, 4f, 0.3f, 0.5f);
        Vfx.Burst(to + Vector3.up, new Color(0.55f, 0.36f, 0.96f), 24, 4f, 0.3f, 0.5f);
    }

    // ─── Fields and looks (every screen) ──────────────────────────────────────

    private void FixedUpdate()
    {
        if (player.IsLocal || player.IsDownAfterTag) return;
        bool magnet = player.Has(PlayerFlag.Magnet), repel = player.Has(PlayerFlag.Repel);
        if (!magnet && !repel) return;

        // This screen moves only its own player
        var local = Player.Local;
        if (local == null || local.IsCaught || !local.AffectedBy(player.ActorNumber)) return;
        Vector3 to = player.transform.position - local.transform.position;
        float d = to.magnitude;
        if (d < 0.1f) return;
        if (magnet && d < MagnetRange)
            WorldFx.Push(local.Body, to / d * MagnetPull * (1f - d / MagnetRange * 0.5f), ForceMode.Acceleration, player.ActorNumber);
        if (repel && d < RepelRange)
            WorldFx.Push(local.Body, (-to / d + Vector3.up * 0.3f) * RepelPush, ForceMode.Acceleration, player.ActorNumber);
    }

    private void Update()
    {
        if (player.IsDownAfterTag || Time.time < nextVisual) return;
        nextVisual = Time.time + 0.15f;
        Vector3 feet = transform.position, chest = feet + Vector3.up * 1.4f;

        if (player.Has(PlayerFlag.Repel))  Vfx.Ring(feet, new Color(0.4f, 0.8f, 1f, 0.6f), 16, RepelRange * 2.5f, 0.25f, 0.35f);
        if (player.Has(PlayerFlag.Magnet)) Vfx.Burst(chest, new Color(0.9f, 0.3f, 0.9f), 6, -6f, 0.2f, 0.5f);
        if (player.Has(PlayerFlag.Golden) && !player.IsLocal) Vfx.Burst(chest, new Color(1f, 0.85f, 0.3f), 3, 1.5f, 0.15f, 0.5f);
        if (player.Has(PlayerFlag.Jetpack))
            Vfx.Burst(feet, new Color(1f, 0.55f, 0.15f), 6, 5f, 0.25f, 0.3f, gravity: true, direction: Vector3.down, spread: 0.3f);
    }
}
