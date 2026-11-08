using Photon.Pun;
using UnityEngine;

/// <summary>
/// A shield that soaks up the next 50 damage (for up to 12 s): <see cref="BubbleShieldEffect"/> sets
/// <see cref="PlayerHealth.Shield"/>. Health modes only. (Named BubbleShield: the Bubble donut's visual already owns
/// the name ShieldBubble.)
/// </summary>
[AbilityInfo("A shield that soaks up the next 50 damage you take (up to 12 s).", DisplayName = "Shield Bubble", HealthModesOnly = true)]
public class BubbleShield : QuickAbility
{
    [TunableParam("Lasts", 1f, 60f)]
    [SerializeField] private float seconds = 12f;

    protected override void Activate()
    {
        PlayerEffect.Apply(typeof(BubbleShieldEffect), Owner, seconds);
        Owner.photonView.RPC(nameof(RPC_BubbleFx), RpcTarget.All, Owner.transform.position + Vector3.up * 1.4f);
    }

    [PunRPC]
    private void RPC_BubbleFx(Vector3 at)
    {
        Sfx.At(SfxKind.ZapUp, at, 0.9f, 30f, 1.2f);
        Vfx.Ring(at, new Color(0.45f, 0.75f, 1f), 40, 4f, 0.35f, 0.5f);
        Vfx.Burst(at, new Color(0.45f, 0.75f, 1f, 0.7f), 20, 2.5f, 0.3f, 0.6f);
    }
}
