/// <summary>Chaos: everyone gets moon gravity at once (the taker's donut sends it to every player).</summary>
public class LowGravityPartyEffect : PlayerEffect
{
    public override string DisplayName => "Low Gravity Party";
    public override string Description => "Everyone's floaty";
    public override bool   IsChaos     => true;

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) player.Modifiers.Set(Stat.Gravity, this, 0.35f);
    }

    protected override void OnEffectEnd()
    {
        if (player != null) player.Modifiers.Remove(Stat.Gravity, this);
    }
}
