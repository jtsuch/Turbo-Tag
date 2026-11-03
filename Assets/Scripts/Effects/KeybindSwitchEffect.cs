/// <summary>Effect: inverts movement controls (W ↔ S, A ↔ D) for the duration.</summary>
public class KeybindSwitchEffect : PlayerEffect
{
    protected override void OnEffectStart()
    {
        if (IsLocalEffect) player.Modifiers.Set(Stat.ControlDirection, this, -1f);
    }

    protected override void OnEffectEnd()
    {
        if (player != null) player.Modifiers.Remove(Stat.ControlDirection, this);
    }
}
