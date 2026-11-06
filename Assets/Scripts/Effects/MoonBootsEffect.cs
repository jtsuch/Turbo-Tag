/// <summary>Boon: half gravity: floaty, higher and longer jumps.</summary>
public class MoonBootsEffect : PlayerEffect
{
    public override string DisplayName => "Moon Boots";
    public override string Description => "Half gravity, floaty jumps";
    public override bool   IsGood      => true;

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) player.Modifiers.Set(Stat.Gravity, this, 0.5f);
    }

    protected override void OnEffectEnd()
    {
        if (player != null) player.Modifiers.Remove(Stat.Gravity, this);
    }
}
