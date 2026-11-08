using Photon.Pun;
using UnityEngine;

/// <summary>
/// Shock Dart (Resources/Object/ShockDart): flies fast and flat. A direct hit does <see cref="directDamage"/> and
/// slows (<see cref="ShockedEffect"/>); hitting anything else zaps everyone within <see cref="zapRadius"/> for less.
/// The thrower's client decides and deals it.
/// </summary>
public class ShockDartObject : NetworkedProjectile
{
    [TunableParam("Direct Damage", 0f, 200f)]
    [SerializeField] private float directDamage = 40f;
    [TunableParam("Zap Damage", 0f, 200f)]
    [SerializeField] private float zapDamage = 20f;
    [TunableParam("Zap Radius", 0.5f, 10f)]
    [SerializeField] private float zapRadius = 2.5f;
    [TunableParam("Slow Time", 0f, 10f)]
    [SerializeField] private float slowSeconds = 2.5f;

    private bool done;

    public override float GravityScale => 0.3f;
    public override Vector3 LaunchVelocity(Vector3 thrown) => thrown * 1.6f;

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsAuthority || done) return;
        done = true;
        Vector3 at = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        var victim = collision.collider.GetComponentInParent<Player>();
        if (victim != null && TacticalNet.IsTarget(null, victim, enemiesOnly: false))
        {
            AbilityDamage.Hit(victim, directDamage * Power, "ShockDart", Body.linearVelocity, trap: false);
            TacticalNet.Send(victim, TacticalNet.Status.Shock, slowSeconds);
        }
        else
        {
            foreach (var p in Player.All)
            {
                if (!TacticalNet.IsTarget(null, p, enemiesOnly: false)) continue;
                if (Vector3.Distance(AbilityDamage.Chest(p), at) > zapRadius * Mathf.Sqrt(Power)) continue;
                AbilityDamage.Hit(p, zapDamage * Power, "ShockDart", AbilityDamage.Chest(p) - at, trap: false);
                TacticalNet.Send(p, TacticalNet.Status.Shock, slowSeconds * 0.6f);
            }
        }
        HideAndFreeze();
        photonView.RPC(nameof(RPC_Zap), RpcTarget.All, at);
        Invoke(nameof(Remove), 0.3f);
    }

    private void Remove() => DestroyNetworked();

    [PunRPC]
    private void RPC_Zap(Vector3 at)
    {
        HideAndFreeze();
        Sfx.At(SfxKind.ZapDown, at, 1f, 40f, 1.4f);
        Vfx.Burst(at, new Color(0.4f, 0.95f, 1f), 26, 7f, 0.18f, 0.35f);
        Vfx.Ring(at, new Color(0.4f, 0.95f, 1f), 24, zapRadius * 2.5f, 0.2f, 0.3f);
    }
}
