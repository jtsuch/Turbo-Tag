/// <summary>Boon: full control in the air, as on the ground (air control is capped at ground level).</summary>
public class AirControlEffect : PlayerEffect
{
    public override string DisplayName => "Perfect Air Control";
    public override string Description => "Steer in the air like on the ground";

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) player.Modifiers.Set(Stat.AirControl, this, 10f); // Capped at 1 (ground) by PlayerMovement
    }

    protected override void OnEffectEnd()
    {
        if (player != null) player.Modifiers.Remove(Stat.AirControl, this);
    }
}
