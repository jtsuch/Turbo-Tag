using UnityEngine;

/// <summary>Boon (escape): every other player is frozen for a few seconds (shields block it).</summary>
public class FreezeOthersEffect : PlayerEffect
{
    public override string DisplayName => "Freeze!";
    public override string Description => "Everyone else freezes";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => true;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        if (player.TryGetComponent(out DonutNet net)) net.FreezeOthers(3f);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;

    }
}
