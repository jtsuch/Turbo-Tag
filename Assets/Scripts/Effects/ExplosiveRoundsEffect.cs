using UnityEngine;

/// <summary>Boon (gun modes): each shot sets off a small blast where it lands (pushes, and splash damage to enemies).</summary>
public class ExplosiveRoundsEffect : PlayerEffect
{
    public override string DisplayName => "Explosive Rounds";
    public override string Description => "Every shot blows up where it lands";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.Gun.ExplosiveRounds = true;
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.Gun.ExplosiveRounds = false;
    }
}
