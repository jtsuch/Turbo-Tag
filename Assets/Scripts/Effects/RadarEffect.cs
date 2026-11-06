/// <summary>Boon: every player shows on your minimap, whatever the mode normally allows.</summary>
public class RadarEffect : PlayerEffect
{
    public override string DisplayName => "Radar";
    public override string Description => "Everyone shows on your map";

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) Minimap.ShowEveryone = true;
    }

    protected override void OnEffectEnd()
    {
        if (IsLocalEffect) Minimap.ShowEveryone = false;
    }
}
