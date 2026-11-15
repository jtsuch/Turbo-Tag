using UnityEngine;

/// <summary>Boon (gun modes): the next fatal hit leaves you standing on 50 health instead. Lasts until used (or the timer).</summary>
public class SecondLifeEffect : PlayerEffect
{
    public override string DisplayName => "Second Life";
    public override string Description => "Your next death leaves you on 50 health";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.SecondLife = true;
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.SecondLife = false;
    }

    private void Update()
    {
        if (IsLocalEffect && !player.SecondLife) { CancelEffect(); Destroy(this); } // Used up
    }
}
