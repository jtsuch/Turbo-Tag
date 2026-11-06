/// <summary>Curse: no jumping (jump strength 0).</summary>
public class StickyFeetEffect : PlayerEffect
{
    public override string DisplayName => "Sticky Feet";
    public override string Description => "No jumping";
    public override bool   IsGood      => false;

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) player.Modifiers.Set(Stat.Jump, this, 0f);
    }

    protected override void OnEffectEnd()
    {
        if (player != null) player.Modifiers.Remove(Stat.Jump, this);
    }
}
