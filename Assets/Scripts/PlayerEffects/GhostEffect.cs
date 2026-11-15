/// <summary>Boon: nearly invisible to everyone else (Cloak's see-through look, synced by PlayerAppearance).</summary>
public class GhostEffect : PlayerEffect
{
    public override string DisplayName => "Ghost";
    public override string Description => "Nearly invisible";

    protected override void OnEffectStart()
    {
        if (IsLocalEffect && player.Appearance != null) player.Appearance.Cloaked = true;
    }

    protected override void OnEffectEnd()
    {
        if (IsLocalEffect && player != null && player.Appearance != null) player.Appearance.Cloaked = false;
    }
}
