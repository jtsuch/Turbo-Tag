using Photon.Pun;
using UnityEngine;

/// <summary>
/// Sprays a cone of ink ahead (15.4 m, 30° either side): everyone in it with a clear line gets ink over their screen for
/// a few seconds (<see cref="InkedEffect"/>). Hits teammates too. Health modes only.
/// </summary>
[AbilityInfo("Spray a cone of ink ahead: anyone caught in it gets a screen full of ink for a few seconds.", DisplayName = "Ink Blast", HealthModesOnly = true)]
public class InkBlast : QuickAbility
{
    [TunableParam("Range", 2f, 40f)]
    [SerializeField] private float range = 15.4f;
    [TunableParam("Cone Angle", 5f, 90f)]
    [SerializeField] private float angle = 30f;
    [TunableParam("Ink Time", 0.5f, 10f)]
    [SerializeField] private float inkSeconds = 3.5f;

    protected override void Activate()
    {
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera.transform : transform;
        foreach (var p in Player.All)
        {
            if (!TacticalNet.IsTarget(Owner, p, enemiesOnly: false)) continue;
            Vector3 to = AbilityDamage.Chest(p) - cam.position;
            if (to.magnitude > range || Vector3.Angle(cam.forward, to) > angle) continue;
            if (!AbilityDamage.InSight(cam.position, p, Owner.transform)) continue;
            TacticalNet.Send(p, TacticalNet.Status.Ink, inkSeconds);
        }
        Owner.photonView.RPC(nameof(RPC_InkFx), RpcTarget.All, cam.position + cam.forward * 0.8f, cam.forward);
    }

    [PunRPC]
    private void RPC_InkFx(Vector3 from, Vector3 dir)
    {
        AoeDebug.Cone(from - dir * 0.8f, dir, range, angle, new Color(0.75f, 0.45f, 1f), "Ink Blast (line of sight)");
        Sfx.At(SfxKind.Splat, from, 1f, 35f, 0.8f);
        for (int i = 1; i <= 6; i++)
            Vfx.Burst(from + dir * (i * 2.2f), new Color(0.05f, 0.04f, 0.1f, 0.95f), 14, 2f + i, 0.35f + i * 0.12f, 0.6f, direction: dir, spread: 0.5f);
    }
}
