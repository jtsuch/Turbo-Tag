using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Frag grenade (Resources/Object/Frag): bounces, goes off after <see cref="fuse"/> s. The thrower's client deals the
/// damage (up to <see cref="maxDamage"/> at the middle, nothing at <see cref="radius"/>, only with a clear line from the
/// blast; teammates and the thrower too); every screen gets the blast and pushes its own bodies.
/// </summary>
public class FragObject : NetworkedProjectile
{
    [TunableParam("Fuse", 0.2f, 10f)]
    [SerializeField] private float fuse = 2f;
    [TunableParam("Radius", 1f, 20f)]
    [SerializeField] private float radius = 6f;
    [TunableParam("Max Damage", 0f, 300f)]
    [SerializeField] private float maxDamage = 85f;

    protected override void OnLaunched()
    {
        if (IsAuthority) StartCoroutine(Fuse());
    }

    private IEnumerator Fuse()
    {
        yield return new WaitForSeconds(fuse);
        Vector3 at = transform.position;
        foreach (var p in Player.All)
        {
            if (!TacticalNet.IsTarget(null, p, enemiesOnly: false)) continue;
            float d = Vector3.Distance(AbilityDamage.Chest(p), at);
            if (d > radius || !AbilityDamage.InSight(at + Vector3.up * 0.2f, p, transform)) continue;
            float t = 1f - d / radius;
            AbilityDamage.Hit(p, maxDamage * Mathf.Lerp(0.25f, 1f, t * t) * Power, "Frag", AbilityDamage.Chest(p) - at, trap: false);
        }
        HideAndFreeze();
        photonView.RPC(nameof(RPC_Blast), RpcTarget.All, at);
        yield return new WaitForSeconds(0.3f);
        DestroyNetworked();
    }

    [PunRPC]
    private void RPC_Blast(Vector3 at)
    {
        HideAndFreeze();
        Fx.Explosion(at, radius * 0.5f);
        WorldFx.Explode(at, radius, 18f, default, 1f, sourceActor: photonView.OwnerActorNr, cause: "Frag");
    }
}
