using Photon.Pun;
using UnityEngine;

/// <summary>
/// Heals 50 over 4 s (<see cref="HealingEffect"/>): the teammate under your crosshair (within 25 m, in sight), or
/// yourself if there isn't one. Health modes only.
/// </summary>
[AbilityInfo("Heal 50 over a few seconds: the teammate under your crosshair, or yourself.", DisplayName = "Heal Orb", HealthModesOnly = true)]
public class HealOrb : QuickAbility
{
    [TunableParam("Reach", 2f, 60f)]
    [SerializeField] private float reach = 25f;
    [TunableParam("Heal Time", 0.5f, 15f)]
    [SerializeField] private float healSeconds = 4f;

    protected override void Activate()
    {
        Player target = Owner;
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera.transform : null;
        if (cam != null)
        {
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(cam.position, cam.forward, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= best || hit.collider.transform.IsChildOf(Owner.transform)) continue;
                best = hit.distance;
                var p = hit.collider.GetComponentInParent<Player>();
                target = p != null && p.IsTeammateOf(Owner.ActorNumber) && !p.IsCaught ? p : Owner;
            }
        }
        TacticalNet.Send(target, TacticalNet.Status.Heal, healSeconds);
        Owner.photonView.RPC(nameof(RPC_HealFx), RpcTarget.All, Owner.transform.position + Vector3.up * 1.2f, AbilityDamage.Chest(target));
    }

    [PunRPC]
    private void RPC_HealFx(Vector3 from, Vector3 to)
    {
        Sfx.At(SfxKind.Heal, to, 1f, 30f);
        if ((to - from).sqrMagnitude > 1f) GunFx.Tracer(from, to);
        Vfx.Burst(to, new Color(0.45f, 1f, 0.5f), 24, 3f, 0.25f, 0.8f, direction: Vector3.up, spread: 0.6f);
    }
}
