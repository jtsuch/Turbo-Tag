/// <summary>Boon: no ability cooldowns, and Basic ability meters never run down.</summary>
public class NoCooldownEffect : PlayerEffect
{
    public override string DisplayName => "No Cooldowns";
    public override string Description => "Spam every ability";

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) player.Modifiers.Set(Stat.Cooldown, this, 0f);
    }

    protected override void OnEffectEnd()
    {
        if (player != null) player.Modifiers.Remove(Stat.Cooldown, this);
    }
}
