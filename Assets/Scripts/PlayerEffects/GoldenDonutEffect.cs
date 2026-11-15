using UnityEngine;

/// <summary>Boon (rare): three random good effects at once (DonutManager rolls them).</summary>
public class GoldenDonutEffect : PlayerEffect
{
    public override string DisplayName => "Golden Donut";
    public override string Description => "Three good effects at once!";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => true;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        Sfx.Here(SfxKind.Fanfare, 0.8f, 1.2f);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;

    }
}
