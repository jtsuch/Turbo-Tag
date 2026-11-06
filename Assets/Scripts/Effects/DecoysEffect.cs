using UnityEngine;

/// <summary>Boon (escape): three look-alikes sprint off in different directions.</summary>
public class DecoysEffect : PlayerEffect
{
    public override string DisplayName => "Decoys";
    public override string Description => "Three fake yous run off";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => true;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        if (player.TryGetComponent(out DonutNet net)) net.Decoys();
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;

    }
}
