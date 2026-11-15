using UnityEngine;

/// <summary>Boon: the next two donuts you eat are guaranteed good, and a fresh one appears near you.</summary>
public class LuckyStreakEffect : PlayerEffect
{
    public override string DisplayName => "Lucky Streak";
    public override string Description => "Next two donuts are good; one's nearby";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => true;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        Donut.LuckyRolls = 2;
        if (DonutManager.Instance != null) DonutManager.Instance.RequestNear(player.transform.position);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;

    }
}
