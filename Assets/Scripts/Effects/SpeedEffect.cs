/// <summary>Boon: 50% faster movement.</summary>
public class SpeedEffect : PlayerEffect
{
    public override string DisplayName => "Speed Demon";
    public override string Description => "50% faster";
    public override bool   IsGood      => true;

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) player.Modifiers.Set(Stat.Speed, this, 1.5f);
    }

    protected override void OnEffectEnd()
    {
        if (player != null) player.Modifiers.Remove(Stat.Speed, this);
    }
}
