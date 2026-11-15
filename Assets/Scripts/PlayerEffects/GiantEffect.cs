/// <summary>Curse: twice the size (an easy target; size syncs to everyone).</summary>
public class GiantEffect : PlayerEffect
{
    public override string DisplayName => "Giant";
    public override string Description => "You're twice the size";
    public override bool   IsGood      => false;

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) player.Modifiers.Set(Stat.Size, this, 2f);
    }

    protected override void OnEffectEnd()
    {
        if (player != null) player.Modifiers.Remove(Stat.Size, this);
    }
}
