/// <summary>Boon: jumps twice as high (jump speed ×√2).</summary>
public class RocketJumpEffect : PlayerEffect
{
    public override string DisplayName => "Rocket Jump";
    public override string Description => "Jumps go twice as high";
    public override bool   IsGood      => true;

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) player.Modifiers.Set(Stat.Jump, this, 1.414f);
    }

    protected override void OnEffectEnd()
    {
        if (player != null) player.Modifiers.Remove(Stat.Jump, this);
    }
}
