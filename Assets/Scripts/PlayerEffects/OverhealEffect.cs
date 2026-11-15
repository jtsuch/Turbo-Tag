using UnityEngine;

/// <summary>Boon (gun modes): health jumps to double the maximum and drains back down.</summary>
public class OverhealEffect : PlayerEffect
{
    public override string DisplayName => "Overheal";
    public override string Description => "200 health, draining back to normal";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.Health.Overheal();
        Fx.Heal(player.transform.position + Vector3.up, true);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;

    }
}
