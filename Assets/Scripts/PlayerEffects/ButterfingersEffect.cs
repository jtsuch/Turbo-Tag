/// <summary>Curse: abilities switched off (anything held is dropped).</summary>
public class ButterfingersEffect : PlayerEffect
{
    public override string DisplayName => "Butterfingers";
    public override string Description => "No abilities";
    public override bool   IsGood      => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.AbilitiesLockedByEffect = true;
        if (player.Abilities != null) player.Abilities.CancelAll();
    }

    protected override void OnEffectEnd()
    {
        if (IsLocalEffect && player != null) player.AbilitiesLockedByEffect = false;
    }
}
