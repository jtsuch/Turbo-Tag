using Photon.Pun;
using UnityEngine;

/// <summary>
/// Stomps out an echo: every enemy within <see cref="radius"/> shows through walls for you and your team for a moment
/// (walls don't stop the echo). The revealed players' map borders pulse. Health modes only.
/// </summary>
[AbilityInfo("Stomp: every enemy close by shows through walls for you and your team for a moment.", DisplayName = "Echo Stomp", HealthModesOnly = true)]
public class EchoStomp : QuickAbility
{
    [TunableParam("Radius", 3f, 40f)]
    [SerializeField] private float radius = 17.25f;
    [TunableParam("Reveal Time", 0.5f, 10f)]
    [SerializeField] private float revealSeconds = 3f;

    protected override void Activate()
    {
        float radius = this.radius * UpgradePower;   // S&D: 75–125%
        foreach (var p in Player.All)
        {
            if (!TacticalNet.IsTarget(Owner, p, enemiesOnly: true)) continue;
            if (Vector3.Distance(p.transform.position, Owner.transform.position) > radius) continue;
            TacticalNet.Reveal(Owner.ActorNumber, p.ActorNumber, revealSeconds);
        }
        Owner.photonView.RPC(nameof(RPC_EchoFx), RpcTarget.All, Owner.transform.position, radius);
    }

    [PunRPC]
    private void RPC_EchoFx(Vector3 at, float reach)
    {
        AoeDebug.Sphere(at, reach, new Color(1f, 0.6f, 0.15f), "Echo Stomp reveal");
        Sfx.At(SfxKind.Land, at, 1f, 45f, 0.6f);
        Sfx.At(SfxKind.Warp, at, 0.7f, 45f, 0.5f);
        Vfx.Ring(at + Vector3.up * 0.2f, new Color(1f, 0.55f, 0.3f), 64, reach * 1.6f, 0.5f, 0.7f);
        GroundRing.Show(at, reach, new Color(1f, 0.55f, 0.3f), 1.4f); // Exactly how far it reached
        if (PlayerCam.Local != null && Player.Local != null && Vector3.Distance(Player.Local.transform.position, at) < reach) PlayerCam.Local.AddShake(0.2f);
    }
}
